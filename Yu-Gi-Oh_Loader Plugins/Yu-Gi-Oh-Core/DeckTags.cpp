#include "DeckTags.h"

#include <Windows.h>
#include <cstring>
#include <cwctype>
#include <format>
#include <iterator>
#include <map>
#include <mutex>
#include <vector>

#include "Detours.h"
#include "Logger.h"
#include "Save.h"
#include "UserDecks.h"

namespace
{
    constexpr const char* kExtensionTag = "DeckTags";
    constexpr uint32_t kVersion = 2;             // 1 was { id, chars, text } for the text box of the first version
    constexpr unsigned kCurrentProfile = 0xFFFFFFFD;
    constexpr size_t kUsedOffset = 300;          // DeckListItem.IsValid: the slot holds a deck
    constexpr size_t kMaxTagChars = 4096;        // of one deck's text, read from a save or given by a plugin

    // ScreenDeckEditor
    constexpr size_t kEditorState = 656;
    constexpr size_t kEditorSlide = 2464;        // the tween that slides the editor's columns (sub_140839D80)
    constexpr float kSlidePanelLeft = 33.0f - 1568.5172f;   // the panel (x 1568.5172) where the card grid and the deck list sit (x 33)
    constexpr float kTagLineDrop = 12.0f;        // the Tags icons are taller than the source line's text: clear the heading
    constexpr size_t kEditorDeckId = 708;        // the deck being edited (id)
    constexpr size_t kEditorDeckZone = 6208;     // widget_UserDeckZone
    constexpr size_t kEditorPanel = 7408;        // widget_RecipeInfo (mode 1) beside the deck list
    constexpr size_t kEditorTrunk = 9984;        // widget_TrunkZone
    constexpr size_t kEditorIsNewDeck = 49016;   // set for a new or copied deck, 0 for a rename
    constexpr int kStateTrunk = 3, kStateFilterPanel = 6;
    constexpr int64_t kHeaderDeckList = 0x73;    // the state 0 header (ScreenDeckEditor::OnEnter)
    constexpr int kStatusOk = 3;                 // TextInputDialog_GetStatus
    constexpr int kSoundDecide = 39, kSoundUnable = 71;

    // widget_RecipeInfo: +N holder, +N+16 its text layer
    constexpr size_t kPanelBody = 48, kPanelHeading = 256, kPanelLine = 384, kPartText = 16;
    constexpr uintptr_t kPanelDesc = 0x140D2AF90;      // mode 0/1 layout: the float at +28 is the width
    constexpr uintptr_t kHeadingDesc = 0x140D2B160;    // what CreateFromLayout builds +256 from (modes 0, 2, 4)
    constexpr uintptr_t kLineDesc = 0x140D2B220;       // ... and +384

    // g_TrunkFilterState: +0 which rows are on (a bit per row), +8 the row's picks, +128 the row's card bits, +248 changed flags, +552 the Name text
    constexpr uintptr_t kFilterState = 0x140C91C60;
    constexpr size_t kStateMasks = 8, kStateCompare = 128, kStateChanged = 248, kStateText = 552;
    constexpr int kRows = 15;
    constexpr uintptr_t kRowTable = 0x140A7A260;       // 72 bytes each: +16 options, +24 option table (40 bytes each: +24 icon id)

    // Actions no menu of the game returns (ScreenDeckEditor_DoMenuAction's switch stops at 12)
    constexpr int kActionEditTags = 100, kActionFilter = 101;
    constexpr int kFilterPanelModeList = 0, kFilterPanelModeNumbers = 2, kFilterPanelModeText = 3;   // TrunkFilterCategories_SetMode
    constexpr size_t kPanelMode = 3968;                // widget_TrunkFilterCategories
    constexpr size_t kEditorHelp = 264;                // widget_Help
    constexpr int kInputAccept = 0x10;                 // the "ENT" / Start prompt beside Deck Options: nothing in the panel reads it
    constexpr int kIconsPerLine = 5;                   // on the Tags row

    bool IsMaskRow(int row) { return row == 1 || row == 2 || row == 3 || row == 4 || row == 8 || row == 12 || row == 13 || row == 14; }   // sub_1408B88D0 gives these mode 1

    struct Tags
    {
        uint32_t RowsOn = 0;
        uint64_t Masks[kRows]{};
        uint64_t Compare[kRows]{};
        std::wstring Text;

        bool Empty() const
        {
            if (!Text.empty())
                return false;
            for (uint64_t mask : Masks)
                if (mask)
                    return false;
            return true;
        }
    };

