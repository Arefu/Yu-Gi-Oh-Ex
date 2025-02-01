namespace WolfX
{
    public partial class WolfUI : Form
    {
        public WolfUI()
        {
            InitializeComponent();
        }

        private void WOLFUI_TOOLITEM_LoadGame_Click(object sender, EventArgs e)
        {
            using var OpenFolder = new FolderBrowserDialog();
            OpenFolder.Description = "Select Extracted YGO_2020 Folder";

            if (OpenFolder.ShowDialog() != DialogResult.OK || OpenFolder.SelectedPath.EndsWith("YGO_2020") == false)
                MessageBox.Show("Please Select the YGO_2020 Folder", "Invalid Folder", MessageBoxButtons.OK, MessageBoxIcon.Error);
            else
                State.Path = OpenFolder.SelectedPath;
        }

        private void WOLFUI_TOOLITEM_OpenConfigEditor_Click(object sender, EventArgs e)
        {
            var Config = new WolfX.File_Type_UI.Config();
            Config.ShowDialog();
        }

   
    }
}