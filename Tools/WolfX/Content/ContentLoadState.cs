using System.Text.Json;
using System.Text.Json.Nodes;

namespace WolfEx
{
    /// <summary>A failed read must never turn an existing file into an empty editable document.</summary>
    internal sealed class ContentLoadState
    {
        public string? Error { get; private set; }
        public void Reset() => Error = null;
        public void Failed(Exception error) => Error = error.Message;
        public void EnsureLoaded()
        {
            if (Error != null)
                throw new InvalidDataException("Saving is blocked because the file could not be loaded. Repair it and reopen the game folder first.\n" + Error);
        }

        internal static JsonNode? Read(string path) => !File.Exists(path) ? null :
            JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions
            { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })
            ?? throw new InvalidDataException(path + ": expected a JSON document.");

        internal static JsonArray Objects(JsonNode? node, string field)
        {
            if (node is not JsonArray array || array.Any(item => item is not JsonObject))
                throw new InvalidDataException("Expected an array of objects: " + field);
            return array;
        }

        internal static JsonArray Cards(JsonNode root)
        {
            var array = Objects(root is JsonArray ? root : (root as JsonObject)?["cards"], "cards");
            var ids = new HashSet<int>();
            foreach (JsonObject entry in array)
                if (entry["id"] is not JsonValue id || !id.TryGetValue<int>(out int value) || !ids.Add(value))
                    throw new InvalidDataException("Every card must have a unique integer id.");
            return array;
        }

        internal static void Fields<T>(JsonObject entry, params string[] fields)
        {
            foreach (string field in fields)
                if (entry.ContainsKey(field) && (entry[field] is not JsonValue value || !value.TryGetValue<T>(out _)))
                    throw new InvalidDataException("Invalid value for " + field + ".");
        }
    }
}
