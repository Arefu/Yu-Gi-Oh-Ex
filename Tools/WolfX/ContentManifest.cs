using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Wolf.Editors;

namespace WolfX
{
    /// <summary>
    /// Yu-Gi-Oh-Ex\content.json: every piece of additional content in the Yu-Gi-Oh-Ex folder and the plugins it needs, so the loader can switch
    /// them on and say which are missing (Dependencies\Yu-Gi-Oh-Ex\Yu-Gi-Oh-Manifest.h, ApplyContent). Which plugin applies which file
    /// comes from the plugins' manifests ("content"), read from the plugin folder the game uses ([Yu-Gi-Oh-Core] PluginsPath in Config.ini),
    /// with <see cref="Known"/> for plugins that aren't there. Rewritten whenever the folder changes.
    /// </summary>
    internal static class ContentManifest
    {
        public const string FileName = "content.json";

        /// <summary>What the plugins in this repository apply, for when their manifests can't be found (content made for another PC).</summary>
        private static readonly (string Pattern, string Plugin)[] Known =
        [
            ("cards.json", "Yu-Gi-Oh-MoreCards"), ("unlocks.json", "Yu-Gi-Oh-MoreCards"), ("genres.json", "Yu-Gi-Oh-MoreCards"),
            ("relatedcards.json", "Yu-Gi-Oh-MoreCards"), ("text.json", "Yu-Gi-Oh-MoreCards"),
            ("characters.json", "Yu-Gi-Oh-Campaign"), ("decks.json", "Yu-Gi-Oh-Campaign"), ("storyduels.json", "Yu-Gi-Oh-Campaign"),
            ("storyscripts.json", "Yu-Gi-Oh-Campaign"),
            ("packs.json", "Yu-Gi-Oh-BetterCardShop"),
            (@"pages\", "Yu-Gi-Oh-RIX"), (@"menus\", "Yu-Gi-Oh-RIX"),
        ];

        /// <summary>What WolfX saves that no plugin applies yet: listed (with no plugins) so people can see it isn't used in the game.</summary>
        private static readonly string[] NotAppliedYet = ["cardlinks.json", @"tutorials\", @"howtoplay\", @"sprites\", @"animlists\"];

        private static readonly JsonSerializerOptions WriteOptions = new()
        {
            TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
            WriteIndented = true,
        };

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetPrivateProfileString(string section, string key, string fallback, StringBuilder value, int size, string file);

        /// <summary>The plugin folder the game uses: [Yu-Gi-Oh-Core] PluginsPath in its Config.ini, else &lt;game&gt;\Plugins.</summary>
        public static string PluginsFolder(string gameFolder)
        {
            string ini = Path.Combine(gameFolder, "Config.ini");
            if (File.Exists(ini))
            {
                var value = new StringBuilder(1024);
                GetPrivateProfileString("Yu-Gi-Oh-Core", "PluginsPath", "", value, value.Capacity, ini);
                if (value.Length > 0 && Directory.Exists(value.ToString()))
                    return value.ToString();
            }
            return Path.Combine(gameFolder, "Plugins");
        }

        /// <summary>Content pattern ("cards.json", "pages\") -> the plugins that apply it, from the manifests and <see cref="Known"/>.</summary>
        public static Dictionary<string, List<string>> Claims(string gameFolder)
        {
            var claims = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            string folder = PluginsFolder(gameFolder);
            foreach (string manifest in new[] { folder, Path.Combine(folder, "YGO-Ex") }.Where(Directory.Exists)
                         .SelectMany(f => Directory.EnumerateFiles(f, "*.json")))
            {
                try
                {
                    if (JsonNode.Parse(File.ReadAllText(manifest), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })?["content"] is not JsonArray list)
                        continue;
                    string plugin = Path.GetFileNameWithoutExtension(manifest);
                    foreach (string pattern in list.Select(n => n?.GetValue<string>()).OfType<string>())
                        Add(claims, pattern, plugin);
                }
                catch (Exception ex) when (ex is JsonException or IOException or InvalidOperationException)
                {
                    // not a manifest, or a broken one: it claims nothing
                }
            }
            foreach (var (pattern, plugin) in Known)
                if (!claims.ContainsKey(Normal(pattern)))
                    Add(claims, pattern, plugin);
            return claims;
        }

        private static string Normal(string pattern) => pattern.Replace('/', '\\');

        private static void Add(Dictionary<string, List<string>> claims, string pattern, string plugin)
        {
            pattern = Normal(pattern);
            if (!claims.TryGetValue(pattern, out var plugins))
                claims[pattern] = plugins = [];
            if (!plugins.Contains(plugin, StringComparer.OrdinalIgnoreCase))
                plugins.Add(plugin);
        }

        /// <summary>
        /// The content entries for the folder: each top-level file and sub-folder ("pages\") a plugin applies, and the ones WolfX makes that
        /// nothing applies yet. Anything else there (backups, pictures cards.json points at, Core's saves.json) is left out.
        /// </summary>
        public static List<(string File, List<string> Plugins)> Build(GameFolderFiles files)
        {
            var result = new List<(string, List<string>)>();
            if (!Directory.Exists(files.ExFolder))
                return result;
            var claims = Claims(files.GameFolder);
            List<string> PluginsFor(string entry) =>
                claims.Where(c => c.Key.EndsWith('\\') ? entry.StartsWith(c.Key, StringComparison.OrdinalIgnoreCase) : c.Key.Equals(entry, StringComparison.OrdinalIgnoreCase))
                    .SelectMany(c => c.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            foreach (string file in Directory.EnumerateFiles(files.ExFolder, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                string name = Path.GetFileName(file);
                if (name.Equals(FileName, StringComparison.OrdinalIgnoreCase))
                    continue;
                var plugins = PluginsFor(name);
                if (plugins.Count == 0 && !NotAppliedYet.Contains(name, StringComparer.OrdinalIgnoreCase))
                    continue;
                // a card with an effect needs the effect engine as well
                if (name.Equals("cards.json", StringComparison.OrdinalIgnoreCase) && File.ReadAllText(file).Contains("\"effectClone\"", StringComparison.Ordinal))
                    plugins.Add("Yu-Gi-Oh-Effects");
                result.Add((name, plugins));
            }
            foreach (string folder in Directory.EnumerateDirectories(files.ExFolder).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                if (!Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Any())
                    continue;
                string entry = Path.GetFileName(folder) + "\\";
                var plugins = PluginsFor(entry);
                if (plugins.Count > 0 || NotAppliedYet.Contains(entry, StringComparer.OrdinalIgnoreCase))
                    result.Add((entry, plugins));
            }
            return result;
        }

        /// <summary>Writes content.json when what it should say changed (deletes it when the folder has no content). Returns the entries.</summary>
        public static List<(string File, List<string> Plugins)> Update(GameFolderFiles files)
        {
            var entries = Build(files);
            string path = files.ExPath(FileName);
            try
            {
                if (entries.Count == 0)
                {
                    if (File.Exists(path))
                        File.Delete(path);
                    return entries;
                }
                var list = new JsonArray();
                foreach (var (file, plugins) in entries)
                {
                    var entry = new JsonObject { ["file"] = file, ["plugins"] = new JsonArray(plugins.Select(p => (JsonNode)p).ToArray()) };
                    if (plugins.Count == 0)
                        entry["note"] = "no plugin applies this yet";
                    list.Add(entry);
                }
                var root = new JsonObject
                {
                    ["note"] = "Made by WolfX: the content in this folder and the plugins it needs. The loader switches them on and says which are missing.",
                    ["content"] = list,
                };
                string text = root.ToJsonString(WriteOptions) + Environment.NewLine;
                if (!File.Exists(path) || File.ReadAllText(path) != text)
                    File.WriteAllText(path, text);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // the next change tries again
            }
            return entries;
        }
    }
}