    struct MenuItem   // widget_CommandMenu's rows (sub_140891CA0)
    {
        int Action, Icon;
        int64_t Title;         // a string id or a wchar_t*
        int64_t Description;
    };

    struct MenuItems   // the std::vector it copies from
    {
        const MenuItem* First;
        const MenuItem* Last;
        const MenuItem* End;
    };

    struct HelpEntry   // widget_Help's prompts (sub_14089E9D0)
    {
        int Mask, Button;      // the input, and which button's icon
        int64_t Text;          // a string id or a wchar_t*
    };

    struct NodeRef   // shared_ptr<DFX::TBase>
    {
        void* Node;
        long* Control;
    };

    uintptr_t orig_OnSaveDialogResult = 0x140835F20;   // RIX__ScreenDeckEditor__OnSaveDialogResult
    uintptr_t orig_BuildDeckOptions = 0x1408394A0;     // ScreenDeckEditor_BuildDeckOptions
    uintptr_t orig_DoMenuAction = 0x140836C30;         // ScreenDeckEditor_DoMenuAction
    uintptr_t orig_Update = 0x1408379F0;               // RIX::ScreenDeckEditor::Update
    uintptr_t orig_OnEnter = 0x140838FB0;              // RIX::ScreenDeckEditor::OnEnter
    uintptr_t orig_SetupWidgets = 0x140838200;         // RIX::ScreenDeckEditor::SetupWidgets
    uintptr_t orig_ShowSelectedDeck = 0x140839B30;     // ScreenDeckEditor_ShowSelectedDeck
    uintptr_t orig_SetFilterMode = 0x1408B92D0;        // TrunkFilterCategories_SetMode
    uintptr_t orig_SetRowPick = 0x140870030;           // TrunkFilterState_SetRowPick
    uintptr_t orig_PanelInput = 0x1408B7D50;           // RIX::widget_TrunkFilterCategories::ProcessInput

    const auto DialogStatus = reinterpret_cast<int(__fastcall*)()>(0x1408CA640);                     // TextInputDialog_GetStatus
    const auto DialogText = reinterpret_cast<const wchar_t*(__fastcall*)()>(0x1408CA650);            // TextInputDialog_GetText
    const auto PushState = reinterpret_cast<void(__fastcall*)(uint8_t*, int)>(0x140839790);          // ScreenDeckEditor_PushState
    const auto PopState = reinterpret_cast<void(__fastcall*)(uint8_t*)>(0x140839190);                // ScreenDeckEditor_PopState
    const auto RequestAutosave = reinterpret_cast<int64_t(__fastcall*)()>(0x140806950);              // YGO::SAVE::RequestAutosave
    const auto UserDeckAt = reinterpret_cast<uint8_t*(__fastcall*)(unsigned, int)>(0x14076E8E0);     // Deck_UserDeckAt (UserDecks' hook answers the extras)
    const auto SetMenuItems = reinterpret_cast<void(__fastcall*)(uint8_t*, const MenuItems*)>(0x140891080);
    const auto PlaySound = reinterpret_cast<void(__fastcall*)(int)>(0x14086C280);                    // YGO::UI::PlayUISound
    const auto SetHeaderText = reinterpret_cast<void(__fastcall*)(uint8_t*, int64_t)>(0x140822F00);  // RIX::Screen::SetHeaderText: an id or a wchar_t*
    const auto RefreshDeckList = reinterpret_cast<void(__fastcall*)(uint8_t*)>(0x1408C2620);         // DeckSlotList_Refresh
    const auto ReapplyTrunk = reinterpret_cast<void(__fastcall*)(uint8_t*)>(0x1408BEF90);            // RIX::widget_TrunkZone::Reapply
    const auto SelectedDeck = reinterpret_cast<unsigned*(__fastcall*)(uint8_t*)>(0x1408C2120);       // widget_UserDeckZone: the selected id, or null
    const auto SetFilterText = reinterpret_cast<void(__fastcall*)(uint8_t*, const wchar_t*)>(0x14086FD90);
    const auto SetEnabled = reinterpret_cast<void(__fastcall*)(void*, char)>(0x14075A490);           // RIX::widget_Base::SetEnabled
    const auto SetTextById = reinterpret_cast<void(__fastcall*)(void*, int64_t)>(0x14075E000);       // RIX::widget_Text::SetTextById
    const auto SetWideText = reinterpret_cast<void(__fastcall*)(void*, const wchar_t*)>(0x14075E260);   // RIX::TextLayer_SetWideText
    const auto BuildPanelPart = reinterpret_cast<int64_t(__fastcall*)(const void*, void*, NodeRef*, int, float)>(0x1408AA900);
    const auto HelpClear = reinterpret_cast<void(__fastcall*)(void*)>(0x14089F630);
    const auto HelpAdd = reinterpret_cast<void*(__fastcall*)(void*, const HelpEntry*, unsigned char)>(0x14089E9D0);
    const auto HelpLayout = reinterpret_cast<void(__fastcall*)(void*)>(0x14089EC90);
    const auto SlideTo = reinterpret_cast<void(__fastcall*)(uint8_t*, uint8_t*, float)>(0x140839D80);   // ScreenDeckEditor_SlideTo
    const auto SetNodePosition = reinterpret_cast<void(__fastcall*)(void*, float, float)>(0x14075A300);   // RIX::Node::SetPosition

