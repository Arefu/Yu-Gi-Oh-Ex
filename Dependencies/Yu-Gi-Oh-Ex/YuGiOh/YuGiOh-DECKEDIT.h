#pragma once
#include <cstddef>
#include <cstdint>

// The deck editor screen and its card filter panel, with the same names and layout as the IDB's types (YuGiOh.exe.i64). Only what has been traced is
// named; the rest is padding. Traced 2026-11-02, docs/UserDecks.md. The static_asserts below keep this in step with the game.
namespace YGO::DECKEDIT
{
    // The game's std::wstring. Plugin code can't use its own: a Debug build's std::wstring has a different layout.
    struct GameWString
    {
        union
        {
            wchar_t Buffer[8];
            wchar_t* Pointer;
        };
        size_t Size;
        size_t Capacity;

        const wchar_t* Data() const { return Capacity >= 8 ? Pointer : Buffer; }
    };

    constexpr int kFilterRows = 15, kRowValues = 5;

    // g_TrunkFilterState (0x140C91C60): the card filter panel's choices. The game has one, shared by the card grid.
    struct TrunkFilterState
    {
        uint32_t RowsOn;                              // a bit per row: the row is applied
        uint32_t RowsOnPad;
        uint64_t Picks[kFilterRows];                  // a bit per option of the row. On a number row, the comparisons (NumberPick)
        uint64_t CardBits[kFilterRows];               // what those options test a card against
        int32_t ChangedFlags;                         // -1 = changed; TrunkFilterState_TakeChanged clears a bit
        int32_t Values[kFilterRows][kRowValues];      // a number row's values, by NumberPick index
        GameWString NameText;                         // row 0
        GameWString NameTextFolded;
    };

    // TrunkFilterState.Picks on a number row (TrunkFilterState_NumberPasses 0x1408700B0). Values[row][n] goes with bit n; Between uses 3 and 4.
    enum NumberPick : uint64_t
    {
        PickEqual = 1,      // value == Values[0]
        PickAtMost = 2,     // value <= Values[1]
        PickAtLeast = 4,    // value >= Values[2]
        PickBetween = 8,    // Values[3] <= value <= Values[4]
        PickUnknown = 0x10, // the card's value is "?" (kValueUnknown)
    };
    constexpr int32_t kValueUnknown = 2040114126;

    struct TrunkFilterOption
    {
        int32_t Bit;
        int32_t Pad04;
        int64_t Text;       // a string id or a wchar_t*
        void* TextFn;
        int32_t Icon;       // -1 = none; U+E000 + icon is the inline icon
        int32_t Pad1C;
        uint64_t CardBit;
    };

    // g_TrunkFilterRows (0x140A7A260): the panel's rows
    struct TrunkFilterRow
    {
        int32_t Id;
        int32_t Icon;
        int64_t Title;
        uint64_t OptionCount;
        TrunkFilterOption* Options;
        void* MaskValueFn;      // a card's bits, for an icon row
        void* NumberValueFn;    // a card's number, for a number row
        int32_t ValueMin, ValueMax, ValueStep, Pad3C;
        void* ValueLabels;
    };

    struct TTextSpec
    {
        void* Vftable;
        uint32_t Colour, ColourBottom, Colour2, Colour2Bottom;
        uint32_t Flags;
        float WrapWidth;
        int32_t FontId;
        float LineScale;    // YGO::TEXT::Layout_Build multiplies the font's line height by this
        int32_t Align;
        int32_t Pad2C;
    };

    struct TLayerText
    {
        char TLayerBase[152];
        TTextSpec Spec;
        GameWString Text;
        int64_t TextId;
        char HasTextId;
        char PadF1[7];
        char Layout[96];
        int32_t RebuildStamp;   // 0 = lay the text out again
        int32_t Pad15C;
    };

    struct WidgetBase
    {
        void* Vftable;
        char Focused;
        char Pad09[7];
        void* Node;
        void* NodeRef;
        char Rest[16];
    };

    struct NodeRef   // shared_ptr<DFX::TBase>
    {
        void* Node;
        long* Control;
    };

    struct RecipeInfoPart
    {
        void* Holder;
        void* HolderRef;
        TLayerText* TextLayer;
        void* TextLayerRef;
    };

