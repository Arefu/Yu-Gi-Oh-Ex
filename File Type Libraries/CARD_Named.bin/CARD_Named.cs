using System.Collections;

namespace CARD_Named
{
    public static class Card_Named
    {
        public static int NUMBER_OF_ARCHETYPES;
        public static int NUMBER_OF_CARDS_WITH_ARCHETYPES;

        public static Dictionary<int, List<short>> _CardIDsAndArchetype = [];

        public enum Archetype
        {
            CardsLikeToon = 1,
            CardsLikeArchfiend = 2,
            CardsLikeGravekeeper = 3,
            CardsLikeGuardian = 4,
            CardsLikeDarkScorpion = 5,
            CardsLikeAmazoness = 6,
            CardsLikeNinja = 7,
            CardsLikeLV = 8,
            CardsLikeElementalHero = 9,
            CardsLikeDestinyHero =10,
            CardsLikeNeos = 11,
            CardsLikeNeoAndNeoSpacian = 12,
            CardsLikeNeoFusion= 13,
            CardsLikeOjama = 14,
            CardsLikeBatteryman = 15,
            CardsLikeDarkWorld = 16,
            CardsLikeBES = 17,
            CardsLikeAncientGear = 18,
            CardsLikeSphinx = 19,
            CardsLikeMachina = 20,
            CardsLikeHarpie = 21,
            CardsLikeRoid = 22,
            CardsLikeVehicroid = 23,
            CardsLikeNeoSpacian = 24,
            CardsLikeChrysalis = 25,
            CardsLikeAlien = 26,
            CardsLikePhantomBeast = 27,
            CardsLikeHero = 28,
            CardsLikeAllureQueen = 29,
            CardsLikeGadget = 30,
            CardsLikeSixSamurai = 31,
            CardsLikeCrystalBeast = 32,
            CardslikeVolcanic = 33,
            CardsLikeBlazeAccelerator = 34,
            CardsLikeVenom = 35,
            CardsLikeCloudian = 36,
            CardslikeGladiatorBeast = 37,
            CardsLikeGladiatorBeastsBattle = 38,
            CardsLikeBambooSword = 39,
            CardsLikeEvilHero = 40,
            //41
            CardsLikeArcanaForce = 42,
            //43
            CardsLikeSkyblaster = 43,
            CardsLikeExodia = 44,
            CardsLikeUltimateCrystal = 45,
            CardsLikeCyberDragon = 46,
            CardsLikeKuriboh = 287,
        }

        public static string Get_ArchetypeNameFromID(Archetype archetype)
        {
            switch (archetype)
            {
                case Archetype.CardsLikeToon:
                    return "Toon";
                case Archetype.CardsLikeArchfiend:
                    return "Archfiend";
                case Archetype.CardsLikeGravekeeper:
                    return "Gravekeeper";
                case Archetype.CardsLikeGuardian:
                    return "Guardian";
                case Archetype.CardsLikeDarkScorpion:
                    return "Dark Scorpion";
                case Archetype.CardsLikeAmazoness:
                    return "Amazoness";
                case Archetype.CardsLikeNinja:
                    return "Ninja";
                case Archetype.CardsLikeLV:
                    return "LV";
                case Archetype.CardsLikeElementalHero:
                    return "Elemental HERO";
                case Archetype.CardsLikeNeos:
                    return "Neos";
                case Archetype.CardsLikeNeoAndNeoSpacian:
                    return "Neos & Neo-Spacian";
                case Archetype.CardsLikeNeoFusion:
                    return "Neo Fusion";
                case Archetype.CardsLikeOjama:
                    return "Ojama";

                case Archetype.CardsLikeKuriboh:
                    return "Kuriboh";
                default:
                    return archetype.ToString();
            }
        }

        public static Dictionary<Archetype, List<int>> CardsInArchetype = new Dictionary<Archetype, List<int>>();

        public static void Load(string path)
        {
            var Reader = new BinaryReader(File.Open(path, FileMode.Open, FileAccess.Read));

            var Archetypes = Reader.ReadUInt16();
            var NumberOfCards = Reader.ReadUInt16();

            //Advance Reader.
            Reader.BaseStream.Position = 0x6;
            var CountOfCardsInArchetypes = new List<ushort>();
            for (var Archetype = 0; Archetype < Archetypes; Archetype++)
            {
                var NumberOfCardsInArchetype = Reader.ReadUInt16();
                var SumOfCards = Reader.ReadUInt16();

                CountOfCardsInArchetypes.Add(NumberOfCardsInArchetype);

                if (SumOfCards == NumberOfCards)
                    break;
            }

            Reader.ReadUInt16();

            for (ushort Archetype = 0; Archetype < CountOfCardsInArchetypes.Count; Archetype++)
            {
                var TempList = new List<int>();

                for (var Card = 0; Card < CountOfCardsInArchetypes[Archetype]; Card++)
                {
                    TempList.Add(Reader.ReadUInt16());
                }

                CardsInArchetype.Add((Archetype)Archetype, TempList);
            }
        }

        public static void Save()
        {
        }
    }
}