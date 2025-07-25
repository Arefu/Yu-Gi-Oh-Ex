namespace Types
{
    public static class CRED
    {
        public static List<string> Load(string Path)
        {
            var Credits = new List<String>();
            using StreamReader Reader = new StreamReader(Path, System.Text.Encoding.Unicode);
            while (!Reader.EndOfStream)
            {
                Credits.Add(Reader.ReadLine());
            }
            return Credits;
        }

        public static void Save(List<string> Credits)
        {
            using StreamWriter Writer = new StreamWriter(File.Open("credits.dat", FileMode.Create), System.Text.Encoding.Unicode);
            foreach (var Credit in Credits)
            {
                Writer.WriteLine(Credit);
            }
        }
    }
}