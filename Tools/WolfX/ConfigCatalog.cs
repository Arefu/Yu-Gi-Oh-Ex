namespace WolfX.WolfX.File_Type_UI
{
    public enum SettingKind { Toggle, Number, Text, Path, Choice }

    /// <summary>One setting a plugin reads from Config.ini.</summary>
    public sealed record ConfigSetting(string Section, string Key, SettingKind Kind, string Default, string Help, string[]? Choices = null);

    /// <summary>Every Config.ini setting the tools read, with a hint for each. Keep in step with the GetPrivateProfile calls in the plugins.</summary>
    public static class ConfigCatalog
    {
        private static readonly string[] LogLevels = ["debug", "info", "warn", "error"];

        public const string PluginSection = "Yu-Gi-Oh-RIX";
        public const string GuiPluginPrefix = "YGO-Ex/";

        /// <summary>One row of the plugin list ([Yu-Gi-Oh-RIX]): the loader writes a line for every plugin it finds, so the rows come from the file.</summary>
        public static ConfigSetting ForPlugin(string key)
        {
            bool gui = key.StartsWith(GuiPluginPrefix, StringComparison.OrdinalIgnoreCase);
            return gui
                ? new(PluginSection, key, SettingKind.Toggle, "0",
                    "A plugin Yu-Gi-Oh-Core starts once the main menu is up (a DLL in Plugins\\YGO-Ex). Applies the next time the game starts; the in-game Plugins menu (Help & Options) changes the same line. A plugin that draws an ImGui window also needs Yu-Gi-Oh-GUI on: it is the shared ImGui host, so plugins do not hook DirectX themselves.")
                : new(PluginSection, key, SettingKind.Toggle, "0",
                    "Load this plugin when the game starts (0 = the loader skips it; new plugins start off). Applies the next time the game starts; the in-game Plugins menu (Help & Options) changes the same line. Yu-Gi-Oh-RIX itself is always loaded.");
        }

        /// <summary>Settings that share the plugin list's section and so are not plugins.</summary>
        public static bool IsPluginListSetting(string key) => All.Any(s => s.Section == PluginSection && s.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

        public static readonly ConfigSetting[] All =
        [
            new("Yu-Gi-Oh-Loader", "LoadOrder", SettingKind.Text, "",
                "Names of plugin DLLs (without .dll) the loader loads first, in this order, separated by spaces. Plugins not listed load afterwards."),

            new(PluginSection, "PluginsPerPage", SettingKind.Number, "5",
                "How many plugins the in-game Plugins list (Help & Options) shows on one page, 3 to 5. Fewer looks less crowded; there are Next and Previous buttons."),

            new("Yu-Gi-Oh-Console", "LogLevel", SettingKind.Choice, "info",
                "Lowest severity shown in the console window. debug shows everything, info hides debug lines, warn shows warnings and errors, error shows errors only.", LogLevels),

            new("Yu-Gi-Oh-Core", "PluginsPath", SettingKind.Path, "",
                "The loader's Plugins folder, e.g. <repo>\\Binaries\\Debug\\Plugins\\. The Effects plugin reads its files from a folder named Effects inside it. Must end with a backslash."),

            new("Yu-Gi-Oh-BetterLoad", "Archive", SettingKind.Text, "YGO_2020",
                "Name of the game's .toc/.dat archive pair the loader reads."),
            new("Yu-Gi-Oh-BetterLoad", "AllowMultiInstance", SettingKind.Toggle, "0",
                "Allow more than one copy of the game to run at once."),
            new("Yu-Gi-Oh-BetterLoad", "LooseLoading", SettingKind.Toggle, "0",
                "Load files from a folder next to the game (see FolderName) before looking in the archive, so extracted or modified files win."),
            new("Yu-Gi-Oh-BetterLoad", "FolderName", SettingKind.Text, "YGO_2020",
                "Folder that LooseLoading reads from."),

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

            new("Yu-Gi-Oh-SpeedHacks", "Speed", SettingKind.Number, "2", "Duel speed multiplier. 1 is normal."),
            new("Yu-Gi-Oh-SpeedHacks", "NoAnimations", SettingKind.Toggle, "1", "Skip card and monster animations in duels."),
            new("Yu-Gi-Oh-SpeedHacks", "NoMovies", SettingKind.Toggle, "1", "Skip the cutscene movies."),

            new("Yu-Gi-Oh-BetterCardShop", "BetterShop-Cost", SettingKind.Number, "500", "Price, in points, of one card bought in the Better Shop window (Yu-Gi-Oh-BetterCardShop)."),

            new("Yu-Gi-Oh-AnimeCards", "UseLargeAtkDefFont", SettingKind.Toggle, "1", "Use the larger ATK/DEF number font on anime style cards."),
            new("Yu-Gi-Oh-AnimeCards", "CustomAtkX", SettingKind.Number, "135", "Anime card layout: ATK text X position."),
            new("Yu-Gi-Oh-AnimeCards", "CustomAtkY", SettingKind.Number, "507", "Anime card layout: ATK text Y position."),
            new("Yu-Gi-Oh-AnimeCards", "CustomDefX", SettingKind.Number, "250", "Anime card layout: DEF text X position."),
            new("Yu-Gi-Oh-AnimeCards", "CustomDefY", SettingKind.Number, "500", "Anime card layout: DEF text Y position."),
            new("Yu-Gi-Oh-AnimeCards", "AtkTextScale", SettingKind.Number, "2.5", "Size of the ATK text."),
            new("Yu-Gi-Oh-AnimeCards", "DefTextScale", SettingKind.Number, "2.5", "Size of the DEF text."),
            new("Yu-Gi-Oh-AnimeCards", "CardArtScale", SettingKind.Number, "1.3", "Size of the card art."),
            new("Yu-Gi-Oh-AnimeCards", "CardArtOffsetX", SettingKind.Number, "0", "Card art horizontal offset."),
            new("Yu-Gi-Oh-AnimeCards", "CardArtOffsetY", SettingKind.Number, "-50", "Card art vertical offset."),
            new("Yu-Gi-Oh-AnimeCards", "CustomSTIconX", SettingKind.Number, "500", "Spell/Trap icon X position."),
            new("Yu-Gi-Oh-AnimeCards", "CustomSTIconY", SettingKind.Number, "183", "Spell/Trap icon Y position."),

            new("Yu-Gi-Oh-Funky", "DemoMenuButtons", SettingKind.Toggle, "0",
                "Add the demo buttons to the game's menus (Debug Tools and Credits on the main menu, Funky Tools in options). Needs Yu-Gi-Oh-RIX."),
        ];
    }
}
