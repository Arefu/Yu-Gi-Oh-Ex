using System.Drawing;

namespace Types
{
    public class Animlist_Item
    {
        public string ItemName { get; set; }
        public Point ItemPosition { get; set; }
        public int ItemSlide { get; set; }
    }
    public static class Animlist
    {
         
        public static List<Animlist_Item> Load(String Path, bool Cropped = true)
        {
            //StreamReader
            if(File.Exists(Path) == false)
            {
                throw new FileNotFoundException("File not found", Path);
            }

            var Items = new List<Animlist_Item>();

            using (var Reader =  new StreamReader(Path)) {
                var Line = Reader.ReadLine();

                if (Cropped == true)
                {
                }
                else
                {
                    var ItemProperties = Line.Split(',');
                    var Item = new Animlist_Item();
                    Item.ItemName = ItemProperties[0];
                    Item.ItemPosition = new Point(int.Parse(ItemProperties[1]), int.Parse(ItemProperties[2]));
                    Item.ItemSlide = int.Parse(ItemProperties[3]);
                    Items.Add(Item);
                }
            }
            return Items;
        }

        public static void Save()
        {

        }
    }
}
