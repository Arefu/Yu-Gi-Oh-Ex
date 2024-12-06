namespace Types
{
    public static class CRED
    {
        public static List<string> Load(string Path)
        {
            using StringReader reader = new StringReader(File.Open(Path, FileMode.Open).ToString());
            //Read file in BigEndianUnicode encoding
            string line = reader.ReadLine();
            while (line != null)
            {
                //Do something with the line
                line = reader.ReadLine();
            }

            return new List<string>(); 
        }

        public static void Save()
        {

        }
    }
}
