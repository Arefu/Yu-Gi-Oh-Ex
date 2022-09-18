using WolfX.Handler.Tools;
using WolfX.Handlers;

namespace WolfX
{
    public partial class WolfUI : Form
    {
        public WolfUI()
        {
            CheckForIllegalCrossThreadCalls = false;
            Form = this;
            InitializeComponent();
            this.lv_ArchivePreviewer.LargeImageList = Preview_Generator.Get_ArchiveImageFromDll();
        }

        private void File_Open_Click(object sender, EventArgs e)
        {
            using var FolderBrowser = new FolderBrowserDialog();
            var Res = FolderBrowser.ShowDialog();

            if (Res == DialogResult.OK && !string.IsNullOrWhiteSpace(FolderBrowser.SelectedPath))
            {
                if (new DirectoryInfo(FolderBrowser.SelectedPath).Name == "YGO_2020")
                {
                    LBL_GameStatusLabel.Text = @"Ready";
                    LBL_GameStatusLabel.ForeColor = Color.Green;

                    WolfX_TabManager.Enabled = true;
                    WolfX_UI_State.WorkingDirectory = FolderBrowser.SelectedPath;
                    WolfX_UI_State.IsLoaded = true;
                    new Thread(Handlers.Card_Loader.Load).Start();
                    new Thread(Handlers.Archive_Handler.Load).Start();
                }
            }
        }

        private void File_Exit_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }

        private void Lv_ArchivePreviewer_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            this.lv_ArchivePreviewer.SuspendLayout();
            new Thread(() => Archive_Handler.OpenArchive(lv_ArchivePreviewer.SelectedItems[0].Text)).Start();
        }

        private void Tools_Verify_Click(object sender, EventArgs e)
        {
        }

        private void btn_CloseArchive_Click(object sender, EventArgs e)
        {
            Archive_Handler.CloseArchive();
        }

        private void btn_ExtractAll_Click(object sender, EventArgs e)
        {
            var Archive = "";
            if (lv_ArchivePreviewer.SelectedItems.Count != 1 && lbl_Name.Text == "")
            {
                MessageBox.Show(@"Please select an archive to extract from.");
                return;
            }
            else if (lbl_Name.Text == "")
                Archive = lv_ArchivePreviewer.SelectedItems[0].Text;
            else
                Archive = lbl_Name.Text;

            var Files = Archive_Handler.GetFiles(Archive);
            new DirectoryInfo(new FileInfo(Archive).Name).Create();

            using var Reader = new BinaryReader(File.Open($"{WolfX_UI_State.WorkingDirectory}\\{Archive}",
                FileMode.Open, FileAccess.Read, FileShare.Read));

            foreach (var File in Files)
            {
                Reader.BaseStream.Position = File.Offset;
                System.IO.File.WriteAllBytes($"{Archive}\\{File.Name}", Reader.ReadBytes((int)File.Size));
            }
        }
    }
}