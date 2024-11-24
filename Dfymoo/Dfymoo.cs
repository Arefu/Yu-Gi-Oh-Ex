using System.Drawing;
using System.Security.AccessControl;

namespace Types
{
    public class Dfymoo_Item
    {

        public string ItemName { get; set; }

        public Point ItemPointFrom { get; set; }
        public Point ItemPointTo { get; set; }

        public Point ItemPositionOnScreenFrom { get; set; }
        public Point ItemPositionOnScreenTo { get; set; }
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
                while (Reader.EndOfStream == false)
                {
                    var Item = new Dfymoo_Item();
                    var Line = Reader.ReadLine();
                    if (Line.StartsWith("n"))
                    {
                        Item.ItemName = Line.Split(' ')[1];
                    }


                    Items.Add(Item);
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
