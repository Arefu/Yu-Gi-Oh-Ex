using System.Drawing;
using System.Security.AccessControl;

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
                        if (Category.StartsWith("n"))
                            Item.ItemName = Category.Split(' ')[1];
                        else if (Category.StartsWith("s"))
                        {
                            Item.ItemStartPoint = new Point(int.Parse(Category.Split(' ')[1]), int.Parse(Category.Split(' ')[2]));
                            Item.ItemDimensions = new Point(int.Parse(Category.Split(' ')[3]), int.Parse(Category.Split(' ')[4]));
                        }
                        else if (Category.StartsWith("o"))
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

        static int Get_NumberOfItemsInDfymoo(string Path)
        {
            return Load(Path).Count;
        }
    }
}
