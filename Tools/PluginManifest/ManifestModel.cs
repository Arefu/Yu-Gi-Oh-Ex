using System.Text.Json;
using System.Text.Json.Serialization;

namespace PluginManifest
{
    /// <summary>What a plugin's &lt;DLL name&gt;.json holds (see Dependencies\Yu-Gi-Oh-Ex\Yu-Gi-Oh-Manifest.h).</summary>
    public sealed class ManifestModel
    {
        [JsonPropertyName("title")] public string Title { get; set; } = "";
        [JsonPropertyName("author")] public string Author { get; set; } = "";
        [JsonPropertyName("version")] public string Version { get; set; } = "";
        [JsonPropertyName("description")] public string Description { get; set; } = "";
        [JsonPropertyName("enforced")] public bool Enforced { get; set; }
        [JsonPropertyName("requires")] public List<string> Requires { get; set; } = new();
        [JsonPropertyName("dlls")] public List<string> Dlls { get; set; } = new();
        /// <summary>The Yu-Gi-Oh-Ex files this plugin applies ("cards.json", a folder as "pages" + backslash); WolfX writes content.json from it.</summary>
        [JsonPropertyName("content")] public List<string> Content { get; set; } = new();
        /// <summary>Older plugins this one took over: names, or { "name", "note" } (kept as written).</summary>
        [JsonPropertyName("replaces")] public List<JsonElement> Replaces { get; set; } = new();
        /// <summary>Needs a save made with it: true (tag = the DLL's name) or a tag string (kept as written).</summary>
        [JsonPropertyName("newSave")] public JsonElement? NewSave { get; set; }
        /// <summary>The Config.ini settings this plugin reads (WolfX's Config editor merges them over its built-in list). Kept as written.</summary>
        [JsonPropertyName("settings")] public List<JsonElement> Settings { get; set; } = new();

        private static readonly JsonSerializerOptions Options = new()
        {
            TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        /// <summary>A missing or broken file is just an empty manifest.</summary>
        public static ManifestModel Read(string path)
        {
            try
            {
                if (File.Exists(path))
                    return JsonSerializer.Deserialize<ManifestModel>(File.ReadAllText(path), Options) ?? new ManifestModel();
            }
            catch (JsonException)
            {
            }
            return new ManifestModel();
        }

        /// <summary>The JSON as it is written: only the fields that are set, so a plain plugin gets a two line file.</summary>
        public string ToJson()
        {
            var fields = new Dictionary<string, object>();
            if (Title.Length > 0) fields["title"] = Title;
            if (Author.Length > 0) fields["author"] = Author;
            if (Version.Length > 0) fields["version"] = Version;
            if (Description.Length > 0) fields["description"] = Description;
            if (Enforced) fields["enforced"] = true;
            if (Requires.Count > 0) fields["requires"] = Requires;
            if (Dlls.Count > 0) fields["dlls"] = Dlls;
            if (Content.Count > 0) fields["content"] = Content;
            if (Replaces.Count > 0) fields["replaces"] = Replaces;
            if (NewSave is { ValueKind: JsonValueKind.True or JsonValueKind.String } newSave) fields["newSave"] = newSave;
            if (Settings.Count > 0) fields["settings"] = Settings;
            return JsonSerializer.Serialize(fields, Options) + Environment.NewLine;
        }
    }

    /// <summary>One DLL the tool found: a launcher plugin (Plugins\) or one Yu-Gi-Oh-Core starts (Plugins\YGO-Ex\).</summary>
    public sealed record PluginFile(string Name, bool Gui, string Folder)
    {
        public string ManifestPath => Path.Combine(Folder, Name + ".json");
        public bool HasManifest => File.Exists(ManifestPath);
        public override string ToString() => (Gui ? "YGO-Ex / " : "") + Name + (HasManifest ? "" : "   (no manifest)");
    }
}
