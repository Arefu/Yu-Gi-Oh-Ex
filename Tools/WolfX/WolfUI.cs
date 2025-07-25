using System.IO;
using WolfX.WolfX.File_Type_UI;

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

            if (Directory.Exists("YGO_2020"))
                State.Path = new DirectoryInfo("YGO_2020").FullName;

            if (string.IsNullOrEmpty(State.Path) == false && WolfX_TabManager.SelectedTab?.Text == "Card Manager")
            {
                CARDS_BTN_OpenCards_Click(this, new EventArgs());
                return;
            }
            else
            {
                var Result = OpenFolder.ShowDialog();
                if (Result != DialogResult.OK || OpenFolder.SelectedPath.EndsWith("YGO_2020") == false)
                {
                    MessageBox.Show("Please Select the YGO_2020 Folder", "Invalid Folder", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                else
                    State.Path = OpenFolder.SelectedPath;
            }
        }

        private void WOLFUI_TOOLITEM_OpenConfigEditor_Click(object sender, EventArgs e)
        {
            var Config = new WolfX.File_Type_UI.Config();
            Config.ShowDialog();
        }

        
    }
}