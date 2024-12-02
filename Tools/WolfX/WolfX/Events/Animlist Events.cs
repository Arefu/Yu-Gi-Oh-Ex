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

                ANIMS_Editor = new AnimlistUI(OpenFile.FileName);
                ANIMS_Editor.Anim_Items = Animlist.Load(OpenFile.FileName, false);

                foreach (var Item in ANIMS_Editor.Anim_Items)
                {
                    ANIMS_LV_ItemsInScene.Items.Add(Item.ItemName);
                }
       
                Form.ANIMS_LBL_Count.Text = ANIMS_Editor.Anim_Items.Count.ToString();

                ANIMS_Editor.PrepareScene();
                ANIMS_Editor.Show();
            }
        }
    }
}
