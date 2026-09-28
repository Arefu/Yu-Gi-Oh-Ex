#include "StorePages.h"

#include <Windows.h>
#include <malloc.h>
#include <cstdint>
#include <cstring>
#include <functional>
#include <string>

#include "../../Dependencies/Yu-Gi-Oh-Ex/Yu-Gi-Oh-Log.h"
#include "../../Dependencies/Yu-Gi-Oh-Ex/Yu-Gi-Oh-RIX.h"
#include "../../Dependencies/Yu-Gi-Oh-Ex/YuGiOh/YuGiOh-CARDS.h"
#include "../../Dependencies/Yu-Gi-Oh-Ex/YuGiOh/YuGiOh-PASS.h"
#include "../../Dependencies/Yu-Gi-Oh-Ex/YuGiOh/YuGiOh-RIX.h"
#include "../../Dependencies/Yu-Gi-Oh-Ex/YuGiOh/YuGiOh-SAVE.h"

// The game's own widgets, built on the Battle Pack screen the pages live on (see "widgets" in YuGiOh-RIX.h). They are made the first time a
// page shows, kept for the rest of the game (the screen object lives as long as the game does) and shown / hidden with their page.
namespace
{
    namespace W = YGO::RIX;

    constexpr const char* MODULE_NAME = "Yu-Gi-Oh-BetterCardShop";
    constexpr int kSaveCardCount = 20000;   // the save's card table (one byte per card id, bits 0-2 = copies owned)
    constexpr int kTokenKind = 10;          // CARD_PROPS::Kind of a token; the trunk leaves them out
    constexpr int kDigits = 8;              // a card password
    constexpr int kUp = 1, kDown = 2, kLeft = 4, kRight = 8, kConfirm = 0x1000;
    constexpr uint64_t kUnlockCost = 1000;  // DP for unlocking a card with its password (whatever the card)

    // Memory for a game widget: zeroed (some fields are only read, never set by the constructors) and aligned like the game's own.
    void* NewObject(size_t size)
    {
        void* memory = _aligned_malloc(size, 16);
        if (memory)
            std::memset(memory, 0, size);
        return memory;
    }

    uint8_t* SaveCardTable()
    {
        __try
        {
            return YGO::SAVE::Get_CardUnlockTable(YGO::SAVE::CURRENT_PROFILE);
        }
        __except (EXCEPTION_EXECUTE_HANDLER)
        {
            return nullptr;
        }
    }

    // The profile's DP. Like the card table, only there once a profile is loaded, so it is read guarded.
    uint64_t* Wallet()
    {
        __try
        {
            uint8_t* section = YGO::SAVE::Get_PlayerSection(YGO::SAVE::CURRENT_PROFILE);
            return section ? reinterpret_cast<uint64_t*>(section + YGO::SAVE::PlayerSection::Wallet) : nullptr;
        }
        __except (EXCEPTION_EXECUTE_HANDLER)
        {
            return nullptr;
        }
    }

    std::string CardName(uint16_t id)
    {
        const wchar_t* name = reinterpret_cast<const wchar_t*>(YGO::CARDS::Get_CardNameFromKonamiId(static_cast<short>(id)));
        if (!name)
            return std::to_string(id);
        int size = WideCharToMultiByte(CP_UTF8, 0, name, -1, nullptr, 0, nullptr, nullptr);
        std::string text(size > 1 ? size - 1 : 0, '\0');
        if (size > 1)
            WideCharToMultiByte(CP_UTF8, 0, name, -1, text.data(), size, nullptr, nullptr);
        return text;
    }

    // ---------------------------------------------------------------- Card Shop: the trunk

    void* g_Trunk = nullptr;
    void* g_DeckState = nullptr;
    void* g_CardInfo = nullptr;
    uint16_t g_ShownCard = 0xFFFF;

