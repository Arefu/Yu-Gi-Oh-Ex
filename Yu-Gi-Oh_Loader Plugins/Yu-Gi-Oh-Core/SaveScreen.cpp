#include <Windows.h>
#include <algorithm>
#include <cstring>
#include <format>
#include <functional>
#include <string>
#include <vector>

#include "Detours.h"
#include "Logger.h"
#include "Save.h"
#include "SaveScreen.h"
#include "SaveSlots.h"
#include "YuGiOh/YuGiOh-RIX.h"
#include "YuGiOh/YuGiOh-SAVE.h"

// How the game's screens work (all traced in YuGiOh.exe.i64, see docs/SaveSlots.md):
//  - a screen is a RIX::ScreenBase (656 bytes) with its own vftable (22 slots, base RIX::ScreenBase2 at 0x140A716B8);
//  - SetScreenId puts it in g_ScreenMap, which is how NavigateToScreen / GotoScreen find it;
//  - Load (slot 2) makes its root node, help bar and dialog and calls SetupWidgets (slot 8); Activate (slot 4) sets the flag Tick needs;
//  - Tick (slot 1) runs every frame for every screen from the App's main scheduler: it calls OnEnter (12) when the screen becomes
//    current, Update (14) while it is, OnLeave (13) when it stops being current, and draws it;
//  - ScreenCommonBg draws the backdrop of the current screen from its ScreenType (3 = the Card Shop's).
// The game builds its screens in RIX::App::Initialize; this one is built the same way when the title screen first shows, and ticked
// right after the main scheduler's jobs.
namespace R = YGO::RIX;

namespace
{
    constexpr int kScreenId = 100;              // not used by the game (its ids end at 43)
    constexpr int kScreenTitle = 5;
    constexpr int kScreenSignIn = 6;            // reads the save after Play, then goes to the main menu (8)
    constexpr int kScreenTypeCardShop = 3;
    constexpr size_t kScreenSize = 0x290 + 0x70; // RIX::ScreenBase plus slack

    constexpr uintptr_t kScreenBaseVftable = 0x140A716B8;
    constexpr int kVftableSlots = 22;
    constexpr int kSlotDestructor = 0;
    constexpr int kSlotSetupWidgets = 8;
    constexpr int kSlotOnEnter = 12;
    constexpr int kSlotOnLeave = 13;
    constexpr int kSlotUpdate = 14;

    constexpr uintptr_t kGlobalInstance = 0x1429275D8;   // YGOInstance: +0x1F0 the UI, +0x200 the main screen scheduler
    constexpr size_t kMainScheduler = 0x200;

    constexpr uintptr_t kHelpSelect = 0x140A7A1A0;  // the game's own help bar entry (confirm "Select")

    constexpr int kUp = 1, kDown = 2, kLeft = 4, kRight = 8, kConfirm = 0x1000;
    constexpr int kSoundConfirm = 39;            // what the title plays on Play
    constexpr int kSoundError = 71;

    auto GameNew = reinterpret_cast<void*(__fastcall*)(size_t)>(0x14090D2B8);
    auto ScreenBase_Constructor = reinterpret_cast<void*(__fastcall*)(void*)>(0x140821B20);
    auto ScreenBase_SetScreenId = reinterpret_cast<void(__fastcall*)(void*, int)>(0x140822E00);
    auto ScreenBase_SetScreenType = reinterpret_cast<void(__fastcall*)(void*, int)>(0x1408228C0);
    auto ScreenBase_Load = reinterpret_cast<void(__fastcall*)(void*, int64_t)>(0x140822470);
    auto ScreenBase_Activate = reinterpret_cast<void(__fastcall*)(void*)>(0x140822390);
    auto ScreenBase_Tick = reinterpret_cast<void(__fastcall*)(void*, int64_t)>(0x140821EC0);

    // The profile in memory, for starting a new game in an empty slot (see ResetLiveProfile).
    auto Find_Profile = reinterpret_cast<void*(__fastcall*)(void*, unsigned int)>(0x1408004D0);
    auto Get_ProfileSaveManager = reinterpret_cast<int64_t(__fastcall*)(void*)>(0x140874930);
    auto GetOrInitProfileBuffer = reinterpret_cast<void*(__fastcall*)(int64_t, const void*)>(0x140871500);
    constexpr size_t kProfileBlob = 10208;      // the profile save manager's 44008 byte blob (Get_OrCreateBlob)
    constexpr size_t kCardTable = 0x5DC8;
    constexpr size_t kCardTableSize = 20000;

