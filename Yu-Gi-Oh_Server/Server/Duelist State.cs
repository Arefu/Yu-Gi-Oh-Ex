using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Yu_Gi_Oh_Server.Server
{
    internal class Duelist_State
    {
        internal int NumberOfCardsInHand { get; }
        internal int NumberOfCardsInDeck { get; }
        internal int NumberOfCardsInGraveyard { get; }
        internal int NumberOfCardsInExtraDeck { get; }
        internal int NumberOfCardsInDiscardPile { get; }

        internal Tuple<int, int> FieldSpell;

        internal Dictionary<int, int> CardsInHand;
        internal Dictionary<int, int> CardsInDeck;
        internal Dictionary<int, int> CardsInGraveyard;
        internal Dictionary<int, int> CardsInExtraDeck;
        internal Dictionary<int, int> CardsInDiscardPile;

        internal List<Tuple<int, int, int, int>> CardsInMonsterZone;
        internal List<Tuple<int, int, int, int>> CardsInSpellTrapZone;
    }
}