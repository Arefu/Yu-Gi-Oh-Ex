namespace CARD_Pass
{
    public static class CARD_Pass
    {
        public static List<int> _Passwords = [];

        public static List<int> Load(string Path)
        {
            if (Path == "-1")
                return _Passwords;

            var Passwords = new List<int>();

            using var Reader = new BinaryReader(File.Open(Path, FileMode.Open, FileAccess.Read));

            while(Reader.BaseStream.Position != Reader.BaseStream.Length)
            {
                Passwords.Add(Reader.ReadInt32());
            }

            _Passwords = Passwords;
            return Passwords;
        }

        public static void Save()
        {
            using var Writer = new BinaryWriter(File.Create("CARD_Pass.bin"));

            foreach(var Password in _Passwords)
            {
                Writer.Write(Password);
            }
        }
    }
}
