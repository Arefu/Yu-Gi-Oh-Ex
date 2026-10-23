using System.Globalization;
using System.Text.RegularExpressions;

namespace Yami_Yugi.Workers
{
    internal static class Unpacker
    {
        private static readonly Regex Spaces = new(@"  +", RegexOptions.Compiled);

        internal static void Unpack(string[] Args)
        {
            var File = new FileInfo(Args[0]);
            var Archive = new FileInfo(Path.ChangeExtension(File.FullName, ".dat"));
            var Folder = Path.GetFileNameWithoutExtension(File.FullName);
            Console.Out.WriteLine($"Unpacking {Archive.Name} with {File.Name}");
            Directory.CreateDirectory(Folder);

            // streamed: each file goes through Streaming's 1 MB buffer, never whole into memory
            using var Dat = new FileStream(Archive.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, Streaming.BufferSize, FileOptions.SequentialScan);
            foreach (string Line in System.IO.File.ReadLines(File.FullName))
            {
                if (Line == "UT" || Line.Trim().Length == 0)
                    continue;

                var Information = Spaces.Replace(Line.TrimStart(' '), " ").Split(' ', 3);
                long Size = long.Parse(Information[0], NumberStyles.HexNumber);
                string Name = Information[2];

                string Target = Path.Combine(Folder, Name);
                Directory.CreateDirectory(Path.GetDirectoryName(Target)!);
                Console.Out.WriteLine($"Unpacking: {Name}, It's {Size} Bytes Big.");

                using (var Output = new FileStream(Target, FileMode.Create, FileAccess.Write, FileShare.None, Streaming.BufferSize))
                    Streaming.CopyExactly(Dat, Output, Size);

                Dat.Position += Streaming.Padded(Size) - Size;   // the archive's 4-byte padding
            }
        }
    }
}
