using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Types;
using WolfX.Types;

namespace WolfX
{
    public partial class WolfUI
    {
        private void ANIMS_BTN_OpenScene_Click(object sender, EventArgs e)
        {
            using (var OpenFile = new OpenFileDialog())
            {
                OpenFile.Title = "Select Animlist File";
                OpenFile.Filter = "Animlist File|*.txt";
                if (OpenFile.ShowDialog() != DialogResult.OK)
                {
                    MessageBox.Show("Please Select an Animlist File", "No Animlist File Selected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                AnimlistUI Editor = new AnimlistUI();
                Editor.Anim_Items = Animlist.Load(OpenFile.FileName, false);
                foreach(var Item in Editor.Anim_Items)
                {
                    ANIMS_LV_ItemsInScene.Items.Add(Item.ItemName);
                }
                Editor.SceneLocation = OpenFile.FileName;

                Editor.Show();
                Editor.SetupScene();
            }
        }
    }
}
