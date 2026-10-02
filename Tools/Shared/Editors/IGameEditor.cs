namespace Wolf.Editors
{
    /// <summary>
    /// A WolfX page that works on the open game data (<see cref="GameFolderFiles.Current"/>). Saving puts standard content (changes to
    /// things the game already has) back into the game's own files, and additional content (things the game's files can't hold: custom
    /// cards, new entries past the game's limits) into JSON in the Yu-Gi-Oh-Ex folder for the launcher plugins.
    /// </summary>
    public interface IGameEditor
    {
        /// <summary>Reads everything again from this data (another folder was opened, or another page saved files this one shows).</summary>
        void Open(GameFolderFiles files);

        /// <summary>True when there are changes that are not saved.</summary>
        bool Dirty { get; }

        /// <summary>Saves. Returns false when it couldn't (the page shows why).</summary>
        bool Save();

        /// <summary>The game files this page reads (archive paths), so it reopens when another page writes one of them.</summary>
        IReadOnlyCollection<string> Files { get; }

        /// <summary>One line for the page header: where its standard and additional content are saved.</summary>
        string SavesTo { get; }
    }

    /// <summary>A page the Card Manager also shows, for the card picked there (Card genres, Related cards, Card links): the same instance.</summary>
    public interface ICardFocus
    {
        /// <summary>True: only the picked card's part (no card list or toolbar), inside the Card Manager. False: the whole page.</summary>
        bool CardOnly { get; set; }

        /// <summary>Picks this card (one with nothing yet, such as a new custom card, too).</summary>
        void ShowCard(int konamiId);
    }

    /// <summary>What the game itself has, to tell standard content (saved in the game's files) from additional content (JSON).</summary>
    public static class GameContent
    {
        /// <summary>The game's Konami card ids; custom cards start at 15300 (ids 14969-15234 are "ghosts" the game half-knows).</summary>
        public const int FirstCardId = 3900, LastCardId = 14968;

        /// <summary>CARD_Named archetype codes 1-418 are the game's; custom archetypes come after.</summary>
        public const int FirstCustomArchetype = 419;

        public static bool IsGameCard(int id) => id is >= FirstCardId and <= LastCardId;
    }
}