    enum class Mode { None, EditTags, Filter };

    std::map<unsigned, Tags> g_Tags;   // by deck id; only decks with tags
    std::mutex g_Lock;                 // the save thread writes them
    bool g_Ready = false;              // a profile was loaded or made: until then a save write leaves the extension alone

    Tags g_Filter;                     // the deck list's filter (game thread)
    Mode g_Mode = Mode::None;          // the filter panel is open for tags or the list's filter
    bool g_Accepted = false;           // it was closed with Accept: anything else leaves what it was opened on as it was
    unsigned g_ModeDeck = 0;
    uint8_t g_SavedState[kStateChanged]{};   // the player's card filter while the panel is borrowed
    std::wstring g_SavedText;

    // A constant game address must not reach memcpy as a constant: Release folds it (feedback: release absolute address).
    uint8_t* FilterState()
    {
        const volatile uintptr_t address = kFilterState;
        return reinterpret_cast<uint8_t*>(address);
    }

    bool IsUserDeck(unsigned id) { return id < static_cast<unsigned>(UserDecks::kSaveDecks) || id >= UserDecks::kFirstExtraId; }

    bool SlotUsed(unsigned id)
    {
        if (!IsUserDeck(id))
            return false;
        const int index = id < UserDecks::kFirstExtraId ? static_cast<int>(id) : UserDecks::kSaveDecks + static_cast<int>(id - UserDecks::kFirstExtraId);
        const uint8_t* deck = UserDeckAt(kCurrentProfile, index);
        return deck && deck[kUsedOffset] != 0;
    }

    std::wstring Trim(const std::wstring& text)
    {
        size_t first = 0, last = text.size();
        while (first < last && std::iswspace(text[first]))
            ++first;
        while (last > first && std::iswspace(text[last - 1]))
            --last;
        return text.substr(first, last - first);
    }

    std::wstring Lower(std::wstring text)
    {
        for (wchar_t& c : text)
            c = static_cast<wchar_t>(std::towlower(c));
        return text;
    }

    std::vector<std::wstring> SplitTags(const std::wstring& text)
    {
        std::vector<std::wstring> tags;
        size_t at = 0;
        while (at <= text.size())
        {
            size_t comma = text.find(L',', at);
            if (comma == std::wstring::npos)
                comma = text.size();
            std::wstring tag = Trim(text.substr(at, comma - at));
            at = comma + 1;
            if (!tag.empty())
                tags.push_back(std::move(tag));
        }
        return tags;
    }

    // "  DARK,, GX,dark " -> "DARK, GX"
    std::wstring Normalize(const std::wstring& text)
    {
        std::wstring joined;
        std::vector<std::wstring> seen;
        for (const std::wstring& tag : SplitTags(text))
        {
            std::wstring key = Lower(tag);
            bool repeat = false;
            for (const std::wstring& other : seen)
                repeat = repeat || other == key;
            if (repeat)
                continue;
            seen.push_back(std::move(key));
            if (!joined.empty())
                joined += L", ";
            joined += tag;
        }
        if (joined.size() > kMaxTagChars)
            joined.resize(kMaxTagChars);
        return joined;
    }

    // ---- the save

