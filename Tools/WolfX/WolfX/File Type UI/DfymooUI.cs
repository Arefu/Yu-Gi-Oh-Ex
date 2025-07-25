namespace WolfX.Types
{
    public partial class DfymooUI : Form
    {
        public DfymooUI(String PicturePath)
        {
            InitializeComponent();
            _PicturePath = PicturePath;

            DFY_Picture.Image = Image.FromFile(PicturePath.Replace(".dfymoo", ".png"));
        }

        private void DFY_Picture_Paint(object sender, PaintEventArgs e)
        {
            if (DFY_Item == null)
                return;

            using (var Pen = new Pen(Color.HotPink, 6))
            {
                _Rect = new Rectangle(DFY_Item.ItemStartPoint.X, DFY_Item.ItemStartPoint.Y, DFY_Item.ItemDimensions.X, DFY_Item.ItemDimensions.Y);
                e.Graphics.DrawRectangle(Pen, _Rect);
            }
        }
    }
}