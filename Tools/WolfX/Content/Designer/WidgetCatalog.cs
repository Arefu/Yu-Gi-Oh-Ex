using System.Drawing.Drawing2D;

namespace WolfEx.Designer
{
    /// <summary>How far Yu-Gi-Oh-RIX can put a widget on a page in the game today.</summary>
    internal enum InGame
    {
        /// <summary>Loaded from the page file and built in the game.</summary>
        Yes,
        /// <summary>RIX (or its header YuGiOh-RIX.h) can build it, but only from a plugin's code; the page file does not build it yet.</summary>
        Code,
        /// <summary>Preview only: the class is known (IDA) but nothing in RIX builds it yet.</summary>
        No,
    }

    internal delegate void WidgetPainter(Graphics g, PageElement element, GameArt art);

    /// <summary>One kind of thing that can go on a page: a game widget class (RTTI name from YuGiOh.exe) and how the designer draws it.</summary>
    internal sealed class WidgetKind
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public required string Category { get; init; }
        public required string GameClass { get; init; }
        public InGame Support { get; init; } = InGame.No;
        public string Notes { get; init; } = "";
        public SizeF Size { get; init; } = new(300, 120);
        public string? DefaultText { get; init; }
        public float DefaultTextSize { get; init; } = 30;
        public bool HasText => DefaultText != null;
        public bool HasButtons { get; init; }
        public bool HasSprite { get; init; }
        public bool HasAction { get; init; }
        public bool HasHighlight { get; init; }
        public bool HasTextStyle { get; init; }
        public bool KeepAspect { get; init; }
        public required WidgetPainter Paint { get; init; }

        public string SupportText => Support switch
        {
            InGame.Yes => "shows in game",
            InGame.Code => "not from a page yet",
            _ => "designer only",
        };

