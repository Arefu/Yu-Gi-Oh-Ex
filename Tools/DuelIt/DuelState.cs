namespace DuelIt
{
    /// <summary>
    /// Duel zones as the engine numbers them (docs/EffectSystem.md section 28, docs/MultiplayerSystem.md). Field zones 0-12 are
    /// provisional: 0-4 monster, 5-6 extra monster, 7-11 spell/trap, 12 field spell.
    /// </summary>
    public static class Zones
    {
        public const int FieldCount = 13;
        public const int Hand = 13, Extra = 14, Deck = 15, Grave = 16, Banished = 17;

        public static string Name(int zone) => zone switch
        {
            >= 0 and <= 4 => $"Monster {zone + 1}",
            5 or 6 => $"Extra Monster {zone - 4}",
            >= 7 and <= 11 => $"Spell/Trap {zone - 6}",
            12 => "Field",
            Hand => "Hand",
            Extra => "Extra Deck",
            Deck => "Deck",
            Grave => "Graveyard",
            Banished => "Banished",
            _ => $"zone {zone}",
        };

        /// <summary>An engine card location: zone&lt;&lt;1 | side | index&lt;&lt;6 (seen in YGO__UI__OnCardMove).</summary>
        public static (int Zone, int Side, int Index) Decode(int location) => ((location & 0x3F) >> 1, location & 1, location >> 6);
    }

    public sealed class SideState
    {
        public int LifePoints = 8000;
        public List<int> Deck = [], Hand = [], Extra = [], Grave = [], Banished = [];
        public int?[] Field = new int?[Zones.FieldCount];

        public List<int>? Pile(int zone) => zone switch
        {
            Zones.Hand => Hand,
            Zones.Extra => Extra,
            Zones.Deck => Deck,
            Zones.Grave => Grave,
            Zones.Banished => Banished,
            _ => null,
        };

        public SideState Clone() => new()
        {
            LifePoints = LifePoints,
            Deck = [.. Deck], Hand = [.. Hand], Extra = [.. Extra], Grave = [.. Grave], Banished = [.. Banished],
            Field = (int?[])Field.Clone(),
        };
    }

    /// <summary>
    /// The duel as of one event. Built by applying events in order; it's a presentation of what the log says, not a rules
    /// simulation (the game's rules engine did that already - its results are what got recorded).
    /// </summary>
    public sealed class DuelState
    {
        public SideState[] Sides = [new(), new()];
        public Dictionary<int, int> CardIdBySlot = [];
        public int LocalSide;
        public int Turn;
        public int TurnSide = -1;
        public string Phase = "-";
        public string? Result;
        public int? LastMovedSlot;
        public int Mismatches;   // a MOVE whose "from" didn't match where this replay had the card: a gap in what's recorded
        private bool _turnStarted;

        public int CardId(int slot) => CardIdBySlot.TryGetValue(slot, out int id) ? id : 0;

        public DuelState Clone()
        {
            return new DuelState
            {
                Sides = [Sides[0].Clone(), Sides[1].Clone()],
                CardIdBySlot = CardIdBySlot,   // fixed after the CARDS lines, shared
                LocalSide = LocalSide, Turn = Turn, TurnSide = TurnSide, Phase = Phase, Result = Result, LastMovedSlot = LastMovedSlot, Mismatches = Mismatches, _turnStarted = _turnStarted,
            };
        }

        public void Apply(DuelEvent e)
        {
            LastMovedSlot = null;
            switch (e.Tag)
            {
                case "CARDS":
                    ApplyCards(e);
                    break;
                case "MSG":
                    ApplyMessage(e);
                    break;
                // LP lines (LP_ApplyDamage/Gain taps) are informational: the change itself arrives as MSG 0x23/0x24.
                case "MOVE":
                    ApplyMove(e);
                    break;
                case "DUEL_BEGIN":
                    LocalSide = e.Int("localSide") & 1;
                    break;
                case "DUEL_END":
                    SetLifePoints(e);
                    Result = DuelText.Winner(e.Int("winner"), LocalSide) + ", " + DuelText.WinReason(e.Int("reason"));
                    break;
            }
        }

        private void ApplyCards(DuelEvent e)
        {
            int side = e.Int("side") & 1;
            var pile = e.Get("zone") switch
            {
                "deck" => Sides[side].Deck,
                "extra" => Sides[side].Extra,
                "hand" => Sides[side].Hand,
                _ => null,
            };
            if (pile == null)
                return;
            pile.Clear();
            foreach (string pair in e.Get("cards").Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = pair.Split(':');
                if (parts.Length == 2 && int.TryParse(parts[0], out int slot) && int.TryParse(parts[1], out int id) && id != 0)   // empty hand slots at duel start read 0:0
                {
                    CardIdBySlot[slot] = id;
                    pile.Add(slot);
                }
            }
        }

        private void ApplyMessage(DuelEvent e)
        {
            int code = e.Int("code");
            int side = e.Int("side") & 1;

            // lp= is read as the message starts: everything executed before it, whatever its format, is already in it, so it
            // corrects LP changes this replay doesn't decode. Recordings from before the 16-bit key fix have garbage there
            // (e.g. 364650304), which the range check skips.
            if (e.LifePoints() is (int lp0, int lp1) && lp0 is >= 0 and <= 99_999_999 && lp1 is >= 0 and <= 99_999_999)
            {
                Sides[0].LifePoints = lp0;
                Sides[1].LifePoints = lp1;
            }

            switch (code)
            {
                // Engine LP messages, applied in execution order, so LP shows the change on the message itself rather than
                // on the next one.
                case 0x23:   // LP_Gain: a1 = amount
                    Sides[side].LifePoints += e.Int("a1");
                    break;
                case 0x24:   // LP_Damage: a1 = amount
                    Sides[side].LifePoints = Math.Max(0, Sides[side].LifePoints - e.Int("a1"));
                    break;
                case 0x25:   // LP_Set (Duel__Msg__Handle_25_LP_Set): a3 != 0 = both sides, turn side's value first; else side = a1 | a2 << 16
                    if (e.Int("a3") != 0)
                    {
                        int first = TurnSide < 0 ? 0 : TurnSide;
                        Sides[first].LifePoints = e.Int("a1");
                        Sides[1 - first].LifePoints = e.Int("a2");
                    }
                    else
                        Sides[side].LifePoints = (e.Int("a1") & 0xFFFF) | ((e.Int("a2") & 0xFFFF) << 16);
                    break;
                // DuelResult (winner, per-side win reasons): the result when the duel-end tap never fired (the game was left
                // from the result, or closed). A DUEL_END line, when there is one, overwrites it.
                case 0x05:
                    Result ??= DuelText.DuelResult(e.Int("a1"), e.Int("a2"), e.Int("a3"), LocalSide);
                    break;
                // TurnStart (0x02) isn't always recorded (the Push-based recordings never had it): a turn is also EndOfTurn (0x03) ->
                // NextTurn (0x04) -> Phase_Draw. So a Draw Phase starts a turn unless a TurnStart just did, and phase messages
                // carry the turn player.
                case 0x02:
                    Turn++;
                    TurnSide = side;
                    Phase = "-";
                    _turnStarted = true;
                    break;
                case >= 0x0A and <= 0x0F:
                    if (code == 0x0A && !_turnStarted)
                        Turn++;
                    _turnStarted = false;
                    TurnSide = side;
                    Phase = DuelText.PhaseName(code - 0x0A);
                    break;
            }
        }

        private void SetLifePoints(DuelEvent e)
        {
            if (e.LifePoints() is (int a, int b))
            {
                Sides[0].LifePoints = a;
                Sides[1].LifePoints = b;
            }
        }

        private void ApplyMove(DuelEvent e)
        {
            int slot = e.Int("slot");
            var (fromZone, fromSide, _) = Zones.Decode(e.Int("from"));
            var (toZone, toSide, toIndex) = Zones.Decode(e.Int("to"));

            var (atZone, atSide) = Remove(slot);
            if (atZone != fromZone || atSide != fromSide)
                Mismatches++;

            var side = Sides[toSide & 1];
            if (toZone < Zones.FieldCount)
                side.Field[toZone] = slot;
            else if (side.Pile(toZone) is { } pile)
            {
                if (toZone == Zones.Hand)
                    pile.Insert(Math.Clamp(toIndex, 0, pile.Count), slot);
                else if (toZone == Zones.Deck)
                    pile.Insert(0, slot);   // back on top (the deck index isn't reliable enough to trust yet)
                else
                    pile.Add(slot);
            }
            LastMovedSlot = slot;
        }

        /// <summary>Takes a card out of wherever it is; returns that zone and side (-1 if it wasn't anywhere).</summary>
        private (int Zone, int Side) Remove(int slot)
        {
            for (int s = 0; s < 2; s++)
            {
                var side = Sides[s];
                for (int z = 0; z < Zones.FieldCount; z++)
                    if (side.Field[z] == slot)
                    {
                        side.Field[z] = null;
                        return (z, s);
                    }
                foreach (int zone in new[] { Zones.Hand, Zones.Extra, Zones.Deck, Zones.Grave, Zones.Banished })
                    if (side.Pile(zone)!.Remove(slot))
                        return (zone, s);
            }
            return (-1, -1);
        }
    }
}
