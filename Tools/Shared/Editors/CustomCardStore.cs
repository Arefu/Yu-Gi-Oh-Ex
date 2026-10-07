using System.Text.Json.Nodes;

namespace Wolf.Editors
{
    /// <summary>
    /// Yu-Gi-Oh-Ex\cards.json, the custom cards (WolfX's New cards page owns it). Everything about a custom card is kept in its own cards.json
    /// entry, so the pages that also edit game cards (Card genres, Related cards, Card links) read and write a custom card's part here, under
    /// its key ("genres", "related", "links"), instead of in their own Yu-Gi-Oh-Ex JSON. Game cards are unchanged: they still go to the
    /// game's files, or the page's JSON for what those can't hold.
    /// </summary>
    public interface ICustomCardStore
    {
        /// <summary>True when cards.json has a card with this id (only those are kept here; an id it doesn't have stays in the page's JSON).</summary>
        bool Has(int id);

        /// <summary>The ids of every custom card.</summary>
        IReadOnlyCollection<int> Ids { get; }

        /// <summary>The card's value under this key, or null when it has none (or there is no such card).</summary>
        JsonNode? Get(int id, string key);

        /// <summary>Sets (null: removes) the card's value under this key; cards.json then has unsaved changes. Nothing happens for an unknown id.</summary>
        void Set(int id, string key, JsonNode? value);

        /// <summary>Saves cards.json when it has changes (a save already under way finishes it). False when it couldn't.</summary>
        bool Save();

        /// <summary>The card list changed shape (loaded, added, deleted, renumbered): read the custom cards' parts again.</summary>
        event Action? Changed;
    }

    /// <summary>The open cards.json (set by WolfX's window), or null outside WolfX.</summary>
    public static class CustomCards
    {
        public static ICustomCardStore? Store { get; set; }

        public static bool Has(int id) => Store?.Has(id) == true;

        /// <summary>The same value as JSON text (so two nodes compare by content).</summary>
        public static bool Same(JsonNode? a, JsonNode? b) => (a?.ToJsonString() ?? "") == (b?.ToJsonString() ?? "");

        /// <summary>Sets the value only when it differs, so reading a card back doesn't mark cards.json changed.</summary>
        public static void SetIfChanged(int id, string key, JsonNode? value)
        {
            if (Store is { } store && store.Has(id) && !Same(store.Get(id, key), value))
                store.Set(id, key, value);
        }
    }
}
