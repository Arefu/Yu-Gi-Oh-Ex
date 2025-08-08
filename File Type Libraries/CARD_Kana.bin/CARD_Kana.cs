namespace CARD_Kana
{
    public static class Card_Kana
    {
        public static List<string> _Kana = [];

        public static void Load(string Path, string Language)
        {
            bool useJp = false;
            if (Language[0] == 'J')
                useJp = true;

            var Kana = new List<string>();

            if (File.Exists(Path.Replace("CARD_Kana1", "CARD_Kana2")) == false && File.Exists(Path.Replace("CARD_Kana1", "CARD_Kana3")) == false)
            {
                return;
            }

            using var Kana1Reader = new BinaryReader(File.Open(Path, FileMode.Open, FileAccess.Read));
            using var Kana2Reader = new BinaryReader(File.Open(Path.Replace($"CARD_Kana1_", $"CARD_Kana2_"), FileMode.Open, FileAccess.Read));
            using var Kana3Reader = new BinaryReader(File.Open(Path.Replace($"CARD_Kana1_", $"CARD_Kana3_"), FileMode.Open, FileAccess.Read));

            while (Kana1Reader.BaseStream.Position != Kana1Reader.BaseStream.Length)
            {
                if (Kana2Reader.BaseStream.Position == Kana2Reader.BaseStream.Length)
                    break;
                if (Kana3Reader.BaseStream.Position == Kana3Reader.BaseStream.Length)
                    break;

                char[] rawKana1 = Kana1Reader.ReadChars(2);
                char[] rawKana2 = Kana2Reader.ReadChars(2);
                char[] rawKana3 = Kana3Reader.ReadChars(2);

                string Kana1 = new string(rawKana1).TrimEnd('\0');
                string Kana2 = new string(rawKana2).TrimEnd('\0');
                string Kana3 = new string(rawKana3).TrimEnd('\0');
                Kana.Add($"{Kana1}{Kana2}{Kana3}");
            }

            _Kana = Kana;
        }

        public static void Save(char Language)
        {
            using var Kana1Writer = new BinaryWriter(File.Open($"CARD_Kana1_{Language}.bin", FileMode.Create, FileAccess.Write));
            using var Kana2Writer = new BinaryWriter(File.Open($"CARD_Kana2_{Language}.bin", FileMode.Create, FileAccess.Write));
            using var Kana3Writer = new BinaryWriter(File.Open($"CARD_Kana3_{Language}.bin", FileMode.Create, FileAccess.Write));

            foreach (var combined in _Kana)
            {
                string kana1 = combined.Length >= 2 ? combined.Substring(0, 2) : combined.PadRight(2, '\0');
                string kana2 = combined.Length >= 4 ? combined.Substring(2, 2) : combined.Length > 2 ? combined.Substring(2).PadRight(2, '\0') : "\0\0";
                string kana3 = combined.Length >= 6 ? combined.Substring(4, 2) : combined.Length > 4 ? combined.Substring(4).PadRight(2, '\0') : "\0\0";

                Kana1Writer.Write(kana1.ToCharArray());
                Kana2Writer.Write(kana2.ToCharArray());
                Kana3Writer.Write(kana3.ToCharArray());
            }
        }
    }
}