    void Read()
    {
        g_Tags.clear();
        const int size = Save::GetExtension(kExtensionTag, nullptr, 0);
        if (size <= 0)
            return;
        std::vector<uint8_t> bytes(size);
        Save::GetExtension(kExtensionTag, bytes.data(), size);
        const auto u32 = [&](size_t at) { uint32_t value; std::memcpy(&value, bytes.data() + at, 4); return value; };
        if (bytes.size() < 8 || (u32(0) != 1 && u32(0) != kVersion))
        {
            Logger::WriteLog("Deck tags: the save's tags are in an unknown format, ignored", MODULE_NAME, 2);
            return;
        }
        const bool withMasks = u32(0) == kVersion;
        const size_t fixed = withMasks ? 12 + 2 * kRows * sizeof(uint64_t) : 8;   // before the text
        const uint32_t count = u32(4);
        size_t at = 8;
        for (uint32_t k = 0; k < count && bytes.size() - at >= fixed; ++k)
        {
            Tags tags;
            const uint32_t id = u32(at);
            size_t next = at + 4;
            if (withMasks)
            {
                tags.RowsOn = u32(next);
                next += 4;
                std::memcpy(tags.Masks, bytes.data() + next, sizeof(tags.Masks));
                next += sizeof(tags.Masks);
                std::memcpy(tags.Compare, bytes.data() + next, sizeof(tags.Compare));
                next += sizeof(tags.Compare);
            }
            const uint32_t chars = u32(next);
            next += 4;
            if (chars > kMaxTagChars || (bytes.size() - next) / sizeof(wchar_t) < chars)
            {
                Logger::WriteLog("Deck tags: the save's tags are cut short, the rest ignored", MODULE_NAME, 2);
                break;
            }
            tags.Text.resize(chars);
            std::memcpy(tags.Text.data(), bytes.data() + next, chars * sizeof(wchar_t));
            at = next + chars * sizeof(wchar_t);
            if (!withMasks && chars)   // the first version's text box: now the panel's Name row
            {
                tags.RowsOn = 1;
                tags.Masks[0] = 1;
            }
            g_Tags[id] = std::move(tags);
        }
    }

    // Save.cpp runs this on every save write, before the extension is written.
    int __cdecl WriteTags(unsigned char*, int, void*)
    {
        std::lock_guard<std::mutex> guard(g_Lock);
        if (!g_Ready)
            return 0;
        if (g_Tags.empty())
        {
            Save::SetExtension(kExtensionTag, nullptr, 0);
            return 0;
        }
        std::vector<uint8_t> bytes;
        const auto put = [&](const void* data, size_t size)
        {
            const uint8_t* from = static_cast<const uint8_t*>(data);
            bytes.insert(bytes.end(), from, from + size);
        };
        const uint32_t count = static_cast<uint32_t>(g_Tags.size());
        put(&kVersion, 4);
        put(&count, 4);
        for (const auto& [id, tags] : g_Tags)
        {
            const uint32_t chars = static_cast<uint32_t>(tags.Text.size());
            put(&id, 4);
            put(&tags.RowsOn, 4);
            put(tags.Masks, sizeof(tags.Masks));
            put(tags.Compare, sizeof(tags.Compare));
            put(&chars, 4);
            put(tags.Text.data(), tags.Text.size() * sizeof(wchar_t));
        }
        Save::SetExtension(kExtensionTag, bytes.data(), static_cast<int>(bytes.size()));
        return 0;   // the blob itself is not changed
    }

    // A deleted deck's tags go here rather than on the save thread: the deck lookup belongs on the game's thread.
    void DropDeleted()
    {
        std::lock_guard<std::mutex> guard(g_Lock);
        for (auto it = g_Tags.begin(); it != g_Tags.end();)
            it = SlotUsed(it->first) ? std::next(it) : g_Tags.erase(it);
    }

    // ---- the filter panel's state

    bool RowOn(uint32_t rowsOn, int row) { return (rowsOn >> row & 1) != 0; }

    std::wstring ReadStateText()
    {
        const uint8_t* state = FilterState();
        const size_t size = *reinterpret_cast<const size_t*>(state + kStateText + 16);
        const size_t capacity = *reinterpret_cast<const size_t*>(state + kStateText + 24);
        const wchar_t* text = capacity >= 8 ? *reinterpret_cast<const wchar_t* const*>(state + kStateText) : reinterpret_cast<const wchar_t*>(state + kStateText);
        return std::wstring(text, size);
    }

    void MarkStateChanged() { *reinterpret_cast<int*>(FilterState() + kStateChanged) = -1; }

    void LoadState(const Tags& tags)
    {
        uint8_t* state = FilterState();
        std::memset(state, 0, kStateChanged);
        *reinterpret_cast<uint32_t*>(state) = tags.RowsOn;
        std::memcpy(state + kStateMasks, tags.Masks, sizeof(tags.Masks));
        std::memcpy(state + kStateCompare, tags.Compare, sizeof(tags.Compare));
        SetFilterText(state, tags.Text.c_str());
        MarkStateChanged();
    }

