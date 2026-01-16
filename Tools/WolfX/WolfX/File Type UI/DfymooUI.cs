namespace WolfX.Types
{
    public partial class DfymooUI : Form
    {
        private Color _penColor = Color.HotPink;
        private decimal _penSize;
        private Point startPoint;
        private Point endPoint;
        private bool isDrawing = false;
        private Rectangle drawnRectangle;

        public DfymooUI(String PicturePath, Color BGColor, Color PenColor, decimal PenSize)

        {
            InitializeComponent();
            _PicturePath = PicturePath;

            DFY_Picture_Container.BackColor = BGColor;
            _penColor = PenColor;
            _penSize = PenSize;
            DFY_Picture.Image = System.Drawing.Image.FromFile(PicturePath.Replace(".dfymoo", ".png"));
        }

        private void DFY_Picture_Paint(object sender, PaintEventArgs e)
        {
            using var Pen = new Pen(_penColor, (float)_penSize);
            if (DFY_Item != null)
            {
                drawnRectangle = Rectangle.Empty;
                DFY_Picture.Invalidate(); // Triggers repaint to remove the rectangle
                _Rect = new Rectangle(DFY_Item.ItemStartPoint.X, DFY_Item.ItemStartPoint.Y, DFY_Item.ItemDimensions.X, DFY_Item.ItemDimensions.Y);
                e.Graphics.DrawRectangle(Pen, _Rect);
            }

            if (isDrawing)
            {
                Rectangle rect = GetRectangle(startPoint, endPoint);
                e.Graphics.DrawRectangle(Pen, rect);
            }
            else if (drawnRectangle != Rectangle.Empty)
            {
                e.Graphics.DrawRectangle(Pen, drawnRectangle);
            }
        }

        private void DFY_Picture_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                isDrawing = true;
                startPoint = e.Location;
                endPoint = e.Location;
                DFY_Picture.Invalidate();
            }
        }

        private void DFY_Picture_MouseMove(object sender, MouseEventArgs e)
        {
            if (isDrawing)
            {
                endPoint = e.Location;
                DFY_Picture.Invalidate();
            }
        }

        private void DFY_Picture_MouseUp(object sender, MouseEventArgs e)
        {
            if (isDrawing)
            {
                isDrawing = false;
                drawnRectangle = GetRectangle(startPoint, endPoint);
                DFY_Picture.Invalidate();
            }
        }

        private Rectangle GetRectangle(Point p1, Point p2)
        {
            int x = Math.Min(p1.X, p2.X);
            int y = Math.Min(p1.Y, p2.Y);
            int width = Math.Abs(p1.X - p2.X);
            int height = Math.Abs(p1.Y - p2.Y);
            return new Rectangle(x, y, width, height);
        }
    }
}