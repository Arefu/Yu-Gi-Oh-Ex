using System.Diagnostics;
using System.Net.WebSockets;

namespace CARD_Named
{
    public static class CARD_Named
    {
        public static int NUMBER_OF_ARCHETYPES;
        public static int NUMBER_OF_CARDS_WITH_ARCHETYPES;

        public static Dictionary<int, List<short>> _CardIDsAndArchetype = [];

        public enum Archetype
        {
            Toon = 0x0,
            Archfiend = 0x1
        }

        public static Dictionary<int, List<short>> Load(string path)
        {
            return [];
        }

        public static void Save()
        {

        }
    }
}
