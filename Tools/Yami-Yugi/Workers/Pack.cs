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

            File.AppendAllText("YGO_2020.toc", "UT\n");

            using var Writer = new BinaryWriter(File.Open("YGO_2020.dat", FileMode.Append, FileAccess.Write));
            foreach (var Item in Directory.GetFiles($"{Args[0]}", "*.*", SearchOption.AllDirectories))
            {
                var CurrentFileName = Item;

                Console.Out.WriteLine($"Packing File: {Path.GetFileName(CurrentFileName)}.");
                var CurrentFileNameLength = CurrentFileName.Split(["YGO_2020"], StringSplitOptions.None).Last().TrimStart('\\').Length.ToString("x");
                var CurrentFileSize = new FileInfo($"{CurrentFileName}").Length.ToString("x");

                while (CurrentFileSize.Length != 12)
                    CurrentFileSize = CurrentFileSize.Insert(0, " ");
                while (CurrentFileNameLength.Length != 2)
                    CurrentFileNameLength = CurrentFileNameLength.Insert(0, " ");

                var BReader = new BinaryReader(File.Open(CurrentFileName, FileMode.Open, FileAccess.Read));
                var NewSize = new FileInfo(CurrentFileName).Length;
                while (NewSize % 4 != 0)
                    NewSize++;

                var BufferSize = NewSize - new FileInfo(CurrentFileName).Length;
                Writer.Write(BReader.ReadBytes((int)new FileInfo(CurrentFileName).Length));

                if (BufferSize > 0)
                {
                    while (BufferSize != 0)
                    {
                        Writer.Write([00]);
                        BufferSize--;
                    }
                }

                File.AppendAllText("YGO_2020.toc", $"{CurrentFileSize} {CurrentFileNameLength} {CurrentFileName.Split(["YGO_2020\\"], StringSplitOptions.None).Last()}\n");
            }
        }
    }
}