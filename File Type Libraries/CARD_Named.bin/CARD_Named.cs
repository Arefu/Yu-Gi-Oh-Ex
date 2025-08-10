namespace CARD_Named
{
    public static class Card_Named
    {
        public static int NUMBER_OF_ARCHETYPES;
        public static int NUMBER_OF_CARDS_WITH_ARCHETYPES;

        public static Dictionary<int, List<short>> _CardIDsAndArchetype = [];

        public enum Archetype
        {
            None = 0,
            Toon = 1,
            Archfiend = 2,
            Gravekeeper = 3,
            Guardian = 4,
            DarkScorpion = 5,
            Amazoness = 6,
            Ninja = 7,
            LV = 8,
            ElementalHero = 9,
            DestinyHero = 10,
            Neos = 11,
            NeoSpacianWithSubArchetype = 12,
            ElementalHeroNeo = 13,
            Ojama = 14,
            Batteryman = 15,
            DarkWorld = 16,
            BES = 17,
            AncientGear = 18,
            Sphinx = 19,
            Machina = 20,
            Harpie = 21,
            Roid = 22,
            Vehicroid = 23,
            NeoSpacian = 24,
            Chrysalis = 25,
            Alien = 26,
            PhantomBeast = 27,
            Hero = 28,
            AllureQueen = 29,
            Gadget = 30,
            SixSamurai = 31,
            CrystalBeast = 32,
            Volcanic = 33,
            BlazeAccelerator = 34,
            Venom = 35,
            Cloudian = 36,
            GladiatorBeast = 37,
            GladiatorBeastsBattle = 38,
            BambooSword = 39,
            EvilHero = 40,
            //41
            ArcanaForce = 42,
            //43
            Skyblaster = 44,
            Exodia = 45,
            UltimateCrystal = 46,
            CyberDragonFusionRequirement = 47,
            IceBarrier = 48,
            AllyOfJustice = 49,
            Saber = 50,
            Worm = 51,
            Lightsworn = 52,
            Frog = 53,
            NitroWarrior = 54,
            Genex = 55,
            MistValley = 56,
            Flamvell = 57,
            AllHero = 58,
            Morphtronic = 59,
            IronChain = 61,
            Naturia = 62,
            Clear = 63,
            RedEyes = 64,
            Blackwing =  65,
            SlashAssaultMode = 66,
            Fabled = 67,
            Jurrac = 68,






            Kuriboh = 287,


























            //390
            Mathmech = 391,
            Dragonmaid = 392,
            Generaider = 393,
            Ignister =  394,
            Ai = 395,
            AncientWarriors  =  396,
            Megalith = 397,
            Palladium = 398,
            Onomat = 399,
            UtopicFuture = 400,
            Rose = 401,
            Rebellion = 402,
            Barbaros = 405,
            Phantasm = 412,
            SacredBeast = 413,
            SpiralSpearStrike = 414,
            Potan = 417
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
            using var Writer = new BinaryWriter(File.Open("Card_named.bin", FileMode.Create, FileAccess.Write));
            ushort archetypeCount = (ushort)CardsInArchetype.Count;
            ushort totalCards = (ushort)CardsInArchetype.Sum(kv => kv.Value.Count);

            Writer.Write((ushort)(archetypeCount + 1));
            Writer.Write(totalCards);

            Writer.BaseStream.Position = 0x6;

            ushort runningSum = 0;
            foreach (var kv in CardsInArchetype)
            {
                ushort cardsInArchetype = (ushort)kv.Value.Count;
                runningSum += cardsInArchetype;

                Writer.Write(cardsInArchetype);
                Writer.Write(runningSum);

                if (runningSum == totalCards)
                    break;
            }

            Writer.Write((ushort)0);

            foreach (var kv in CardsInArchetype)
            {
                foreach (var cardId in kv.Value)
                    Writer.Write((ushort)cardId);
            }
        }
    }
}