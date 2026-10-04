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
#include "Prices.h"

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
    constexpr int kFullSet = 3;             // copies the save holds at most
    constexpr int kSoundError = 71, kSoundBought = 52;

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

    // 1000 -> "1,000"
    std::wstring Dp(uint64_t value)
    {
        std::wstring text = std::to_wstring(value);
        for (int i = static_cast<int>(text.size()) - 3; i > 0; i -= 3)
            text.insert(static_cast<size_t>(i), L",");
        return text;
    }

    std::wstring WideName(uint16_t id)
    {
        const wchar_t* name = reinterpret_cast<const wchar_t*>(YGO::CARDS::Get_CardNameFromKonamiId(static_cast<short>(id)));
        return name && *name ? name : std::to_wstring(id);
    }

    // The game's message box with a real OK button (it highlights under the mouse; an empty function would give a prompt-only box). The
    // text is kept here while the box is open.
    std::wstring g_Message;
    void ShowMessage(void* screen, int sound, std::wstring text)
    {
        g_Message = std::move(text);
        std::function<void()> ok = [] {};   // the game moves it into the button; closing the box is the game's job
        if (screen)
            W::ShowMessageText(screen, sound, g_Message.c_str(), &ok);
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
    void* g_ShopScreen = nullptr;
    uint16_t g_BuyCard = 0xFFFF;        // the card the Yes / No box is asking about

    // [Yu-Gi-Oh-BetterCardShop] BetterShop-ShowOwnedCards=1 also lists the cards the profile has a full set of (they can't be bought).
    bool ShowOwnedCards()
    {
        return GetPrivateProfileIntA("Yu-Gi-Oh-BetterCardShop", "BetterShop-ShowOwnedCards", 0, ".\\Config.ini") != 0;
    }

    // The number on each card is the grid's own copy of the counts, which the game fills through a function that answers 0 for ids above
    // 14968 and only when the list is rebuilt. So the save's counts are written into that copy and into the cells on screen directly.
    void RefreshOwnedCounts(char* trunk)
    {
        const uint8_t* owned = SaveCardTable();
        auto ownedOf = [owned](uint16_t id) { return owned && id < kSaveCardCount ? owned[id] & 7 : 0; };

        auto* counts = reinterpret_cast<W::Vector*>(trunk + W::Trunk::GridCounts);
        for (auto* count = static_cast<W::Trunk::GridCount*>(counts->Begin); count && count < counts->End; ++count)
            count->Owned = ownedOf(count->CardId);

        auto* cells = reinterpret_cast<W::Vector*>(trunk + W::Trunk::GridCells);
        for (char* cell = static_cast<char*>(cells->Begin); cell && cell < cells->End; cell += W::Trunk::CellSize)
        {
            const uint16_t id = *reinterpret_cast<uint16_t*>(cell + W::Trunk::CellCardId);
            if (id != 0xFFFF)
                W::Trunk::CellSetOwned(cell, ownedOf(id));
        }
    }

    // Every card, the way TrunkView_BuildCardList lists the owned ones (internal ids 1 up to the bound the game currently uses, no tokens),
    // with the copies the profile owns as the count. Full sets are left out unless BetterShop-ShowOwnedCards is on.
    void FillAllCards(char* trunk)
    {
        const bool showFullSets = ShowOwnedCards();
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
            if (entry.Count >= kFullSet && !showFullSets)
                continue;
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
        RefreshOwnedCounts(trunk);
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
        g_ShopScreen = screen;
        Prices::Reset();   // the packs (and so the rarities) may have changed since last time
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

    // The copies shown for a card in the list, changed in place so the list keeps its scroll and selection.
    void SetListedCount(uint16_t id, uint32_t count)
    {
        char* trunk = static_cast<char*>(g_Trunk);
        auto* index = reinterpret_cast<W::Vector*>(trunk + W::Trunk::IdToEntry);
        auto* entries = reinterpret_cast<W::Vector*>(trunk + W::Trunk::Entries);
        const size_t slots = (static_cast<char*>(index->End) - static_cast<char*>(index->Begin)) / sizeof(int);
        const size_t slot = static_cast<size_t>(id) - W::Trunk::FirstCardId;
        if (id < W::Trunk::FirstCardId || slot >= slots)
            return;
        const int entry = static_cast<int*>(index->Begin)[slot];
        const size_t listed = (static_cast<char*>(entries->End) - static_cast<char*>(entries->Begin)) / sizeof(W::Trunk::Entry);
        if (entry < 0 || static_cast<size_t>(entry) >= listed)
            return;
        static_cast<W::Trunk::Entry*>(entries->Begin)[entry].Count = count;
        RefreshOwnedCounts(trunk);   // the number on the card goes up now, not when the list is next rebuilt
        W::Trunk::Refresh(g_Trunk);
    }

    // Yes in the buy box: one copy for the card's price (checked again, the box may have been open a while).
    void OnBuyConfirmed()
    {
        const uint16_t id = g_BuyCard;
        uint8_t* table = SaveCardTable();
        uint64_t* wallet = Wallet();
        if (id == 0xFFFF || !table || !wallet || id >= kSaveCardCount)
            return;
        const uint64_t price = Prices::CardPrice(id);
        const int owned = table[id] & 7;
        if (owned >= kFullSet || *wallet < price)
            return;

        *wallet -= price;
        table[id] = static_cast<uint8_t>((table[id] & ~7) | (owned + 1) | 8);   // one more copy, and marked as seen
        SetListedCount(id, static_cast<uint32_t>(owned + 1));
        W::PlayUISound(kSoundBought);
        YGO::Log("Card Shop: bought " + CardName(id) + " (" + std::to_string(id) + ") for " + std::to_string(price) + " DP, now " +
                 std::to_string(owned + 1) + " copies", MODULE_NAME, 0);
    }

    // A card was picked: say why it can't be bought, or ask.
    void OfferCard(uint16_t id)
    {
        uint8_t* table = SaveCardTable();
        uint64_t* wallet = Wallet();
        if (id == 0xFFFF || !table || !wallet || id >= kSaveCardCount)
        {
            ShowMessage(g_ShopScreen, kSoundError, L"No profile is loaded, so cards can not be bought.");
            return;
        }

        const std::wstring name = WideName(id);
        const uint64_t price = Prices::CardPrice(id);
        const int owned = table[id] & 7;
        if (owned >= kFullSet)
        {
            ShowMessage(g_ShopScreen, -1, L"You already have 3 copies of " + name + L".");
            return;
        }
        if (*wallet < price)
        {
            ShowMessage(g_ShopScreen, kSoundError, name + L" costs " + Dp(price) + L" DP. You have " + Dp(*wallet) + L" DP.");
            return;
        }

        g_BuyCard = id;
        g_Message = L"Buy " + name + L" for " + Dp(price) + L" DP? You have " + Dp(*wallet) + L" DP and " + std::to_wstring(owned) +
                    (owned == 1 ? L" copy." : L" copies.");
        std::function<void()> onYes = &OnBuyConfirmed;   // a plain function: fits the small buffer, the game takes it over
        W::ShowYesNo(g_ShopScreen, -1, g_Message.c_str(), &onYes);
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
        RefreshOwnedCounts(static_cast<char*>(g_Trunk));   // sorting / filtering rebuilds the grid's counts from the game's (custom ids = 0)

        const uint16_t selected = W::Trunk::GetSelectedCardId(g_Trunk);
        if (selected != g_ShownCard)
        {
            g_ShownCard = selected;
            W::CardInfo::SetCard(g_CardInfo, selected);
        }
        W::CardInfo::Update(g_CardInfo, seconds, held, 0);

        // A card was confirmed (clicked, or confirm on the highlighted one).
        if (action == 0 && kind == 3)
        {
            W::PlayUISound(39);
            OfferCard(selected);
        }
    }

    // ---------------------------------------------------------------- Enter Password: eight digit wheels

    // The digits and the Unlock button in the middle; the card details panel on the left (where the Card Shop has it), shown only while
    // the unlock message is up.
    constexpr float kInfoX = 260.0f;
    constexpr float kInfoY = 120.0f;
    constexpr float kDigitsX = 960.0f;      // centre of the eight digits, and of the Unlock button
    constexpr float kDigitSpacing = 170.0f;

    void* g_Digits[kDigits] = {};
    void* g_PasswordInfo = nullptr;     // the unlocked card's details
    bool g_InfoUntilClosed = false;     // hide the details when the message box closes (the page's frame only runs again then)
    int g_Selected = 0;
    void* g_PasswordScreen = nullptr;   // for the message box

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
        // z above the digits (20): the leftmost digits sit under the panel, and the unlocked card must cover them while it is shown.
        W::CardInfo::CreateFromLayout(g_PasswordInfo, &parent, 30, W::ScreenOwner(screen), kInfoX, kInfoY);
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
        Prices::Reset();   // prices.json may have changed
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

    // Unlock: the card whose password this is (bin\CARD_Pass.bin, see YuGiOh-PASS.h) costs the password price (prices.json's "password" for it, else
    // BetterShop-PasswordCost, 1,000 DP by default), is topped up to a full set (three copies) in the profile's card table and is shown in the details
    // panel while the message is up.
    void __cdecl OnUnlock(int, void*)
    {
        const uint32_t password = EnteredPassword();
        const uint16_t id = YGO::CARDS::FindCardByPassword(password);
        const std::string digits = std::to_string(password);
        if (id == 0)
        {
            YGO::Log("Enter Password: " + digits + " is not a card's password", MODULE_NAME, 0);
            ShowMessage(g_PasswordScreen, kSoundError, L"This password is not for a card.");
            return;
        }

        const std::wstring cardName = WideName(id);
        const uint64_t cost = Prices::PasswordCost(id);
        uint8_t* table = SaveCardTable();
        uint64_t* wallet = Wallet();
        if (!table || !wallet || id >= kSaveCardCount)
        {
            ShowMessage(g_PasswordScreen, kSoundError, L"No profile is loaded, so the card can not be unlocked.");
            return;
        }

        if ((table[id] & 7) >= kFullSet)
        {
            ShowMessage(g_PasswordScreen, -1, L"You already have 3 copies of " + cardName + L".");
            return;
        }

        if (*wallet < cost)
        {
            ShowMessage(g_PasswordScreen, kSoundError, L"Unlocking a card costs " + Dp(cost) + L" DP. You have " + Dp(*wallet) + L" DP.");
            return;
        }

        *wallet -= cost;
        table[id] = static_cast<uint8_t>((table[id] & ~7) | kFullSet | 8);   // three copies, and marked as seen
        ShowPasswordCard(id);   // only for a card that was just unlocked
        YGO::Log("Enter Password: " + digits + " unlocked " + CardName(id) + " (" + std::to_string(id) + ") x3 for " + std::to_string(cost) + " DP", MODULE_NAME, 0);
        ShowMessage(g_PasswordScreen, kSoundBought, cardName + L" is unlocked");
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
        store.Buttons[1] = { L"Card Shop", L"Buy the single cards you want, one copy at a time.", &OnCardShop, nullptr };
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
