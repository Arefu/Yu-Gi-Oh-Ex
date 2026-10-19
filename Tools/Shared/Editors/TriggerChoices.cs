using System.Text.Json.Nodes;

namespace Wolf.Editors
{
    /// <summary>Which of a row's extra columns an event uses (the others are greyed out). Voice Over and Videos pages.</summary>
    [Flags]
    public enum TriggerUses { None = 0, Amount = 1, Cards = 2, How = 4, Reason = 8, Kind = 16, Chain = 32, Standing = 64 }

    /// <summary>A key with the text a combo box shows for it.</summary>
    public sealed class Choice<T>(T key, string text)
    {
        public T Key { get; } = key;
        public string Text { get; } = text;
    }

    /// <summary>
    /// The duel-event conditions voices.json lines and videos.json "duel" entries share (Yu-Gi-Oh-Media Trigger.h): the names the files use
    /// and the text the editors show for them.
    /// </summary>
    public static class TriggerChoices
    {
        /// <summary>"how" on a summon entry.</summary>
        public static readonly (string Key, string Text)[] SummonKinds =
        [
            ("any", "any way"),
            ("normal", "Normal / Tribute Summon"),
            ("flip", "Flip Summon"),
            ("special", "Special Summon (any kind)"),
            ("fusion", "Fusion Summon"),
            ("synchro", "Synchro Summon"),
            ("xyz", "Xyz Summon"),
            ("link", "Link Summon"),
            ("ritual", "Ritual Summon"),
            ("pendulum", "Pendulum Summon"),
        ];

        /// <summary>"kind" on an activate / set / destroyed entry.</summary>
        public static readonly (string Key, string Text)[] CardKinds = [("any", "any card"), ("monster", "a monster"), ("spell", "a Spell"), ("trap", "a Trap")];

        /// <summary>"response" on an activate entry (null = either).</summary>
        public static readonly (string Key, string Text)[] ChainChoices = [("either", "either"), ("start", "starts a chain"), ("response", "in response")];

        /// <summary>"standing" on a turnStart entry: the side's LP against the other duelist's.</summary>
        public static readonly (string Key, string Text)[] Standings = [("any", "any"), ("winning", "winning on LP"), ("losing", "losing on LP"), ("even", "even on LP")];

        /// <summary>"once": at most once per duel (true), once per turn ("turn"), or every time (false).</summary>
        public static readonly (string Key, string Text)[] OnceChoices = [("duel", "once per duel"), ("turn", "once per turn"), ("never", "every time")];

        /// <summary>"reason" on a win / lose entry: the game's DuelWinReason (index = number), docs/StatsAndMatchResults.md.</summary>
        public static readonly (string Key, string Text)[] WinReasons =
        [
            ("any", "any reason"), ("lp", "LP reduced to 0"), ("deckOut", "deck-out"), ("timeLimit", "time / turn limit"), ("surrender", "surrender"),
            ("rules", "broke the rules"), ("exodia", "Exodia the Forbidden One"), ("destinyBoard", "Destiny Board"), ("yataLock", "Yata-Garasu lock"),
            ("lastTurn", "Last Turn"), ("finalCountdown", "Final Countdown"), ("effect", "a match-winning effect (11)"), ("vennominaga", "Vennominaga"),
            ("exodius", "Exodius the Ultimate Forbidden Lord"), ("effect14", "a match-winning effect (14)"), ("leo", "Number 88: Gimmick Puppet of Leo"),
            ("disasterLeo", "Number C88: Gimmick Puppet Disaster Leo"), ("jackpot7", "Jackpot 7"), ("effect18", "a match-winning effect (18)"),
            ("relaySoul", "Relay Soul"), ("ghostrickAngel", "Ghostrick Angel of Mischief"), ("phantasmSpiral", "Phantasm Spiral Assault"),
            ("faWinners", "F.A. Winners"), ("flyingElephant", "Flying Elephant"), ("exodiaDefender", "Exodia, the Legendary Defender"),
        ];

        public static string TextOf((string Key, string Text)[] choices, string key) => choices.FirstOrDefault(c => c.Key == key).Text ?? key;

        /// <summary>A key read from the file (any case), or the first choice.</summary>
        public static string KeyOf((string Key, string Text)[] choices, JsonNode? node) =>
            choices.FirstOrDefault(c => string.Equals(c.Key, node?.ToString(), StringComparison.OrdinalIgnoreCase)).Key ?? choices[0].Key;

        public static double? Num(JsonNode? node) => node is JsonValue value && value.TryGetValue<double>(out double d) ? d : null;

        /// <summary>"reason" is a name ("exodia") or the DuelWinReason number.</summary>
        public static string ReasonKey(JsonNode? node)
        {
            if (Num(node) is double number)
                return number >= 0 && number < WinReasons.Length ? WinReasons[(int)number].Key : "any";
            return KeyOf(WinReasons, node);
        }

        /// <summary>"once" as the file has it: true / false / "turn".</summary>
        public static string OnceKey(JsonNode? node) =>
            node is JsonValue once ? (once.TryGetValue<bool>(out bool o) ? (o ? "duel" : "never") : once.ToString() == "turn" ? "turn" : "duel") : "duel";

        public static JsonNode OnceNode(string key) => key switch { "turn" => JsonValue.Create("turn"), "never" => JsonValue.Create(false), _ => JsonValue.Create(true) };

        /// <summary>"cards": [..] or "card": n.</summary>
        public static List<int> Cards(JsonObject node)
        {
            var cards = new List<int>();
            foreach (string key in new[] { "cards", "card" })
            {
                if (Num(node[key]) is double single)
                    cards.Add((int)single);
                else if (node[key] is JsonArray list)
                    cards.AddRange(list.Select(Num).OfType<double>().Select(d => (int)d));
            }
            return cards;
        }
    }
}