    // Every card, the way TrunkView_BuildCardList lists the owned ones (internal ids 1 up to the bound the game currently uses, no tokens),
    // with the copies the profile owns as the count.
    void FillAllCards(char* trunk)
    {
        W::Trunk::GridReset(trunk + W::Trunk::Grid, 0);
        W::Trunk::GridSetMode(trunk + W::Trunk::Grid, 10);

        auto* index = reinterpret_cast<W::Vector*>(trunk + W::Trunk::IdToEntry);
        auto* entries = reinterpret_cast<W::Vector*>(trunk + W::Trunk::Entries);
        const size_t indexSize = W::Trunk::IdToEntrySize();
        const int none = -1;
        index->End = index->Begin;
        W::Trunk::IntVectorResizeFill(index, indexSize, &none);
        entries->End = entries->Begin;

        // The internal id goes in ecx whole (Yu-Gi-Oh-Cards' ids go past 32767), so this is called through an unsigned prototype.
        auto propsOf = reinterpret_cast<YGO::CARDS::CARD_PROPS*(__fastcall*)(unsigned int)>(YGO::CARDS::Get_CardPropsFromInternalId);
        const uint8_t* owned = SaveCardTable();
        const uint32_t limit = W::Trunk::InternalIdLimit();
        int* ids = static_cast<int*>(index->Begin);
        for (uint32_t internal = 1; internal < limit; ++internal)
        {
            const YGO::CARDS::CARD_PROPS* props = propsOf(internal);
            if (!props)
                continue;
            const uint16_t id = static_cast<uint16_t>(props->KonamiID);
            const size_t slot = static_cast<size_t>(id) - W::Trunk::FirstCardId;
            if (id == 0 || props->Kind == kTokenKind || id < W::Trunk::FirstCardId || slot >= indexSize)
                continue;

            W::Trunk::Entry entry{};
            entry.CardId = id;
            entry.Count = owned && id < kSaveCardCount ? (owned[id] & 7) : 0;
            ids[slot] = static_cast<int>((static_cast<char*>(entries->End) - static_cast<char*>(entries->Begin)) / sizeof(W::Trunk::Entry));
            if (entries->End == entries->Capacity)
                W::Trunk::EntryVectorEmplace(entries, entries->End, &entry);
            else
            {
                *static_cast<W::Trunk::Entry*>(entries->End) = entry;
                entries->End = static_cast<char*>(entries->End) + sizeof(W::Trunk::Entry);
            }
        }

        W::Trunk::ApplyFilter(trunk);
        W::Trunk::SortAndFillGrid(trunk);
        W::Trunk::Refresh(trunk);

        const size_t count = (static_cast<char*>(entries->End) - static_cast<char*>(entries->Begin)) / sizeof(W::Trunk::Entry);
        YGO::Log("Card Shop lists " + std::to_string(count) + " cards (internal ids below " + std::to_string(limit) + ")", MODULE_NAME, 0);
    }

    // Same places as in the deck editor: the trunk at 528.5 / 120, the card details to its left.
    bool BuildShop(void* screen)
    {
        g_Trunk = NewObject(W::Trunk::Size);
        g_DeckState = NewObject(W::DeckState::Size);
        g_CardInfo = NewObject(W::CardInfo::Size);
        if (!g_Trunk || !g_DeckState || !g_CardInfo)
            return false;

        W::Trunk::Construct(g_Trunk);
        W::DeckState::Construct(g_DeckState);
        W::CardInfo::Construct(g_CardInfo);

        // Each create call takes over (and releases) its parent reference, so each gets a counted copy of the screen's root.
        W::SharedNode parent = W::ParentRef(W::ScreenRoot(screen));
        W::Trunk::CreateFromLayout(g_Trunk, &parent, 15, W::ScreenOwner(screen), 528.5f, 120.0f);
        W::Trunk::SetDeckInfo(g_Trunk, g_DeckState);
        parent = W::ParentRef(W::ScreenRoot(screen));
        W::CardInfo::CreateFromLayout(g_CardInfo, &parent, 12, W::ScreenOwner(screen), 260.0f, 120.0f);
        W::CardInfo::SetWidth(g_CardInfo, 486.5f);
        YGO::Log("Card Shop widgets built", MODULE_NAME, 0);
        return true;
    }

    void __cdecl ShopShow(void* screen, void*)
    {
        if (!g_Trunk && !BuildShop(screen))
        {
            YGO::Log("Card Shop: out of memory for the widgets", MODULE_NAME, 2);
            return;
        }

        FillAllCards(static_cast<char*>(g_Trunk));   // again each time, so the counts follow what was bought
        W::WidgetSetVisible(g_Trunk, true);
        W::WidgetSetFocused(g_Trunk, true);
        W::WidgetSetVisible(g_CardInfo, true);
        g_ShownCard = W::Trunk::GetSelectedCardId(g_Trunk);
        W::CardInfo::SetCard(g_CardInfo, g_ShownCard);
    }

