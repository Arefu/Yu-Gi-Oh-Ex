using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

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
