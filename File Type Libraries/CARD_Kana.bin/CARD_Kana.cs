namespace CARD_Kana
{
    public static class CARD_Kana
    {
        public static List<string> _Kana = [];

        public static void Load(string Path, char Language)
        {
            var Kana = new List<string>();

            if (File.Exists(Path.Replace("CARD_Kana1", "CARD_Kana2")) == false && File.Exists(Path.Replace("CARD_Kana1", "CARD_Kana3")) == false)
            {
                return;
            }


            using var Kana1Reader = new BinaryReader(File.Open(Path, FileMode.Open, FileAccess.Read));
            using var Kana2Reader = new BinaryReader(File.Open(Path.Replace($"CARD_Kana1_{Language}", $"CARD_Kana2_{Language}"), FileMode.Open, FileAccess.Read));
            using var Kana3Reader = new BinaryReader(File.Open(Path.Replace($"CARD_Kana1_{Language}", $"CARD_Kana3_{Language}"), FileMode.Open, FileAccess.Read));

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

        public static void Save()
        {

        }
    }
}