    uintptr_t orig_TitleUpdate = 0x1408676A0;   // RIX::ScreenTitle::Update(self, ui, seconds)
    uintptr_t orig_RunJobs = 0x140821170;       // RIX::Scheduler::RunJobs(scheduler, ui, limit)

    // ---- layout (the game's 1920 x 1080 space)
    constexpr float kFrameWidth = 264, kFrameHeight = 354;   // doShared "characterframe_large"
    constexpr float kFrameGap = 48;
    constexpr float kFrameTop = 270;
    constexpr float kPortraitBox = 196;
    constexpr float kPortraitTop = 44;
    constexpr float kNameTop = 266;
    constexpr float kDetailsTop = kFrameTop + kFrameHeight + 18;
    constexpr float kDetailsLine = 30;
    constexpr int kDetailLines = 4;
    constexpr float kDimmed = 0.55f;

    struct SlotView
    {
        float X = 0;
        R::SharedNode Frame, Portrait, Name;
        R::SharedNode Details[kDetailLines];
        std::wstring NameText;                   // the text nodes read these for as long as they live
        std::wstring DetailText[kDetailLines];
    };

    void* g_Screen = nullptr;
    bool g_Failed = false;
    void* g_Vftable[1 + kVftableSlots];         // [0] = the base's RTTI locator, the screen points at [1]
    std::vector<SlotView> g_Views;
    R::SharedNode g_Header, g_HeaderText;
    int g_Selected = 0;                          // 0-based
    int g_PendingNewSlot = 0;                    // the empty slot the Yes/No box is about

    const wchar_t* const kHeaderText = L"Select a Save";
    const wchar_t* const kNewGameText = L"Start a new game in this slot?";
    const wchar_t* const kBadFileText = L"This save file can't be read.";

    std::wstring Grouped(uint64_t value)
    {
        std::wstring text = std::to_wstring(value);
        for (int i = static_cast<int>(text.size()) - 3; i > 0; i -= 3)
            text.insert(static_cast<size_t>(i), L",");
        return text;
    }

    // Big numbers short ("100.0M", "1.2B") from 10 million, as PatchMeOut does for the game's own DP text: "100,024,485 DP" wrapped
    // in the slot's text box.
    std::wstring Short(uint64_t value)
    {
        if (value < 10000000ull)
            return Grouped(value);
        if (value >= 1000000000ull)
            return std::format(L"{}.{}B", value / 1000000000ull, (value / 100000000ull) % 10);
        return std::format(L"{}.{}M", value / 1000000ull, (value / 100000ull) % 10);
    }

    // The current screen record of the main channel: +0 current id, +8 pending id (taken next frame).
    int* MainRecord(int64_t ui)
    {
        int64_t records = *reinterpret_cast<int64_t*>(ui + 856);
        int index = *reinterpret_cast<int*>(ui + 852);
        return records ? reinterpret_cast<int*>(records + 48LL * index) : nullptr;
    }

    // A portrait from pdui/chars, scaled to fit a Box x Box square at X, Y (keeps its shape).
    R::SharedNode AddPortrait(const R::SharedNode* parent, const std::string& sprite, int z, float x, float y, float box)
    {
        R::SharedNode layer{};
        R::Dfx::MakeLayerAnimoo(&layer);
        if (!layer.Node)
            return {};
        R::Dfx::LayerSetResource(layer.Node, "pdui/chars");
        R::Dfx::LayerSelectByName(layer.Node, sprite.c_str());
        *reinterpret_cast<int*>(R::Dfx::LayerGetPlayer(layer.Node) + 48) = 0;   // top-left anchored

        int w = 0, h = 0;
        if (!R::Dfx::LayerSpriteSize(layer.Node, w, h))
        {
            R::ReleaseRef(layer);
            return {};
        }

        const float scale = (std::min)(box / w, box / h);
        R::SharedNode parentRef = R::ParentRef(parent);
        R::SharedNode node{};
        R::Dfx::MakeChildWithContent(&node, &parentRef, &layer, z, x + (box - w * scale) / 2, y + (box - h * scale) / 2);   // both consumed
        if (node.Node)
            R::Dfx::SetScaleXY(node.Node, scale, scale);
        return node;
    }

