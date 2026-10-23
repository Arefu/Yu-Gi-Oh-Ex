using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using CARD_Pass;
using DeckData;
using Types;

namespace Wolf.Editors
{
    /// <summary>
    /// Deck files in and out of the Decks page:
    /// <list type="bullet">
    /// <item>.ydk (YGOPro / EDOPro / Master Duel exports): "#main", "#extra", "!side" sections of card passwords, one per line. Passwords
    /// become the game's Konami ids through bin\CARD_Pass.bin (password per internal id) + bin\CARD_IntID.bin (internal -> Konami id), and
    /// the custom cards' "password" in Yu-Gi-Oh-Ex\cards.json.</item>
    /// <item>.ydc (the game's own deck files, decks.zib): Konami ids as they are.</item>
    /// </list>
    /// </summary>
    public static class DeckFiles
    {
        public sealed record Imported(List<ushort> Main, List<ushort> Extra, List<ushort> Side, List<string> Unknown);

        /// <summary>Password -> Konami id: the game's cards, then the custom ones (cards.json) on top.</summary>
        public static Dictionary<long, ushort> PasswordMap(GameFolderFiles files)
        {
            var map = new Dictionary<long, ushort>();
            if (files.Read(Card_Pass.GamePath) is { } pass && files.Read(CardIdMap.GamePath) is { } ids)
            {
                var passwords = Card_Pass.Parse(pass);
                foreach (var (internalId, konami) in CardIdMap.Parse(ids).KonamiByInternal())
                    if (internalId > 0 && internalId < passwords.Count && passwords[internalId] > 0 && konami is > 0 and <= ushort.MaxValue)
                        map[passwords[internalId]] = (ushort)konami;
            }
            string cardsJson = files.ExPath("cards.json");
            if (File.Exists(cardsJson))
            {
                try
                {
                    var root = JsonNode.Parse(File.ReadAllText(cardsJson));
                    var cards = root as JsonArray ?? root?["cards"] as JsonArray;
                    foreach (var card in cards?.OfType<JsonObject>() ?? [])
                        if (card["id"] is JsonValue id && id.TryGetValue<int>(out int konami) && card["password"] is JsonValue p && p.TryGetValue<long>(out long password) && password > 0)
                            map[password] = (ushort)konami;
                }
                catch (Exception) { }   // a broken cards.json: the game's passwords still work
            }
            return map;
        }

        /// <summary>Reads a .ydk or .ydc. A .ydk password with no card goes to Unknown (the deck is made without it).</summary>
        public static Imported Read(string path, GameFolderFiles files)
        {
            if (Path.GetExtension(path).Equals(".ydc", StringComparison.OrdinalIgnoreCase))
            {
                var ydc = YdcDeck.Parse(File.ReadAllBytes(path));
                return new([.. ydc.Main], [.. ydc.Extra], [.. ydc.Side], []);
            }

            var map = PasswordMap(files);
            var result = new Imported([], [], [], []);
            var section = result.Main;
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0)
                    continue;
                if (line.StartsWith('#') || line.StartsWith('!'))
                {
                    string name = line.TrimStart('#', '!').Trim().ToLowerInvariant();
                    section = name.StartsWith("extra") ? result.Extra : name.StartsWith("side") ? result.Side : name.StartsWith("main") ? result.Main : section;
                    continue;
                }
                string number = line.Split([' ', '\t', '#'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
                if (!long.TryParse(number, out long password))
                    continue;
                if (map.TryGetValue(password, out ushort konami))
                    section.Add(konami);
                else
                    result.Unknown.Add(number);
            }
            return result;
        }

        /// <summary>Writes a .ydk (passwords; a card with none is written as its Konami id with a comment) or a .ydc.</summary>
        public static void Write(string path, YdcDeck deck, GameFolderFiles files, string title)
        {
            if (Path.GetExtension(path).Equals(".ydc", StringComparison.OrdinalIgnoreCase))
            {
                File.WriteAllBytes(path, deck.ToBytes());
                return;
            }
            var byKonami = new Dictionary<ushort, long>();
            foreach (var (password, konami) in PasswordMap(files))
                byKonami.TryAdd(konami, password);
            var text = new StringBuilder();
            text.AppendLine($"#created by WolfX: {title}");
            void Section(string header, IEnumerable<ushort> cards)
            {
                text.AppendLine(header);
                foreach (ushort id in cards)
                    text.AppendLine(byKonami.TryGetValue(id, out long password) ? password.ToString() : $"0 # no password: Konami id {id}");
            }
            Section("#main", deck.Main);
            Section("#extra", deck.Extra);
            Section("!side", deck.Side);
            File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
        }
    }
}
