namespace Types
{
    public static class CRED
    {
        public static List<string> Load(string Path)
        {
            var Credits = new List<String>();
            using StreamReader reader = new StreamReader(Path, System.Text.Encoding.Unicode);
            while(!reader.EndOfStream)
            {
                Credits.Add(reader.ReadLine());
                
            }
            return Credits;
        }

        public static void Save(List<string> Credits)
        {

        }
    }
}
