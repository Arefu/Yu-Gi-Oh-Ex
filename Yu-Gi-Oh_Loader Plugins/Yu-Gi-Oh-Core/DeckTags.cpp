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
#include "YuGiOh/YuGiOh-DECKEDIT.h"

namespace
{
    using namespace YGO::DECKEDIT;

    constexpr const char* kExtensionTag = "DeckTags";
    constexpr uint32_t kVersion = 3;             // 1 was { id, chars, text } for the text box of the first version, 2 had no number rows' values
    constexpr unsigned kCurrentProfile = 0xFFFFFFFD;
    constexpr size_t kUsedOffset = 300;          // DeckListItem.IsValid: the slot holds a deck
    constexpr size_t kMaxTagChars = 4096;        // of one deck's text, read from a save or given by a plugin

    constexpr float kSlidePanelLeft = 33.0f - 1568.5172f;   // the panel (x 1568.5172) where the card grid and the deck list sit (x 33)
    constexpr float kTagLineDrop = 12.0f;        // the Tags icons are taller than the source line's text: clear the heading
    constexpr int kStateTrunk = 3, kStateFilterPanel = 6;
    constexpr int64_t kHeaderDeckList = 0x73;    // the state 0 header (ScreenDeckEditor::OnEnter)
    constexpr int kStatusOk = 3;                 // TextInputDialog_GetStatus
    constexpr int kSoundDecide = 39;

    // widget_RecipeInfo's layouts: the panel's (mode 0/1; its float at +28 is the width), and the source rows', which CreateFromLayout only builds in modes 0, 2, 4
    constexpr uintptr_t kPanelDesc = 0x140D2AF90;
    constexpr uintptr_t kHeadingDesc = 0x140D2B160;    // widget_RecipeInfo.SourceHeading
    constexpr uintptr_t kLineDesc = 0x140D2B220;       // widget_RecipeInfo.SourceLine

    constexpr uintptr_t kFilterState = 0x140C91C60;    // g_TrunkFilterState
    constexpr uintptr_t kRowTable = 0x140A7A260;       // g_TrunkFilterRows
    constexpr int kRows = kFilterRows;

    // Actions no menu of the game returns (ScreenDeckEditor_DoMenuAction's switch stops at 12)
    constexpr int kActionEditTags = 100, kActionFilter = 101;
    // The preview, right of the filter panel and its option list: screen x = local x + the columns' slide. The screen is 1920 wide, margins 33.
    constexpr float kScreenRight = 1920.0f - 33.0f;
    constexpr float kDeckZoneWidth = 527.0f;           // the deck list runs from x 33 to the info panel at 560
    constexpr float kPanelHomeX = 560.0f;              // RIX::ScreenDeckEditor::SetupWidgets puts the info panel here
    constexpr int kIconsPerLine = 5;                   // on the Tags row
    constexpr float kLineUnits = 34.0f;                // how wide a line of it can be before the game wraps it (TagLine)
    int g_IconSpacing = 2;                             // [Yu-Gi-Oh-Core] DeckTagIconSpacing: spaces between them, 0 to 6
    int g_LineSpacing = 140;                           // [Yu-Gi-Oh-Core] DeckTagLineSpacing: percent of the font's line height, 100 to 300
    int g_MaxLines = 4;                                // [Yu-Gi-Oh-Core] DeckTagMaxLines: lines on the Tags row before "+More...", 1 to 8
    bool g_ShowText = false;                         // [Yu-Gi-Oh-Core] DeckTagShowText: the free-text tags too, one to a line (the filter uses them either way)

    bool IsNumberRow(int row) { return (row >= 5 && row <= 7) || (row >= 9 && row <= 11); }   // sub_1408B88D0 gives these mode 2

    struct Tags
    {
        uint32_t RowsOn = 0;
        uint64_t Picks[kRows]{};
        uint64_t CardBits[kRows]{};
        int Values[kRows][kRowValues]{};     // only for rows with picks
        std::wstring Text;