    // What the panel holds now, the rows a deck can use only.
    Tags TakeState()
    {
        const uint8_t* state = FilterState();
        const uint32_t rowsOn = *reinterpret_cast<const uint32_t*>(state);
        const uint64_t* masks = reinterpret_cast<const uint64_t*>(state + kStateMasks);
        const uint64_t* compare = reinterpret_cast<const uint64_t*>(state + kStateCompare);
        Tags tags;
        for (int row = 1; row < kRows; ++row)
            if (IsMaskRow(row) && RowOn(rowsOn, row) && masks[row])
            {
                tags.RowsOn |= 1u << row;
                tags.Masks[row] = masks[row];
                tags.Compare[row] = compare[row];
            }
        if (RowOn(rowsOn, 0))
            tags.Text = Normalize(ReadStateText());
        if (!tags.Text.empty())
        {
            tags.RowsOn |= 1;
            tags.Masks[0] = 1;
        }
        return tags;
    }

    bool Matches(unsigned id)
    {
        Tags none;
        const auto it = g_Tags.find(id);
        const Tags& tags = it == g_Tags.end() ? none : it->second;
        for (int row = 1; row < kRows; ++row)
            if (g_Filter.Masks[row] && !(tags.Masks[row] & g_Filter.Masks[row]))
                return false;
        if (g_Filter.Text.empty())
            return true;
        const std::wstring have = Lower(tags.Text);
        for (const std::wstring& word : SplitTags(g_Filter.Text))
            if (have.find(Lower(word)) != std::wstring::npos)
                return true;
        return false;
    }

    // ---- showing a deck's tags: the row other deck panels show a deck's source in; icons as inline text (U+E000 + icon id)

    std::wstring TagLine(unsigned id)
    {
        Tags tags;
        {
            std::lock_guard<std::mutex> guard(g_Lock);
            const auto it = g_Tags.find(id);
            if (it == g_Tags.end())
                return {};
            tags = it->second;
        }
        const volatile uintptr_t table = kRowTable;
        std::wstring line;
        int icons = 0;
        for (int row = 1; row < kRows; ++row)
        {
            if (!tags.Masks[row])
                continue;
            const uint8_t* entry = reinterpret_cast<const uint8_t*>(table) + 72 * row;
            const uint64_t options = *reinterpret_cast<const uint64_t*>(entry + 16);
            const uint8_t* option = *reinterpret_cast<const uint8_t* const*>(entry + 24);
            for (uint64_t k = 0; k < options && k < 64; ++k)
            {
                const int icon = *reinterpret_cast<const int*>(option + 40 * k + 24);
                if (!(tags.Masks[row] >> k & 1) || icon < 0 || icon >= 0x1900)
                    continue;
                if (icons)
                    line += icons % kIconsPerLine == 0 ? L'\n' : L' ';
                line += static_cast<wchar_t>(0xE000 + icon);
                ++icons;
            }
        }
        if (!tags.Text.empty())
        {
            if (icons)
                line += L'\n';
            line += tags.Text;
        }
        return line;
    }

    void ShowTags(uint8_t* editor)
    {
        uint8_t* panel = editor + kEditorPanel;
        void* heading = *reinterpret_cast<void**>(panel + kPanelHeading);
        void* lineNode = *reinterpret_cast<void**>(panel + kPanelLine);
        if (!heading || !lineNode)
            return;
        const unsigned* id = SelectedDeck(editor + kEditorDeckZone);
        const std::wstring line = id && SlotUsed(*id) ? TagLine(*id) : std::wstring();
        if (line.empty())
        {
            SetEnabled(heading, 0);
            SetEnabled(lineNode, 0);
            return;
        }
        SetTextById(*reinterpret_cast<void**>(panel + kPanelHeading + kPartText), reinterpret_cast<int64_t>(L"Tags"));
        SetWideText(*reinterpret_cast<void**>(panel + kPanelLine + kPartText), line.c_str());
        SetEnabled(heading, 1);
        SetEnabled(lineNode, 1);
    }

    // ---- the panel, borrowed

