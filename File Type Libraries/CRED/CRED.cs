using System.Text;

namespace Types
{
    /// <summary>main\ui\credits\credits.dat: the credits, UTF-16LE text (with its byte order mark), one line per credit line.</summary>
    public static class CRED
    {
        public const string GamePath = @"main\ui\credits\credits.dat";

        public static List<string> Load(string path) => Parse(File.ReadAllBytes(path));

        public static List<string> Parse(byte[] data)
        {
            var credits = new List<string>();
            using var reader = new StreamReader(new MemoryStream(data), Encoding.Unicode);
            while (reader.ReadLine() is string line)
                credits.Add(line);
            return credits;
        }

        public static byte[] ToBytes(List<string> credits)
        {
            using var stream = new MemoryStream();
            using (var writer = new StreamWriter(stream, Encoding.Unicode, leaveOpen: true))
            {
                foreach (string credit in credits)
                    writer.WriteLine(credit);
            }
            return stream.ToArray();
        }

        public static void Save(string path, List<string> credits) => File.WriteAllBytes(path, ToBytes(credits));
    }
}
