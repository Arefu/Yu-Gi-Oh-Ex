namespace Types
{
    public static class CRED
    {
        public static List<string> Load(string Path)
        {
            using StreamReader reader = new StreamReader(Path);
            while(!reader.EndOfStream)
            {
                var Line = reader.ReadLine();
                if (Line.Contains("CRED"))
                {
                    var Split = Line.Split(' ');
                    return new List<string> { Split[1], Split[2] };
                }
            }
            return new List<string>(); 
        }

        public static void Save()
        {

        }
    }
}
