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
                while (!Reader.EndOfStream)
                {
                    var Line = Reader.ReadLine();
                    if(String.IsNullOrEmpty(Line))
                        continue;

                    if (Cropped == true)
                    {

                    }
                    else
                    {
                        var ItemProperties = Line.Split(',');
                        var Item = new Animlist_Item();
                        Item.ItemName = ItemProperties[0];
                        Item.ItemPosition = new Point(int.Parse(ItemProperties[1]), int.Parse(ItemProperties[2])); //TOP, LEFT
                        if (ItemProperties.Length == 4)
                            Item.ItemSlide = int.Parse(ItemProperties[3]);
                        else
                            Item.ItemSlide = -1; //NO SLIDE INFORMATION
                        Items.Add(Item);
                    }
                }
            }
            return Items;
        }

        public static void Save()
        {

        }
    }
}
