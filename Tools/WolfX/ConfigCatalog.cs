namespace WolfX.WolfX.File_Type_UI
{
    public enum SettingKind { Toggle, Number, Text, Path, Choice }

    /// <summary>One setting a plugin reads from Config.ini.</summary>
    /// <summary>
    /// One setting a plugin reads from Config.ini. <paramref name="From"/> is the manifest's "from" (the game shows choices made from the game
    /// folder: "archives", "folders", "files:&lt;folder&gt;\&lt;pattern&gt;"); the Config Editor's Pick button browses for those and for paths.
    /// </summary>
    public sealed record ConfigSetting(string Section, string Key, SettingKind Kind, string Default, string Help, string[]? Choices = null, string? From = null);

    /// <summary>Every Config.ini setting the tools read, with a hint for each. Keep in step with the GetPrivateProfile calls in the plugins.</summary>
    public static class ConfigCatalog
    {
        private static readonly string[] LogLevels = ["debug", "info", "warn", "error"];

        /// <summary>The plugin list is in Core's own section, next to its settings: Core owns it (the loader writes it and injects the
        /// plugins, Core keeps it while the game runs and gives RIX's in-game Plugins menu its on/off toggles).</summary>
        public const string PluginSection = "Yu-Gi-Oh-Core";
        public const string GuiPluginPrefix = "YGO-Ex/";

        /// <summary>One row of the plugin list ([Yu-Gi-Oh-Core]): the loader writes a line for every plugin it finds, so the rows come from the file.</summary>
        public static ConfigSetting ForPlugin(string key)
        {
            bool gui = key.StartsWith(GuiPluginPrefix, StringComparison.OrdinalIgnoreCase);
            return gui
                ? new(PluginSection, key, SettingKind.Toggle, "0",
                    "A plugin Yu-Gi-Oh-Core starts once the main menu is up (a DLL in Plugins\\YGO-Ex). Applies the next time the game starts; the in-game Plugins menu (Help & Options) changes the same line. A plugin that draws an ImGui window also needs Yu-Gi-Oh-GUI on: it is the shared ImGui host, so plugins do not hook DirectX themselves.")
                : new(PluginSection, key, SettingKind.Toggle, "0",
                    "Load this plugin when the game starts (0 = the loader skips it; new plugins start off). Applies the next time the game starts; the in-game Plugins menu (Help & Options) changes the same line. Yu-Gi-Oh-Core and Yu-Gi-Oh-RIX are always loaded.");
        }

        /// <summary>
        /// <see cref="All"/> plus the settings the installed plugins list in their manifests ("settings" in &lt;DLL&gt;.json, the list the in-game
        /// Plugin Settings screen shows): a manifest's entry replaces the built-in one for the same [section] key, and a plugin this list doesn't
        /// know brings its own. <paramref name="configFile"/> is the game's Config.ini (its folder and [Yu-Gi-Oh-Core] PluginsPath find the plugins).
        /// </summary>
        public static List<ConfigSetting> WithManifests(string configFile)
        {
            var settings = All.ToList();
            if (configFile.Length == 0 || Path.GetDirectoryName(configFile) is not { } gameFolder)
                return settings;
            string plugins = global::WolfX.ContentManifest.PluginsFolder(gameFolder);
            foreach (string folder in new[] { plugins, Path.Combine(plugins, "YGO-Ex") })
            {
                if (!Directory.Exists(folder))
                    continue;
                foreach (string manifest in Directory.EnumerateFiles(folder, "*.json"))
                {
                    try
                    {
                        if (System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(manifest))?["settings"] is not System.Text.Json.Nodes.JsonArray list)
                            continue;
                        string plugin = Path.GetFileNameWithoutExtension(manifest);
                        foreach (var node in list.OfType<System.Text.Json.Nodes.JsonObject>())
                        {
                            string Text(string name) => node[name] is System.Text.Json.Nodes.JsonValue v && v.TryGetValue<string>(out var s) ? s : "";
                            if (Text("key").Length == 0)
                                continue;
                            string section = Text("section") is { Length: > 0 } s ? s : plugin;
                            var kind = Enum.TryParse<SettingKind>(Text("kind"), ignoreCase: true, out var parsed) ? parsed : SettingKind.Text;
                            string[]? choices = node["choices"] is System.Text.Json.Nodes.JsonArray c
                                ? c.Select(x => x is System.Text.Json.Nodes.JsonValue v && v.TryGetValue<string>(out var t) ? t : "").Where(t => t.Length > 0).ToArray() : null;
                            var setting = new ConfigSetting(section, Text("key"), kind, Text("default"), Text("help"), choices is { Length: > 0 } ? choices : null,
                                                            Text("from") is { Length: > 0 } from ? from : null);
                            int at = settings.FindIndex(x => x.Section.Equals(section, StringComparison.OrdinalIgnoreCase) && x.Key.Equals(setting.Key, StringComparison.OrdinalIgnoreCase));
                            if (at >= 0)
                                settings[at] = setting;
                            else
                                settings.Add(setting);
                        }
                    }
                    catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or InvalidOperationException)
                    {
                        // a broken manifest only loses its own settings
                    }
                }
            }
            return settings;
        }

        /// <summary>A key in the plugin list's section ([Yu-Gi-Oh-Core]) that is one of Core's settings, not a plugin.</summary>
        public static bool IsPluginListSetting(string key, IEnumerable<ConfigSetting>? settings = null) =>
            (settings ?? All).Any(s => s.Section.Equals(PluginSection, StringComparison.OrdinalIgnoreCase) && s.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

        public static readonly ConfigSetting[] All =
        [
            new("Yu-Gi-Oh-Loader", "LoadOrder", SettingKind.Text, "",
                "Names of plugin DLLs (without .dll) the loader loads first, in this order, separated by spaces. Plugins not listed load afterwards."),

            new("Yu-Gi-Oh-RIX", "PluginsPerPage", SettingKind.Number, "5",
                "How many plugins the in-game Plugins list (Help & Options) shows on one page, 3 to 5. Fewer looks less crowded; there are Next and Previous buttons."),

            new("Yu-Gi-Oh-Console", "LogLevel", SettingKind.Choice, "info",
                "Lowest severity shown in the console window. debug shows everything, info hides debug lines, warn shows warnings and errors, error shows errors only.", LogLevels),
            new("Yu-Gi-Oh-Console", "FileLogLevel", SettingKind.Choice, "debug",
                "Lowest severity written to console.log. Separate from LogLevel, so the file can keep debug lines the window hides.", LogLevels),

            new("Yu-Gi-Oh-Core", "EngineRules2020", SettingKind.Toggle, "0",
                "Duel rules: on = the game's own 2020 rules (Master Rule 5), off = 2019 rules (Master Rule 4). Core sets the engine's rules byte when the game starts, so restart after changing it."),

            new("Yu-Gi-Oh-Core", "PluginsPath", SettingKind.Path, "",
                "The loader's Plugins folder, e.g. <repo>\\Binaries\\Debug\\Plugins\\. The Effects plugin reads its files from a folder named Effects inside it. Must end with a backslash."),

            // where the game's data comes from (Core Loading.h / Patch.h; the Yu-Gi-Oh-Core plugin until 2026-10-04)
            new("Yu-Gi-Oh-Core", "Archive", SettingKind.Text, "YGO_2020",
                "Name of the game's .toc/.dat archive pair the game opens.", From: "archives"),
            new("Yu-Gi-Oh-Core", "AllowMultiInstance", SettingKind.Toggle, "0",
                "Allow more than one copy of the game to run at once."),
            new("Yu-Gi-Oh-Core", "LooseLoading", SettingKind.Toggle, "0",
                "Load files from a folder next to the game (FolderName) instead of the archive's, so extracted or modified files win (see FileOrder for WolfX's patch)."),
            new("Yu-Gi-Oh-Core", "FolderName", SettingKind.Text, "YGO_2020",
                "The folder LooseLoading reads from.", From: "folders"),
            new("Yu-Gi-Oh-Core", "PatchArchive", SettingKind.Text, "YGO_2020-Ex",
                "WolfX's patch archive (<name>.toc / .dat next to YGO_2020.dat): the game's files WolfX changed. Core loads them over the game's own archive whenever they exist, which is never written. Empty = off (the game's own data only). WolfX saves into the same name.", From: "archives"),
            new("Yu-Gi-Oh-Core", "FileOrder", SettingKind.Choice, "loose",
                "Which copy of a game file wins when both exist: loose = a loose file (LooseLoading, FolderName) wins over WolfX's patch archive; patch = WolfX's patch wins. Either way the game's own YGO_2020.dat comes last. WolfX follows the same setting.", ["loose", "patch"]),

            new("Yu-Gi-Oh-PatchMeOut", "FreeStore", SettingKind.Toggle, "1", "Card shop packs cost nothing."),
            new("Yu-Gi-Oh-PatchMeOut", "NoBan", SettingKind.Toggle, "1", "Remove the Forbidden/Limited card restrictions (banlist)."),
            new("Yu-Gi-Oh-PatchMeOut", "AutoPause", SettingKind.Toggle, "1", "Pause the game when its window loses focus."),
            new("Yu-Gi-Oh-PatchMeOut", "UseJP", SettingKind.Toggle, "0", "Use the Japanese rules and card data where the game has them."),
            new("Yu-Gi-Oh-PatchMeOut", "NoJanken", SettingKind.Toggle, "1", "Skip rock-paper-scissors at the start of a duel."),
            new("Yu-Gi-Oh-PatchMeOut", "StartingLP", SettingKind.Number, "8000", "Life points each duelist starts a duel with."),

            new("Yu-Gi-Oh-Core", "SeedFromGameSave", SettingKind.Toggle, "1",
                "The first time savegame-ex.dat is created, copy your Steam save into it so your progress carries over."),
            new("Yu-Gi-Oh-Core", "GameSaveName", SettingKind.Text, "savegame-ex.dat",
                "File name of the save the game always uses (Yu-Gi-Oh-Core is always loaded). Your original savegame.dat is never touched."),
            new("Yu-Gi-Oh-Core", "SaveSlot", SettingKind.Number, "0",
                "0 = after the title screen, pick a save slot on the save-select screen. 1 to 5 = always load that slot and skip the screen. Slot 1 is the GameSaveName file, slot N the same name with -N (savegame-ex-2.dat); names and avatars are in Yu-Gi-Oh-Ex\\saves.json."),

            new("Yu-Gi-Oh-SpeedHacks", "Speed", SettingKind.Number, "2", "Duel speed multiplier. 1 is normal."),
            new("Yu-Gi-Oh-SpeedHacks", "NoAnimations", SettingKind.Toggle, "1", "Skip card and monster animations in duels."),
            new("Yu-Gi-Oh-SpeedHacks", "NoMovies", SettingKind.Toggle, "1", "Skip the cutscene movies."),

            new("Yu-Gi-Oh-BetterCardShop", "BetterShop-ShowOwnedCards", SettingKind.Toggle, "0",
                "Also list the cards you already have three copies of in the Card Shop. Off = only cards you can still buy are listed."),
            new("Yu-Gi-Oh-BetterCardShop", "BetterShop-PasswordCost", SettingKind.Number, "1000",
                "DP to unlock a card with its password (three copies). Yu-Gi-Oh-Ex\\prices.json can set a card's own password price."),
            new("Yu-Gi-Oh-BetterCardShop", "BetterShop-MonsterPrice", SettingKind.Choice, "attack",
                "How the Card Shop prices one copy of a monster. attack = ATK x 1.8 / 5. classic = Forbidden Memories' price curve (doubles every ~590 ATK+DEF). custom = BetterShop-MonsterFormula. flat = BetterShop-Cost. Yu-Gi-Oh-Ex\\prices.json can set a card's own price.",
                ["attack", "classic", "custom", "flat"]),
            new("Yu-Gi-Oh-BetterCardShop", "BetterShop-MonsterFormula", SettingKind.Text, "ATK * 1.8 / 5",
                "Used when BetterShop-MonsterPrice is custom. Names: ATK, DEF (a Link monster's is 0.8 x ATK), LEVEL, RARE (1 or 0), LINK (1 or 0). + - * / ^ and brackets; min, max, exp, log, sqrt, round, floor, ceil."),
            new("Yu-Gi-Oh-BetterCardShop", "BetterShop-SpellTrapPrice", SettingKind.Choice, "rarity",
                "How the Card Shop prices spells and traps. rarity = what a typical monster of the same pack rarity costs. custom = BetterShop-SpellTrapFormula. flat = BetterShop-Cost.",
                ["rarity", "custom", "flat"]),
            new("Yu-Gi-Oh-BetterCardShop", "BetterShop-SpellTrapFormula", SettingKind.Text, "500 + 500 * RARE",
                "Used when BetterShop-SpellTrapPrice is custom. RARE is 1 for a card in a pack's rare slot, else 0."),
            new("Yu-Gi-Oh-BetterCardShop", "BetterShop-Cost", SettingKind.Number, "500", "DP for one copy when a price setting is flat."),

            new("Yu-Gi-Oh-AnimeCards", "HideLevelBadge", SettingKind.Choice, "1",
                "The level/rank number the duel draws over the monsters in your hand (and the badge on other card views). 0 = show it (vanilla), 1 = hide it in the hand, 2 = hide it everywhere. The anime frames already show the stars on the card. Restart the game after changing it.", ["0", "1", "2"]),
            new("Yu-Gi-Oh-AnimeCards", "UseLargeAtkDefFont", SettingKind.Toggle, "1", "Draw ATK/DEF with the game's larger (32 px) number font, so the scaled numbers stay sharper."),
            new("Yu-Gi-Oh-AnimeCards", "CustomAtkX", SettingKind.Number, "107.5", "ATK number: centre X on the 400 x 580 card face (the left box)."),
            new("Yu-Gi-Oh-AnimeCards", "CustomAtkY", SettingKind.Number, "526.5", "ATK number: centre Y on the card face."),
            new("Yu-Gi-Oh-AnimeCards", "CustomDefX", SettingKind.Number, "291.5", "DEF number (Link rating on Link monsters): centre X (the right box)."),
            new("Yu-Gi-Oh-AnimeCards", "CustomDefY", SettingKind.Number, "526.5", "DEF number: centre Y on the card face."),
            new("Yu-Gi-Oh-AnimeCards", "AtkTextScale", SettingKind.Number, "2.5", "Size of the ATK text."),
            new("Yu-Gi-Oh-AnimeCards", "DefTextScale", SettingKind.Number, "2.5", "Size of the DEF text."),
            new("Yu-Gi-Oh-AnimeCards", "AttributeX", SettingKind.Number, "346", "Attribute icon: centre X, in the frame's circle (monster frames)."),
            new("Yu-Gi-Oh-AnimeCards", "AttributeY", SettingKind.Number, "469.5", "Attribute icon: centre Y."),
            new("Yu-Gi-Oh-AnimeCards", "LinkAttributeX", SettingKind.Number, "198.5", "Attribute icon on the Link frame: centre X."),
            new("Yu-Gi-Oh-AnimeCards", "LinkAttributeY", SettingKind.Number, "469", "Attribute icon on the Link frame: centre Y."),
            new("Yu-Gi-Oh-AnimeCards", "AttributeSize", SettingKind.Number, "48", "Attribute icon size in pixels on the card face (48 covers the frame's 45 px circle)."),
            new("Yu-Gi-Oh-AnimeCards", "StarsX", SettingKind.Number, "200", "Level/rank stars: the row is centred on this X."),
            new("Yu-Gi-Oh-AnimeCards", "StarsY", SettingKind.Number, "465.5", "Level/rank stars: centre Y (the band between the art and the ATK/DEF boxes). Below 0 keeps the game's own height."),
            new("Yu-Gi-Oh-AnimeCards", "StarsMaxWidth", SettingKind.Number, "240", "Widest the star row may be before it's shrunk to fit (keeps it clear of the attribute circle)."),
            new("Yu-Gi-Oh-AnimeCards", "CardArtScale", SettingKind.Number, "1.3", "Size of the card art."),
            new("Yu-Gi-Oh-AnimeCards", "CardArtOffsetX", SettingKind.Number, "0", "Card art horizontal offset."),
            new("Yu-Gi-Oh-AnimeCards", "CardArtOffsetY", SettingKind.Number, "-50", "Card art vertical offset."),
            new("Yu-Gi-Oh-AnimeCards", "CustomSTIconX", SettingKind.Number, "500", "Spell/Trap icon X position."),
            new("Yu-Gi-Oh-AnimeCards", "CustomSTIconY", SettingKind.Number, "183", "Spell/Trap icon Y position."),

            new("Yu-Gi-Oh-TagDuel", "DeckMode", SettingKind.Choice, "separate",
                "Tag duels: separate = each partner has their own hand and deck (the engine swaps them in at each turn change); shared = partners play one hand and deck.", ["separate", "shared"]),
            new("Yu-Gi-Oh-TagDuel", "PartnerDeck", SettingKind.Path, "",
                "A .ydc deck for your tag partner (relative paths are from the game folder). Empty = a copy of your own deck."),
            new("Yu-Gi-Oh-TagDuel", "OpponentPartnerDeck", SettingKind.Path, "",
                "A .ydc deck for the opponent's tag partner. Empty = a copy of the opponent's deck."),

            new("Yu-Gi-Oh-MP", "ServerUrl", SettingKind.Text, "",
                "Base URL of the multiplayer server (sign-in, event reports). Empty = offline: the duel recorder (Duels.log) and logging still work."),

            new("Yu-Gi-Oh-Funky", "DemoMenuButtons", SettingKind.Toggle, "0",
                "Add the demo buttons to the game's menus (Debug Tools and Credits on the main menu, Funky Tools in options). Needs Yu-Gi-Oh-RIX."),
            // Debug Tools > Duel (Funky DuelTest.cpp): set in the window, kept here between runs.
            new("Yu-Gi-Oh-Funky", "DuelTestEnabled", SettingKind.Toggle, "0", "Stack the chosen hand cards on top of the deck at the start of a duel."),
            new("Yu-Gi-Oh-Funky", "DuelTestAddMissing", SettingKind.Toggle, "1", "Add chosen cards the deck does not have."),
            new("Yu-Gi-Oh-Funky", "DuelTestPlayer", SettingKind.Number, "0", "Whose opening hand is stacked: 0 = you, 1 = the opponent."),
            new("Yu-Gi-Oh-Funky", "DuelTestHand", SettingKind.Text, "", "Card ids for the opening hand, comma separated."),
            new("Yu-Gi-Oh-Funky", "DuelTestExtra", SettingKind.Text, "", "Card ids added to the extra deck, comma separated."),
            new("Yu-Gi-Oh-Funky", "DuelTestDeckYou", SettingKind.Path, "", "A .ydc test deck that replaces your deck in every duel. Empty = your own deck."),
            new("Yu-Gi-Oh-Funky", "DuelTestDeckOpponent", SettingKind.Path, "", "A .ydc test deck that replaces the opponent's deck in every duel. Empty = their own deck."),
            new("Yu-Gi-Oh-Funky", "DuelTestKeepOrder", SettingKind.Toggle, "1", "A test deck is not shuffled: the file's order is the draw order (its first 5 cards are the opening hand)."),
            new("Yu-Gi-Oh-Funky", "DuelTestBrowseDir", SettingKind.Path, "", "The folder the test deck browser shows. Empty = Yu-Gi-Oh-Ex\\TestDecks."),
        ];
    }
}
