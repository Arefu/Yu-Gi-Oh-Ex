
using System.IO;
using Types;
using WolfX.Types;

namespace WolfX
{
    public partial class WolfUI
    {
        //DFYMOO Tab Events
        private void DFY_BTN_Load_Click(object sender, EventArgs e)
        {
            DFYMOO_ItemList.Items.Clear();

            if (DFY_Editor != null)
                DFY_Editor.Close();

            using var OpenFile = new OpenFileDialog();
            OpenFile.Title = "Select DFYMOO File";
            OpenFile.Filter = "DFYMOO File|*.dfymoo";
            if (OpenFile.ShowDialog() != DialogResult.OK)
            {
                MessageBox.Show("Please Select a DFYMOO File", "No DFYMOO File Selected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            DFY_Editor = new DfymooUI(Path.GetFullPath(OpenFile.FileName));
            DFY_Editor.DFY_Items = Dfymoo.Load(OpenFile.FileName);

            foreach (var Item in DFY_Editor.DFY_Items)
            {
                DFYMOO_ItemList.Items.Add(Item.ItemName);
            }

            lbl_Dfymoo_name.Text = OpenFile.SafeFileName;
            lbl_Dfymoo_NumOfItems.Text = DFY_Editor.DFY_Items.Count.ToString();

            DFY_Editor.Show();
        }
        private void DFY_BTN_Save_Click(object sender, EventArgs e)
        {
            Dfymoo.Save(Path.GetFileName(DFY_Editor._PicturePath), DFY_Editor.DFY_Items, DFY_Editor.DFY_Picture.Size);
        }
        private void DFYMOO_ItemList_ItemSelectionChanged(object sender, ListViewItemSelectionChangedEventArgs e)
        {
            if (!e.IsSelected)
            {
                // Update Editor.DFY_Items List with the Updated version of DFY_Item.
                var index = DFY_Editor.DFY_Items.FindIndex(dfy => dfy.ItemName == DFY_Editor.DFY_Item.ItemName);
                if (index != -1)
                {
                    DFY_Editor.DFY_Items[index] = DFY_Editor.DFY_Item;
                }
            }

            var Item = e.Item;
            foreach (var DFY in DFY_Editor.DFY_Items)
            {
                if (DFY.ItemName == Item.Text)
                {
                    DFY_Editor.DFY_Item = DFY;
                    break;
                }
            }

            DFY_NUD_X.Value = DFY_Editor.DFY_Item.ItemStartPoint.X;
            DFY_NUD_Y.Value = DFY_Editor.DFY_Item.ItemStartPoint.Y;

            DFY_NUD_W.Value = DFY_Editor.DFY_Item.ItemDimensions.X;
            DFY_NUD_H.Value = DFY_Editor.DFY_Item.ItemDimensions.Y;

            DFY_Editor.DFY_Picture.Refresh();
        }
        private void DFY_NUD_X_ValueChanged(object sender, EventArgs e)
        {
            if (DFY_NUD_X.Value != DFY_Editor.DFY_Item.ItemStartPoint.X)
                DFY_Editor.DFY_Item.ItemStartPoint = new Point((int)DFY_NUD_X.Value, DFY_Editor.DFY_Item.ItemStartPoint.Y);

            DFY_Editor.DFY_Picture.Refresh();
        }
        private void DFY_NUD_Y_ValueChanged(object sender, EventArgs e)
        {
            if (DFY_NUD_Y.Value != DFY_Editor.DFY_Item.ItemStartPoint.Y)
                DFY_Editor.DFY_Item.ItemStartPoint = new Point(DFY_Editor.DFY_Item.ItemStartPoint.X, (int)DFY_NUD_Y.Value);

            DFY_Editor.DFY_Picture.Refresh();
        }
        private void DFY_NUD_H_ValueChanged(object sender, EventArgs e)
        {
            if (DFY_NUD_H.Value != DFY_Editor.DFY_Item.ItemDimensions.Y)
                DFY_Editor.DFY_Item.ItemDimensions = new Point(DFY_Editor.DFY_Item.ItemDimensions.X, (int)DFY_NUD_H.Value);

            DFY_Editor.DFY_Picture.Refresh();
        }
        private void DFY_NUD_W_ValueChanged(object sender, EventArgs e)
        {
            if (DFY_NUD_W.Value != DFY_Editor.DFY_Item.ItemDimensions.X)
                DFY_Editor.DFY_Item.ItemDimensions = new Point((int)DFY_NUD_W.Value, DFY_Editor.DFY_Item.ItemDimensions.Y);

            DFY_Editor.DFY_Picture.Refresh();
        }
    }
}
