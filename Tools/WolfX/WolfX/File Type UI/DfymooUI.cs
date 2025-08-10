namespace WolfX.Types
{
    public partial class DfymooUI : Form
    {
        private Color _penColor = Color.HotPink;

        private decimal _penSize;

        public DfymooUI(String PicturePath, Color BGColor, Color PenColor, decimal PenSize)

        {
            InitializeComponent();
            _PicturePath = PicturePath;

            DFY_Picture_Container.BackColor = BGColor;
            _penColor = PenColor;
            _penSize = PenSize;
            DFY_Picture.Image = Image.FromFile(PicturePath.Replace(".dfymoo", ".png"));
        }

        private void DFY_Picture_Paint(object sender, PaintEventArgs e)
        {
            if (DFY_Item == null)
                return;

            using var Pen = new Pen(_penColor, (float)_penSize);
            _Rect = new Rectangle(DFY_Item.ItemStartPoint.X, DFY_Item.ItemStartPoint.Y, DFY_Item.ItemDimensions.X, DFY_Item.ItemDimensions.Y);
            e.Graphics.DrawRectangle(Pen, _Rect);
        }
    }
}