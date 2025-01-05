using System.Runtime.InteropServices;
using System.Text;

namespace WolfX.WolfX.File_Type_UI
{
    public partial class Config : Form
    {
        string _Path = "";
        public Config()
        {
            InitializeComponent();
        }
        [DllImport("kernel32.dll")]
        static extern int GetPrivateProfileString(string Section, string Key, string Default, StringBuilder RetVal, int Size, string FilePath);
        [DllImport("kernel32.dll")]
        static extern uint GetPrivateProfileInt(string lpAppName, string lpKeyName, int nDefault, string lpFileName);
        [DllImport("kernel32.dll")]
        static extern bool WritePrivateProfileString(string lpAppName, string lpKeyName, string lpString, string lpFileName);
        private void Config_Load(object sender, EventArgs e)
        {
            using OpenFileDialog OpenFile = new OpenFileDialog();
            OpenFile.Filter = "Config Files (*.ini)|*.ini";
            OpenFile.Title = "Select Config File";
            if (OpenFile.ShowDialog() != DialogResult.OK) return;

            _Path = OpenFile.FileName;

            uint FreeStore, NoBan, AutoPause, UseJP;
            FreeStore = GetPrivateProfileInt("Yu-Gi-Oh-PatchMeOut", "FreeStore", 0, _Path);
            NoBan = GetPrivateProfileInt("Yu-Gi-Oh-PatchMeOut", "NoBan", 0, _Path);
            AutoPause = GetPrivateProfileInt("Yu-Gi-Oh-PatchMeOut", "AutoPause", 0, _Path);
            UseJP = GetPrivateProfileInt("Yu-Gi-Oh-PatchMeOut", "UseJP", 0, _Path);

            FreeStore_CheckBox.Checked = FreeStore == 1;
            NoBan_CheckBox.Checked = NoBan == 1;
            AutoPause_CheckBox.Checked = AutoPause == 1;
            UseJP_CheckBox.Checked = UseJP == 1;

            string pluginPath;
            uint AutoLoadPlugins;

            StringBuilder sb = new StringBuilder(255);
            StringBuilder archive = new StringBuilder(255);
            GetPrivateProfileString("Yu-Gi-Oh-GUI", "PluginsPath", "", sb, 255, _Path);
            pluginPath = sb.ToString();
            AutoLoadPlugins = GetPrivateProfileInt("Yu-Gi-Oh-GUI", "AutoLoadPlugins", 0, _Path);

            PluginPath_TextBox.Text = pluginPath;
            AutoLoadPlugins_CheckBox.Checked = AutoLoadPlugins == 1;

            GetPrivateProfileString("Yu-Gi-Oh-BetterLoad", "Archive", "", archive, 255, _Path);
            Archive_TextBox.Text = archive.ToString();

        }


        private void BTN_CONFIG_OnSave_Click(object sender, EventArgs e)
        {
            WritePrivateProfileString("Yu-Gi-Oh-PatchMeOut", "FreeStore", (FreeStore_CheckBox.Checked ? 1 : 0).ToString(), _Path);
            WritePrivateProfileString("Yu-Gi-Oh-PatchMeOut", "NoBan", (NoBan_CheckBox.Checked ? 1 : 0).ToString(), _Path);
            WritePrivateProfileString("Yu-Gi-Oh-PatchMeOut", "AutoPause", (AutoPause_CheckBox.Checked ? 1 : 0).ToString(), _Path);
            WritePrivateProfileString("Yu-Gi-Oh-PatchMeOut", "UseJP", (UseJP_CheckBox.Checked ? 1 : 0).ToString(), _Path);
            WritePrivateProfileString("Yu-Gi-Oh-GUI", "PluginsPath", PluginPath_TextBox.Text, _Path);
            WritePrivateProfileString("Yu-Gi-Oh-GUI", "AutoLoadPlugins", (AutoLoadPlugins_CheckBox.Checked ? 1 : 0).ToString(), _Path);
            WritePrivateProfileString("Yu-Gi-Oh-BetterLoad", "Archive", Archive_TextBox.Text, _Path);

            this.Close();
        }
    }
}