    void __cdecl ShopHide(void*, void*)
    {
        if (!g_Trunk)
            return;
        W::WidgetSetFocused(g_Trunk, false);
        W::WidgetSetVisible(g_Trunk, false);
        W::WidgetSetVisible(g_CardInfo, false);
    }

    void __cdecl ShopFrame(void*, int pressed, int held, float seconds, void*)
    {
        if (!g_Trunk)
            return;

        int action = -1;
        int kind = 0;
        char flag = 0;
        W::Trunk::ProcessInput(g_Trunk, pressed, held, &action, &kind, &flag);
        W::Trunk::Update(g_Trunk);

        const uint16_t selected = W::Trunk::GetSelectedCardId(g_Trunk);
        if (selected != g_ShownCard)
        {
            g_ShownCard = selected;
            W::CardInfo::SetCard(g_CardInfo, selected);
        }
        W::CardInfo::Update(g_CardInfo, seconds, held, 0);

        // A card was confirmed (clicked, or confirm on the highlighted one). Buying comes later.
        if (action == 0 && kind == 3)
        {
            W::PlayUISound(39);
            YGO::Log("Card Shop: picked " + CardName(selected) + " (" + std::to_string(selected) + ")", MODULE_NAME, 0);
        }
    }

    // ---------------------------------------------------------------- Enter Password: eight digit wheels

    // The digits and the Unlock button in the middle; the card details panel on the left (where the Card Shop has it), shown only while
    // the unlock message is up.
    constexpr float kInfoX = 260.0f;
    constexpr float kInfoY = 120.0f;
    constexpr float kDigitsX = 960.0f;      // centre of the eight digits, and of the Unlock button
    constexpr float kDigitSpacing = 170.0f;
    constexpr int kFullSet = 3;             // a password gives a full set of the card

    void* g_Digits[kDigits] = {};
    void* g_PasswordInfo = nullptr;     // the unlocked card's details
    bool g_InfoUntilClosed = false;     // hide the details when the message box closes (the page's frame only runs again then)
    int g_Selected = 0;
    void* g_PasswordScreen = nullptr;   // for the message box
    std::wstring g_Message;             // the message box shows it from here while it is open

    int DigitValue(int i)
    {
        return *reinterpret_cast<int*>(static_cast<char*>(g_Digits[i]) + W::EntryDigit::Value);
    }

    void Select(int i)
    {
        g_Selected = i;
        for (int d = 0; d < kDigits; ++d)
            W::EntryDigit::SetSelected(g_Digits[d], d == i);
    }

    // The player match code entry spaces its four digits 270 apart; eight need to be closer to fit next to the card details.
    bool BuildPassword(void* screen)
    {
        for (int i = 0; i < kDigits; ++i)
        {
            g_Digits[i] = NewObject(W::EntryDigit::Size);
            if (!g_Digits[i])
                return false;
            W::EntryDigit::Construct(g_Digits[i]);
            const float x = kDigitsX + (i - (kDigits - 1) / 2.0f) * kDigitSpacing;
            W::SharedNode parent = W::ParentRef(W::ScreenRoot(screen));   // taken over by the call
            W::EntryDigit::CreateFromLayout(g_Digits[i], &parent, 20, W::ScreenOwner(screen), x, 540.0f, W::ScreenData(screen));
            W::EntryDigit::SetValue(g_Digits[i], 0);
        }

        g_PasswordInfo = NewObject(W::CardInfo::Size);
        if (!g_PasswordInfo)
            return false;
        W::CardInfo::Construct(g_PasswordInfo);
        W::SharedNode parent = W::ParentRef(W::ScreenRoot(screen));
        W::CardInfo::CreateFromLayout(g_PasswordInfo, &parent, 12, W::ScreenOwner(screen), kInfoX, kInfoY);
        W::CardInfo::SetWidth(g_PasswordInfo, 486.5f);
        YGO::Log("Password widgets built", MODULE_NAME, 0);
        return true;
    }

