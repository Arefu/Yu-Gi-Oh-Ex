namespace DuelIt
{
    /// <summary>Human readable text for Duels.log events. Card names come from <see cref="CardNames"/>.</summary>
    public static class DuelText
    {
        public static Func<int, string> CardNames { get; set; } = id => $"#{id}";

        public static string PhaseName(int phase) => phase switch
        {
            0 => "Draw Phase",
            1 => "Standby Phase",
            2 => "Main Phase 1",
            3 => "Battle Phase",
            4 => "Main Phase 2",
            5 => "End Phase",
            _ => $"phase {phase}",
        };

        // DuelWinReason (strings_steam_E.bnd, docs/StatsAndMatchResults.md)
        private static readonly string[] WinReasons =
        [
            "none", "LP reduced to 0", "Deck-out", "Out of time / turn limit", "Surrender", "Broke the rules", "Exodia the Forbidden One",
            "Destiny Board", "Yata-Garasu lock", "Last Turn", "Final Countdown", "match-winning effect", "Vennominaga",
            "Exodius the Ultimate Forbidden Lord", "match-winning effect", "Number 88: Gimmick Puppet of Leo",
            "Number C88: Gimmick Puppet Disaster Leo", "Jackpot 7", "match-winning effect", "Relay Soul", "Ghostrick Angel of Mischief",
            "Phantasm Spiral Assault", "F.A. Winners", "Flying Elephant", "Exodia, the Legendary Defender",
        ];

        public static string WinReason(int reason) => reason >= 0 && reason < WinReasons.Length ? WinReasons[reason] : $"reason {reason}";

        /// <summary>PlayerState.field_3792 / DuelOutcome: 1 = the local side won, 2 = the other side, 3 = draw.</summary>
        public static string Winner(int winner, int localSide) => winner switch
        {
            1 => $"{Player(localSide, localSide)} won",
            2 => $"{Player(1 - localSide, localSide)} won",
            3 => "draw",
            _ => "no winner",
        };

        /// <summary>
        /// DuelResult (0x05): a1 = winner (as <see cref="Winner"/>), a2 / a3 = the win reason stored for side 0 / side 1. Only the
        /// winner's is set (the loser's reads "none"), so only that one is shown.
        /// </summary>
        public static string DuelResult(int a1, int a2, int a3, int localSide)
        {
            int winnerSide = a1 == 1 ? localSide : a1 == 2 ? 1 - localSide : -1;
            int reason = winnerSide == 0 ? a2 : winnerSide == 1 ? a3 : (a2 != 0 ? a2 : a3);
            return $"{Winner(a1, localSide)}, {WinReason(reason)}";
        }

        /// <summary>Engine side 0/1 shown as P1/P2, with who's who.</summary>
        public static string Player(int side, int localSide) => side == localSide ? $"P{side + 1} (you)" : $"P{side + 1} (opponent)";

        public static string Card(DuelState state, int slot)
        {
            int id = state.CardId(slot);
            return id == 0 ? $"card #{slot}" : CardNames(id);
        }

        public static string Describe(DuelEvent e, DuelState state, int localSide)
        {
            string P(int side) => Player(side & 1, localSide);
            switch (e.Tag)
            {
                case "DUEL_BEGIN":
                    return $"Duel begins: {e.Get("mode")}{(e.Int("online") != 0 ? $" ({e.Get("match")})" : "")}{(e.Int("tag") != 0 ? ", tag duel" : "")}, you are P{localSide + 1}, RNG {e.Get("rng")}";
                case "SEAT":
                    return $"Seat {e.Get("seat")}: {e.Get("name")}";
                case "CARDS":
                    return $"{P(e.Int("side"))} {e.Get("zone")}: {e.Get("cards").Split(',', StringSplitOptions.RemoveEmptyEntries).Length} cards";
                case "START":
                    return $"{P(e.Int("side"))} goes first";
                case "MOVE":
                {
                    var (fromZone, fromSide, _) = Zones.Decode(e.Int("from"));
                    var (toZone, toSide, _) = Zones.Decode(e.Int("to"));
                    string owner = fromSide == toSide ? P(toSide) : $"{P(fromSide)} -> {P(toSide)}";
                    return $"{owner}: {Card(state, e.Int("slot"))}  {Zones.Name(fromZone)} -> {Zones.Name(toZone)}";
                }
                case "LP":
                    return e.Get("kind") == "gain"
                        ? $"{P(e.Int("side"))} gains {e.Get("amount")} LP  ({e.Get("lp")})"
                        : $"{P(e.Int("side"))} takes {e.Get("amount")} damage  ({e.Get("lp")})";
                case "CHAIN":
                    return $"Chain of {e.Get("len")} resolves ({P(e.Int("side"))})";
                case "MSG":
                    return DescribeMessage(e, P, localSide);
                case "NET_SEND":
                    return $"net -> {e.Get("type")} {e.Get("payload")}";
                case "NET_RECV":
                    return $"net <- {e.Get("data")}";
                case "ROUND":
                    return $"Round {e.Int("round") + 1} result: {e.Get("outcome")}";
                case "STAT":
                    return $"stat {e.Get("stat")} +{e.Get("delta")}";
                case "DUEL_END":
                    return $"Duel over: {Winner(e.Int("winner"), localSide)}, {WinReason(e.Int("reason"))}  (LP {e.Get("lp")})";
                case "MATCH_END":
                    return $"Match finished (DP reward {e.Get("dp")})";
                default:
                    return e.Raw;
            }
        }

        private static string DescribeMessage(DuelEvent e, Func<int, string> P, int localSide)
        {
            int code = e.Int("code");
            int side = e.Int("side");
            int a1 = e.Int("a1"), a2 = e.Int("a2"), a3 = e.Int("a3");
            return code switch
            {
                0x01 => "Duel start",
                0x02 => $"Turn start: {P(side)}",
                >= 0x0A and <= 0x0F => $"{PhaseName(code - 0x0A)} ({P(side)})",
                0x05 => $"Duel result: {DuelResult(a1, a2, a3, localSide)}",
                0x22 => $"{P(side)} loses: {WinReason(a1)}",
                0x23 => $"{P(side)} gains {a1} LP",
                0x24 => $"{P(side)} takes {a1} damage (cause {a2})",
                0x25 => $"LP set to {a1}",
                0x50 => $"{P(side)} hand shuffled (seed {a2})",
                0x56 => $"{P(side)} deck shuffled (seed {a2})",
                0x2E or 0x2F => $"{P(side)}: {CardNames(a1)} ({e.Get("name")})",
                _ => $"{e.Get("name")} [{P(side)}] ({a1}, {a2}, {a3})",
            };
        }
    }
}
