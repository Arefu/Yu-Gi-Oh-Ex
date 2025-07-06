using System.Diagnostics;

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

        public static Dictionary<int, List<short>> Load(string Path)
        {
            var Archetype = new Dictionary<int, List<short>>();
            using var Reader = new BinaryReader(File.Open(Path, FileMode.Open, FileAccess.Read));

            var NUMBER_OF_ARCHETYPES = Reader.ReadUInt16();
            var NUMBER_OF_CARDS_WITH_ARCHETYPES = (int)Reader.ReadUInt16();

            

            return Archetype;
        }

        public static void Save()
        {

        }
    }
}