    // The deck panel beside a deck list. Which parts exist depends on Mode (RIX::widget_RecipeInfo::CreateFromLayout): an unbuilt part is all null.
    struct widget_RecipeInfo
    {
        WidgetBase Base;
        NodeRef Body;
        char Background[32];
        RecipeInfoPart Caption, Portrait, Frame, DeckTypeHeading, DeckType, SourceHeading, SignatureHeading, OwnerName, DeckName, SourceLine, SignatureCard, Validity, Extra1, Extra2;
        int32_t Pad220[2];
        char HasSourceRows, HasSignatureRows;
        char Pad22A[2];
        int32_t Mode;
        char Rest[24];
    };

    struct widget_TrunkFilterMaskOptions
    {
        char Before[680];
        char OptionList[56];
        const TrunkFilterRow* Row;
        char After[8];
    };

    struct widget_TrunkFilterNumOptions
    {
        char Before[2464];
        int32_t SelectedSpinner;
        int32_t HasFifthOption;
        const TrunkFilterRow* Row;    // TrunkFilterNumOptions_SetRow re-reads the row's values only when this changes
    };

    struct widget_TrunkFilterCategories
    {
        void* Vftable;
        char Focused;
        char Pad09[103];
        char ScrollBar[160];
        char ButtonHint[48];
        char RowWidgets[24];
        char ResetButton[168];
        char ToggleButton[168];
        widget_TrunkFilterMaskOptions MaskOptions;
        widget_TrunkFilterNumOptions NumOptions;
        char RowList[56];
        int32_t Mode;
        int32_t PadF84;
    };

    // RIX::ScreenDeckEditor
    struct ScreenDeckEditor
    {
        char ScreenBase[264];
        char Help[168];                 // widget_Help
        char Gap1B0[224];
        int32_t State;                  // DeckEdit_ApplyState's switch
        int32_t Pad294;
        char StateStack[40];            // ScreenDeckEditor_PushState / PopState
        int32_t HeaderMode;
        uint32_t DeckId;                // the deck being edited
        char Gap2C8[1232];
        char ConfirmMenu[520];
        char ColumnsSlide[808];         // the tween that slides the editor's columns (ScreenDeckEditor_SlideTo)
        char DeckOptionsMenu[2936];
        char UserDeckZone[928];         // widget_UserDeckZone
        char UserDeckZoneSlide[272];    // the tween that places it
        widget_RecipeInfo DeckInfoPanel;
        char RecipeZone[1408];
        widget_RecipeInfo RecipeZoneInfo;
        char Trunk[1632];               // widget_TrunkZone
        widget_TrunkFilterCategories FilterPanel;
        char Gap3CE8[33424];
        char IsNewDeck;                 // a new or copied deck, 0 for a rename
        char PadBF79[7];
    };

    static_assert(sizeof(GameWString) == 32);
    static_assert(offsetof(TrunkFilterState, ChangedFlags) == 248 && offsetof(TrunkFilterState, Values) == 252 && offsetof(TrunkFilterState, NameText) == 552);
    static_assert(sizeof(TrunkFilterOption) == 40 && sizeof(TrunkFilterRow) == 72);
    static_assert(offsetof(TTextSpec, LineScale) == 36 && sizeof(TTextSpec) == 48);
    static_assert(offsetof(TLayerText, Text) == 200 && offsetof(TLayerText, RebuildStamp) == 344 && sizeof(TLayerText) == 0x160);
    static_assert(offsetof(WidgetBase, Node) == 16 && sizeof(WidgetBase) == 48);
    static_assert(offsetof(widget_RecipeInfo, SourceHeading) == 256 && offsetof(widget_RecipeInfo, SourceLine) == 384 && offsetof(widget_RecipeInfo, Mode) == 556 && sizeof(widget_RecipeInfo) == 584);
    static_assert(offsetof(widget_TrunkFilterNumOptions, Row) == 2472 && sizeof(widget_TrunkFilterNumOptions) == 2480);
    static_assert(offsetof(widget_TrunkFilterCategories, NumOptions) == 1432 && offsetof(widget_TrunkFilterCategories, Mode) == 3968);
    static_assert(offsetof(ScreenDeckEditor, State) == 656 && offsetof(ScreenDeckEditor, DeckId) == 708 && offsetof(ScreenDeckEditor, ColumnsSlide) == 2464);
    static_assert(offsetof(ScreenDeckEditor, UserDeckZone) == 6208 && offsetof(ScreenDeckEditor, UserDeckZoneSlide) == 7136 && offsetof(ScreenDeckEditor, DeckInfoPanel) == 7408);
    static_assert(offsetof(ScreenDeckEditor, Trunk) == 9984 && offsetof(ScreenDeckEditor, FilterPanel) == 11616 && offsetof(ScreenDeckEditor, IsNewDeck) == 49016);
}