        /// <summary>The same, spelled out (tooltips, the info box).</summary>
        public string SupportExplained => Support switch
        {
            InGame.Yes => "Shows in the game when the page opens.",
            InGame.Code => "Does NOT show from a page file yet. The game has this widget and a C++ plugin can place it (YuGiOh-RIX.h), but the page loader does not build it yet.",
            _ => "Does NOT show in the game: nothing can build this widget yet. It is here to plan layouts.",
        };
    }

    /// <summary>
    /// Every widget class the game has (the 112 widget_* vftables in YuGiOh.exe.i64, see docs/Widgets.md), grouped as in that document.
    /// Kinds with known art draw it from the game's archive; the art of the rest is a best guess from their names, or a labelled box.
    /// </summary>
    internal static class WidgetCatalog
    {
        public const string Category_Page = "Page (built by RIX)";
        public const string Category_Menu = "Menus and text";
        public const string Category_Input = "Settings and input";
        public const string Category_Dialog = "Dialogs and prompts";
        public const string Category_Cards = "Cards and decks";
        public const string Category_Lists = "Screens, lists and panels";
        public const string Category_Duel = "Duel (YGO_FRONT)";
        public const string Category_Art = "Pictures";

        public static readonly string[] Categories =
            [Category_Page, Category_Menu, Category_Input, Category_Dialog, Category_Cards, Category_Lists, Category_Duel, Category_Art];

        // The page's menu buttons: RIX lays them out 100 px apart, centred on ButtonsX, the first one's centre at ButtonsY.
        public const float ButtonSpacing = 100f;
        public const float ButtonWidth = 660f;

        public static readonly IReadOnlyList<WidgetKind> All = Build();

        private static readonly Dictionary<string, WidgetKind> ById = All.ToDictionary(k => k.Id, StringComparer.OrdinalIgnoreCase);

        public static WidgetKind? Find(string id) => ById.GetValueOrDefault(id);

        /// <summary>Sizes the game decides: a button list is always 100 px per button (RIX lays them out that way).</summary>
        public static void Normalise(PageDocument document)
        {
            foreach (var element in document.Elements)
            {
                if (Find(element.Kind)?.HasButtons == true)
                    element.Height = Math.Max(1, element.Buttons?.Count ?? 0) * ButtonSpacing;
            }
        }

        private static List<WidgetKind> Build()
        {
            var kinds = new List<WidgetKind>
            {
                // ---- what a RIX page file builds ----
                new()
                {
                    Id = "header", Name = "Page header", Category = Category_Page, GameClass = "RIX::widget_Title (RIX::Screen::SetHeaderText 0x140822F00)",
                    Support = InGame.Yes, Size = new(962, 131), DefaultText = "Card Store", DefaultTextSize = 44,
                    Notes = "The title at the top of the screen. The game always draws it in its own place; the page file only sets the text.",
                    Paint = (g, e, art) => { Sprite(g, art, "pdui/doShared", "menuheader", e); Text(g, e, e.Bounds, 44); },
                },
                new()
                {
                    Id = "buttonList", Name = "Menu buttons (1-4)", Category = Category_Page, GameClass = "RIX::MenuKit::menu + MenuKit::widget_Item",
                    Support = InGame.Yes, Size = new(ButtonWidth, ButtonSpacing * 2), HasButtons = true,
                    Notes = "The page's buttons, top to bottom, 100 px apart (RIX_PageDesc::ButtonsX / ButtonsY): only the position counts in the game, not the width. Up to four; one list per page. Each button's action uses the menu files' syntax, e.g. {\"page\": \"other\"}.",
                    Paint = PaintButtonList,
                },
                new()
                {
                    Id = "description", Name = "Button description", Category = Category_Page, GameClass = "RIX::MenuKit::widget_Description",
                    Support = InGame.No, Size = new(1200, 60), DefaultText = "What the highlighted button does.", DefaultTextSize = 26,
                    Notes = "Shown by the game under its menu for the highlighted button (each button's Description). Its place is the game's; placing it is preview only.",
                    Paint = (g, e, art) => { Fill(g, e.Bounds, Color.FromArgb(120, 0, 0, 0)); Text(g, e, e.Bounds, 26); },
                },
                new()
                {
                    Id = "helpBar", Name = "Button prompt bar", Category = Category_Page, GameClass = "RIX::widget_Help (screen + 264) / widget_ButtonHelp",
                    Support = InGame.Code, Size = new(560, 48), DefaultText = "Select      Back", DefaultTextSize = 24,
                    Notes = "The prompts at the bottom of every screen. YuGiOh-RIX.h has HelpClear / HelpAdd / HelpLayout.",
                    Paint = (g, e, art) =>
                    {
                        var r = e.Bounds;
                        float icon = Math.Min(36, r.Height);
                        g.DrawSprite(art, "pdui/STEAM_icons", "ICON_ID_BUTTON_A", new RectangleF(r.X, r.Y + (r.Height - icon) / 2, icon, icon));
                        g.DrawSprite(art, "pdui/STEAM_icons", "ICON_ID_BUTTON_B", new RectangleF(r.X + r.Width / 2, r.Y + (r.Height - icon) / 2, icon, icon));
                        Text(g, e, new RectangleF(r.X + icon + 8, r.Y, r.Width - icon - 8, r.Height), 24, centred: false);
                    },
                },
                new()
                {
                    Id = "dialog", Name = "Message / Yes-No box", Category = Category_Page, GameClass = "RIX::widget_Dialog (screen + 432)",
                    Support = InGame.Code, Size = new(1000, 420), DefaultText = "This password is not for a card.", DefaultTextSize = 32, HasHighlight = true,
                    Notes = "ShowMessageText / ShowYesNo (YuGiOh-RIX.h). Always centred by the game; place it to preview. Highlighted = Yes/No buttons.",
                    Paint = PaintDialog,
                },
                new()
                {
                    Id = "entryDigit", Name = "Digit wheel", Category = Category_Page, GameClass = "widget_EntryDigit",
                    Support = InGame.Code, Size = new(110, 260), DefaultText = "0", DefaultTextSize = 64, HasHighlight = true,
                    Notes = "One 0-9 wheel with arrows (the Live code entry). YuGiOh-RIX.h EntryDigit::CreateFromLayout; BetterCardShop's password page uses eight.",
                    Paint = PaintDigit,
                },
                new()
                {
                    Id = "trunk", Name = "Card trunk", Category = Category_Page, GameClass = "RIX::widget_TrunkZone",
                    Support = InGame.Code, Size = new(1300, 860),
                    Notes = "The deck editor's card grid. YuGiOh-RIX.h Trunk::CreateFromLayout; BetterCardShop fills it with every card. Drawn with placeholder cards.",
                    Paint = PaintTrunk,
                },
                new()
                {
                    Id = "cardInfo", Name = "Card info panel", Category = Category_Page, GameClass = "RIX::widget_CardInfo (0x140883380)",
                    Support = InGame.Code, Size = new(486, 840), DefaultText = "Card name", DefaultTextSize = 28,
                    Notes = "The card picture and text on the left of the deck editor. YuGiOh-RIX.h CardInfo::CreateFromLayout / SetWidth / SetCard.",
                    Paint = PaintCardInfo,
                },

                // ---- more of the game's widgets ----
                new()
                {
                    Id = "menuButton", Name = "Single menu button", Category = Category_Menu, GameClass = "RIX::MenuKit::widget_Item",
                    Size = new(ButtonWidth, 100), DefaultText = "Button", DefaultTextSize = 34, HasHighlight = true, HasAction = true,
                    Notes = "One menu button on its own. The game only lays them out in its menus, so a free-standing one is preview only; use Menu buttons.",
                    Paint = (g, e, art) => PaintButton(g, art, e.Bounds, e.Text ?? "", e.Highlighted == true, e.TextSize ?? 34),
                },
                new()
                {
                    Id = "text", Name = "Text", Category = Category_Menu, GameClass = "RIX::widget_Text (SetTextById 0x14075E000)",
                    Support = InGame.Yes, Size = new(500, 60), DefaultText = "Some text", DefaultTextSize = 30, HasTextStyle = true,
                    Notes = "Text in the game's UI font (FONT_ID_PD, the closest size scaled), wrapped to the box's width, aligned left / center / right. " +
                            "Built by RIX as a DFX::TLayerText. The designer draws it in Segoe UI, so the exact width differs a little.",
                    Paint = PaintText,
                },
                Placeholder("textFrame", "Framed text", Category_Menu, "RIX::widget_TextFrame", new(600, 160), "pdui/menuframe"),
                Placeholder("blockText", "Block text", Category_Menu, "widget_BlockText", new(600, 200)),
                Placeholder("textCrawl", "Crawling text", Category_Menu, "YGO_FRONT::widget_TextCrawl", new(800, 60)),
                Placeholder("textScroll", "Scrolling text", Category_Menu, "YGO_FRONT::widget_TextScroll", new(600, 300)),
                Placeholder("title", "Screen title", Category_Menu, "widget_Title", new(962, 131), "pdui/doShared", "menuheader"),
                Placeholder("commandMenu", "Command menu", Category_Menu, "RIX::widget_CommandMenu", new(420, 300), "pdui/menuframe"),
                Placeholder("commandMenuItem", "Command menu item", Category_Menu, "RIX::widget_CommandMenuItem", new(400, 56), "pdui/shared/menu_highlight"),
                Placeholder("menuItemDuel", "Duel menu item", Category_Menu, "YGO_FRONT::widget_MenuItem", new(420, 60), "pdui/shared/menu_highlight"),

                new()
                {
                    Id = "tickLine", Name = "Volume ticks (10)", Category = Category_Input, GameClass = "RIX::ScreenHelpSetting::tickLine_t",
                    Size = new(937, 116),
                    Notes = "The settings screen's 10-step bar (pdui/settings Level1..Level10 at x = 949 + 49 i). Traced in IDA; not built by RIX yet.",
                    Paint = PaintTicks,
                },
                Placeholder("settingsLine", "Settings row", Category_Input, "RIX::widget_SettingsLine", new(900, 99), "pdui/shared/optionbox"),
                new()
                {
                    Id = "entryCode", Name = "Code entry (8 digits)", Category = Category_Input, GameClass = "widget_EntryCode",
                    Size = new(900, 260), DefaultText = "12345678", DefaultTextSize = 64,
                    Notes = "The Live session code: a row of digit wheels.",
                    Paint = PaintCode,
                },
                Placeholder("keyEntry", "Key binding cell", Category_Input, "RIX::widget_KeyEntry", new(237, 99), "pdui/shared/optionbox"),
                Placeholder("category", "Controls category", Category_Input, "RIX::ScreenHelpControls::widget_Category", new(600, 60), "pdui/shared/tab-title-box"),
                Placeholder("keyMapping", "Controls key row", Category_Input, "RIX::ScreenHelpControls::widget_KeyMapping", new(900, 60), "pdui/shared/thin_box"),
                Placeholder("userSlot", "Profile slot", Category_Input, "widget_UserSlot", new(448, 44), "pdui/hud", "username_frame_blue"),
                Placeholder("userSlotManager", "Profile slots", Category_Input, "RIX::widget_UserSlotManager", new(460, 200)),
                Placeholder("user", "User", Category_Input, "widget_User", new(219, 96), "pdui/shared/gamertag_blue"),
                new()
                {
                    Id = "barV", Name = "Scroll bar (vertical)", Category = Category_Input, GameClass = "RIX::widget_BarV",
                    Size = new(22, 567), Notes = "Scroll bar (pdui/shared/scrollbar_blue_track / _thumb).",
                    Paint = (g, e, art) =>
                    {
                        var r = e.Bounds;
                        Picture(g, art, "pdui/shared/scrollbar_blue_track", r);
                        Picture(g, art, "pdui/shared/scrollbar_blue_thumb", new RectangleF(r.X + (r.Width - 18) / 2, r.Y + r.Height * 0.2f, 18, 38));
                    },
                },
                new()
                {
                    Id = "barH", Name = "Bar (horizontal)", Category = Category_Input, GameClass = "RIX::widget_BarH",
                    Size = new(567, 22), Notes = "Horizontal bar / gauge.",
                    Paint = (g, e, art) =>
                    {
                        var r = e.Bounds;
                        var state = g.Save();
                        g.TranslateTransform(r.X, r.Bottom);
                        g.RotateTransform(-90);
                        Picture(g, art, "pdui/shared/scrollbar_red_track", new RectangleF(0, 0, r.Height, r.Width));
                        g.Restore(state);
                    },
                },

                Placeholder("dialogItem", "Dialog button", Category_Dialog, "RIX::widget_Dialog::dialogItem_t", new(400, 70), "pdui/shared/menu_highlight"),
                Placeholder("dialogOptions", "Dialog options", Category_Dialog, "RIX::widget_DialogOptions", new(420, 300), "pdui/menuframe"),
                Placeholder("swipeDialog", "Sliding dialog", Category_Dialog, "RIX::widget_SwipeDialog", new(900, 400), "pdui/menuframe"),
                Placeholder("dialogWindow", "Duel dialog frame", Category_Dialog, "YGO_FRONT::widget_DialogWindow", new(1050, 500), "pdui/menuframe"),
                Placeholder("dialogButton", "Duel dialog button", Category_Dialog, "YGO_FRONT::widget_DialogButton", new(300, 70), "pdui/shared/box_blue_frame"),
                Placeholder("help", "Help text", Category_Dialog, "RIX::widget_Help", new(800, 48)),
                Placeholder("helpItem", "Command help item", Category_Dialog, "RIX::widget_CommandMenu::widget_helpItem", new(300, 48)),
                Placeholder("campaignPanel", "Story dialogue panel", Category_Dialog, "RIX::ScreenCampaignDialog::widget_Panel", new(1342, 310), "pdui/campaign_dialog/dialogue_panel", "dialogue_panel01"),

                new()
                {
                    Id = "card", Name = "Card", Category = Category_Cards, GameClass = "RIX::widget_Card",
                    Size = new(200, 290), KeepAspect = true, Notes = "One card (drawn as a placeholder, no card art is loaded).",
                    Paint = (g, e, art) => PaintCard(g, e.Bounds, e.Highlighted == true),
                },
                Grid("cardDisplay", "Card grid", "RIX::widget_CardDisplay", new(900, 600)),
                Grid("cardDisplayFlex", "Card grid (flexible)", "RIX::widget_CardDisplayFlex", new(900, 600)),
                Placeholder("cardDetails", "Card details", Category_Cards, "widget_CardDetails", new(486, 840), "pdui/menuframe"),
                Placeholder("cardDetailsDuel", "Card details (duel)", Category_Cards, "widget_CardDetailsDuel", new(486, 840), "pdui/menuframe"),
                Placeholder("cardDetailsFull", "Card details (full)", Category_Cards, "widget_CardDetailsFull", new(1300, 840), "pdui/menuframe"),
                Placeholder("cardSortBar", "Card sort bar", Category_Cards, "RIX::widget_CardSortBar", new(600, 60), "pdui/shared/menu_sortwindow"),
                Placeholder("userDeckSortBar", "Deck sort bar", Category_Cards, "RIX::widget_UserDeckSortBar", new(600, 60), "pdui/shared/menu_sortwindow"),
                Grid("deckCardsZone", "Deck cards", "RIX::widget_DeckCardsZone", new(1100, 520)),
                Grid("deckCardsZoneSide", "Side deck cards", "RIX::widget_DeckCardsZoneSide", new(1100, 200)),
                Placeholder("deckItem", "Deck list row", Category_Cards, "RIX::widget_DeckItem", new(640, 64), "pdui/doShared", "frame_reward_description_inactive"),
                Placeholder("deckList", "Deck list", Category_Cards, "RIX::widget_DeckList", new(660, 700), "pdui/menuframe"),
                Placeholder("deckStats", "Deck stats", Category_Cards, "RIX::widget_DeckStats", new(500, 200)),
                Placeholder("userDeckZone", "User decks", Category_Cards, "RIX::widget_UserDeckZone", new(1100, 700)),
                Placeholder("recipeFilterBar", "Recipe filter bar", Category_Cards, "RIX::widget_RecipeFilterBar", new(800, 60)),
                Placeholder("recipeInfo", "Recipe info", Category_Cards, "RIX::widget_RecipeInfo", new(486, 600), "pdui/menuframe"),
                Grid("recipeZone", "Recipe cards", "RIX::widget_RecipeZone", new(1100, 700)),
                Placeholder("recommendedList", "Recommended list", Category_Cards, "RIX::widget_RecommendedList", new(600, 600)),
                Placeholder("trunkBookmark", "Trunk bookmark", Category_Cards, "RIX::widget_TrunkBookmark", new(140, 82), "pdui/doShared", "bookmark_wide"),
                Placeholder("trunkFilterBar", "Trunk filter bar", Category_Cards, "RIX::widget_TrunkFilterBar", new(1300, 60)),
                Placeholder("trunkFilterCategory", "Trunk filter category", Category_Cards, "RIX::widget_TrunkFilterCategory", new(300, 56), "pdui/shared/tab-title-box"),
                Placeholder("trunkFilterCategories", "Trunk filter categories", Category_Cards, "RIX::widget_TrunkFilterCategories", new(320, 500)),
                Placeholder("trunkFilterMaskOption", "Filter tick option", Category_Cards, "RIX::widget_TrunkFilterMaskOption", new(300, 56), "pdui/shared/thin_box"),
                Placeholder("trunkFilterMaskOptions", "Filter tick options", Category_Cards, "RIX::widget_TrunkFilterMaskOptions", new(320, 500)),
                Placeholder("trunkFilterNumOption", "Filter number option", Category_Cards, "RIX::widget_TrunkFilterNumOption", new(300, 56), "pdui/shared/thin_box"),
                Placeholder("trunkFilterNumOptions", "Filter number options", Category_Cards, "RIX::widget_TrunkFilterNumOptions", new(320, 500)),

                Placeholder("packZone", "Pack shelf", Category_Lists, "widget_PackZone", new(1300, 420)),
                new()
                {
                    Id = "packWrapper", Name = "Booster pack", Category = Category_Lists, GameClass = "RIX::widget_PackWrapper",
                    Size = new(211, 300), KeepAspect = true, Notes = "A pack wrapper (pdui/PackWrappers).",
                    Paint = (g, e, art) => Sprite(g, art, "pdui/PackWrappers", "wrap_1_1", e),
                },
                Placeholder("packPicker", "Pack picker", Category_Lists, "widget_PackPicker", new(900, 420)),
                Placeholder("reports", "Pack reports", Category_Lists, "RIX::ScreenBattlePackStore::widget_Reports", new(900, 500)),
                Placeholder("reportWindow", "Report window", Category_Lists, "RIX::widget_ReportWindow", new(900, 500), "pdui/menuframe"),
                Placeholder("reportItem", "Report item", Category_Lists, "RIX::widget_ReportWindow::widget_ReportItem", new(640, 64), "pdui/doShared", "frame_reward_description_inactive"),
                Placeholder("campaignDuel", "Campaign duel", Category_Lists, "RIX::widget_CampaignDuel", new(218, 220), "pdui/doShared", "frame_reward_main_inactive"),
                Placeholder("campaignRewards", "Campaign rewards", Category_Lists, "RIX::widget_CampaignRewards", new(700, 240)),
                Placeholder("rewardWindow", "Reward window", Category_Lists, "RIX::widget_RewardWindow", new(640, 400), "pdui/menuframe"),
                Placeholder("standby", "Result standby", Category_Lists, "RIX::ScreenGameResult::widget_Standby", new(500, 120)),
                Placeholder("playerName", "Player name", Category_Lists, "RIX::widget_PlayerName", new(219, 96), "pdui/shared/gamertag_blue"),
                Placeholder("duelBonusPoints", "Duel bonus points", Category_Lists, "RIX::widget_DuelBonusPoints", new(640, 64), "pdui/doShared", "frame_reward_description_active"),
                Placeholder("faceBox", "Face box", Category_Lists, "RIX::widget_FaceBox", new(128, 128), "pdui/chars", "Playmaker_neutral"),
                Placeholder("duelStatsPItem", "Duel stats (player)", Category_Lists, "RIX::widget_DuelStatsPItem", new(92, 90), "pdui/shared/P1_resultbox"),
                Placeholder("duelStatsEItem", "Duel stats (opponent)", Category_Lists, "RIX::widget_DuelStatsEItem", new(92, 90), "pdui/shared/P2_resultbox"),
                Placeholder("leaderboardCategory", "Leaderboard category", Category_Lists, "RIX::ScreenLiveLeaderboard::widget_Category", new(300, 56), "pdui/shared/tab-title-box"),
                Placeholder("leaderboardRow", "Leaderboard row", Category_Lists, "RIX::ScreenLiveLeaderboard::widget_Row", new(1200, 56), "pdui/shared/thin_box"),
                Placeholder("session", "Lobby session", Category_Lists, "RIX::widget_Session", new(900, 64), "pdui/shared/thin_box"),
                Placeholder("sessionInfo", "Lobby session info", Category_Lists, "RIX::widget_SessionInfo", new(600, 300)),
                Placeholder("rungBody", "Ladder rung", Category_Lists, "RIX::ScreenSelectRung::widget_Body", new(600, 300)),
                Placeholder("tutorialItem", "Tutorial item", Category_Lists, "RIX::ScreenSelectTutorial::widget_Item", new(59, 51), "pdui/tut_item_frame"),
                // Free Duel (screen 21, RIX::ScreenHardChallenge): series tabs (+656) -> opponent list (+904) -> deck picker (+1456) with the two
                // deck panels (+2864 yours, +3448 the opponent's). YuGiOh-RIX.h YGO::RIX::FreeDuel / DeckSelectList / DeckInfoPanel.
                Placeholder("deckSelectList", "Deck picker (tabs)", Category_Lists, "DeckSelectList (Free Duel +1456, campaign deck +664)", new(900, 600), "pdui/menuframe"),
                Placeholder("deckInfoPanel", "Deck info panel", Category_Lists, "DeckInfoPanel (Free Duel +2864 / +3448, campaign deck +2072)", new(480, 600), "pdui/menuframe"),
                Placeholder("pickCharacter", "Character picker", Category_Lists, "RIX::widget_pickCharacter", new(900, 400)),
                Placeholder("pickCharacterItem", "Character", Category_Lists, "RIX::widget_pickCharacter::widget_Item", new(146, 197), "pdui/shared/opponentframe_normal"),
                Placeholder("pickSeries", "Series picker", Category_Lists, "RIX::widget_pickSeries", new(400, 397), "pdui/SeriesLogo", "logo_vrains"),
                Placeholder("liveSettingItem", "Live setting row", Category_Lists, "RIX::ScreenLiveSetting::widget_Item", new(900, 99), "pdui/shared/optionbox"),
                new()
                {
                    Id = "loadSpinner", Name = "Loading spinner", Category = Category_Lists, GameClass = "widget_LoadSpinner",
                    Size = new(134, 134), KeepAspect = true, Notes = "Spinner (drawn with the duel timer ring).",
                    Paint = (g, e, art) => Sprite(g, art, "pdui/hud", "icon_timer_outer", e),
                },
                Placeholder("hurry", "Hurry timer", Category_Lists, "widget_Hurry", new(134, 134), "pdui/hud", "icon_timer"),

                Placeholder("playerHud", "Player HUD", Category_Duel, "YGO_FRONT::widget_PlayerHUD", new(448, 100), "pdui/hud", "username_frame_blue"),
                Placeholder("playerHudFace", "HUD face", Category_Duel, "YGO_FRONT::widget_PlayerHUD::widget_Face", new(128, 128), "pdui/chars", "Playmaker_neutral"),
                Placeholder("playerHudLife", "HUD life points", Category_Duel, "YGO_FRONT::widget_PlayerHUD::widget_Life", new(352, 44), "pdui/hud", "lp_frame_blue"),
                Placeholder("playerHudName", "HUD name", Category_Duel, "YGO_FRONT::widget_PlayerHUD::widget_Name", new(448, 44), "pdui/hud", "username_frame_blue"),
                Placeholder("turnHud", "Turn HUD", Category_Duel, "YGO_FRONT::widget_TurnHUD", new(140, 80), "pdui/hud", "icon_turn"),
                Placeholder("touchText", "Touch text", Category_Duel, "YGO_FRONT::widget_TouchText", new(400, 60)),
                Placeholder("cell", "Field zone", Category_Duel, "YGO_FRONT::widget_Cell", new(256, 358), "pdui/field", "monster_blue"),
                Placeholder("attributeLine", "Zone attribute line", Category_Duel, "YGO_FRONT::widget_Cell::widget_attributeLine", new(144, 50), "pdui/hud", "atk_def_frame"),
                Placeholder("pendulumZoneMark", "Pendulum scale", Category_Duel, "widget_PendulumZoneMark", new(180, 180), "pdui/pendulum", "pend_num_4"),
                Placeholder("phaseBar", "Phase bar", Category_Duel, "widget_PhaseBar", new(128, 64), "pdui/duel/PhaseBar", "blue_mp1"),
                Placeholder("playerStats", "Player stats", Category_Duel, "YGO_FRONT::widget_PlayerStats", new(500, 300)),
                Placeholder("playerStatsItem", "Player stats row", Category_Duel, "YGO_FRONT::widget_PlayerStats::widget_Item", new(500, 48)),
                Placeholder("rawDuelStats", "Duel stats", Category_Duel, "YGO_FRONT::widget_RawDuelStats", new(500, 300)),
                Placeholder("rawDuelStatsItem", "Duel stats row", Category_Duel, "YGO_FRONT::widget_RawDuelStats::widget_Item", new(500, 48)),
                Placeholder("coinItem", "Coin toss", Category_Duel, "YGO_FRONT::DUEL_DIALOG_COIN::widget_Item", new(200, 200)),
                Placeholder("diceItem", "Dice roll", Category_Duel, "YGO_FRONT::DUEL_DIALOG_DICE::widget_Item", new(200, 200)),
                Placeholder("effectTab", "Effect tab", Category_Duel, "YGO_FRONT::DUEL_DIALOG_SELECT_EFFECT::widget_Tab", new(300, 60), "pdui/shared/tab-title-box"),
                Placeholder("selectNameLine", "Card name line", Category_Duel, "YGO_FRONT::DUEL_DIALOG_SELECT_NAME::widget_Line", new(700, 56), "pdui/shared/thin_box"),
                Placeholder("selectGenericLine", "Choice line", Category_Duel, "YGO_FRONT::DUEL_DIALOG_SELECT_GENERIC::widget_Line", new(700, 56), "pdui/shared/thin_box"),
                Placeholder("windowSelectLine", "Window choice line", Category_Duel, "YGO_FRONT::WINDOW_SELECT_GENERIC::widget_Line", new(700, 56), "pdui/shared/thin_box"),
                Placeholder("phaseIcon", "Phase icon", Category_Duel, "YGO_FRONT::DUEL_DIALOG_SELECT_PHASE::widget_PhaseIcon", new(128, 64), "pdui/duel/PhaseBar", "hi_bp"),
                Placeholder("cardPos", "Battle position", Category_Duel, "YGO_FRONT::DUEL_DIALOG_SELECT_STAND::widget_CardPos", new(200, 290)),
                Placeholder("jankenFrame", "Rock-paper-scissors", Category_Duel, "YGO_FRONT::Janken::widget_selectFrame", new(288, 288), "pdui/Janken", "jnkn_player_stone_active"),
                Placeholder("jankenPlayer", "Rock-paper-scissors player", Category_Duel, "YGO_FRONT::Janken::widget_PlayerID", new(905, 242), "pdui/Janken", "vs_background"),

                new()
                {
                    Id = "image", Name = "Image (any sprite)", Category = Category_Art, GameClass = "DFX::TLayerAnimoo (MakeShared 0x14075B580)",
                    Support = InGame.Yes, Size = new(237, 99), HasSprite = true,
                    Notes = "Any sprite of any sheet in the archive (or a loose override). Built by RIX when the page shows: a DFX::TLayerAnimoo in a DFX::TBase node, top-left at x, y, stretched to width x height, drawn above the Battle Pack screen (game z = 20 + z).",
                    Paint = (g, e, art) =>
                    {
                        if (!(e.Resource != null && e.Sprite != null && g.DrawSprite(art, e.Resource, e.Sprite, e.Bounds)))
                            Box(g, e.Bounds, "pick a sprite");
                    },
                },
                new()
                {
                    Id = "panel", Name = "Panel", Category = Category_Art, GameClass = "pdui/menuframe",
                    Size = new(919, 751), Notes = "The game's framed panel picture (pdui/menuframe), stretched.",
                    Paint = (g, e, art) => { if (!Picture(g, art, "pdui/menuframe", e.Bounds)) Box(g, e.Bounds, "panel"); },
                },
            };
            return kinds;
        }

        // ---- painters ----

        private static WidgetKind Placeholder(string id, string name, string category, string gameClass, SizeF size, string? resource = null, string? sprite = null) => new()
        {
            Id = id, Name = name, Category = category, GameClass = gameClass, Size = size,
            Notes = resource == null ? "Not traced yet: drawn as a labelled box." : "Not traced yet: drawn with art that fits its name (" + resource + (sprite != null ? " " + sprite : "") + "), a guess.",
            Paint = (g, e, art) =>
            {
                bool drawn = resource != null && (sprite != null ? g.DrawSprite(art, resource, sprite, e.Bounds) : Picture(g, art, resource, e.Bounds));
                if (!drawn)
                    Box(g, e.Bounds, name);
                else
                    Caption(g, e.Bounds, name);
            },
        };

        private static WidgetKind Grid(string id, string name, string gameClass, SizeF size) => new()
        {
            Id = id, Name = name, Category = Category_Cards, GameClass = gameClass, Size = size,
            Notes = "A grid of cards (placeholders).",
            Paint = (g, e, art) => { PaintCardGrid(g, e.Bounds, 150); Caption(g, e.Bounds, name); },
        };

        private static void Sprite(Graphics g, GameArt art, string resource, string sprite, PageElement e)
        {
            if (!g.DrawSprite(art, resource, sprite, e.Bounds))
                Box(g, e.Bounds, sprite);
        }

        /// <summary>A plain picture from the archive ("pdui/menuframe" = pdui\menuframe.png).</summary>
        private static bool Picture(Graphics g, GameArt art, string resource, RectangleF rect)
        {
            var image = art.Image(resource.Replace('/', '\\') + ".png");
            if (image == null)
                return false;
            g.DrawImage(image, rect);
            return true;
        }

        private static void Text(Graphics g, PageElement e, RectangleF rect, float size, bool centred = true) =>
            g.DrawGameText(e.Text ?? "", rect, e.TextSize ?? size, Color.White, centred);

        private static void Fill(Graphics g, RectangleF rect, Color colour)
        {
            using var brush = new SolidBrush(colour);
            g.FillRectangle(brush, rect);
        }

        /// <summary>A labelled box for widgets without art.</summary>
        public static void Box(Graphics g, RectangleF rect, string label)
        {
            using (var brush = new HatchBrush(HatchStyle.BackwardDiagonal, Color.FromArgb(90, 80, 160, 220), Color.FromArgb(150, 10, 25, 50)))
                g.FillRectangle(brush, rect);
            using (var pen = new Pen(Color.FromArgb(220, 80, 190, 255), 2))
                g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
            g.DrawGameText(label, rect, Math.Clamp(Math.Min(rect.Height * 0.3f, rect.Width / Math.Max(4, label.Length) * 1.6f), 10, 34), Color.White);
        }

        private static void Caption(Graphics g, RectangleF rect, string label)
        {
            float size = Math.Clamp(rect.Height * 0.12f, 12, 22);
            g.DrawGameText(label, new RectangleF(rect.X, rect.Y, rect.Width, size * 1.6f), size, Color.FromArgb(230, 255, 255, 200));
        }

        public static void PaintButton(Graphics g, GameArt art, RectangleF rect, string label, bool highlighted, float textSize)
        {
            bool drawn = highlighted
                ? g.DrawSprite(art, "pdui/ds_btn_active", "ds_btn_active01", rect)
                : g.DrawSprite(art, "pdui/ds_btn_inactive", "ds_btn_inactive01", rect);
            if (!drawn)
                Box(g, rect, "");
            g.DrawGameText(label, rect, textSize, highlighted ? Color.FromArgb(255, 255, 240, 160) : Color.White);
        }

        private static void PaintButtonList(Graphics g, PageElement e, GameArt art)
        {
            var buttons = e.Buttons is { Count: > 0 } list ? list : [new PageButton()];
            float rowHeight = e.Height / buttons.Count;
            for (int i = 0; i < buttons.Count; i++)
            {
                var row = new RectangleF(e.X, e.Y + i * rowHeight, e.Width, rowHeight);
                var face = RectangleF.Inflate(row, 0, -rowHeight * 0.08f);
                PaintButton(g, art, face, buttons[i].Label, i == 0, e.TextSize ?? Math.Min(34, rowHeight * 0.36f));
            }
        }

        /// <summary>Colour of a text element (#RRGGBB / #AARRGGBB), white when missing.</summary>
        public static Color TextColour(string? text)
        {
            if (text is { Length: 7 or 9 } && text[0] == '#' && uint.TryParse(text[1..], System.Globalization.NumberStyles.HexNumber, null, out uint value))
                return Color.FromArgb(unchecked((int)(text.Length == 7 ? 0xFF000000 | value : value)));
            return Color.White;
        }

        // Like the game: lines from the top of the box, wrapped to its width, aligned left / center / right.
        private static void PaintText(Graphics g, PageElement e, GameArt art)
        {
            string text = e.Text ?? "";
            if (text.Length == 0)
                return;
            float size = e.TextSize ?? 30;
            using var path = new GraphicsPath();
            using var family = new FontFamily("Segoe UI");
            using var format = new StringFormat
            {
                Alignment = e.Align switch { "center" or "centre" => StringAlignment.Center, "right" => StringAlignment.Far, _ => StringAlignment.Near },
                LineAlignment = StringAlignment.Near,
            };
            path.AddString(text, family, (int)FontStyle.Bold, size, new RectangleF(e.X, e.Y, Math.Max(1, e.Width), 100000), format);
            var smoothing = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var outline = new Pen(Color.FromArgb(160, 0, 0, 0), Math.Max(1f, size / 8f)) { LineJoin = LineJoin.Round })
                g.DrawPath(outline, path);
            using (var fill = new SolidBrush(TextColour(e.Colour)))
                g.FillPath(fill, path);
            g.SmoothingMode = smoothing;
        }

        private static void PaintDialog(Graphics g, PageElement e, GameArt art)
        {
            var r = e.Bounds;
            if (!Picture(g, art, "pdui/menuframe", r))
                Box(g, r, "");
            var textRect = new RectangleF(r.X + 40, r.Y + 30, r.Width - 80, r.Height * 0.6f);
            g.DrawGameText(e.Text ?? "", textRect, e.TextSize ?? 32, Color.White);
            float bw = Math.Min(360, r.Width * 0.4f), bh = Math.Min(70, r.Height * 0.18f), by = r.Bottom - bh - 30;
            if (e.Highlighted == true)
            {
                PaintButton(g, art, new RectangleF(r.X + r.Width / 2 - bw - 20, by, bw, bh), "Yes", true, bh * 0.4f);
                PaintButton(g, art, new RectangleF(r.X + r.Width / 2 + 20, by, bw, bh), "No", false, bh * 0.4f);
            }
            else
                PaintButton(g, art, new RectangleF(r.X + (r.Width - bw) / 2, by, bw, bh), "OK", true, bh * 0.4f);
        }

        private static void PaintDigit(Graphics g, PageElement e, GameArt art) => DrawDigit(g, art, e.Bounds, (e.Text ?? "0").FirstOrDefault('0'), e.Highlighted == true, e.TextSize);

        private static void DrawDigit(Graphics g, GameArt art, RectangleF r, char digit, bool highlighted, float? textSize)
        {
            float arrowH = Math.Min(r.Height * 0.25f, r.Width * 0.87f), arrowW = arrowH / 74f * 85f;
            var box = new RectangleF(r.X, r.Y + arrowH, r.Width, r.Height - arrowH * 2);
            // arrow_1 points down (the lower arrow); the upper one is the same picture turned over
            string arrow = highlighted ? "arrow_2" : "arrow_1";
            var down = new RectangleF(r.X + (r.Width - arrowW) / 2, r.Bottom - arrowH, arrowW, arrowH);
            if (!g.DrawSprite(art, "pdui/doShared", arrow, down))
                Box(g, down, "v");
            var state = g.Save();
            g.TranslateTransform(0, r.Y * 2 + arrowH);
            g.ScaleTransform(1, -1);
            g.DrawSprite(art, "pdui/doShared", arrow, new RectangleF(down.X, r.Y, arrowW, arrowH));
            g.Restore(state);
            if (!Picture(g, art, highlighted ? "pdui/shared/box_highlight_frame" : "pdui/shared/box_blue_frame", box))
                Box(g, box, "");
            g.DrawGameText(digit.ToString(), box, textSize ?? Math.Min(box.Height * 0.55f, 64), Color.White);
        }

        private static void PaintCode(Graphics g, PageElement e, GameArt art)
        {
            string code = string.IsNullOrEmpty(e.Text) ? "00000000" : e.Text;
            int count = Math.Clamp(code.Length, 1, 12);
            float cell = e.Width / count;
            for (int i = 0; i < count; i++)
                DrawDigit(g, art, new RectangleF(e.X + i * cell + cell * 0.1f, e.Y, cell * 0.8f, e.Height), code[i], i == 0, e.TextSize);
        }

        private static void PaintTicks(Graphics g, PageElement e, GameArt art)
        {
            var r = e.Bounds;
            float sx = r.Width / 937f, sy = r.Height / 116f;
            if (!g.DrawSprite(art, "pdui/Settings", "Sound_Settings_Bar", new RectangleF(r.X, r.Y + (116 - 77) / 2f * sy, r.Width, 77 * sy)))
                Box(g, r, "ticks");
            // the game puts Level<i> at x = 949 + 49 i relative to the bar's centre line; here: evenly across the bar's right part
            for (int i = 0; i < 10; i++)
                g.DrawSprite(art, "pdui/Settings", $"Level{i + 1}", new RectangleF(r.X + (420 + 49 * i) * sx, r.Y, 36 * sx, 116 * sy));
        }

        public static void PaintCard(Graphics g, RectangleF r, bool highlighted = false)
        {
            using (var brush = new LinearGradientBrush(r, Color.FromArgb(255, 120, 80, 40), Color.FromArgb(255, 60, 35, 20), LinearGradientMode.Vertical))
                g.FillRectangle(brush, r);
            var art = new RectangleF(r.X + r.Width * 0.12f, r.Y + r.Height * 0.18f, r.Width * 0.76f, r.Width * 0.76f);
            using (var brush = new SolidBrush(Color.FromArgb(255, 30, 50, 70)))
                g.FillRectangle(brush, art);
            using (var pen = new Pen(highlighted ? Color.Gold : Color.FromArgb(255, 200, 170, 90), Math.Max(1, r.Width / 60)))
                g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
        }

        private static void PaintCardGrid(Graphics g, RectangleF r, float cardWidth)
        {
            float cw = cardWidth, ch = cw * 1.45f, gap = cw * 0.1f;
            int cols = Math.Max(1, (int)((r.Width + gap) / (cw + gap)));
            int rows = Math.Max(1, (int)((r.Height + gap) / (ch + gap)));
            float ox = r.X + (r.Width - (cols * (cw + gap) - gap)) / 2, oy = r.Y + (r.Height - (rows * (ch + gap) - gap)) / 2;
            var clip = g.Clip;
            g.SetClip(r);
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < cols; x++)
                    PaintCard(g, new RectangleF(ox + x * (cw + gap), oy + y * (ch + gap), cw, ch), x == 0 && y == 0);
            g.Clip = clip;
        }

        private static void PaintTrunk(Graphics g, PageElement e, GameArt art)
        {
            var r = e.Bounds;
            Fill(g, r, Color.FromArgb(150, 5, 15, 35));
            PaintCardGrid(g, RectangleF.Inflate(r, -20, -20), 140);
            using var pen = new Pen(Color.FromArgb(200, 60, 170, 230), 2);
            g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
        }

        private static void PaintCardInfo(Graphics g, PageElement e, GameArt art)
        {
            var r = e.Bounds;
            Fill(g, r, Color.FromArgb(190, 8, 18, 40));
            float cw = r.Width * 0.8f;
            var card = new RectangleF(r.X + (r.Width - cw) / 2, r.Y + 20, cw, cw * 1.45f);
            PaintCard(g, card);
            g.DrawGameText(e.Text ?? "", new RectangleF(r.X + 10, card.Bottom + 10, r.Width - 20, 50), e.TextSize ?? 28, Color.White);
            using (var pen = new Pen(Color.FromArgb(200, 60, 170, 230), 2))
                g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
        }
    }
}
