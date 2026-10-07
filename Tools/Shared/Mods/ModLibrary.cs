using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using StartingCollection;

namespace Wolf.Mods
{
    /// <summary>A plugin a mod needs, and where to get it when it isn't part of Yu-Gi-Oh-Ex (empty = no link).</summary>
    public sealed class ModRequirement
    {
        public string Plugin { get; set; } = "";
        public string Url { get; set; } = "";
    }

    /// <summary>
    /// mod.json: who made a mod and what it needs. The game reads the same file (Dependencies\Yu-Gi-Oh-Ex\Yu-Gi-Oh-Mods.h, docs/Mods.md).
    /// Older mods with only an Info.ini (title=, desc=, long-desc=) are read from that.
    /// </summary>
    public sealed class ModInfo
    {
        public const string FileName = "mod.json";

        public string Name { get; set; } = "";
        public string Version { get; set; } = "";
        public string Author { get; set; } = "";
        public string Website { get; set; } = "";
        public string Description { get; set; } = "";
        public string Details { get; set; } = "";
        public List<ModRequirement> Requires { get; set; } = [];

        public static ModInfo Read(string folder, string fallbackName)
        {
            string json = Path.Combine(folder, FileName);
            if (File.Exists(json))
            {
                try
                {
                    return FromJson(JsonNode.Parse(File.ReadAllText(json), documentOptions: ModLibrary.ReadOptions) as JsonObject, fallbackName);
                }
                catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
                {
                    return new ModInfo { Name = fallbackName, Description = "mod.json can't be read: " + ex.Message };
                }
            }
            string ini = Path.Combine(folder, "Info.ini");
            return File.Exists(ini) ? FromInfoIni(File.ReadAllLines(ini), fallbackName) : new ModInfo { Name = fallbackName };
        }

        public static ModInfo FromJson(JsonObject? root, string fallbackName)
        {
            var info = new ModInfo { Name = fallbackName };
            if (root == null)
                return info;
            string Text(string key) => root[key] is JsonValue value && value.TryGetValue(out string? text) ? text : "";
            if (Text("name").Length > 0)
                info.Name = Text("name");
            info.Version = Text("version");
            info.Author = Text("author");
            info.Website = Text("website");
            info.Description = Text("description");
            info.Details = Text("details");
            if (root["requires"] is JsonArray requires)
            {
                foreach (JsonNode? item in requires)
                {
                    var need = new ModRequirement();
                    if (item is JsonValue plain && plain.TryGetValue(out string? name))
                        need.Plugin = name;
                    else if (item is JsonObject entry)
                    {
                        need.Plugin = entry["plugin"] is JsonValue p && p.TryGetValue(out string? plugin) ? plugin : "";
                        need.Url = entry["url"] is JsonValue u && u.TryGetValue(out string? url) ? url : "";
                    }
                    need.Plugin = ModLibrary.StripDll(need.Plugin.Trim());
                    if (need.Plugin.Length > 0)
                        info.Requires.Add(need);
                }
            }
            return info;
        }

        private static ModInfo FromInfoIni(string[] lines, string fallbackName)
        {
            var info = new ModInfo { Name = fallbackName };
            foreach (string line in lines)
            {
                int equals = line.IndexOf('=');
                if (equals <= 0)
                    continue;
                string key = line[..equals].Trim().ToLowerInvariant();
                string value = line[(equals + 1)..].Trim().Trim('"');
                switch (key)
                {
                    case "title": info.Name = value; break;
                    case "desc": info.Description = value; break;
                    case "long-desc": info.Details = value; break;
                }
            }
            return info;
        }

        public JsonObject ToJson()
        {
            var root = new JsonObject { ["name"] = Name };
            void Put(string key, string value)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    root[key] = value.Trim();
            }
            Put("version", Version);
            Put("author", Author);
            Put("website", Website);
            Put("description", Description);
            Put("details", Details);
            if (Requires.Count > 0)
            {
                var requires = new JsonArray();
                foreach (ModRequirement need in Requires.Where(r => r.Plugin.Length > 0))
                    requires.Add(string.IsNullOrWhiteSpace(need.Url) ? JsonValue.Create(need.Plugin)
                                                                     : new JsonObject { ["plugin"] = need.Plugin, ["url"] = need.Url.Trim() });
                root["requires"] = requires;
            }
            return root;
        }