    void EnterPanel(uint8_t* editor, Mode mode)
    {
        const uint8_t* state = FilterState();
        std::memcpy(g_SavedState, state, sizeof(g_SavedState));
        g_SavedText = ReadStateText();
        g_ModeDeck = *reinterpret_cast<unsigned*>(editor + kEditorDeckId);
        if (mode == Mode::Filter)
            LoadState(g_Filter);
        else
        {
            std::lock_guard<std::mutex> guard(g_Lock);
            const auto it = g_Tags.find(g_ModeDeck);
            LoadState(it == g_Tags.end() ? Tags() : it->second);
        }
        g_Mode = mode;
        g_Accepted = false;
        PlaySound(kSoundDecide);
        PushState(editor, kStateFilterPanel);
        SlideTo(editor, editor + kEditorSlide, kSlidePanelLeft);   // state 6 slides to the card grid + panel; no decks or cards to show here, so the panel alone
        SetHeaderText(editor, reinterpret_cast<int64_t>(mode == Mode::Filter ? L"Filter Decks" : L"Deck Tags"));

        // State 6's prompt is the game's Back; here Back doesn't keep the picks, so the bar says what each button does.
        void* help = editor + kEditorHelp;
        const HelpEntry accept{ kInputAccept, 9, reinterpret_cast<int64_t>(mode == Mode::Filter ? L"Apply Filter" : L"Accept Tags") };
        const HelpEntry cancel{ 0x2104, 1, reinterpret_cast<int64_t>(L"Cancel") };
        HelpClear(help);
        HelpAdd(help, &accept, 1);
        HelpAdd(help, &cancel, 1);
        HelpLayout(help);
        Logger::WriteLog(std::format("Deck tags: filter panel open for {} (deck {})", mode == Mode::Filter ? "the list's filter" : "tags", g_ModeDeck), MODULE_NAME, 0);
    }

    void RestorePlayerFilter()
    {
        uint8_t* state = FilterState();
        std::memcpy(state, g_SavedState, sizeof(g_SavedState));
        SetFilterText(state, g_SavedText.c_str());
        MarkStateChanged();
    }

    void LeavePanel(uint8_t* editor)
    {
        const Mode mode = g_Mode;
        const bool accepted = g_Accepted;
        Tags picked = TakeState();
        g_Mode = Mode::None;
        g_Accepted = false;
        if (accepted && mode == Mode::Filter)
            g_Filter = std::move(picked);
        else if (accepted)
        {
            {
                std::lock_guard<std::mutex> guard(g_Lock);
                if (picked.Empty())
                    g_Tags.erase(g_ModeDeck);
                else
                    g_Tags[g_ModeDeck] = std::move(picked);
            }
            RequestAutosave();
        }
        RestorePlayerFilter();
        ReapplyTrunk(editor + kEditorTrunk);
        RefreshDeckList(editor + kEditorDeckZone);
        PopState(editor);
        SetHeaderText(editor, kHeaderDeckList);
        reinterpret_cast<void(__fastcall*)(uint8_t*)>(orig_ShowSelectedDeck)(editor);
        ShowTags(editor);
        Logger::WriteLog(std::format("Deck tags: filter panel closed ({})", !accepted ? "cancelled" : mode == Mode::Filter ? "list filter set" : "tags saved"), MODULE_NAME, 0);
    }

    // ---- hooks

    // A rename keeps a deck's tags. A new or copied deck starts with none: a slot used again must not keep a deleted deck's.
    void __fastcall Hook_OnSaveDialogResult(uint8_t* editor)
    {
        const wchar_t* name = DialogStatus() == kStatusOk ? DialogText() : nullptr;
        const bool named = name && name[0];
        const unsigned deck = *reinterpret_cast<unsigned*>(editor + kEditorDeckId);
        const bool isNew = editor[kEditorIsNewDeck] != 0;

        reinterpret_cast<void(__fastcall*)(uint8_t*)>(orig_OnSaveDialogResult)(editor);

        if (!named)
            return;
        DropDeleted();
        if (isNew)
        {
            std::lock_guard<std::mutex> guard(g_Lock);
            g_Tags.erase(deck);
        }
    }

    // After the game's rows: Edit Tags (when the slot holds a deck) and Filter Decks (always: it is the way out of a filter that hides everything).
    void __fastcall Hook_BuildDeckOptions(uint8_t* editor, uint8_t* menu)
    {
        reinterpret_cast<void(__fastcall*)(uint8_t*, uint8_t*)>(orig_BuildDeckOptions)(editor, menu);
        const MenuItems current = *reinterpret_cast<const MenuItems*>(menu + 416);
        const size_t count = current.Last - current.First;
        std::vector<MenuItem> items(current.First, current.Last);
        if (count)
            items.push_back({ kActionEditTags, -1, reinterpret_cast<int64_t>(L"Edit Tags"), 0 });
        items.push_back({ kActionFilter, -1, reinterpret_cast<int64_t>(g_Filter.Empty() ? L"Filter Decks" : L"Filter Decks (on)"), 0 });
        const MenuItems all{ items.data(), items.data() + items.size(), items.data() + items.size() };
        SetMenuItems(menu, &all);   // copies them
    }

