using System.Text.Json;

namespace WolfX
{
    internal static class ContentCompatibility
    {
        internal static readonly JsonDocumentOptions JsonOptions = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };
        internal sealed record Entry(string Content, string Plugins, string Status, bool Problem);

        internal static bool HasCompiledEffect(JsonElement node)
        {
            if (node.ValueKind == JsonValueKind.Object)
                return node.EnumerateObject().Any(p => p.NameEquals("effectClone") && p.Value.ValueKind == JsonValueKind.Object || HasCompiledEffect(p.Value));
            return node.ValueKind == JsonValueKind.Array && node.EnumerateArray().Any(HasCompiledEffect);
        }

        internal static List<Entry> Check(string gameFolder, string exFolder)
        {
            string pluginsFolder = ContentManifest.PluginsFolder(gameFolder);
            var dlls = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string folder in new[] { pluginsFolder, Path.Combine(pluginsFolder, "YGO-Ex") }.Where(Directory.Exists))
                foreach (string dll in Directory.EnumerateFiles(folder, "*.dll"))
                    dlls.TryAdd(Path.GetFileNameWithoutExtension(dll), dll);

            var result = new List<Entry>();
            foreach (var (content, plugins) in ContentManifest.Build(gameFolder, exFolder))
            {
                var problems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (plugins.Count == 0) problems.Add("No plugin applies this content; it will not appear in game.");
                var checkedPlugins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                void CheckPlugin(string plugin)
                {
                    plugin = plugin.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? plugin[..^4] : plugin;
                    if (!checkedPlugins.Add(plugin)) return;
                    if (!dlls.TryGetValue(plugin, out string? dll))
                    {
                        problems.Add($"Missing plugin: {plugin}");
                        return;
                    }
                    string manifest = Path.ChangeExtension(dll, ".json");
                    if (!File.Exists(manifest)) return;
                    try
                    {
                        using var json = JsonDocument.Parse(File.ReadAllText(manifest), JsonOptions);
                        foreach (string key in new[] { "requires", "Requires", "dlls", "Dlls" })
                            if (json.RootElement.TryGetProperty(key, out var needs))
                            {
                                if (needs.ValueKind != JsonValueKind.Array) throw new JsonException($"{key} must be an array.");
                                foreach (var need in needs.EnumerateArray())
                                {
                                    if (need.ValueKind != JsonValueKind.String) throw new JsonException($"{key} must contain plugin names.");
                                    CheckPlugin(need.GetString()!);
                                }
                            }
                    }
                    catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
                    {
                        problems.Add($"Cannot read manifest for {plugin}: {ex.Message}");
                    }
                }
                foreach (string plugin in plugins) CheckPlugin(plugin);
                if (plugins.Count > 0) CheckPlugin("Yu-Gi-Oh-Core");
                string path = Path.Combine(exFolder, content.TrimEnd('\\'));
                var jsonFiles = content.EndsWith('\\') ? Directory.EnumerateFiles(path, "*.json", SearchOption.AllDirectories) : new[] { path };
                foreach (string file in jsonFiles)
                {
                    try { using var parsed = JsonDocument.Parse(File.ReadAllText(file), JsonOptions); }
                    catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
                    {
                        problems.Add($"Cannot read {Path.GetRelativePath(exFolder, file)}: {ex.Message}");
                    }
                }
                result.Add(new Entry(content, string.Join(", ", plugins), problems.Count > 0 ? string.Join(Environment.NewLine, problems) :
                    "Required plugins are installed. The loader enables them for this content.", problems.Count > 0));
            }
            return result;
        }
    }
}
