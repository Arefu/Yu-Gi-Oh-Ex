using System.IO;

using Types;

namespace WolfX
{
    public partial class WolfUI
    {
        private void ARCHIVE_BTN_OpenZIB_Click(object sender, EventArgs e)
        {
            ARCHIVE_LV_ArchiveItems.Items.Clear();
            ARCHIVE_LBL_ArchiveSize.Text = "";
            ARCHIVE_LBL_ArchiveItems.Text = "";
            ARCHIVE_LBL_ArchiveName.Text = "";

            using (var OpenDialog = new OpenFileDialog())
            {
                OpenDialog.Title = "Open ZIB Archive";
                OpenDialog.Filter = "ZIB Archive (*.zib)|*.zib";
                if (State.Path != null)
                {
                    OpenDialog.InitialDirectory = State.Path;
                }
                if (OpenDialog.ShowDialog() != DialogResult.OK)
                {
                    MessageBox.Show("No ZIB Archive Selected", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);

                    return;
                }
                var Items = ZIB.Load(OpenDialog.FileName);
                var Images = new ImageList();
                Images.ImageSize = new Size(32, 32);
                if (ARCHIVE_CB_ShowPreviewImage.Checked && ZIB._ImageArchive == true)
                {
                    ARCHIVE_LV_ArchiveItems.View = View.LargeIcon;
                    ARCHIVE_LV_ArchiveItems.LargeImageList = Images;
                }
                else
                {
                    ARCHIVE_LV_ArchiveItems.View = View.List;
                    ARCHIVE_CB_ShowPreviewImage.Checked = false;
                    ARCHIVE_LV_ArchiveItems.LargeImageList = null;
                }
              

                foreach (var Item in Items)
                {
                    if (ARCHIVE_CB_ShowPreviewImage.Checked)
                    {
                        ARCHIVE_LV_ArchiveItems.Items.Add(Item.Name, Item.Name);
                        Images.Images.Add(Item.Name, ZIB.Shrink_ImageFromArchive(ZIB.Get_SpecificItemFromArchive(Item.Name)));
                    }
                    else
                    {
                        ARCHIVE_LV_ArchiveItems.Items.Add(Item.Name);
                    }
                }

                ARCHIVE_BTN_ExtractZIB.Enabled = true;
                ARCHIVE_LBL_ArchiveName.Text = new FileInfo(OpenDialog.FileName).Name;
                ARCHIVE_LBL_ArchiveItems.Text = Items.Count.ToString();
                ARCHIVE_LBL_ArchiveSize.Text = Utility.FormatSize(new FileInfo(OpenDialog.FileName).Length);
            }
        }

        private void ARCHIVE_BTN_SaveZIB_Click(object sender, EventArgs e)
        {
            using (var SelectFolder = new FolderBrowserDialog())
            {
                SelectFolder.Description = "Select ZIB Folder To Repack";
                SelectFolder.UseDescriptionForTitle = true;

                if (SelectFolder.ShowDialog() != DialogResult.OK)
                {
                    MessageBox.Show("No Folder Selected", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                if (new DirectoryInfo(SelectFolder.SelectedPath).Name.EndsWith(".zib") == false)
                {
                    MessageBox.Show("Please Select A Valid Extracted .ZIB Folder", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                ZIB.Save(SelectFolder.SelectedPath);
            }
        }

        private void ARCHIVE_LV_ArchiveItems_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (ZIB._Loaded == false)
                return;

            new DirectoryInfo(new FileInfo(ZIB._Archive).Name).Create();

            File.WriteAllBytes($"{new FileInfo(ZIB._Archive).Name}/{ZIB._Items[ARCHIVE_LV_ArchiveItems.SelectedItems[0].Index].Name}", ZIB.Get_SpecificItemFromArchive(ARCHIVE_LV_ArchiveItems.SelectedItems[0].Text).ToArray());
        }

        private void ARCHIVE_BTN_ExtractZIB_Click(object sender, EventArgs e)
        {
            if (ZIB._Loaded == false)
                return;

            new DirectoryInfo($"!{new FileInfo(ZIB._Archive).Name}").Create();

            foreach (var Item in ZIB._Items)
            {
                File.WriteAllBytes($"!{new FileInfo(ZIB._Archive).Name}/{Item.Name}", ZIB.Get_SpecificItemFromArchive(Item.Name).ToArray());
            }
        }


    }
}