    void __fastcall Hook_DoMenuAction(uint8_t* editor, int action)
    {
        if (action == kActionEditTags)
            EnterPanel(editor, Mode::EditTags);
        else if (action == kActionFilter)
            EnterPanel(editor, Mode::Filter);
        else
            reinterpret_cast<void(__fastcall*)(uint8_t*, int)>(orig_DoMenuAction)(editor, action);
    }

    // State 6 goes to state 3 (the card grid) when the panel closes, by Accept or Back; here it goes back to where it was opened from instead.
    void __fastcall Hook_Update(uint8_t* editor, int64_t context, double seconds)
    {
        reinterpret_cast<void(__fastcall*)(uint8_t*, int64_t, double)>(orig_Update)(editor, context, seconds);
        if (g_Mode != Mode::None && *reinterpret_cast<int*>(editor + kEditorState) == kStateTrunk)
            LeavePanel(editor);
    }

    // Every visit to the editor starts with the whole list.
    void __fastcall Hook_OnEnter(uint8_t* editor)
    {
        if (g_Mode != Mode::None)
        {
            RestorePlayerFilter();
            g_Mode = Mode::None;
        }
        g_Filter = Tags();
        reinterpret_cast<void(__fastcall*)(uint8_t*)>(orig_OnEnter)(editor);
    }

    // The editor's panel is mode 1, which has no source row: give it the one modes 0, 2 and 4 get, from the same layout.
    int64_t __fastcall Hook_SetupWidgets(uint8_t* editor)
    {
        const int64_t result = reinterpret_cast<int64_t(__fastcall*)(uint8_t*)>(orig_SetupWidgets)(editor);
        uint8_t* panel = editor + kEditorPanel;
        if (*reinterpret_cast<void**>(panel + kPanelHeading) || *reinterpret_cast<void**>(panel + kPanelLine))
            return result;
        const volatile uintptr_t panelDesc = kPanelDesc;
        const float width = *reinterpret_cast<const float*>(panelDesc + 28) - 30.0f;
        const auto build = [&](uintptr_t desc, size_t part, int z)
        {
            NodeRef parent = *reinterpret_cast<NodeRef*>(panel + kPanelBody);
            if (parent.Control)
                _InterlockedIncrement(parent.Control + 2);   // the builder takes a reference and drops it
            BuildPanelPart(reinterpret_cast<const void*>(desc), panel + part, &parent, z, width);
            SetEnabled(*reinterpret_cast<void**>(panel + part), 0);
        };
        build(kHeadingDesc, kPanelHeading, 9);
        build(kLineDesc, kPanelLine, 10);
        const volatile uintptr_t lineDesc = kLineDesc;
        const float* linePosition = reinterpret_cast<const float*>(lineDesc);
        SetNodePosition(*reinterpret_cast<void**>(panel + kPanelLine), linePosition[0], linePosition[1] + kTagLineDrop);
        return result;
    }

    void __fastcall Hook_ShowSelectedDeck(uint8_t* editor)
    {
        reinterpret_cast<void(__fastcall*)(uint8_t*)>(orig_ShowSelectedDeck)(editor);
        ShowTags(editor);
    }

    // The number rows (Level, ATK...) have no meaning for a deck.
    void __fastcall Hook_SetFilterMode(uint8_t* widget, int mode)
    {
        if (g_Mode != Mode::None && mode == kFilterPanelModeNumbers)
        {
            PlaySound(kSoundUnable);
            return;
        }
        reinterpret_cast<void(__fastcall*)(uint8_t*, int)>(orig_SetFilterMode)(widget, mode);
    }

    // Accept, from the row list or an open option list (the text dialog answers for itself): closes the panel as Back does, and Hook_Update takes the picks.
    char __fastcall Hook_PanelInput(uint8_t* panel, int pressed)
    {
        if (g_Mode != Mode::None && (pressed & kInputAccept) && *reinterpret_cast<int*>(panel + kPanelMode) != kFilterPanelModeText)
        {
            g_Accepted = true;
            reinterpret_cast<void(__fastcall*)(uint8_t*, int)>(orig_SetFilterMode)(panel, kFilterPanelModeList);
            const auto setFocus = (*reinterpret_cast<void(__fastcall***)(uint8_t*, char)>(panel))[1];   // vftable +8, what Input_CategoryList closes it with
            setFocus(panel, 0);
            PlaySound(kSoundDecide);
            return 1;
        }
        return reinterpret_cast<char(__fastcall*)(uint8_t*, int)>(orig_PanelInput)(panel, pressed);
    }

