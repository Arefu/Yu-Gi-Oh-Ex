using System.Threading;

namespace WolfX
{
    public partial class WolfUI : Form
    {
        public WolfUI()
        {
            InitializeComponent();
        }

        private void File_Open_Click(object sender, EventArgs e)
        {
            using var FolderBrowser = new FolderBrowserDialog();
            var Res = FolderBrowser.ShowDialog();

            if (Res == DialogResult.OK && !string.IsNullOrWhiteSpace(FolderBrowser.SelectedPath))
            {
                if (new DirectoryInfo(FolderBrowser.SelectedPath).Name == "YGO_2020")
                {
                    LBL_GameStatusLabel.Text = "Ready";
                    LBL_GameStatusLabel.ForeColor = Color.Green;

                    WolfX_TabManager.Enabled = true;
                    WolfX_UI_State.Working_Directory = FolderBrowser.SelectedPath;
                    WolfX_UI_State.IsLoaded = true;
                }
            }
        }

        private void WolfX_TabManager_EnabledChanged(object sender, EventArgs e)
        {
            if (WolfX_TabManager.Enabled == true)
            {
                new Thread(Handlers.Card_Loader.Load).Start();
            }
        }
    }
}