    void ForEachNode(SlotView& view, const std::function<void(R::SharedNode&)>& action)
    {
        action(view.Frame);
        action(view.Portrait);
        action(view.Name);
        for (auto& line : view.Details)
            action(line);
    }

    void ClearViews()
    {
        const R::SharedNode* root = R::ScreenRoot(g_Screen);
        for (auto& view : g_Views)
            ForEachNode(view, [&](R::SharedNode& node) { R::Dfx::RemoveImage(root, node); });
        g_Views.clear();
        R::Dfx::RemoveImage(root, g_Header);
        R::Dfx::RemoveImage(root, g_HeaderText);
    }

    void ShowSelection()
    {
        for (int i = 0; i < static_cast<int>(g_Views.size()); ++i)
        {
            const float alpha = i == g_Selected ? 1.0f : kDimmed;
            ForEachNode(g_Views[i], [&](R::SharedNode& node) { if (node.Node) R::Dfx::SetAlpha(node.Node, alpha); });
        }
    }

    // (Re)builds the whole screen from the slot files: called every time the screen is entered, so the details are current.
    void Build()
    {
        ClearViews();
        const R::SharedNode* root = R::ScreenRoot(g_Screen);
        if (!root->Node)
            return;

        g_Header = R::Dfx::AddImage(root, "pdui/doShared", "menuheader", 5, 0, 40);
        g_HeaderText = R::Dfx::AddText(root, kHeaderText, 6, 80, 72, 0, 44);

        const int count = SaveSlots::Count();
        const float total = count * kFrameWidth + (count - 1) * kFrameGap;
        const float left = (1920 - total) / 2;

        g_Views.resize(count);   // sized once: the text nodes point into these strings
        for (int i = 0; i < count; ++i)
        {
            const int slot = i + 1;
            SlotView& view = g_Views[i];
            view.X = left + i * (kFrameWidth + kFrameGap);

            const SaveSlots::Summary summary = SaveSlots::Read(slot);
            view.NameText = SaveSlots::NameOf(slot);
            if (!summary.Exists)
            {
                view.DetailText[0] = L"Empty slot";
                view.DetailText[1] = L"Start a new game";
            }
            else if (!summary.Valid)
            {
                view.DetailText[0] = L"Unreadable save file";
            }
            else
            {
                view.DetailText[0] = std::format(L"{} DP", Short(summary.Wallet));
                view.DetailText[1] = std::format(L"{} cards", Short(summary.CardsOwned));
                view.DetailText[2] = std::format(L"{} wins / {} duels", Grouped(summary.Wins), Grouped(summary.Duels));
                // no "last played" date: the game's text box wrapped "2026-10-02 14:33" and drew it over the line above. The screen
                // opens on the last slot played anyway (SaveSlots::LastSlot).
            }

            view.Frame = R::Dfx::AddImage(root, "pdui/doShared", "characterframe_large", 10, view.X, kFrameTop);
            view.Portrait = AddPortrait(root, SaveSlots::AvatarSpriteOf(slot), 11, view.X + (kFrameWidth - kPortraitBox) / 2,
                                        kFrameTop + kPortraitTop, kPortraitBox);
            view.Name = R::Dfx::AddText(root, view.NameText.c_str(), 12, view.X, kFrameTop + kNameTop, kFrameWidth, 23,
                                        R::Dfx::TextAlign::Centre);
            for (int line = 0; line < kDetailLines; ++line)
                if (!view.DetailText[line].empty())
                    view.Details[line] = R::Dfx::AddText(root, view.DetailText[line].c_str(), 12, view.X, kDetailsTop + line * kDetailsLine,
                                                         kFrameWidth, 20, R::Dfx::TextAlign::Centre, line == 0 ? 0xFFFFFFFF : 0xFFC8C8C8);
        }

        g_Selected = std::clamp(SaveSlots::LastSlot() - 1, 0, count - 1);
        ShowSelection();
    }

