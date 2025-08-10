namespace Types
{
    public static class CRED
    {
        public static List<string> Load(string Path)
        {
            var Credits = new List<string>();
            using StreamReader Reader = new(Path, System.Text.Encoding.Unicode);
            while (!Reader.EndOfStream)
            {
                var Line = Reader.ReadLine();
                if (Line == null) continue;

                Credits.Add(Line);
            }
            return Credits;
        }

        public static void Save(List<string> Credits)
        {
            using StreamWriter Writer = new(File.Open("credits.dat", FileMode.Create), System.Text.Encoding.Unicode);
            foreach (var Credit in Credits)
            {
                Writer.WriteLine(Credit);
            }
        }
    }
}