namespace CARD_Pass
{
    public static class CARD_Pass
    {
        public static List<int> _Passwords = [];

        public static List<int> Load(string Path)
        {
            var Passwords = new List<int>();

            using var Reader = new BinaryReader(File.Open(Path, FileMode.Open, FileAccess.Read));

            while(Reader.BaseStream.Position != Reader.BaseStream.Length)
            {
                Passwords.Add(Reader.ReadInt32());
            }

            _Passwords = Passwords;
            return Passwords;
        }

        public static void Save(List<int> Passwords)
        {

        }
    }
}