    void ShowHelpBar()
    {
        void* help = static_cast<char*>(g_Screen) + 264;
        R::HelpClear(help);
        R::HelpAdd(help, reinterpret_cast<const R::HelpEntry*>(kHelpSelect), 1);
        R::HelpLayout(help);
    }

    // An empty slot starts a new profile. The title screen already read another slot into memory, and when the save file is missing the game
    // writes out whatever profile is in memory (YGO::SAVE::Profile_WriteLiveBlob), so that profile is reset to a new one first.
    void ResetLiveProfile()
    {
        void* profile = Find_Profile(YGO::SAVE::g_SaveSystem, YGO::SAVE::CURRENT_PROFILE);
        int64_t manager = profile ? Get_ProfileSaveManager(profile) : 0;
        if (!manager)
        {
            Logger::WriteLog("No profile in memory to reset for the new save", MODULE_NAME, 1);
            return;
        }
        // InitNewBlob only clears bits 0-3 of the card table (the rest have no known meaning); a first start has them zero, so they are cleared too.
        if (auto* blob = *reinterpret_cast<unsigned char**>(manager + kProfileBlob))
            std::memset(blob + kCardTable, 0, kCardTableSize);
        GetOrInitProfileBuffer(manager, nullptr);   // InitNewBlob: the same as a first start
        Save::ApplySharedVolume(*reinterpret_cast<unsigned char**>(manager + kProfileBlob));   // new profiles keep the volume too
    }

    void Continue(int slot, bool newGame)
    {
        SaveSlots::Select(slot);
        if (newGame)
            ResetLiveProfile();
        R::PlayUISound(kSoundConfirm);
        R::GotoScreenFrom(g_Screen, kScreenSignIn);   // SignIn reads the save from the slot's file, then opens the main menu
    }

    void OnNewGameConfirmed()
    {
        if (g_PendingNewSlot > 0)
            Continue(g_PendingNewSlot, true);
    }

    void Choose(int slot)
    {
        const SaveSlots::Summary summary = SaveSlots::Read(slot);
        if (!summary.Exists)
        {
            g_PendingNewSlot = slot;
            std::function<void()> onYes = &OnNewGameConfirmed;   // fits the small buffer, the game takes it over
            R::ShowYesNo(g_Screen, -1, kNewGameText, &onYes);
            return;
        }
        if (!summary.Valid)
        {
            R::EmptyFunction none;
            R::ShowMessageText(g_Screen, kSoundError, kBadFileText, &none);
            return;
        }
        Continue(slot, false);
    }

    int SlotUnderMouse()
    {
        const int64_t mouse = R::Input::MousePosition();
        const float x = static_cast<float>(static_cast<int32_t>(mouse));
        const float y = static_cast<float>(static_cast<int32_t>(mouse >> 32));
        for (int i = 0; i < static_cast<int>(g_Views.size()); ++i)
            if (x >= g_Views[i].X && x < g_Views[i].X + kFrameWidth && y >= kFrameTop && y < kDetailsTop + kDetailLines * kDetailsLine)
                return i;
        return -1;
    }

    // ---- the vftable slots this screen overrides

    void* __fastcall Vt_Destructor(void* self, unsigned int)
    {
        return self;   // the screen lives as long as the game; g_ScreenMap keeps pointing at it
    }

    void __fastcall Vt_SetupWidgets(void*, int64_t)
    {
        // Everything is built in OnEnter, from the slot files as they are then.
    }

    void __fastcall Vt_OnEnter(void*, int64_t)
    {
        Build();
        ShowHelpBar();
    }

    void __fastcall Vt_OnLeave(void*, int64_t)
    {
    }

    void __fastcall Vt_Update(void*, int64_t)
    {
        if (g_Views.empty())
            return;

        int pressed = R::Input::GetPressed(R::InputState) | R::Input::GetRepeat(R::InputState);
        pressed |= R::Input::HelpBarPressed(static_cast<char*>(g_Screen) + 264);

        int selected = g_Selected;
        if (R::Input::MouseActive())
        {
            const int hover = SlotUnderMouse();
            if (hover >= 0)
            {
                selected = hover;
                if (R::Input::TakeMouseClick(R::InputState, kConfirm))
                    pressed |= kConfirm;
            }
        }
        if (pressed & (kLeft | kUp))
            selected = (std::max)(0, selected - 1);
        if (pressed & (kRight | kDown))
            selected = (std::min)(static_cast<int>(g_Views.size()) - 1, selected + 1);

        if (selected != g_Selected)
        {
            g_Selected = selected;
            ShowSelection();
        }

        if (pressed & kConfirm)
            Choose(g_Selected + 1);   // no Back: the title just sends you here again, so cancel bounced between the two
    }

