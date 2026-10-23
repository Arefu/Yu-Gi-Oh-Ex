using DeckData;
using Types;
using WolfX.Types;

namespace Wolf.Editors
{
    /// <summary>
    /// The Characters and Decks pages show each other's data (a character's deck list, a deck's owner list). Each page registers a getter for
    /// its own in-memory table here, and the other page reads it when it is shown, so a deck or character you just added (saved or not)
    /// can be picked without reopening the game data. The getters return null while the page has nothing open.
    /// </summary>
    internal static class LiveTables
    {
        /// <summary>The Characters page's table, with its unsaved changes.</summary>
        public static Func<CharacterTable?>? Characters;

        /// <summary>The Decks page's table, with its unsaved changes.</summary>
        public static Func<DeckDataTable?>? Decks;
    }
}