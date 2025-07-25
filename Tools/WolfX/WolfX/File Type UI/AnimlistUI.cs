namespace WolfX.Types
{
    public partial class AnimlistUI : Form
    {
        public AnimlistUI(string SceneLocation)
        {
            this.SceneLocation = SceneLocation;
            InitializeComponent();
        }

        public void PrepareScene()
        {
        }

        private void ANIMS_Picture_Paint(object sender, PaintEventArgs e)
        {
        }
    }
}