    // ---- building the screen

    void Create(int64_t ui)
    {
        if (R::FindScreenObject(kScreenId))
        {
            Logger::WriteLog(std::format("Screen id {} is already taken, the save-select screen is off", kScreenId), MODULE_NAME, 2);
            g_Failed = true;
            return;
        }

        void** base = reinterpret_cast<void**>(kScreenBaseVftable);
        g_Vftable[0] = base[-1];
        std::memcpy(&g_Vftable[1], base, sizeof(void*) * kVftableSlots);
        g_Vftable[1 + kSlotDestructor] = reinterpret_cast<void*>(&Vt_Destructor);
        g_Vftable[1 + kSlotSetupWidgets] = reinterpret_cast<void*>(&Vt_SetupWidgets);
        g_Vftable[1 + kSlotOnEnter] = reinterpret_cast<void*>(&Vt_OnEnter);
        g_Vftable[1 + kSlotOnLeave] = reinterpret_cast<void*>(&Vt_OnLeave);
        g_Vftable[1 + kSlotUpdate] = reinterpret_cast<void*>(&Vt_Update);

        void* screen = GameNew(kScreenSize);
        if (!screen)
        {
            g_Failed = true;
            return;
        }
        std::memset(screen, 0, kScreenSize);
        ScreenBase_Constructor(screen);
        *reinterpret_cast<void***>(screen) = &g_Vftable[1];
        ScreenBase_SetScreenId(screen, kScreenId);
        ScreenBase_SetScreenType(screen, kScreenTypeCardShop);
        ScreenBase_Load(screen, ui);
        ScreenBase_Activate(screen);
        g_Screen = screen;
        Logger::WriteLog(std::format("Save-select screen ready (screen id {})", kScreenId), MODULE_NAME, 0);
    }

    // ---- hooks

    // Play on the title asks for ScreenSignIn (6) through the pending screen of the main record; this screen goes in between.
    void __fastcall Hook_TitleUpdate(int64_t self, int64_t ui, double seconds)
    {
        reinterpret_cast<void(__fastcall*)(int64_t, int64_t, double)>(orig_TitleUpdate)(self, ui, seconds);

        if (!g_Screen)
            return;
        int* record = MainRecord(ui);
        if (record && record[0] == kScreenTitle && record[2] == kScreenSignIn)
        {
            record[2] = kScreenId;
            *reinterpret_cast<int*>(static_cast<char*>(g_Screen) + 60) = kScreenTitle;   // where Back goes (GotoScreen bookkeeping)
        }
    }

    void __fastcall Hook_RunJobs(int64_t scheduler, int64_t ui, unsigned int limit)
    {
        reinterpret_cast<void(__fastcall*)(int64_t, int64_t, unsigned int)>(orig_RunJobs)(scheduler, ui, limit);

        char* instance = *reinterpret_cast<char**>(kGlobalInstance);
        if (!instance || scheduler != *reinterpret_cast<int64_t*>(instance + kMainScheduler) || g_Failed)
            return;

        if (!g_Screen)
        {
            // Built once the title screen is up: the game has finished loading its own screens by then.
            int* record = MainRecord(ui);
            if (record && record[0] == kScreenTitle)
                Create(ui);
            return;
        }
        ScreenBase_Tick(g_Screen, ui);
    }
}

bool SaveScreen::Install()
{
    if (!SaveSlots::ShouldAsk())
        return true;

    DetourTransactionBegin();
    DetourUpdateThread(GetCurrentThread());
    DetourAttach(&(PVOID&)orig_TitleUpdate, Hook_TitleUpdate);
    DetourAttach(&(PVOID&)orig_RunJobs, Hook_RunJobs);
    LONG err = DetourTransactionCommit();

    Logger::WriteLog(std::format("Save-select screen hooks: {}", err), MODULE_NAME, err == 0 ? 0 : 2);
    return err == 0;
}