    // The card details panel shows the card a password was entered for while its message is open; 0xFFFF hides it.
    void ShowPasswordCard(uint16_t id)
    {
        if (!g_PasswordInfo)
            return;
        W::CardInfo::SetCard(g_PasswordInfo, id);
        W::WidgetSetVisible(g_PasswordInfo, id != 0xFFFF);
        g_InfoUntilClosed = id != 0xFFFF;
    }

    void __cdecl PasswordShow(void* screen, void*)
    {
        g_PasswordScreen = screen;
        if (!g_Digits[0] && !BuildPassword(screen))
        {
            YGO::Log("Enter Password: out of memory for the widgets", MODULE_NAME, 2);
            return;
        }
        for (int i = 0; i < kDigits; ++i)
        {
            W::WidgetSetVisible(g_Digits[i], true);
            W::EntryDigit::SetValue(g_Digits[i], 0);
        }
        Select(0);
        ShowPasswordCard(0xFFFF);
    }

    void __cdecl PasswordHide(void*, void*)
    {
        for (void* digit : g_Digits)
        {
            if (digit)
                W::WidgetSetVisible(digit, false);
        }
        if (g_PasswordInfo)
            W::WidgetSetVisible(g_PasswordInfo, false);
    }

    void Change(int i, int by)
    {
        W::PlayUISound(16);
        W::EntryDigit::SetValue(g_Digits[i], DigitValue(i) + by + 10);   // SetValue keeps value % 10
    }

    void Move(int to)
    {
        if (to < 0 || to >= kDigits)
            return;
        W::PlayUISound(37);
        Select(to);
    }

    uint32_t EnteredPassword()
    {
        uint32_t password = 0;
        for (int i = 0; i < kDigits; ++i)
            password = password * 10 + static_cast<uint32_t>(DigitValue(i));
        return password;
    }

    // The game's message box with a real OK button (it highlights under the mouse; an empty function would give a prompt-only box).
    void ShowMessage(int sound, std::wstring text)
    {
        g_Message = std::move(text);
        std::function<void()> ok = [] {};   // the game moves it into the button; closing the box is the game's job
        if (g_PasswordScreen)
            W::ShowMessageText(g_PasswordScreen, sound, g_Message.c_str(), &ok);
    }

    // Unlock: the card whose password this is (bin\CARD_Pass.bin, see YuGiOh-PASS.h) costs kUnlockCost DP, is topped up to a full set (three
    // copies) in the profile's card table and is shown in the details panel while the message is up.
    void __cdecl OnUnlock(int, void*)
    {
        const uint32_t password = EnteredPassword();
        const uint16_t id = YGO::CARDS::FindCardByPassword(password);
        const std::string digits = std::to_string(password);
        if (id == 0)
        {
            YGO::Log("Enter Password: " + digits + " is not a card's password", MODULE_NAME, 0);
            ShowMessage(71, L"This password is not for a card.");
            return;
        }

        const wchar_t* name = reinterpret_cast<const wchar_t*>(YGO::CARDS::Get_CardNameFromKonamiId(static_cast<short>(id)));
        const std::wstring cardName = name && *name ? name : std::to_wstring(id);
        uint8_t* table = SaveCardTable();
        uint64_t* wallet = Wallet();
        if (!table || !wallet || id >= kSaveCardCount)
        {
            ShowMessage(71, L"No profile is loaded, so the card can not be unlocked.");
            return;
        }

        if ((table[id] & 7) >= kFullSet)
        {
            ShowMessage(-1, L"You already have 3 copies of " + cardName + L".");
            return;
        }

        if (*wallet < kUnlockCost)
        {
            ShowMessage(71, L"Unlocking a card costs 1,000 DP. You have " + std::to_wstring(*wallet) + L" DP.");
            return;
        }

        *wallet -= kUnlockCost;
        table[id] = static_cast<uint8_t>((table[id] & ~7) | kFullSet | 8);   // three copies, and marked as seen
        ShowPasswordCard(id);   // only for a card that was just unlocked
        YGO::Log("Enter Password: " + digits + " unlocked " + CardName(id) + " (" + std::to_string(id) + ") x3 for 1000 DP", MODULE_NAME, 0);
        ShowMessage(52, cardName + L" is unlocked");
    }

