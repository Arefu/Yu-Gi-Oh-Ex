using System.Drawing;

namespace Types
{
    public class Dfymoo_Item
    {

        public string ItemName { get; set; }

        public Point ItemStartPoint { get; set; }
        public Point ItemDimensions { get; set; }

        public Point ItemOrigin { get; set; }
        public Point ItemOriginDimensions { get; set; }
    }

    public static class Dfymoo
    {
        public static List<Dfymoo_Item> Load(string Path)
        {
            if (File.Exists(Path) == false)
            {
                throw new FileNotFoundException("File not found", Path);
            }

            var Items = new List<Dfymoo_Item>();

            using (var Reader = new StreamReader(Path))
            {
                var Sections = Reader.ReadToEnd();
                var Lines = Sections.Split("~", StringSplitOptions.RemoveEmptyEntries);
                var Item = new Dfymoo_Item();
                var _Line = String.Empty;

                foreach (var Line in Lines)
                {
                    foreach (var Category in Line.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (Category.StartsWith("n")) //DFYMOO Item Name
                        {
                            var ItemName = Category.Split(' ', 2)[1];
                            Item.ItemName = ItemName;
                        }
                        else if (Category.StartsWith("s")) //DFYMOO Item Size
                        {
                            Item.ItemStartPoint = new Point(int.Parse(Category.Split(' ')[1]), int.Parse(Category.Split(' ')[2]));
                            Item.ItemDimensions = new Point(int.Parse(Category.Split(' ')[3]), int.Parse(Category.Split(' ')[4]));
                        }
                        else if (Category.StartsWith("o")) //?
                        {
                            Item.ItemOrigin = new Point(int.Parse(Category.Split(' ')[1]), int.Parse(Category.Split(' ')[2]));
                            Item.ItemOriginDimensions = new Point(int.Parse(Category.Split(' ')[3]), int.Parse(Category.Split(' ')[4]));
                        }
                    }
                    if (Item.ItemName != null)
                    {
                        Items.Add(Item);
                        Item = new Dfymoo_Item();
                    }
                }
            }

            return Items;
        }

        public static void Save(string Path, List<Dfymoo_Item> Items, Size ImageSize)
        {
            using (var Writer = new StreamWriter(Path))
            {
                Writer.WriteLine("i tk2d 1");
                Writer.WriteLine($"w {ImageSize.Width}");
                Writer.WriteLine($"h {ImageSize.Height}");
                Writer.WriteLine("~");

                foreach (var Item in Items)
                {
                    if (Item.ItemName != "")
                        Writer.WriteLine($"n {Item.ItemName}");
                    if (Item.ItemStartPoint != Point.Empty && Item.ItemDimensions != Point.Empty)
                        Writer.WriteLine($"s {Item.ItemStartPoint.X} {Item.ItemStartPoint.Y} {Item.ItemDimensions.X} {Item.ItemDimensions.Y}");
                    if (Item.ItemOrigin != Point.Empty && Item.ItemOriginDimensions != Point.Empty)
                        Writer.WriteLine($"o {Item.ItemOrigin.X} {Item.ItemOrigin.Y} {Item.ItemOriginDimensions.X} {Item.ItemOriginDimensions.Y}");
                    Writer.WriteLine("~");
                }
            }
        }

    }
}
