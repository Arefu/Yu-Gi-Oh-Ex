using System.IO;
using Types;

namespace WolfX.Types
{
    public partial class AnimlistUI : Form
    {
        public class LayerItem
        {
            public Image Img;
            public Point Position;
            public bool Selected;
            public string Name; // filename
        }

        private List<LayerItem> Layers = new List<LayerItem>();
        private LayerItem selectedLayer = null;
        private Point lastMouse;
        private Panel panel1 = new Panel();

        public AnimlistUI(string SceneLocation)
        {
            InitializeComponent();

            panel1.Dock = DockStyle.Fill;
            panel1.BackColor = Color.White;
            panel1.DoubleBuffered(true); // extension method below
            this.Controls.Add(panel1);

            panel1.Paint += Panel1_Paint;
            panel1.MouseDown += Panel1_MouseDown;
            panel1.MouseMove += Panel1_MouseMove;
            panel1.MouseUp += Panel1_MouseUp;

            LoadLayers(); // load Animlist.Scene her
        }

        public void PrepareScene()
        {
            return;
            foreach (var Item in Animlist.Scene)
            {
                var _PB = new PictureBox
                {
                    SizeMode = PictureBoxSizeMode.AutoSize,
                    Location = Item.ItemPosition,
                    Image = Image.FromFile(Item.ItemName),
                    BackColor = Color.Transparent
                };

                PNL_AnimList_Container.Controls.Add(_PB);
                _PB.BringToFront(); // Ensures newest is on top of older ones
            }

            PNL_AnimList_Container.SendToBack();
        }

        private void LoadLayers()
        {
            // Example Animlist.Scene
            var AnimlistScene = new[]
            {
                new { ItemName = "background", ItemPosition = new Point(0,0) },
                new { ItemName = "overlay", ItemPosition = new Point(50,50) }
            };

            string CurrentDirectory = AppDomain.CurrentDomain.BaseDirectory;

            Layers.Clear();

            foreach (var item in AnimlistScene)
            {
                var matchedFile = Directory.EnumerateFiles(CurrentDirectory)
                    .FirstOrDefault(f => Path.GetFileNameWithoutExtension(f)
                        .Equals(item.ItemName, StringComparison.OrdinalIgnoreCase));

                if (matchedFile == null)
                    continue;

                Layers.Add(new LayerItem
                {
                    Img = Image.FromFile(matchedFile),
                    Position = item.ItemPosition,
                    Name = Path.GetFileName(matchedFile),
                    Selected = false
                });
            }

            panel1.Invalidate();
        }

        private void Panel1_Paint(object sender, PaintEventArgs e)
        {
            e.Graphics.Clear(panel1.BackColor);

            foreach (var layer in Layers)
            {
                e.Graphics.DrawImage(layer.Img, layer.Position);

                if (layer.Selected)
                {
                    e.Graphics.DrawRectangle(Pens.Red,
                        new Rectangle(layer.Position, layer.Img.Size));
                }
            }
        }

        private void Panel1_MouseDown(object sender, MouseEventArgs e)
        {
            selectedLayer = null;

            // Top-down: last = topmost
            for (int i = Layers.Count - 1; i >= 0; i--)
            {
                var layer = Layers[i];
                var rect = new Rectangle(layer.Position, layer.Img.Size);
                if (rect.Contains(e.Location))
                {
                    selectedLayer = layer;
                    layer.Selected = true;

                    // Move layer to top
                    Layers.RemoveAt(i);
                    Layers.Add(layer);

                    lastMouse = e.Location;
                    panel1.Invalidate();
                    break;
                }
            }
        }

        private void Panel1_MouseMove(object sender, MouseEventArgs e)
        {
            if (selectedLayer != null && e.Button == MouseButtons.Left)
            {
                int dx = e.X - lastMouse.X;
                int dy = e.Y - lastMouse.Y;
                selectedLayer.Position = new Point(
                    selectedLayer.Position.X + dx,
                    selectedLayer.Position.Y + dy
                );
                lastMouse = e.Location;
                panel1.Invalidate();
            }
        }

        private void Panel1_MouseUp(object sender, MouseEventArgs e)
        {
            if (selectedLayer != null)
                selectedLayer.Selected = false;
            selectedLayer = null;
            panel1.Invalidate();
        }
    }

    // Extension method for DoubleBuffered on Panel
    public static class ControlExtensions
    {
        public static void DoubleBuffered(this Control control, bool enable)
        {
            System.Reflection.PropertyInfo doubleBufferPropertyInfo =
                control.GetType().GetProperty("DoubleBuffered",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            doubleBufferPropertyInfo.SetValue(control, enable, null);
        }
    }
}
