using System.Text.Json;

namespace WolfEx
{
    /// <summary>A page that edits its own JSON file in the Yu-Gi-Oh-Ex folder (additional content), hosted by WolfUI.</summary>
    internal interface IContentPanel
    {
        string Title { get; }

        /// <summary>Reads the page's file from the Yu-Gi-Oh-Ex folder (it may not exist yet).</summary>
        void LoadFrom(string extraCardsFolder, string gameFolder);

        /// <summary>Writes the page's file. Returns false when something has to be fixed first.</summary>
        bool SaveTo(string extraCardsFolder);

        /// <summary>True when the page has changed since it was loaded or saved (<see cref="MarkSaved"/>).</summary>
        bool Dirty { get; }

        /// <summary>Called after loading and after a save: what the page holds now is what its file holds.</summary>
        void MarkSaved();
    }

    /// <summary>Remembers a page's data as it was loaded or saved (as JSON text), to tell whether it changed since.</summary>
    internal sealed class ContentSnapshot(Func<object> state)
    {
        private static readonly JsonSerializerOptions Options = new() { TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(), IncludeFields = true };
        private string? _saved;

        public void Mark() => _saved = Take();

        public bool Changed => _saved != null && Take() != _saved;

        private string Take() => JsonSerializer.Serialize(state(), Options);
    }
}
