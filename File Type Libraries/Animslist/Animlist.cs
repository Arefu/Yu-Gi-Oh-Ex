using System.Drawing;

namespace Types
{
    public class Animlist_Item(string Item, Point Position, int Slide)
    {
        public string ItemName { get; set; } = Item;
        public Point ItemPosition { get; set; } = Position;
        public int ItemSlide { get; set; } = Slide;
    }

    public static class Animlist
    {
        public enum Side
        {
            Left = 0,
            Right = 1
        }

        public static Side TitleScreenSide;

        public static List<Animlist_Item> Scene = new();

        public static void Load(String Path)
        {
            DirectoryInfo? CurrentDirectory = new FileInfo(Path).Directory;
            if (CurrentDirectory == null) return;

            var Reader = new StreamReader(Path);
            do
            {
                string? Line = Reader.ReadLine();
                if (Line == null) return;

                var Split = Line.Split(",");
                if (Split.Length == 0) return;

                if (string.Equals(Split[0], "side", StringComparison.OrdinalIgnoreCase))
                {
                    if (Split.Length != 2) return;
                    if (!Enum.TryParse(Split[1], out TitleScreenSide)) return;
                }
                else
                {
                    if (Split.Length != 4) return;
                    var matchedFile = Directory.EnumerateFiles(CurrentDirectory.FullName).FirstOrDefault(f => System.IO.Path.GetFileNameWithoutExtension(f).Equals(Split[0], StringComparison.OrdinalIgnoreCase));

                    if (matchedFile == null)
                        return;

                    Scene.Add(new Animlist_Item($"{CurrentDirectory.FullName}\\{System.IO.Path.GetFileName(matchedFile)}", new Point(Convert.ToInt32(Split[1]), Convert.ToInt32(Split[2])), Convert.ToInt32(Split[3])));
                }
            } while (!Reader.EndOfStream);
        }

        public static void Save()
        {
        }
    }
}