        bool Empty() const
        {
            if (!Text.empty())
                return false;
            for (uint64_t mask : Picks)
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

    uintptr_t orig_OnSaveDialogResult = 0x140835F20;   // RIX__ScreenDeckEditor__OnSaveDialogResult
    uintptr_t orig_BuildDeckOptions = 0x1408394A0;     // ScreenDeckEditor_BuildDeckOptions
    uintptr_t orig_DoMenuAction = 0x140836C30;         // ScreenDeckEditor_DoMenuAction
    uintptr_t orig_Update = 0x1408379F0;               // RIX::ScreenDeckEditor::Update
    uintptr_t orig_OnEnter = 0x140838FB0;              // RIX::ScreenDeckEditor::OnEnter
    uintptr_t orig_SetupWidgets = 0x140838200;         // RIX::ScreenDeckEditor::SetupWidgets
    uintptr_t orig_ShowSelectedDeck = 0x140839B30;     // ScreenDeckEditor_ShowSelectedDeck
    uintptr_t orig_SetRowPick = 0x140870030;           // TrunkFilterState_SetRowPick

    const auto DialogStatus = reinterpret_cast<int(__fastcall*)()>(0x1408CA640);                     // TextInputDialog_GetStatus
    const auto DialogText = reinterpret_cast<const wchar_t*(__fastcall*)()>(0x1408CA650);            // TextInputDialog_GetText
    const auto PushState = reinterpret_cast<void(__fastcall*)(ScreenDeckEditor*, int)>(0x140839790);  // ScreenDeckEditor_PushState
    const auto PopState = reinterpret_cast<void(__fastcall*)(ScreenDeckEditor*)>(0x140839190);        // ScreenDeckEditor_PopState
    const auto RequestAutosave = reinterpret_cast<int64_t(__fastcall*)()>(0x140806950);              // YGO::SAVE::RequestAutosave
    const auto UserDeckAt = reinterpret_cast<uint8_t*(__fastcall*)(unsigned, int)>(0x14076E8E0);     // Deck_UserDeckAt (UserDecks' hook answers the extras)
    const auto SetMenuItems = reinterpret_cast<void(__fastcall*)(uint8_t*, const MenuItems*)>(0x140891080);
    const auto PlaySound = reinterpret_cast<void(__fastcall*)(int)>(0x14086C280);                    // YGO::UI::PlayUISound
    const auto SetHeaderText = reinterpret_cast<void(__fastcall*)(ScreenDeckEditor*, int64_t)>(0x140822F00);  // RIX::Screen::SetHeaderText: an id or a wchar_t*
    const auto RefreshDeckList = reinterpret_cast<void(__fastcall*)(void*)>(0x1408C2620);            // DeckSlotList_Refresh(UserDeckZone)
    const auto ReapplyTrunk = reinterpret_cast<void(__fastcall*)(void*)>(0x1408BEF90);               // RIX::widget_TrunkZone::Reapply
    const auto SelectedDeck = reinterpret_cast<unsigned*(__fastcall*)(void*)>(0x1408C2120);          // UserDeckZone_SelectedDeckId: the selected id, or null
    const auto SetFilterText = reinterpret_cast<void(__fastcall*)(TrunkFilterState*, const wchar_t*)>(0x14086FD90);   // TrunkFilterState_SetText
    const auto SetEnabled = reinterpret_cast<void(__fastcall*)(void*, char)>(0x14075A490);           // RIX::widget_Base::SetEnabled
    const auto SetTextById = reinterpret_cast<void(__fastcall*)(TLayerText*, int64_t)>(0x14075E000);  // RIX::widget_Text::SetTextById
    const auto SetWideText = reinterpret_cast<void(__fastcall*)(TLayerText*, const wchar_t*)>(0x14075E260);   // RIX::TextLayer_SetWideText
    const auto BuildPanelPart = reinterpret_cast<int64_t(__fastcall*)(const void*, RecipeInfoPart*, NodeRef*, int, float)>(0x1408AA900);   // RecipeInfo_BuildTextPart
    const auto HelpClear = reinterpret_cast<void(__fastcall*)(void*)>(0x14089F630);
    const auto HelpAdd = reinterpret_cast<void*(__fastcall*)(void*, const HelpEntry*, unsigned char)>(0x14089E9D0);
    const auto HelpLayout = reinterpret_cast<void(__fastcall*)(void*)>(0x14089EC90);
    const auto SlideTo = reinterpret_cast<void(__fastcall*)(ScreenDeckEditor*, void*, float)>(0x140839D80);   // ScreenDeckEditor_SlideTo
    const auto SetNodePosition = reinterpret_cast<void(__fastcall*)(void*, float, float)>(0x14075A300);   // RIX::Node::SetPosition
    const auto SetNodeX = reinterpret_cast<void(__fastcall*)(void*, float)>(0x14075A2F0);            // RIX::Node::SetX
    const auto ShowRowOptions = reinterpret_cast<void(__fastcall*)(widget_TrunkFilterCategories*)>(0x1408B9970);   // TrunkFilterCategories_ShowRowOptions

    enum class Mode { None, EditTags, Filter };

    std::map<unsigned, Tags> g_Tags;   // by deck id; only decks with tags
    std::mutex g_Lock;                 // the save thread writes them
    bool g_Ready = false;              // a profile was loaded or made: until then a save write leaves the extension alone

    Tags g_Filter;                     // the deck list's filter (game thread)
    Mode g_Mode = Mode::None;          // the filter panel is open for tags or the list's filter
    unsigned g_ModeDeck = 0;
    uint8_t g_SavedState[offsetof(TrunkFilterState, NameText)]{};   // the player's card filter while the panel is borrowed, all but its text
    std::wstring g_SavedText;

    // A constant game address must not be used as a constant: Release folds it (feedback: release absolute address).
    TrunkFilterState* FilterState()
    {
        const volatile uintptr_t address = kFilterState;
        return reinterpret_cast<TrunkFilterState*>(address);
    }

    const TrunkFilterRow* FilterRows()
    {
        const volatile uintptr_t address = kRowTable;
        return reinterpret_cast<const TrunkFilterRow*>(address);
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
        if (bytes.size() < 8 || u32(0) < 1 || u32(0) > kVersion)
        {
            Logger::WriteLog("Deck tags: the save's tags are in an unknown format, ignored", MODULE_NAME, 2);
            return;
        }
        const bool withPicks = u32(0) >= 2, withValues = u32(0) >= 3;
        const size_t fixed = (withPicks ? 12 + 2 * kRows * sizeof(uint64_t) : 8) + (withValues ? sizeof(Tags::Values) : 0);   // before the text
        const uint32_t count = u32(4);
        size_t at = 8;
        for (uint32_t k = 0; k < count && bytes.size() - at >= fixed; ++k)
        {
            Tags tags;
            const uint32_t id = u32(at);
            size_t next = at + 4;
            if (withPicks)
            {
                tags.RowsOn = u32(next);
                next += 4;
                std::memcpy(tags.Picks, bytes.data() + next, sizeof(tags.Picks));
                next += sizeof(tags.Picks);
                std::memcpy(tags.CardBits, bytes.data() + next, sizeof(tags.CardBits));
                next += sizeof(tags.CardBits);
            }
            if (withValues)
            {
                std::memcpy(tags.Values, bytes.data() + next, sizeof(tags.Values));
                next += sizeof(tags.Values);
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
            if (!withPicks && chars)   // the first version's text box: now the panel's Name row
            {
                tags.RowsOn = 1;
                tags.Picks[0] = 1;
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
            put(tags.Picks, sizeof(tags.Picks));
            put(tags.CardBits, sizeof(tags.CardBits));
            put(tags.Values, sizeof(tags.Values));
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
        const GameWString& text = FilterState()->NameText;
        return std::wstring(text.Data(), text.Size);
    }

    void LoadState(const Tags& tags)
    {
        TrunkFilterState* state = FilterState();
        state->RowsOn = tags.RowsOn;
        std::memcpy(state->Picks, tags.Picks, sizeof(state->Picks));
        std::memcpy(state->CardBits, tags.CardBits, sizeof(state->CardBits));
        // A row with no picks keeps the values it has: its widgets show them, and zero is out of range for some rows.
        for (int row = 1; row < kRows; ++row)
            if (tags.Picks[row])
                std::memcpy(state->Values[row], tags.Values[row], sizeof(state->Values[row]));
        SetFilterText(state, tags.Text.c_str());
        state->ChangedFlags = -1;
    }

    // What the panel holds now.
    Tags TakeState()
    {
        const TrunkFilterState* state = FilterState();
        const uint32_t rowsOn = state->RowsOn;
        Tags tags;
        for (int row = 1; row < kRows; ++row)
            if (RowOn(rowsOn, row) && state->Picks[row])
            {
                tags.RowsOn |= 1u << row;
                tags.Picks[row] = state->Picks[row];
                tags.CardBits[row] = state->CardBits[row];
                std::memcpy(tags.Values[row], state->Values[row], sizeof(tags.Values[row]));
            }
        if (RowOn(rowsOn, 0))
            tags.Text = Normalize(ReadStateText());
        if (!tags.Text.empty())
        {
            tags.RowsOn |= 1;
            tags.Picks[0] = 1;
        }
        return tags;
    }

    // The game's own test on a card's number (sub_1408700B0).
    bool NumberPasses(int value, uint64_t picks, const int* numbers)
    {
        if (value == kValueUnknown)
            return (picks & PickUnknown) != 0;
        return ((picks & PickEqual) && value == numbers[0]) || ((picks & PickAtMost) && value <= numbers[1]) || ((picks & PickAtLeast) && value >= numbers[2]) ||
               ((picks & PickBetween) && value >= numbers[3] && value <= numbers[4]);
    }

    // A deck's number tags are values of its own: Level 4, or ATK 1000 to 2000. It passes when any of them passes the filter's test.
    bool NumberRowMatches(const Tags& tags, int row)
    {
        const uint64_t have = tags.Picks[row], want = g_Filter.Picks[row];
        const int* mine = tags.Values[row];
        const int* theirs = g_Filter.Values[row];
        return ((have & PickEqual) && NumberPasses(mine[0], want, theirs)) || ((have & PickAtMost) && NumberPasses(mine[1], want, theirs)) ||
               ((have & PickAtLeast) && NumberPasses(mine[2], want, theirs)) ||
               ((have & PickBetween) && (NumberPasses(mine[3], want, theirs) || NumberPasses(mine[4], want, theirs))) ||
               ((have & PickUnknown) && NumberPasses(kValueUnknown, want, theirs));
    }

    bool Matches(unsigned id)
    {
        Tags none;
        const auto it = g_Tags.find(id);
        const Tags& tags = it == g_Tags.end() ? none : it->second;
        for (int row = 1; row < kRows; ++row)
        {
            if (!g_Filter.Picks[row])
                continue;
            if (IsNumberRow(row) ? !NumberRowMatches(tags, row) : !(tags.Picks[row] & g_Filter.Picks[row]))
                return false;
        }
        if (g_Filter.Text.empty())
            return true;
        const std::wstring have = Lower(tags.Text);
        for (const std::wstring& word : SplitTags(g_Filter.Text))
            if (have.find(Lower(word)) != std::wstring::npos)
                return true;
        return false;
    }

    // ---- showing a deck's tags: the row other deck panels show a deck's source in; icons as inline text (U+E000 + icon id)

    std::wstring TagLine(const Tags& tags)
    {
        const TrunkFilterRow* rows = FilterRows();
        std::vector<std::vector<std::wstring>> lines;

        // The game wraps a line that is too wide, which would add a row the limit below doesn't know about, so lines break before that. Rough widths, in
        // characters of the row's font: an icon is about 3, a space about half. A row of 34 fitted and one of 35 wrapped (screenshots, 2026-11-02).
        const auto itemUnits = [](const std::wstring& item)
        {
            float units = 0;
            for (const wchar_t c : item)
                units += c >= 0xE000 ? 3.0f : 1.0f;
            return units;
        };
        const float gapUnits = g_IconSpacing * 0.5f;
        const auto fits = [&](const std::vector<std::wstring>& line, const std::wstring& item)
        {
            if (line.size() >= kIconsPerLine)
                return false;
            float units = itemUnits(item);
            for (const std::wstring& other : line)
                units += itemUnits(other) + gapUnits;
            return units <= kLineUnits;
        };
        const auto add = [&](const std::wstring& item)
        {
            if (lines.empty() || !fits(lines.back(), item))
                lines.emplace_back();
            lines.back().push_back(item);
        };
        for (int row = 1; row < kRows; ++row)
        {
            if (!tags.Picks[row])
                continue;
            if (IsNumberRow(row))
            {
                // The row's own icon and the value: Level 4, ATK >=1000, DEF 500-1000.
                const std::wstring icon(1, static_cast<wchar_t>(0xE000 + rows[row].Icon));
                const uint64_t picks = tags.Picks[row];
                const int* value = tags.Values[row];
                if (picks & PickEqual)
                    add(icon + std::to_wstring(value[0]));
                if (picks & PickAtMost)
                    add(icon + L"<=" + std::to_wstring(value[1]));
                if (picks & PickAtLeast)
                    add(icon + L">=" + std::to_wstring(value[2]));
                if (picks & PickBetween)
                    add(icon + std::to_wstring(value[3]) + L"-" + std::to_wstring(value[4]));
                if (picks & PickUnknown)
                    add(icon + L"?");
                continue;
            }
            for (uint64_t k = 0; k < rows[row].OptionCount && k < 64; ++k)
            {
                const int icon = rows[row].Options[k].Icon;
                if ((tags.Picks[row] >> k & 1) && icon >= 0 && icon < 0x1900)
                    add(std::wstring(1, static_cast<wchar_t>(0xE000 + icon)));
            }
        }
        if (g_ShowText)
            for (const std::wstring& tag : SplitTags(tags.Text))
                lines.push_back({ tag });

        // The panel has room for so many lines above its validity text; past that, the last one says there are more.
        if (lines.size() > static_cast<size_t>(g_MaxLines))
        {
            lines.resize(g_MaxLines);
            const std::wstring more = L"+More...";
            while (!lines.back().empty() && !fits(lines.back(), more))
                lines.back().pop_back();
            lines.back().push_back(more);
        }

        std::wstring text;
        for (const std::vector<std::wstring>& items : lines)
        {
            if (!text.empty())
                text += L'\n';
            for (size_t k = 0; k < items.size(); ++k)
            {
                if (k)
                    text.append(g_IconSpacing, L' ');
                text += items[k];
            }
        }
        return text;
    }

    void ShowLine(ScreenDeckEditor* editor, const std::wstring& line)
    {
        widget_RecipeInfo& panel = editor->DeckInfoPanel;
        void* heading = panel.SourceHeading.Holder;
        void* lineNode = panel.SourceLine.Holder;
        if (!heading || !lineNode)
            return;
        if (line.empty())
        {
            SetEnabled(heading, 0);
            SetEnabled(lineNode, 0);
            return;
        }
        SetTextById(panel.SourceHeading.TextLayer, reinterpret_cast<int64_t>(L"Tags"));
        SetWideText(panel.SourceLine.TextLayer, line.c_str());
        SetEnabled(heading, 1);
        SetEnabled(lineNode, 1);
    }

    void ShowTags(ScreenDeckEditor* editor)
    {
        const unsigned* id = SelectedDeck(editor->UserDeckZone);
        Tags tags;
        if (id && SlotUsed(*id))
        {
            std::lock_guard<std::mutex> guard(g_Lock);
            const auto it = g_Tags.find(*id);
            if (it != g_Tags.end())
                tags = it->second;
        }
        ShowLine(editor, TagLine(tags));
    }

    // ---- the panel, borrowed

    void SetWidgetVisible(void* widget, char visible)   // vftable slot 3, as DeckEdit_ApplyState shows and hides them
    {
        (*static_cast<void(__fastcall***)(void*, char)>(widget))[3](widget, visible);
    }

    void SetWidgetFocused(void* widget, char focused)   // vftable slot 1
    {
        (*static_cast<void(__fastcall***)(void*, char)>(widget))[1](widget, focused);
    }

    // State 6 hides both of these. Whichever the mode previews goes at the far right: its local x is the screen x less the columns' slide. Both move with
    // the columns, so they arrive with the panel.
    void ShowPreview(ScreenDeckEditor* editor, Mode mode)
    {
        if (mode == Mode::Filter)
        {
            SetWidgetFocused(editor->UserDeckZone, 0);
            SetWidgetVisible(editor->UserDeckZone, 1);
            SlideTo(editor, editor->UserDeckZoneSlide, kScreenRight - kDeckZoneWidth - kSlidePanelLeft);   // state 0 slides it home
            return;
        }
        const volatile uintptr_t panelDesc = kPanelDesc;
        const float width = *reinterpret_cast<const float*>(panelDesc + 28);
        SetNodeX(editor->DeckInfoPanel.Base.Node, kScreenRight - width - kSlidePanelLeft);
        SetWidgetVisible(&editor->DeckInfoPanel, 1);
    }

    bool SamePicks(const Tags& a, const Tags& b)
    {
        return a.Text == b.Text && std::memcmp(a.Picks, b.Picks, sizeof(a.Picks)) == 0;
    }

    // Every frame the panel is open: what it holds now, on the preview. The deck list is only rebuilt when the picks change (it revalidates every deck).
    void UpdatePreview(ScreenDeckEditor* editor)
    {
        Tags live = TakeState();
        if (g_Mode == Mode::EditTags)
            ShowLine(editor, TagLine(live));
        else if (!SamePicks(live, g_Filter))
        {
            g_Filter = std::move(live);
            RefreshDeckList(editor->UserDeckZone);
        }
    }

    // The number option list keeps the values it read for its row until it is given another row, so after swapping the state it would show the old ones.
    void ReloadRowOptions(ScreenDeckEditor* editor)
    {
        editor->FilterPanel.NumOptions.Row = nullptr;
        ShowRowOptions(&editor->FilterPanel);
    }

    void EnterPanel(ScreenDeckEditor* editor, Mode mode)
    {
        std::memcpy(g_SavedState, FilterState(), sizeof(g_SavedState));
        g_SavedText = ReadStateText();
        g_ModeDeck = editor->DeckId;
        if (mode == Mode::Filter)
            LoadState(g_Filter);
        else
        {
            std::lock_guard<std::mutex> guard(g_Lock);
            const auto it = g_Tags.find(g_ModeDeck);
            LoadState(it == g_Tags.end() ? Tags() : it->second);
        }
        ReloadRowOptions(editor);
        g_Mode = mode;
        PlaySound(kSoundDecide);
        PushState(editor, kStateFilterPanel);
        SlideTo(editor, editor->ColumnsSlide, kSlidePanelLeft);   // state 6 slides to the card grid + panel; no decks or cards to show here, so the panel alone
        SetHeaderText(editor, reinterpret_cast<int64_t>(mode == Mode::Filter ? L"Filter Decks" : L"Deck Tags"));

        // State 6's prompt says Back, which read as if the picks were lost. They apply as they are made, so it says Done. Same input as the game's entry.
        void* help = editor->Help;
        const HelpEntry done{ 0x2104, 1, reinterpret_cast<int64_t>(L"Done") };
        HelpClear(help);
        HelpAdd(help, &done, 1);
        HelpLayout(help);

        ShowPreview(editor, mode);
        Logger::WriteLog(std::format("Deck tags: filter panel open for {} (deck {})", mode == Mode::Filter ? "the list's filter" : "tags", g_ModeDeck), MODULE_NAME, 0);
    }

    void RestorePlayerFilter()
    {
        TrunkFilterState* state = FilterState();
        std::memcpy(state, g_SavedState, sizeof(g_SavedState));
        SetFilterText(state, g_SavedText.c_str());
        state->ChangedFlags = -1;
    }

    void LeavePanel(ScreenDeckEditor* editor)
    {
        const Mode mode = g_Mode;
        Tags picked = TakeState();
        g_Mode = Mode::None;
        if (mode == Mode::Filter)
            g_Filter = std::move(picked);
        else
        {
            SetNodeX(editor->DeckInfoPanel.Base.Node, kPanelHomeX);
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
        ReloadRowOptions(editor);
        ReapplyTrunk(editor->Trunk);
        RefreshDeckList(editor->UserDeckZone);
        PopState(editor);
        SetHeaderText(editor, kHeaderDeckList);
        reinterpret_cast<void(__fastcall*)(ScreenDeckEditor*)>(orig_ShowSelectedDeck)(editor);
        ShowTags(editor);
        Logger::WriteLog(std::format("Deck tags: filter panel closed ({})", mode == Mode::Filter ? "list filter set" : "tags saved"), MODULE_NAME, 0);
    }

    // ---- hooks

    // A rename keeps a deck's tags. A new or copied deck starts with none: a slot used again must not keep a deleted deck's.
    void __fastcall Hook_OnSaveDialogResult(ScreenDeckEditor* editor)
    {
        const wchar_t* name = DialogStatus() == kStatusOk ? DialogText() : nullptr;
        const bool named = name && name[0];
        const unsigned deck = editor->DeckId;
        const bool isNew = editor->IsNewDeck != 0;

        reinterpret_cast<void(__fastcall*)(ScreenDeckEditor*)>(orig_OnSaveDialogResult)(editor);

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
    void __fastcall Hook_BuildDeckOptions(ScreenDeckEditor* editor, uint8_t* menu)
    {
        reinterpret_cast<void(__fastcall*)(ScreenDeckEditor*, uint8_t*)>(orig_BuildDeckOptions)(editor, menu);
        const MenuItems current = *reinterpret_cast<const MenuItems*>(menu + 416);
        const size_t count = current.Last - current.First;
        std::vector<MenuItem> items(current.First, current.Last);
        if (count)
            items.push_back({ kActionEditTags, -1, reinterpret_cast<int64_t>(L"Edit Tags"), 0 });
        items.push_back({ kActionFilter, -1, reinterpret_cast<int64_t>(g_Filter.Empty() ? L"Filter Decks" : L"Filter Decks (on)"), 0 });
        const MenuItems all{ items.data(), items.data() + items.size(), items.data() + items.size() };
        SetMenuItems(menu, &all);   // copies them
    }

    void __fastcall Hook_DoMenuAction(ScreenDeckEditor* editor, int action)
    {
        if (action == kActionEditTags)
            EnterPanel(editor, Mode::EditTags);
        else if (action == kActionFilter)
            EnterPanel(editor, Mode::Filter);
        else
            reinterpret_cast<void(__fastcall*)(ScreenDeckEditor*, int)>(orig_DoMenuAction)(editor, action);
    }

    // State 6 goes to state 3 (the card grid) when the panel closes; here it goes back to where it was opened from instead.
    void __fastcall Hook_Update(ScreenDeckEditor* editor, int64_t context, double seconds)
    {
        reinterpret_cast<void(__fastcall*)(ScreenDeckEditor*, int64_t, double)>(orig_Update)(editor, context, seconds);
        if (g_Mode == Mode::None)
            return;
        if (editor->State == kStateTrunk)
            LeavePanel(editor);
        else if (editor->State == kStateFilterPanel)
            UpdatePreview(editor);
    }

    // Every visit to the editor starts with the whole list.
    void __fastcall Hook_OnEnter(ScreenDeckEditor* editor)
    {
        if (g_Mode != Mode::None)
        {
            RestorePlayerFilter();
            g_Mode = Mode::None;
        }
        g_Filter = Tags();
        reinterpret_cast<void(__fastcall*)(ScreenDeckEditor*)>(orig_OnEnter)(editor);
    }

    // The editor's panel is mode 1, which has no source row: give it the one modes 0, 2 and 4 get, from the same layout.
    int64_t __fastcall Hook_SetupWidgets(ScreenDeckEditor* editor)
    {
        const int64_t result = reinterpret_cast<int64_t(__fastcall*)(ScreenDeckEditor*)>(orig_SetupWidgets)(editor);
        widget_RecipeInfo& panel = editor->DeckInfoPanel;
        if (panel.SourceHeading.Holder || panel.SourceLine.Holder)
            return result;
        const volatile uintptr_t panelDesc = kPanelDesc;
        const float width = *reinterpret_cast<const float*>(panelDesc + 28) - 30.0f;
        const auto build = [&](uintptr_t desc, RecipeInfoPart& part, int z)
        {
            NodeRef parent = panel.Body;
            if (parent.Control)
                _InterlockedIncrement(parent.Control + 2);   // the builder takes a reference and drops it
            BuildPanelPart(reinterpret_cast<const void*>(desc), &part, &parent, z, width);
            SetEnabled(part.Holder, 0);
        };
        build(kHeadingDesc, panel.SourceHeading, 9);
        build(kLineDesc, panel.SourceLine, 10);
        const volatile uintptr_t lineDesc = kLineDesc;
        const float* linePosition = reinterpret_cast<const float*>(lineDesc);
        SetNodePosition(panel.SourceLine.Holder, linePosition[0], linePosition[1] + kTagLineDrop);
        panel.SourceLine.TextLayer->Spec.LineScale = g_LineSpacing / 100.0f;
        panel.SourceLine.TextLayer->RebuildStamp = 0;
        return result;
    }

    void __fastcall Hook_ShowSelectedDeck(ScreenDeckEditor* editor)
    {
        reinterpret_cast<void(__fastcall*)(ScreenDeckEditor*)>(orig_ShowSelectedDeck)(editor);
        ShowTags(editor);
    }

    // Confirming an option makes it the row's only pick for cards. A deck can have several: it adds. Confirming a picked one still takes it off
    // (the panel checks that before it gets here).
    int64_t __fastcall Hook_SetRowPick(TrunkFilterState* state, int row, char option)
    {
        if (g_Mode == Mode::None || row < 0 || row >= kRows)
            return reinterpret_cast<int64_t(__fastcall*)(TrunkFilterState*, int, char)>(orig_SetRowPick)(state, row, option);
        state->Picks[row] |= 1ull << option;
        state->ChangedFlags = -1;
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
    tags.Picks[0] = clean.empty() ? 0 : 1;
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
    return it == g_Tags.end() ? 0 : it->second.Picks[row];
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
    const std::string ini = Save::GameFolder() + "Config.ini";
    const int spacing = static_cast<int>(GetPrivateProfileIntA("Yu-Gi-Oh-Core", "DeckTagIconSpacing", 2, ini.c_str()));
    g_IconSpacing = spacing < 0 ? 0 : spacing > 6 ? 6 : spacing;
    const int lineSpacing = static_cast<int>(GetPrivateProfileIntA("Yu-Gi-Oh-Core", "DeckTagLineSpacing", 140, ini.c_str()));
    g_LineSpacing = lineSpacing < 100 ? 100 : lineSpacing > 300 ? 300 : lineSpacing;
    const int maxLines = static_cast<int>(GetPrivateProfileIntA("Yu-Gi-Oh-Core", "DeckTagMaxLines", 4, ini.c_str()));
    g_MaxLines = maxLines < 1 ? 1 : maxLines > 8 ? 8 : maxLines;
    g_ShowText = GetPrivateProfileIntA("Yu-Gi-Oh-Core", "DeckTagShowText", 0, ini.c_str()) != 0;

    const Site sites[] = {
        { &orig_OnSaveDialogResult, Hook_OnSaveDialogResult },
        { &orig_BuildDeckOptions, Hook_BuildDeckOptions },
        { &orig_DoMenuAction, Hook_DoMenuAction },
        { &orig_Update, Hook_Update },
        { &orig_OnEnter, Hook_OnEnter },
        { &orig_SetupWidgets, Hook_SetupWidgets },
        { &orig_ShowSelectedDeck, Hook_ShowSelectedDeck },
        { &orig_SetRowPick, Hook_SetRowPick },
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
