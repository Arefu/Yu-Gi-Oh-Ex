namespace WolfX.WolfX.File_Type_UI
{
    public enum SettingKind { Toggle, Number, Text, Path, Choice }

    /// <summary>One setting a plugin reads from Config.ini.</summary>
    /// <summary>
    /// One setting a plugin reads from Config.ini. <paramref name="From"/> is the manifest's "from" (the game shows choices made from the game
    /// folder: "archives", "folders", "files:&lt;folder&gt;\&lt;pattern&gt;"); the Config Editor's Pick button browses for those and for paths.
    /// </summary>
    public sealed record ConfigSetting(string Section, string Key, SettingKind Kind, string Default, string Help, string[]? Choices = null, string? From = null);

    /// <summary>The Config.ini settings the tools show: read from the plugins' manifests (each plugin declares its own).</summary>
    public static class ConfigCatalog
    {
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
        /// The settings the installed plugins declare in their manifests ("settings" in &lt;DLL&gt;.json; <see cref="All"/> is empty): a later manifest's entry replaces an earlier one for the same [section] key, and a plugin this list doesn't
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

        /// <summary>
        /// Settings WolfX knows without any plugin installed: none. Since 2026-11-01 every plugin declares its own Config.ini settings in its
        /// manifest ("settings" in &lt;DLL&gt;.json, docs/PluginManifests.md) and <see cref="WithManifests"/> reads them; the 85 entries that
        /// were listed here moved into the manifests. Add a setting to its plugin's manifest, never here.
        /// </summary>
        public static readonly ConfigSetting[] All = [];
    }
}