    // Like widget_EntryCode::ProcessInput, for eight digits: up / down change the digit, left / right move. The mouse only acts on a click
    // (the game's own code entry also selects on hover; that was not wanted): a click on a digit selects it, on an arrow it changes it too.
    // Confirm and Back belong to the Unlock button under the digits (RIX runs it after this).
    void __cdecl PasswordFrame(void*, int pressed, int held, float seconds, void*)
    {
        if (!g_Digits[0])
            return;

        // ScreenBase::Tick sends the input to the message box and skips the page while it is open, so running again means OK was pressed.
        if (g_InfoUntilClosed)
            ShowPasswordCard(0xFFFF);
        if (g_PasswordInfo)
            W::CardInfo::Update(g_PasswordInfo, seconds, held, 0);

        if (W::Input::MouseClickPending())
        {
            const int64_t mouse = W::Input::MousePosition();
            for (int i = 0; i < kDigits; ++i)
            {
                const int hit = W::EntryDigit::HitTest(g_Digits[i], mouse);
                if (hit < 0)
                    continue;
                W::Input::TakeMouseClick(W::InputState, kConfirm);   // ours: the Unlock button must not see it
                if (i != g_Selected)
                    Move(i);
                if (hit == 0)
                    Change(i, 1);
                else if (hit == 1)
                    Change(i, -1);
                return;
            }
        }

        if (pressed & kUp)
            Change(g_Selected, 1);
        else if (pressed & kDown)
            Change(g_Selected, -1);
        else if (pressed & kLeft)
            Move(g_Selected - 1);
        else if (pressed & kRight)
            Move(g_Selected + 1);
    }

    // ---------------------------------------------------------------- the pages

    RIX_PageDesc CustomPage(const wchar_t* header, RIX_PageCallback show, RIX_PageCallback hide, RIX_PageFrameCallback frame)
    {
        RIX_PageDesc page{};
        page.Size = sizeof(page);
        page.Header = header;
        page.ButtonCount = 0;
        page.OnShow = show;
        page.OnHide = hide;
        page.OnFrame = frame;
        return page;
    }

    void __cdecl OnBoosterPacks(int, void*)
    {
        RIX::Functions().GotoScreen(RIX_SCREEN_CARD_SHOP);   // the game's own card shop; its Back returns to the Card Store
    }

    void __cdecl OnCardShop(int, void*)
    {
        RIX_PageDesc page = CustomPage(L"Card Shop", &ShopShow, &ShopHide, &ShopFrame);
        if (!RIX::Functions().OpenPage(&page))
            YGO::Log("Could not open the Card Shop page", MODULE_NAME, 2);
    }

    void __cdecl OnEnterPassword(int, void*)
    {
        RIX_PageDesc page = CustomPage(L"Enter Password", &PasswordShow, &PasswordHide, &PasswordFrame);
        page.ButtonCount = 1;
        page.Buttons[0] = { L"Unlock", L"Unlock the card with this password.", &OnUnlock, nullptr };
        page.ButtonsY = 820.0f;   // under the digits (their lower arrows reach about 710)
        page.ButtonsX = kDigitsX;
        if (!RIX::Functions().OpenPage(&page))
            YGO::Log("Could not open the Enter Password page", MODULE_NAME, 2);
    }

    void __cdecl OnCardStore(int, void*)
    {
        RIX_PageDesc store{};
        store.Size = sizeof(store);
        store.Header = L"Card Store";
        store.ButtonCount = 3;
        store.Buttons[0] = { L"Booster Packs", L"Buy booster packs of random cards.", &OnBoosterPacks, nullptr };
        store.Buttons[1] = { L"Card Shop", L"Buy the single cards you want.", &OnCardShop, nullptr };
        store.Buttons[2] = { L"Enter Password", L"Enter a card's eight digit password.", &OnEnterPassword, nullptr };
        if (!RIX::Functions().OpenPage(&store))
            YGO::Log("Could not open the Card Store", MODULE_NAME, 2);
    }
}

namespace StorePages
{
    bool Install()
    {
        if (!RIX::Load())
            return false;
        RIX::Functions().SetMainMenuItemAction(RIX_ITEM_CARD_SHOP, &OnCardStore, nullptr);
        return true;
    }
}
