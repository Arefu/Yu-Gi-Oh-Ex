namespace Yami_Yugi.Workers
{
    internal static class Packer
    {
        internal static void Pack(string[] Args)
        {
            if (File.Exists("YGO_2020.dat"))
                File.Delete("YGO_2020.dat");
            if (File.Exists("YGO_2020.toc"))
                File.Delete("YGO_2020.toc");

            // streamed: each file goes through Streaming's 1 MB buffer, never whole into memory; every file is closed after it is copied
            using var Dat = new FileStream("YGO_2020.dat", FileMode.CreateNew, FileAccess.Write, FileShare.None, Streaming.BufferSize);
            using var Toc = new StreamWriter("YGO_2020.toc") { NewLine = "\n" };
            Toc.WriteLine("UT");
            byte[] Padding = new byte[3];

            foreach (var Item in Directory.EnumerateFiles(Args[0], "*.*", SearchOption.AllDirectories))
            {
                Console.Out.WriteLine($"Packing File: {Path.GetFileName(Item)}.");
                string Relative = Item.Split(["YGO_2020\\"], StringSplitOptions.None).Last();
                long Size = new FileInfo(Item).Length;

                using (var Input = new FileStream(Item, FileMode.Open, FileAccess.Read, FileShare.Read, Streaming.BufferSize, FileOptions.SequentialScan))
                    Streaming.CopyExactly(Input, Dat, Size);
                Dat.Write(Padding, 0, (int)(Streaming.Padded(Size) - Size));   // 4-byte alignment

                string NameLength = Item.Split(["YGO_2020"], StringSplitOptions.None).Last().TrimStart('\\').Length.ToString("x").PadLeft(2);
                Toc.WriteLine($"{Size.ToString("x").PadLeft(12)} {NameLength} {Relative}");
            }
        }
    }
}
