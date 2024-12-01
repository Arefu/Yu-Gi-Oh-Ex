using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

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
    }
}