        public string ToJsonText() => ToJson().ToJsonString(ModLibrary.WriteOptions);
    }

    /// <summary>A mod in &lt;game&gt;\Mods\&lt;id&gt;, and what is in it.</summary>
    public sealed class Mod
    {
        public required string Id { get; init; }
        public required string Folder { get; init; }
        public required ModInfo Info { get; set; }
        public bool Enabled { get; set; } = true;
        /// <summary>True for the game folder's own Yu-Gi-Oh-Ex + YGO_2020-Ex (WolfX's workspace): always on, always last, not in the list file.</summary>
        public bool IsLocal { get; init; }

        public string ContentFolder => Path.Combine(Folder, "Yu-Gi-Oh-Ex");
        public string LooseFolder => Path.Combine(Folder, IsLocal ? ModLibrary.LocalLooseFolderName(Folder) : "YGO_2020");
        public string PatchToc => Path.Combine(Folder, "YGO_2020-Ex.toc");
        public string PluginFolder => Path.Combine(Folder, "Plugins");

        private List<string>? _gameFiles;
        private List<string>? _contentFiles;
        private (HashSet<int> New, HashSet<int> Changed)? _cards;

        /// <summary>The game files it changes (archive paths, e.g. bin\pd_limits.bin): its loose files and its patch archive's.</summary>
        public IReadOnlyList<string> GameFiles()
        {
            if (_gameFiles != null)
                return _gameFiles;
            var files = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            if (LooseFolder.Length > 0 && Directory.Exists(LooseFolder))
                foreach (string file in Directory.EnumerateFiles(LooseFolder, "*", SearchOption.AllDirectories))
                    files.Add(Path.GetRelativePath(LooseFolder, file));
            if (File.Exists(PatchToc) && File.Exists(Path.ChangeExtension(PatchToc, ".dat")))
            {
                try
                {
                    if (TocArchive.TryOpen(PatchToc) is TocArchive patch)
                        foreach (string path in patch.Paths)
                            files.Add(path.Replace('/', '\\'));
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
                {
                    // a broken patch shows up as no files; the game says the same in its log
                }
            }
            return _gameFiles = [.. files];
        }

        /// <summary>The content files (relative to its Yu-Gi-Oh-Ex folder).</summary>
        public IReadOnlyList<string> ContentFiles()
        {
            if (_contentFiles != null)
                return _contentFiles;
            if (!Directory.Exists(ContentFolder))
                return _contentFiles = [];
            return _contentFiles = [.. Directory.EnumerateFiles(ContentFolder, "*", SearchOption.AllDirectories)
                .Select(file => Path.GetRelativePath(ContentFolder, file))
                .Where(file => !ModLibrary.IsPrivateContent(file))
                .Order(StringComparer.OrdinalIgnoreCase)];
        }

        /// <summary>The ids in its cards.json: new cards (15300 and up) and changed game cards.</summary>
        public (HashSet<int> New, HashSet<int> Changed) CardIds()
        {
            if (_cards is { } cached)
                return cached;
            var result = (New: new HashSet<int>(), Changed: new HashSet<int>());
            foreach (int id in ModLibrary.IdsIn(Path.Combine(ContentFolder, "cards.json"), "cards"))
                (id >= ModLibrary.FirstNewCardId ? result.New : result.Changed).Add(id);
            _cards = result;
            return result;
        }

        /// <summary>The plugins it needs: mod.json "requires" and its content.json (written by WolfX), with links where given.</summary>
        public IReadOnlyList<ModRequirement> NeededPlugins()
        {
            var needs = new List<ModRequirement>();
            foreach (ModRequirement need in Info.Requires)
                if (!needs.Any(n => n.Plugin.Equals(need.Plugin, StringComparison.OrdinalIgnoreCase)))
                    needs.Add(need);
            foreach (string plugin in ModLibrary.ContentPlugins(ContentFolder))
                if (!needs.Any(n => n.Plugin.Equals(plugin, StringComparison.OrdinalIgnoreCase)))
                    needs.Add(new ModRequirement { Plugin = plugin });
            return needs;
        }

        /// <summary>The plugin DLLs it brings (Plugins\*.dll and Plugins\YGO-Ex\*.dll), by name without .dll.</summary>
        public IReadOnlyList<string> BundledPlugins() =>
            Directory.Exists(PluginFolder)
                ? [.. Directory.EnumerateFiles(PluginFolder, "*.dll", SearchOption.AllDirectories).Select(Path.GetFileNameWithoutExtension).OfType<string>()]
                : [];

        /// <summary>One line saying what it changes, for the list.</summary>
        public string Summary()
        {
            var parts = new List<string>();
            var (added, changed) = CardIds();
            if (added.Count > 0)
                parts.Add($"{added.Count} new card{(added.Count == 1 ? "" : "s")}");
            if (changed.Count > 0)
                parts.Add($"{changed.Count} card change{(changed.Count == 1 ? "" : "s")}");
            int other = ContentFiles().Count(f => !f.Equals("cards.json", StringComparison.OrdinalIgnoreCase) && !f.Equals("content.json", StringComparison.OrdinalIgnoreCase)
                                                && !IsArt(f));
            if (other > 0)
                parts.Add($"{other} content file{(other == 1 ? "" : "s")}");
            int art = ContentFiles().Count(IsArt);
            if (art > 0)
                parts.Add($"{art} picture{(art == 1 ? "" : "s")}");
            int game = GameFiles().Count;
            if (game > 0)
                parts.Add($"{game} game file{(game == 1 ? "" : "s")}");
            int plugins = BundledPlugins().Count;
            if (plugins > 0)
                parts.Add($"{plugins} plugin{(plugins == 1 ? "" : "s")}");
            return parts.Count == 0 ? "nothing" : string.Join(", ", parts);
        }

        private static bool IsArt(string file) =>
            Path.GetExtension(file).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".dds";

        public void Forget()
        {
            _gameFiles = null;
            _contentFiles = null;
            _cards = null;
        }
    }

    public enum IssueLevel { Info, Warning, Error }

    /// <summary>Something worth knowing about the mods that are on. <see cref="ModId"/> is the mod it is about (null = all of them).</summary>
    public sealed record ModIssue(IssueLevel Level, string? ModId, string Text, string? Url = null);

    /// <summary>What goes into a new mod (<see cref="ModLibrary.Create"/>).</summary>
    public sealed class ModCreateOptions
    {
        public required ModInfo Info { get; init; }
        /// <summary>The Yu-Gi-Oh-Ex folder to take content from, and the entries in it (files or folders, relative) to include.</summary>
        public string ContentFolder { get; init; } = "";
        public List<string> ContentEntries { get; init; } = [];
        /// <summary>A patch archive's .toc to include (its .dat comes along), or empty.</summary>
        public string PatchToc { get; init; } = "";
        /// <summary>A folder laid out like the archive (bin\..., main\...) to include as loose game files, or empty.</summary>
        public string LooseFolder { get; init; } = "";
        /// <summary>Plugin DLLs to bring along (their .json manifests come with them). YGO-Ex plugins (started by Core) go to Plugins\YGO-Ex.</summary>
        public List<string> Plugins { get; init; } = [];
    }

    /// <summary>
    /// Mods: content packs in &lt;game&gt;\Mods\&lt;id&gt;\ that the game loads on top of each other (docs/Mods.md). Installing, removing,
    /// ordering, packing and checking them. The game side is Dependencies\Yu-Gi-Oh-Ex\Yu-Gi-Oh-Mods.h; both follow the same rules:
    /// &lt;game&gt;\Mods\modlist.json is the order (later wins) and what is on, unlisted folders load after the listed ones (by name) and are on,
    /// and the game folder's own Yu-Gi-Oh-Ex / YGO_2020-Ex always load last.
    /// </summary>
    public static class ModLibrary
    {
        public const string ModsFolderName = "Mods";
        public const string ListFileName = "modlist.json";
        public const int FirstNewCardId = 15300;

        /// <summary>The plugins that come with Yu-Gi-Oh-Ex, so a mod needing one doesn't need a link.</summary>
        public static readonly string[] StockPlugins =
        [
            "Yu-Gi-Oh-Core", "Yu-Gi-Oh-RIX", "Yu-Gi-Oh-MoreCards", "Yu-Gi-Oh-Campaign", "Yu-Gi-Oh-Effects", "Yu-Gi-Oh-Music",
            "Yu-Gi-Oh-AnimeCards", "Yu-Gi-Oh-MP", "Yu-Gi-Oh-TagDuel", "Yu-Gi-Oh-Console", "Yu-Gi-Oh-GUI", "Yu-Gi-Oh-PatchMeOut",
            "Yu-Gi-Oh-BetterCardShop", "Yu-Gi-Oh-Funky", "Yu-Gi-Oh-SpeedHacks",
        ];

        /// <summary>What content.json files name when there is no manifest to ask (same table as WolfX's ContentManifest).</summary>
        private static readonly (string Pattern, string Plugin)[] KnownContent =
        [
            ("cards.json", "Yu-Gi-Oh-MoreCards"), ("unlocks.json", "Yu-Gi-Oh-MoreCards"), ("genres.json", "Yu-Gi-Oh-MoreCards"),
            ("relatedcards.json", "Yu-Gi-Oh-MoreCards"), ("text.json", "Yu-Gi-Oh-MoreCards"),
            ("characters.json", "Yu-Gi-Oh-Campaign"), ("decks.json", "Yu-Gi-Oh-Campaign"), ("storyduels.json", "Yu-Gi-Oh-Campaign"),
            ("storyscripts.json", "Yu-Gi-Oh-Campaign"),
            ("packs.json", "Yu-Gi-Oh-BetterCardShop"), ("prices.json", "Yu-Gi-Oh-BetterCardShop"),
            ("summoning.json", "Yu-Gi-Oh-Effects"), ("effects.json", "Yu-Gi-Oh-Effects"),
            ("music.json", "Yu-Gi-Oh-Music"), ("voices.json", "Yu-Gi-Oh-Music"),
            (@"pages\", "Yu-Gi-Oh-RIX"), (@"menus\", "Yu-Gi-Oh-RIX"),
        ];

        /// <summary>Files in a Yu-Gi-Oh-Ex folder that belong to the player, never to a mod: save slots and decks the game exported.</summary>
        public static bool IsPrivateContent(string relative) =>
            relative.Equals("saves.json", StringComparison.OrdinalIgnoreCase) ||
            relative.StartsWith(@"Exported Decks\", StringComparison.OrdinalIgnoreCase) ||
            relative.Equals("Exported Decks", StringComparison.OrdinalIgnoreCase);

        internal static readonly JsonDocumentOptions ReadOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        internal static readonly JsonSerializerOptions WriteOptions = new()
        {
            TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetPrivateProfileString(string section, string key, string fallback, StringBuilder value, int size, string file);

        private static string IniValue(string gameFolder, string section, string key, string fallback)
        {
            string ini = Path.Combine(gameFolder, "Config.ini");
            if (!File.Exists(ini))
                return fallback;
            var value = new StringBuilder(1024);
            GetPrivateProfileString(section, key, fallback, value, value.Capacity, ini);
            return value.ToString();
        }

        internal static string LocalLooseFolderName(string gameFolder) =>
            IniValue(gameFolder, "Yu-Gi-Oh-Core", "LooseLoading", "0") == "1" ? IniValue(gameFolder, "Yu-Gi-Oh-Core", "FolderName", "YGO_2020") : "";

        public static string StripDll(string name) =>
            name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;

        public static string ModsFolder(string gameFolder) => Path.Combine(gameFolder, ModsFolderName);

        /// <summary>A folder name for a mod: letters, digits and hyphens (the game opens the mod's patch through an 8-bit path).</summary>
        public static string MakeId(string name)
        {
            var id = new StringBuilder();
            foreach (char c in name.Normalize(NormalizationForm.FormD))
            {
                if (char.IsAsciiLetterOrDigit(c))
                    id.Append(char.ToLowerInvariant(c));
                else if ((c == ' ' || c == '-' || c == '_' || c == '.') && id.Length > 0 && id[^1] != '-')
                    id.Append('-');
            }
            string result = id.ToString().Trim('-');
            return result.Length == 0 ? "mod" : result.Length > 48 ? result[..48].TrimEnd('-') : result;
        }

        // ---- the list ----

        /// <summary>
        /// True when a folder in Mods is a mod: it has a mod.json (Install writes one for a zip without it). Anything else is left alone:
        /// people keep other things there (copies of YGO_2020.toc/.dat, extracted files). Same rule as Yu-Gi-Oh-Mods.h IsModFolder.
        /// </summary>
        public static bool IsModFolder(string folder) =>
            !folder.EndsWith(".installing", StringComparison.OrdinalIgnoreCase) && File.Exists(Path.Combine(folder, ModInfo.FileName));

        /// <summary>Every mod in &lt;game&gt;\Mods in load order, switched on or off as modlist.json says.</summary>
        public static List<Mod> Load(string gameFolder)
        {
            string root = ModsFolder(gameFolder);
            if (!Directory.Exists(root))
                return [];
            var found = Directory.GetDirectories(root)
                .Where(IsModFolder)
                .Order(StringComparer.OrdinalIgnoreCase)
                .Select(folder => new Mod { Id = Path.GetFileName(folder), Folder = folder, Info = ModInfo.Read(folder, Path.GetFileName(folder)) })
                .ToList();

            var ordered = new List<Mod>();
            string listFile = Path.Combine(root, ListFileName);
            if (File.Exists(listFile))
            {
                try
                {
                    if (JsonNode.Parse(File.ReadAllText(listFile), documentOptions: ReadOptions)?["mods"] is JsonArray list)
                    {
                        foreach (JsonNode? entry in list)
                        {
                            if (entry?["id"] is not JsonValue idValue || !idValue.TryGetValue(out string? id))
                                continue;
                            Mod? mod = found.FirstOrDefault(m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase) && !ordered.Contains(m));
                            if (mod == null)
                                continue;
                            mod.Enabled = entry["enabled"] is not JsonValue on || !on.TryGetValue(out bool enabled) || enabled;
                            ordered.Add(mod);
                        }
                    }
                }
                catch (Exception ex) when (ex is JsonException or IOException)
                {
                    // a broken list: every mod on, by name (what the game does too)
                }
            }
            ordered.AddRange(found.Where(m => !ordered.Contains(m)));
            return ordered;
        }

        /// <summary>The game folder's own content (Yu-Gi-Oh-Ex, YGO_2020-Ex, loose files when LooseLoading is on), as a mod that loads last.</summary>
        public static Mod Local(string gameFolder) => new()
        {
            Id = "",
            Folder = gameFolder,
            IsLocal = true,
            Info = new ModInfo { Name = "Your own files (Yu-Gi-Oh-Ex folder)", Description = "What you made with WolfX. Always loads last, on top of every mod." },
        };

        public static void SaveList(string gameFolder, IEnumerable<Mod> mods)
        {
            Directory.CreateDirectory(ModsFolder(gameFolder));
            var list = new JsonArray();
            foreach (Mod mod in mods.Where(m => !m.IsLocal))
                list.Add(new JsonObject { ["id"] = mod.Id, ["enabled"] = mod.Enabled });
            var root = new JsonObject { ["mods"] = list };
            WriteAtomic(Path.Combine(ModsFolder(gameFolder), ListFileName), root.ToJsonString(WriteOptions));
        }

        private static void WriteAtomic(string path, string text)
        {
            string temp = path + ".tmp";
            File.WriteAllText(temp, text, new UTF8Encoding(false));
            File.Move(temp, path, overwrite: true);
        }

        // ---- plugins ----

        /// <summary>The Plugins folder the loader uses ([Yu-Gi-Oh-Core] PluginsPath, which the loader writes every time it starts), or null.</summary>
        public static string? PluginsFolder(string gameFolder)
        {
            string path = IniValue(gameFolder, "Yu-Gi-Oh-Core", "PluginsPath", "");
            if (path.Length > 0 && Directory.Exists(path))
                return path;
            // next to this tool: Binaries\<config>\Tools\..\Plugins
            string beside = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Plugins"));
            return Directory.Exists(beside) ? beside : null;
        }

        /// <summary>The loader next to the Plugins folder, or null.</summary>
        public static string? LoaderExe(string? pluginsFolder)
        {
            if (pluginsFolder == null)
                return null;
            string exe = Path.Combine(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(pluginsFolder)) ?? "", "Yu-Gi-Oh_Loader.exe");
            return File.Exists(exe) ? exe : null;
        }

        /// <summary>The plugins installed (Plugins\*.dll and Plugins\YGO-Ex\*.dll), by name without .dll.</summary>
        public static HashSet<string> InstalledPlugins(string? pluginsFolder)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (pluginsFolder == null || !Directory.Exists(pluginsFolder))
                return names;
            foreach (string folder in new[] { pluginsFolder, Path.Combine(pluginsFolder, "YGO-Ex") }.Where(Directory.Exists))
                foreach (string dll in Directory.EnumerateFiles(folder, "*.dll"))
                    names.Add(Path.GetFileNameWithoutExtension(dll));
            return names;
        }

        public static bool IsStock(string plugin) => StockPlugins.Contains(plugin, StringComparer.OrdinalIgnoreCase);

        /// <summary>The plugins a Yu-Gi-Oh-Ex folder's content needs: its content.json (WolfX writes it), else what its files are known to need.</summary>
        public static IReadOnlyList<string> ContentPlugins(string contentFolder)
        {
            var plugins = new List<string>();
            void Add(string name)
            {
                name = StripDll(name);
                if (name.Length > 0 && !plugins.Contains(name, StringComparer.OrdinalIgnoreCase))
                    plugins.Add(name);
            }
            string manifest = Path.Combine(contentFolder, "content.json");
            if (File.Exists(manifest))
            {
                try
                {
                    if (JsonNode.Parse(File.ReadAllText(manifest), documentOptions: ReadOptions)?["content"] is JsonArray content)
                        foreach (JsonNode? entry in content)
                            if (entry?["plugins"] is JsonArray names)
                                foreach (JsonNode? name in names)
                                    if (name is JsonValue value && value.TryGetValue(out string? text))
                                        Add(text);
                    return plugins;
                }
                catch (Exception ex) when (ex is JsonException or IOException)
                {
                    // fall back to the known table
                }
            }
            if (!Directory.Exists(contentFolder))
                return plugins;
            foreach (var (pattern, plugin) in KnownContent)
            {
                bool present = pattern.EndsWith('\\')
                    ? Directory.Exists(Path.Combine(contentFolder, pattern.TrimEnd('\\')))
                    : File.Exists(Path.Combine(contentFolder, pattern));
                if (present)
                    Add(plugin);
            }
            return plugins;
        }

        // ---- checks ----

        /// <summary>The "id"s of a content file's top-level array (cards.json's "cards", characters.json's "characters", ...).</summary>
        internal static IEnumerable<int> IdsIn(string path, string arrayKey)
        {
            if (!File.Exists(path))
                yield break;
            JsonNode? root;
            try
            {
                root = JsonNode.Parse(File.ReadAllText(path), documentOptions: ReadOptions);
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                yield break;
            }
            JsonArray? list = root as JsonArray ?? root?[arrayKey] as JsonArray;
            if (list == null)
                yield break;
            foreach (JsonNode? entry in list)
                if (entry?["id"] is JsonValue value && value.TryGetValue(out int id))
                    yield return id;
        }

        private static string Ranges(IEnumerable<int> ids)
        {
            var sorted = ids.Order().ToList();
            var parts = new List<string>();
            for (int i = 0; i < sorted.Count;)
            {
                int j = i;
                while (j + 1 < sorted.Count && sorted[j + 1] == sorted[j] + 1)
                    ++j;
                parts.Add(i == j ? $"{sorted[i]}" : $"{sorted[i]}-{sorted[j]}");
                i = j + 1;
            }
            return parts.Count > 6 ? string.Join(", ", parts.Take(6)) + $" and {parts.Count - 6} more" : string.Join(", ", parts);
        }

        /// <summary>
        /// What to know before playing with these mods (the ones that are on, in order, with the game folder's own content last):
        /// plugins that are missing, two mods adding cards with the same ids (only one of each can exist, and the save keeps cards by id),
        /// game files changed by more than one (the later one wins), and ids that clash in other content.
        /// </summary>
        public static List<ModIssue> Check(IReadOnlyList<Mod> modsInOrder, string? pluginsFolder)
        {
            var issues = new List<ModIssue>();
            var active = modsInOrder.Where(m => m.Enabled || m.IsLocal).ToList();
            var installed = InstalledPlugins(pluginsFolder);

            foreach (Mod mod in active)
            {
                foreach (ModRequirement need in mod.NeededPlugins())
                {
                    if (installed.Contains(need.Plugin) || mod.BundledPlugins().Contains(need.Plugin, StringComparer.OrdinalIgnoreCase))
                        continue;
                    string where = need.Url.Length > 0 ? "get it from the link" : IsStock(need.Plugin) ? "it comes with Yu-Gi-Oh-Ex, install the full set" : "no link given, ask the mod's author";
                    issues.Add(new ModIssue(IssueLevel.Error, mod.Id, $"Needs the plugin {need.Plugin}, which isn't installed ({where}).", need.Url.Length > 0 ? need.Url : null));
                }
                var (added, changed) = mod.CardIds();
                var outside = added.Where(id => id > 19999).ToList();
                if (outside.Count > 0)
                    issues.Add(new ModIssue(IssueLevel.Error, mod.Id, $"Card ids {Ranges(outside)} are above 19999; the game's save has no room for them."));
            }

            // the same new card id in two mods: the later one replaces the earlier (Cards plugin), and the save can't tell them apart
            for (int later = 1; later < active.Count; ++later)
            {
                for (int earlier = 0; earlier < later; ++earlier)
                {
                    var clash = active[later].CardIds().New.Intersect(active[earlier].CardIds().New).ToList();
                    if (clash.Count == 0)
                        continue;
                    issues.Add(new ModIssue(IssueLevel.Warning, active[later].Id,
                        $"New cards {Ranges(clash)} use the same ids as \"{active[earlier].Info.Name}\": only this mod's versions exist in the game. " +
                        "Cards you own are kept by id, so they would turn into the other mod's cards if you swap them. Ask an author to move their ids."));
                    issues.Add(new ModIssue(IssueLevel.Warning, active[earlier].Id,
                        $"New cards {Ranges(clash)} are replaced by \"{active[later].Info.Name}\", which uses the same ids."));
                }
            }

            // game files changed by more than one: the last one wins (Core Patch.h)
            var owners = new Dictionary<string, List<Mod>>(StringComparer.OrdinalIgnoreCase);
            foreach (Mod mod in active)
                foreach (string file in mod.GameFiles())
                {
                    if (!owners.TryGetValue(file, out var list))
                        owners[file] = list = [];
                    list.Add(mod);
                }
            foreach (var group in owners.Where(o => o.Value.Count > 1).GroupBy(o => (Winner: o.Value[^1], Losers: string.Join("|", o.Value.Take(o.Value.Count - 1).Select(m => m.Id)))))
            {
                Mod winner = group.Key.Winner;
                var losers = group.First().Value.Take(group.First().Value.Count - 1).ToList();
                var files = group.Select(g => g.Key).ToList();
                string list = files.Count > 4 ? string.Join(", ", files.Take(4)) + $" and {files.Count - 4} more" : string.Join(", ", files);
                foreach (Mod loser in losers)
                    issues.Add(new ModIssue(IssueLevel.Warning, loser.Id,
                        $"{(winner.IsLocal ? "Your own files replace" : $"\"{winner.Info.Name}\" (later in the order) replaces")} its {list}: those changes of this mod don't show."));
                issues.Add(new ModIssue(IssueLevel.Info, winner.Id,
                    $"Its {list} replace{(files.Count == 1 ? "s" : "")} the same file{(files.Count == 1 ? "" : "s")} from {string.Join(", ", losers.Select(l => l.IsLocal ? "your own files" : $"\"{l.Info.Name}\""))}."));
            }

            // the same id in other content made of numbered entries: the later one wins
            foreach (var (file, key, what) in new[] { ("characters.json", "characters", "characters"), ("decks.json", "decks", "decks") })
            {
                for (int later = 1; later < active.Count; ++later)
                    for (int earlier = 0; earlier < later; ++earlier)
                    {
                        var clash = IdsIn(Path.Combine(active[later].ContentFolder, file), key)
                            .Intersect(IdsIn(Path.Combine(active[earlier].ContentFolder, file), key)).ToList();
                        if (clash.Count > 0)
                            issues.Add(new ModIssue(IssueLevel.Info, active[later].Id,
                                $"Changes the same {what} as \"{active[earlier].Info.Name}\" ({Ranges(clash)}); this mod's changes win."));
                    }
            }
            return issues;
        }

        // ---- install, remove, export, create ----

        public static bool IsGameRunning() => Process.GetProcessesByName("YuGiOh").Length > 0;

        /// <summary>What a mod .zip holds, before it is installed.</summary>
        public sealed class Package
        {
            public required string ZipPath { get; init; }
            public required string Root { get; init; }   // "" or "folder/": where mod.json / Yu-Gi-Oh-Ex / ... sit in the zip
            public required ModInfo Info { get; init; }
            public required string SuggestedId { get; init; }
            public List<string> Plugins { get; } = [];    // zip entries under Plugins/ (any file: dlls and their manifests)
            public IEnumerable<string> PluginDlls => Plugins.Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)).Select(Path.GetFileName).OfType<string>();
        }

        private static readonly string[] ModMarkers = ["mod.json", "info.ini", "yu-gi-oh-ex/", "ygo_2020/", "ygo_2020-ex.toc", "plugins/"];

        /// <summary>Reads a mod .zip (mod.json at the top, or inside one folder). Throws InvalidDataException when it isn't a mod.</summary>
        public static Package Inspect(string zipPath)
        {
            using ZipArchive zip = ZipFile.OpenRead(zipPath);
            var names = zip.Entries.Select(e => e.FullName.Replace('\\', '/')).ToList();
            bool IsMod(string prefix) => names.Any(n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                ModMarkers.Any(m => n[prefix.Length..].StartsWith(m, StringComparison.OrdinalIgnoreCase)));

            string root = "";
            if (!IsMod(""))
            {
                var tops = names.Select(n => n.Split('/')[0]).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                root = tops.Count == 1 && IsMod(tops[0] + "/") ? tops[0] + "/" : throw new InvalidDataException(
                    "This .zip isn't a mod: it needs a mod.json, a Yu-Gi-Oh-Ex folder, a YGO_2020 folder or a YGO_2020-Ex patch at the top (or in one folder).");
            }

            string fallback = root.Length > 0 ? root.TrimEnd('/') : Path.GetFileNameWithoutExtension(zipPath);
            ModInfo info;
            ZipArchiveEntry? manifest = zip.Entries.FirstOrDefault(e => e.FullName.Replace('\\', '/').Equals(root + "mod.json", StringComparison.OrdinalIgnoreCase));
            ZipArchiveEntry? ini = zip.Entries.FirstOrDefault(e => e.FullName.Replace('\\', '/').Equals(root + "Info.ini", StringComparison.OrdinalIgnoreCase));
            if (manifest != null)
            {
                using var reader = new StreamReader(manifest.Open());
                try
                {
                    info = ModInfo.FromJson(JsonNode.Parse(reader.ReadToEnd(), documentOptions: ReadOptions) as JsonObject, fallback);
                }
                catch (JsonException ex)
                {
                    throw new InvalidDataException("Its mod.json isn't valid JSON: " + ex.Message);
                }
            }
            else if (ini != null)
            {
                string temp = Path.GetTempFileName();
                try
                {
                    ini.ExtractToFile(temp, overwrite: true);
                    info = ReadIni(temp, fallback);
                }
                finally
                {
                    File.Delete(temp);
                }
            }
            else
                info = new ModInfo { Name = fallback };

            var package = new Package { ZipPath = zipPath, Root = root, Info = info, SuggestedId = MakeId(info.Name) };
            package.Plugins.AddRange(names.Where(n => n.StartsWith(root + "Plugins/", StringComparison.OrdinalIgnoreCase) && !n.EndsWith('/')));
            return package;
        }

        private static ModInfo ReadIni(string path, string fallback)
        {
            string folder = Path.Combine(Path.GetTempPath(), "ygoex-mod-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                File.Copy(path, Path.Combine(folder, "Info.ini"));
                return ModInfo.Read(folder, fallback);
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        /// <summary>
        /// Unpacks a mod into &lt;game&gt;\Mods\&lt;id&gt; (replacing what was there: an update keeps its place and on/off state), and copies the
        /// plugins it brings into the loader's Plugins folder when <paramref name="installPlugins"/> is true. Returns the mod list, saved.
        /// </summary>
        public static List<Mod> Install(string gameFolder, Package package, string id, bool installPlugins, string? pluginsFolder)
        {
            string target = Path.Combine(ModsFolder(gameFolder), id);
            List<Mod> mods = Load(gameFolder);

            // unpack next to it first, so a broken zip doesn't leave half a mod
            string staging = target + ".installing";
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
            Directory.CreateDirectory(staging);
            string fullStaging = Path.GetFullPath(staging) + Path.DirectorySeparatorChar;
            using (ZipArchive zip = ZipFile.OpenRead(package.ZipPath))
            {
                foreach (ZipArchiveEntry entry in zip.Entries)
                {
                    string name = entry.FullName.Replace('\\', '/');
                    if (!name.StartsWith(package.Root, StringComparison.OrdinalIgnoreCase))
                        continue;
                    string relative = name[package.Root.Length..];
                    if (relative.Length == 0)
                        continue;
                    string destination = Path.GetFullPath(Path.Combine(staging, relative.Replace('/', Path.DirectorySeparatorChar)));
                    if (!destination.StartsWith(fullStaging, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException($"The zip has a file that points outside the mod folder ({entry.FullName}); not installed.");
                    if (name.EndsWith('/'))
                    {
                        Directory.CreateDirectory(destination);
                        continue;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    entry.ExtractToFile(destination, overwrite: true);
                }
            }
            // a mod without mod.json gets one (from its Info.ini or the zip's name), so the Mod Manager and the game agree on its name
            if (!File.Exists(Path.Combine(staging, ModInfo.FileName)))
                File.WriteAllText(Path.Combine(staging, ModInfo.FileName), package.Info.ToJsonText(), new UTF8Encoding(false));

            if (Directory.Exists(target))
                Directory.Delete(target, recursive: true);
            Directory.Move(staging, target);

            if (installPlugins && pluginsFolder != null)
                CopyPlugins(Path.Combine(target, "Plugins"), pluginsFolder);

            // keep an updated mod's place and state; a new one goes last (on top), switched on
            List<Mod> updated = Load(gameFolder);
            var order = new List<Mod>();
            foreach (Mod old in mods)
            {
                Mod? now = updated.FirstOrDefault(m => m.Id.Equals(old.Id, StringComparison.OrdinalIgnoreCase));
                if (now != null)
                {
                    now.Enabled = old.Enabled;
                    order.Add(now);
                }
            }
            order.AddRange(updated.Where(m => !order.Contains(m)));
            SaveList(gameFolder, order);
            return order;
        }

        /// <summary>Copies a mod's Plugins folder (dlls, manifests, Plugins\YGO-Ex\...) into the loader's Plugins folder.</summary>
        public static void CopyPlugins(string modPlugins, string pluginsFolder)
        {
            if (!Directory.Exists(modPlugins))
                return;
            foreach (string file in Directory.EnumerateFiles(modPlugins, "*", SearchOption.AllDirectories))
            {
                string destination = Path.Combine(pluginsFolder, Path.GetRelativePath(modPlugins, file));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                try
                {
                    File.Copy(file, destination, overwrite: true);
                }
                catch (IOException ex)
                {
                    throw new IOException($"{Path.GetFileName(file)} couldn't be copied to the Plugins folder ({ex.Message}). Close the game and try again.", ex);
                }
            }
        }

        /// <summary>Deletes a mod's folder and takes it off the list. Plugins it installed stay (other mods may need them).</summary>
        public static List<Mod> Remove(string gameFolder, Mod mod, IReadOnlyList<Mod> order)
        {
            if (Directory.Exists(mod.Folder))
                Directory.Delete(mod.Folder, recursive: true);
            var rest = order.Where(m => m != mod && !m.IsLocal).ToList();
            SaveList(gameFolder, rest);
            return rest;
        }

        /// <summary>Packs an installed mod (its whole folder, plugins included) into a .zip to share.</summary>
        public static void Export(Mod mod, string zipPath)
        {
            if (mod.IsLocal)
                throw new InvalidOperationException("Use Create mod for your own files.");
            string temp = zipPath + ".tmp";
            if (File.Exists(temp))
                File.Delete(temp);
            ZipFile.CreateFromDirectory(mod.Folder, temp, CompressionLevel.Optimal, includeBaseDirectory: false);
            File.Move(temp, zipPath, overwrite: true);
        }

        /// <summary>The top-level entries of a Yu-Gi-Oh-Ex folder a new mod can take (the player's own files left out).</summary>
        public static List<string> ContentEntries(string contentFolder)
        {
            if (!Directory.Exists(contentFolder))
                return [];
            return [.. Directory.EnumerateFileSystemEntries(contentFolder)
                .Select(Path.GetFileName).OfType<string>()
                .Where(name => !IsPrivateContent(name) && !name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.OrdinalIgnoreCase)];
        }

        /// <summary>Writes a new mod .zip: mod.json, the chosen content as Yu-Gi-Oh-Ex\, the patch as YGO_2020-Ex.toc/.dat, loose game files as
        /// YGO_2020\, plugins as Plugins\ (and Plugins\YGO-Ex\ for the ones next to a YGO-Ex folder).</summary>
        public static void Create(ModCreateOptions options, string zipPath)
        {
            string temp = zipPath + ".tmp";
            if (File.Exists(temp))
                File.Delete(temp);
            using (var stream = File.Create(temp))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                ZipArchiveEntry manifest = zip.CreateEntry(ModInfo.FileName, CompressionLevel.Optimal);
                using (var writer = new StreamWriter(manifest.Open(), new UTF8Encoding(false)))
                    writer.Write(options.Info.ToJsonText());

                void AddFile(string source, string entryName) => zip.CreateEntryFromFile(source, entryName.Replace('\\', '/'), CompressionLevel.Optimal);
                void AddFolder(string source, string prefix)
                {
                    foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
                        AddFile(file, prefix + Path.GetRelativePath(source, file));
                }

                foreach (string entry in options.ContentEntries)
                {
                    string path = Path.Combine(options.ContentFolder, entry);
                    if (File.Exists(path))
                        AddFile(path, @"Yu-Gi-Oh-Ex\" + entry);
                    else if (Directory.Exists(path))
                        AddFolder(path, @"Yu-Gi-Oh-Ex\" + entry + @"\");
                }
                if (options.PatchToc.Length > 0)
                {
                    AddFile(options.PatchToc, "YGO_2020-Ex.toc");
                    AddFile(Path.ChangeExtension(options.PatchToc, ".dat"), "YGO_2020-Ex.dat");
                }
                if (options.LooseFolder.Length > 0 && Directory.Exists(options.LooseFolder))
                    AddFolder(options.LooseFolder, @"YGO_2020\");
                foreach (string dll in options.Plugins)
                {
                    bool gui = string.Equals(Path.GetFileName(Path.GetDirectoryName(dll)), "YGO-Ex", StringComparison.OrdinalIgnoreCase);
                    string prefix = gui ? @"Plugins\YGO-Ex\" : @"Plugins\";
                    AddFile(dll, prefix + Path.GetFileName(dll));
                    string json = Path.ChangeExtension(dll, ".json");
                    if (File.Exists(json))
                        AddFile(json, prefix + Path.GetFileName(json));
                }
            }
            File.Move(temp, zipPath, overwrite: true);
        }
    }
}