    // Confirming an option makes it the row's only pick for cards. A deck can have several: it adds. Confirming a picked one still takes it off
    // (the panel checks that before it gets here).
    int64_t __fastcall Hook_SetRowPick(uint8_t* state, int row, char option)
    {
        if (g_Mode == Mode::None || row < 0 || row >= kRows)
            return reinterpret_cast<int64_t(__fastcall*)(uint8_t*, int, char)>(orig_SetRowPick)(state, row, option);
        reinterpret_cast<uint64_t*>(state + kStateMasks)[row] |= 1ull << option;
        *reinterpret_cast<int*>(state + kStateChanged) = -1;
        return row;
    }

    struct Site
    {
        uintptr_t* Target;
        void* Hook;
    };
}

std::wstring DeckTags::Get(unsigned deckId)
{
    if (!SlotUsed(deckId))
        return {};
    std::lock_guard<std::mutex> guard(g_Lock);
    const auto it = g_Tags.find(deckId);
    return it == g_Tags.end() ? std::wstring() : it->second.Text;
}

bool DeckTags::Set(unsigned deckId, const std::wstring& text)
{
    if (!SlotUsed(deckId))
        return false;
    const std::wstring clean = Normalize(text);
    std::lock_guard<std::mutex> guard(g_Lock);
    Tags& tags = g_Tags[deckId];
    tags.Text = clean;
    tags.Masks[0] = clean.empty() ? 0 : 1;
    tags.RowsOn = clean.empty() ? tags.RowsOn & ~1u : tags.RowsOn | 1u;
    if (tags.Empty())
        g_Tags.erase(deckId);
    return true;
}

uint64_t DeckTags::GetMask(unsigned deckId, int row)
{
    if (row < 0 || row >= kRows || !SlotUsed(deckId))
        return 0;
    std::lock_guard<std::mutex> guard(g_Lock);
    const auto it = g_Tags.find(deckId);
    return it == g_Tags.end() ? 0 : it->second.Masks[row];
}

size_t DeckTags::FilterSlots(int* ids, size_t count)
{
    if (g_Filter.Empty())
        return count;
    std::lock_guard<std::mutex> guard(g_Lock);
    size_t kept = 0;
    bool haveEmpty = false;
    for (size_t k = 0; k < count; ++k)
    {
        const unsigned id = static_cast<unsigned>(ids[k]);
        const bool used = SlotUsed(id);
        if (used ? Matches(id) : !haveEmpty)
            ids[kept++] = ids[k];
        haveEmpty = haveEmpty || !used;
    }
    return kept;
}

void DeckTags::OnProfile(bool loaded)
{
    std::lock_guard<std::mutex> guard(g_Lock);
    g_Tags.clear();
    if (loaded)
        Read();
    g_Ready = true;
    g_Filter = Tags();
    Logger::WriteLog(std::format("Deck tags: {} deck(s) with tags in this save", g_Tags.size()), MODULE_NAME, 0);
}

void DeckTags::Install()
{
    const Site sites[] = {
        { &orig_OnSaveDialogResult, Hook_OnSaveDialogResult },
        { &orig_BuildDeckOptions, Hook_BuildDeckOptions },
        { &orig_DoMenuAction, Hook_DoMenuAction },
        { &orig_Update, Hook_Update },
        { &orig_OnEnter, Hook_OnEnter },
        { &orig_SetupWidgets, Hook_SetupWidgets },
        { &orig_ShowSelectedDeck, Hook_ShowSelectedDeck },
        { &orig_SetFilterMode, Hook_SetFilterMode },
        { &orig_SetRowPick, Hook_SetRowPick },
        { &orig_PanelInput, Hook_PanelInput },
    };
    DetourTransactionBegin();
    DetourUpdateThread(GetCurrentThread());
    for (const Site& site : sites)
        DetourAttach(&reinterpret_cast<PVOID&>(*site.Target), site.Hook);
    const LONG error = DetourTransactionCommit();
    if (error != NO_ERROR)
        Logger::WriteLog(std::format("Deck tags: the hooks failed ({}), the deck editor has no tags or filter", error), MODULE_NAME, 2);

    // Kept whether or not the hooks went in: plugins can still read and set them.
    Save::AddWriteFilter(WriteTags, nullptr);
    Logger::WriteLog("Deck tags: on (Deck Options: Edit Tags, Filter Decks)", MODULE_NAME, 0);
}
