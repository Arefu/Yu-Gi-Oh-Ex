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
#include "Yu-Gi-Oh-RIX.h"
#include "YuGiOh/YuGiOh-SAVE.h"
#include "YuGiOh/YuGiOh-Addresses.h"

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
    constexpr int kScreenMainMenu = 8;
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

    constexpr uintptr_t kHelpSelect = YGO::Addr::HelpEntry_Select;  // the game's own help bar entry (confirm "Select")

    constexpr int kUp = 1, kDown = 2, kLeft = 4, kRight = 8, kConfirm = 0x1000, kCancel = 0x2000;
    constexpr int kDelete = 0x4000, kPicture = 0x8000;   // X / Y, or their help bar prompts
    constexpr unsigned kWheelUp = 0x20000000u, kWheelDown = 0x40000000u;
    constexpr int kSoundConfirm = 39;            // what the title plays on Play
    constexpr int kSoundError = 71;
    constexpr int kSoundCancel = 18;
    constexpr int kSoundStep = 16;               // what the page engine's sliders play

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
    constexpr int kColumns = 5;                              // one row of slots shows; the bar on the right scrolls the rows
    constexpr float kBarX = 1800, kBarHeight = kFrameHeight + kDetailLines * kDetailsLine + 18;

    struct SlotView
    {
        int Tile = 0;                            // 0-based; Count() = the "new slot" tile
        float X = 0;
        R::SharedNode Frame, Portrait, Name;
        R::SharedNode Details[kDetailLines];
        std::wstring NameText;                   // the text nodes read these for as long as they live
        std::wstring DetailText[kDetailLines];
        bool Blocked = false;                    // made with other "newSave" plugins (SaveSlots::CanPlay): the details say why
    };

    void* g_Screen = nullptr;
    bool g_Failed = false;
    int g_LastScreen = -1;                       // the main record's current screen last frame
    bool g_BackFromGame = false;                 // the title was reached by backing out of the main menu
    void* g_Vftable[1 + kVftableSlots];         // [0] = the base's RTTI locator, the screen points at [1]
    std::vector<SlotView> g_Views;
    R::SharedNode g_Header, g_HeaderText;
    int g_Selected = 0;                          // tile, 0-based
    int g_TopRow = 0;                            // the row on screen
    int g_PendingDelete = 0;
    bool g_Picking = false;                      // Change Picture: left / right step through the portraits on the selected slot
    std::vector<std::string> g_PickKeys;
    int g_PickIndex = 0;
    std::string g_PickOriginal;
    int g_PickCooldown = 0;                      // frames until the next step (held keys / the wheel step at most every kPickDelay)
    constexpr int kPickDelay = 12;               // ~0.2 s at 60 fps
    R::SharedNode g_ArrowLeft, g_ArrowRight, g_PickTitle;
    float g_ArrowW = 0, g_ArrowH = 0;            // arrow_left1's size (ScreenHelpVideo's selector arrows)
    float g_ArrowY = 0, g_ArrowLeftX = 0, g_ArrowRightX = 0;
    const wchar_t* const kPickTitleText = L"Choose a picture";
    void* g_Bar = nullptr;                       // widget_BarV
    std::wstring g_DeleteText;                   // the Yes/No box reads it while open
    std::wstring g_BlockedText;                  // ... and the message box this one

    const wchar_t* const kHeaderText = L"Select a Save";
    const wchar_t* const kBadFileText = L"This save file can't be read.";
    const wchar_t* const kDeleteFailedText = L"The save file could not be deleted, so the slot was kept.";
    const R::HelpEntry kHelpDelete{ kDelete, 1, reinterpret_cast<int64_t>(L"Delete") };
    const R::HelpEntry kHelpPicture{ kPicture, 1, reinterpret_cast<int64_t>(L"Change Picture") };
    const R::HelpEntry kHelpPickPrevious{ kLeft, 1, reinterpret_cast<int64_t>(L"Previous") };
    const R::HelpEntry kHelpPickNext{ kRight, 1, reinterpret_cast<int64_t>(L"Next") };
    const R::HelpEntry kHelpPickOk{ kConfirm, 0, reinterpret_cast<int64_t>(L"Use Picture") };
    const R::HelpEntry kHelpPickBack{ kCancel, 1, reinterpret_cast<int64_t>(L"Back") };

    int TileCount() { return SaveSlots::Count() + (SaveSlots::Count() < SaveSlots::MaxSlots ? 1 : 0); }
    int RowCount() { return (TileCount() + kColumns - 1) / kColumns; }
    bool IsNewTile(int tile) { return tile >= SaveSlots::Count(); }

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
        for (SlotView& view : g_Views)
        {
            const float alpha = view.Tile == g_Selected ? 1.0f : kDimmed;
            ForEachNode(view, [&](R::SharedNode& node) { if (node.Node) R::Dfx::SetAlpha(node.Node, alpha); });
        }
    }

    // The game's scroll bar on the right, built the first time (ScreenSelectTutorial::SetupWidgets' recipe); hidden with one row.
    void ShowBar()
    {
        const R::SharedNode* root = R::ScreenRoot(g_Screen);
        if (!g_Bar && root->Node)
        {
            g_Bar = GameNew(256);
            std::memset(g_Bar, 0, 256);
            R::ScrollBar::Construct(g_Bar);
            R::SharedNode parent = R::ParentRef(root);
            R::ScrollBar::CreateFromLayout(g_Bar, &parent, 15, R::ScreenOwner(g_Screen), kBarX, kFrameTop, kBarHeight);
            if (void* node = *reinterpret_cast<void**>(static_cast<char*>(g_Bar) + R::ScrollBar::Node))
                R::ScrollBar::SetScaleX(node, 3.0f);
        }
        if (g_Bar)
            R::ScrollBar::SetRange(g_Bar, static_cast<float>(g_TopRow), 1.0f, RowCount() > 1 ? static_cast<float>(RowCount()) : 0.0f);
    }

    // (Re)builds the row on screen from the slot files: on entering the screen, scrolling, adding and deleting, so the details are current.
    void Build()
    {
        ClearViews();
        const R::SharedNode* root = R::ScreenRoot(g_Screen);
        if (!root->Node)
            return;

        g_Header = R::Dfx::AddImage(root, "pdui/doShared", "menuheader", 5, 0, 40);
        g_HeaderText = R::Dfx::AddText(root, kHeaderText, 6, 80, 72, 0, 44);

        g_TopRow = std::clamp(g_TopRow, 0, (std::max)(0, RowCount() - 1));
        const int first = g_TopRow * kColumns;
        const int count = (std::min)(kColumns, TileCount() - first);
        const float total = count * kFrameWidth + (count - 1) * kFrameGap;
        const float left = (1920 - total) / 2;

        g_Views.resize(count);   // sized once per build: the text nodes point into these strings
        for (int i = 0; i < count; ++i)
        {
            const int slot = first + i + 1;
            SlotView& view = g_Views[i];
            view.Tile = first + i;
            view.X = left + i * (kFrameWidth + kFrameGap);

            if (IsNewTile(view.Tile))
            {
                view.NameText = L"New Save";
                view.DetailText[0] = L"Start a new profile";
                view.Frame = R::Dfx::AddImage(root, "pdui/doShared", "characterframe_large", 10, view.X, kFrameTop);
                view.Name = R::Dfx::AddText(root, view.NameText.c_str(), 12, view.X, kFrameTop + kNameTop, kFrameWidth, 23, R::Dfx::TextAlign::Centre);
                view.Details[0] = R::Dfx::AddText(root, view.DetailText[0].c_str(), 12, view.X, kDetailsTop, kFrameWidth, 20, R::Dfx::TextAlign::Centre);
                continue;
            }

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
            else if (!SaveSlots::CanPlay(slot))
            {
                view.Blocked = true;
                const std::vector<std::wstring> why = SaveSlots::WhyNot(slot);
                for (int line = 0; line < kDetailLines && line < static_cast<int>(why.size()); ++line)
                    view.DetailText[line] = why[line];
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
                                                         kFrameWidth, 20, R::Dfx::TextAlign::Centre,
                                                         view.Blocked ? 0xFFFF9080 : line == 0 ? 0xFFFFFFFF : 0xFFC8C8C8);
        }

        ShowSelection();
        ShowBar();
    }

    void ShowHelpBar()
    {
        void* help = static_cast<char*>(g_Screen) + 264;
        R::HelpClear(help);
        if (g_Picking)
        {
            R::HelpAdd(help, &kHelpPickPrevious, 1);
            R::HelpAdd(help, &kHelpPickNext, 1);
            R::HelpAdd(help, &kHelpPickOk, 1);
            R::HelpAdd(help, &kHelpPickBack, 1);
        }
        else
        {
            R::HelpAdd(help, reinterpret_cast<const R::HelpEntry*>(kHelpSelect), 1);
            R::HelpAdd(help, &kHelpPicture, 1);
            R::HelpAdd(help, &kHelpDelete, 1);
        }
        R::HelpLayout(help);
    }

    // Selects a tile, scrolling its row onto the screen.
    void SelectTile(int tile)
    {
        tile = std::clamp(tile, 0, TileCount() - 1);
        const int row = tile / kColumns;
        g_Selected = tile;
        if (row != g_TopRow)
        {
            g_TopRow = row;
            Build();
        }
        else
            ShowSelection();
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
        SaveSlots::Select(slot, newGame);
        if (newGame)
            ResetLiveProfile();
        R::PlayUISound(kSoundConfirm);
        // SignIn reads the save from the slot's file, then fades into the main menu itself. Hand off to it as the title's Play does (pending
        // screen, no fade): GotoScreen faded out to SignIn first, so loading showed two transitions (the flicker). The bookkeeping GotoScreen
        // does is kept, so a failed load returns here.
        *reinterpret_cast<int*>(static_cast<char*>(g_Screen) + 64) = kScreenSignIn;
        if (void* signIn = R::FindScreenObject(kScreenSignIn))
            *reinterpret_cast<int*>(static_cast<char*>(signIn) + 60) = kScreenId;
        R::SetPendingScreen(R::GetUI(), kScreenSignIn);
    }

    int g_PendingPlay = 0;   // the blocked slot the "play anyway" box is asking about

    void OnPlayAnyway()
    {
        const int slot = g_PendingPlay;
        g_PendingPlay = 0;
        if (slot <= 0)
            return;
        Logger::WriteLog(std::format("Save slot {} played with different plugins than it was made with", slot), MODULE_NAME, 1);
        Continue(slot, false);
    }

    void Choose(int slot)
    {
        const SaveSlots::Summary summary = SaveSlots::Read(slot);
        if (!summary.Exists)
        {
            Continue(slot, true);   // an empty slot starts its new game straight away
            return;
        }
        if (!summary.Valid)
        {
            R::EmptyFunction none;
            R::ShowMessageText(g_Screen, kSoundError, kBadFileText, &none);
            return;
        }
        if (!SaveSlots::CanPlay(slot))
        {
            // A plugin that changes the game this much ("newSave" in its manifest) expects saves made with it, and its saves expect it:
            // loading the other kind can mix their cards, decks and progress. Yes plays it anyway (the save keeps its old tags).
            g_BlockedText = L"This save was made with different plugins.";
            for (const std::wstring& line : SaveSlots::WhyNot(slot))
                g_BlockedText += L"\n" + line;
            g_BlockedText += L"\nThings might break. Play it anyway?";
            g_PendingPlay = slot;
            std::function<void()> onYes = &OnPlayAnyway;
            R::ShowYesNo(g_Screen, kSoundError, g_BlockedText.c_str(), &onYes);
            return;
        }
        Continue(slot, false);
    }

    void OnDeleteConfirmed()
    {
        const int slot = g_PendingDelete;
        g_PendingDelete = 0;
        if (slot <= 0)
            return;
        if (!SaveSlots::Delete(slot))
        {
            R::EmptyFunction none;
            R::ShowMessageText(g_Screen, kSoundError, kDeleteFailedText, &none);
            return;
        }
        g_Selected = std::clamp(g_Selected, 0, TileCount() - 1);
        g_TopRow = g_Selected / kColumns;
        Build();
    }

    void AskDelete(int tile)
    {
        if (IsNewTile(tile))
        {
            R::PlayUISound(kSoundError);
            return;
        }
        const int slot = tile + 1;
        const bool exists = SaveSlots::Read(slot).Exists;
        g_PendingDelete = slot;
        g_DeleteText = exists ? std::format(L"Delete \"{}\"?", SaveSlots::NameOf(slot))
                              : std::format(L"Remove the empty slot \"{}\"?", SaveSlots::NameOf(slot));
        std::function<void()> onYes = &OnDeleteConfirmed;   // fits the small buffer, the game takes it over
        R::ShowYesNo(g_Screen, -1, g_DeleteText.c_str(), &onYes);
    }

    void AddSlot()
    {
        const int slot = SaveSlots::Add();
        if (slot <= 0)
        {
            R::PlayUISound(kSoundError);
            return;
        }
        Continue(slot, true);   // straight into the new profile
    }

    // ---- Change Picture

    SlotView* SelectedView()
    {
        for (SlotView& view : g_Views)
            if (view.Tile == g_Selected)
                return &view;
        return nullptr;
    }

    bool ShowPortrait(const std::string& key)
    {
        SlotView* view = SelectedView();
        if (!view)
            return false;
        const R::SharedNode* root = R::ScreenRoot(g_Screen);
        R::Dfx::RemoveImage(root, view->Portrait);
        view->Portrait = AddPortrait(root, key + "_neutral", 11, view->X + (kFrameWidth - kPortraitBox) / 2, kFrameTop + kPortraitTop, kPortraitBox);
        return view->Portrait.Node != nullptr;
    }

    void HidePickControls()
    {
        const R::SharedNode* root = R::ScreenRoot(g_Screen);
        R::Dfx::RemoveImage(root, g_ArrowLeft);
        R::Dfx::RemoveImage(root, g_ArrowRight);
        R::Dfx::RemoveImage(root, g_PickTitle);
    }

    // Arrows either side of the portrait (the left one flipped for the right, as ScreenHelpVideo's selectors) and a title over the frame.
    void ShowPickControls()
    {
        SlotView* view = SelectedView();
        if (!view)
            return;
        const R::SharedNode* root = R::ScreenRoot(g_Screen);
        if (g_ArrowW == 0)
        {
            R::SharedNode probe{};
            R::Dfx::MakeLayerAnimoo(&probe);
            int w = 0, h = 0;
            if (probe.Node)
            {
                R::Dfx::LayerSetResource(probe.Node, "pdui/doshared");
                R::Dfx::LayerSelectByName(probe.Node, "arrow_left1");
                if (R::Dfx::LayerSpriteSize(probe.Node, w, h))
                    g_ArrowW = static_cast<float>(w), g_ArrowH = static_cast<float>(h);
                R::ReleaseRef(probe);
            }
        }
        HidePickControls();
        g_ArrowY = kFrameTop + kPortraitTop + (kPortraitBox - g_ArrowH) / 2;
        g_ArrowLeftX = view->X + 4;
        g_ArrowRightX = view->X + kFrameWidth - 4 - g_ArrowW;
        if (g_ArrowW > 0)
        {
            g_ArrowLeft = R::Dfx::AddImage(root, "pdui/doshared", "arrow_left1", 13, g_ArrowLeftX, g_ArrowY);
            g_ArrowRight = R::Dfx::AddImage(root, "pdui/doshared", "arrow_left1", 13, g_ArrowRightX, g_ArrowY, -g_ArrowW, g_ArrowH);
        }
        g_PickTitle = R::Dfx::AddText(root, kPickTitleText, 13, view->X - 40, kFrameTop - 34, kFrameWidth + 80, 23, R::Dfx::TextAlign::Centre);
    }

    // -1 / 1 when the mouse is on the left / right arrow, else 0.
    int ArrowUnderMouse()
    {
        if (g_ArrowW <= 0)
            return 0;
        const int64_t mouse = R::Input::MousePosition();
        const float x = static_cast<float>(static_cast<int32_t>(mouse));
        const float y = static_cast<float>(static_cast<int32_t>(mouse >> 32));
        if (y < g_ArrowY - 10 || y > g_ArrowY + g_ArrowH + 10)
            return 0;
        if (x >= g_ArrowLeftX - 10 && x < g_ArrowLeftX + g_ArrowW + 10) return -1;
        if (x >= g_ArrowRightX - 10 && x < g_ArrowRightX + g_ArrowW + 10) return 1;
        return 0;
    }

    void StartPicking()
    {
        if (IsNewTile(g_Selected))
        {
            R::PlayUISound(kSoundError);
            return;
        }
        g_PickKeys = SaveSlots::AvatarKeys();
        g_PickOriginal = SaveSlots::AvatarKeyOf(g_Selected + 1);
        const auto at = std::find(g_PickKeys.begin(), g_PickKeys.end(), g_PickOriginal);
        g_PickIndex = at == g_PickKeys.end() ? 0 : static_cast<int>(at - g_PickKeys.begin());
        g_Picking = true;
        g_PickCooldown = kPickDelay;   // the Y press that opened it doesn't also step
        R::PlayUISound(kSoundConfirm);
        ShowPickControls();
        ShowHelpBar();
    }

    // Steps to the next portrait that the sheet has (some characters have none).
    void StepPicture(int step)
    {
        const int count = static_cast<int>(g_PickKeys.size());
        g_PickCooldown = kPickDelay;
        R::PlayUISound(kSoundStep);
        for (int tries = 0; tries < count; ++tries)
        {
            g_PickIndex = (g_PickIndex + step + count) % count;
            if (ShowPortrait(g_PickKeys[g_PickIndex]))
                return;
        }
    }

    void StopPicking(bool keep)
    {
        if (keep)
            SaveSlots::SetAvatar(g_Selected + 1, g_PickKeys[g_PickIndex]);
        else
            ShowPortrait(g_PickOriginal);
        R::PlayUISound(keep ? kSoundConfirm : kSoundCancel);
        HidePickControls();
        g_Picking = false;
        ShowHelpBar();
    }

    int TileUnderMouse()
    {
        const int64_t mouse = R::Input::MousePosition();
        const float x = static_cast<float>(static_cast<int32_t>(mouse));
        const float y = static_cast<float>(static_cast<int32_t>(mouse >> 32));
        for (const SlotView& view : g_Views)
            if (x >= view.X && x < view.X + kFrameWidth && y >= kFrameTop && y < kDetailsTop + kDetailLines * kDetailsLine)
                return view.Tile;
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
        g_Selected = std::clamp(SaveSlots::LastSlot() - 1, 0, TileCount() - 1);
        g_TopRow = g_Selected / kColumns;
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

        // the wheel scrolls a row (the masks ScreenSelectTutorial feeds its list)
        pressed |= R::DeckSelectList::TabButtons(R::InputState) & static_cast<int>(kWheelUp | kWheelDown);

        if (g_Picking)
        {
            pressed |= static_cast<int>(R::InputCancelPressed(R::InputState, kCancel));
            if (R::Input::MouseActive())
            {
                const int arrow = ArrowUnderMouse();
                if (arrow != 0 && R::Input::TakeMouseClick(R::InputState, kConfirm))
                {
                    g_PickCooldown = 0;   // a click always steps
                    pressed |= arrow < 0 ? kLeft : kRight;
                }
                else if (arrow == 0 && TileUnderMouse() == g_Selected && R::Input::TakeMouseClick(R::InputState, kConfirm))
                    pressed |= kConfirm;
            }
            if (g_PickCooldown > 0)
                --g_PickCooldown;
            else if (pressed & (kLeft | kUp | kWheelUp)) StepPicture(-1);
            else if (pressed & (kRight | kDown | kWheelDown)) StepPicture(1);
            if (pressed & kConfirm) StopPicking(true);
            else if (pressed & kCancel) StopPicking(false);
            return;
        }

        int selected = g_Selected;
        if (R::Input::MouseActive())
        {
            const int hover = TileUnderMouse();
            if (hover >= 0)
            {
                selected = hover;
                if (R::Input::TakeMouseClick(R::InputState, kConfirm))
                    pressed |= kConfirm;
            }
        }
        if (pressed & kLeft) selected -= 1;
        if (pressed & kRight) selected += 1;
        if (pressed & (kUp | kWheelUp)) selected -= kColumns;
        if (pressed & (kDown | kWheelDown)) selected += kColumns;

        if (selected != g_Selected && selected >= 0 && selected < TileCount() + kColumns - 1)
            SelectTile(selected);   // clamps: down from the row above the last lands on the last tile

        if (pressed & kDelete)
            AskDelete(g_Selected);
        else if (pressed & kPicture)
            StartPicking();
        else if (pressed & kConfirm)
        {
            if (IsNewTile(g_Selected))
                AddSlot();
            else
                Choose(g_Selected + 1);   // no Back: the title just sends you here again, so cancel bounced between the two
        }
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
        if (record && record[0] == kScreenTitle && (record[2] == kScreenSignIn || g_BackFromGame))
        {
            g_BackFromGame = false;   // backing out of the game lands here, not on the title
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

        // Back on the main menu goes to the title (the game saves first); from there this screen takes over, so the player picks a slot.
        if (int* record = MainRecord(ui))
        {
            if (record[0] != g_LastScreen)
            {
                if (record[0] == kScreenTitle && g_LastScreen == kScreenMainMenu)
                    g_BackFromGame = true;
                g_LastScreen = record[0];
            }
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
