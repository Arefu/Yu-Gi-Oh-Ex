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

        private void Config_Load(object sender, EventArgs e)
        {
            using OpenFileDialog OpenFile = new OpenFileDialog();
            OpenFile.Filter = "Config Files (*.ini)|*.ini";
            OpenFile.Title = "Select Config File";
            if (OpenFile.ShowDialog() != DialogResult.OK) return;

            _Path = OpenFile.FileName;

            uint FreeStore, NoBan, AutoPause, UseJP, NoJanken, AllowMultiInstance, AutoLoadPlugins;
            FreeStore = PInvoke.GetPrivateProfileInt("Yu-Gi-Oh-PatchMeOut", "FreeStore", 0, _Path);
            NoBan = PInvoke.GetPrivateProfileInt("Yu-Gi-Oh-PatchMeOut", "NoBan", 0, _Path);
            AutoPause = PInvoke.GetPrivateProfileInt("Yu-Gi-Oh-PatchMeOut", "AutoPause", 0, _Path);
            UseJP = PInvoke.GetPrivateProfileInt("Yu-Gi-Oh-PatchMeOut", "UseJP", 0, _Path);
            NoJanken = PInvoke.GetPrivateProfileInt("Yu-Gi-Oh-PatchMeOut", "NoJanken", 0, _Path);

            FreeStore_CheckBox.Checked = FreeStore == 1;
            NoBan_CheckBox.Checked = NoBan == 1;
            AutoPause_CheckBox.Checked = AutoPause == 1;
            UseJP_CheckBox.Checked = UseJP == 1;
            DisableJanken_CB.Checked = NoJanken == 1;

            string pluginPath;

            StringBuilder sb = new StringBuilder(255);
            StringBuilder archive = new StringBuilder(255);
            PInvoke.GetPrivateProfileString("Yu-Gi-Oh-GUI", "PluginsPath", "", sb, 255, _Path);
            pluginPath = sb.ToString();
            AutoLoadPlugins = PInvoke.GetPrivateProfileInt("Yu-Gi-Oh-GUI", "AutoLoadPlugins", 0, _Path);

            PluginPath_TextBox.Text = pluginPath;
            AutoLoadPlugins_CheckBox.Checked = AutoLoadPlugins == 1;

            PInvoke.GetPrivateProfileString("Yu-Gi-Oh-BetterLoad", "Archive", "", archive, 255, _Path);
            Archive_TextBox.Text = archive.ToString();

            AllowMultiInstance = PInvoke.GetPrivateProfileInt("Yu-Gi-Oh-BetterLoad", "AllowMultiInstance", 0, _Path);
            AllowMultiInstance_CB.Checked = AllowMultiInstance == 1;
        }


        private void BTN_CONFIG_OnSave_Click(object sender, EventArgs e)
        {
            PInvoke.WritePrivateProfileString("Yu-Gi-Oh-PatchMeOut", "FreeStore", (FreeStore_CheckBox.Checked ? 1 : 0).ToString(), _Path);
            PInvoke.WritePrivateProfileString("Yu-Gi-Oh-PatchMeOut", "NoBan", (NoBan_CheckBox.Checked ? 1 : 0).ToString(), _Path);
            PInvoke.WritePrivateProfileString("Yu-Gi-Oh-PatchMeOut", "AutoPause", (AutoPause_CheckBox.Checked ? 1 : 0).ToString(), _Path);
            PInvoke.WritePrivateProfileString("Yu-Gi-Oh-PatchMeOut", "UseJP", (UseJP_CheckBox.Checked ? 1 : 0).ToString(), _Path);
            PInvoke.WritePrivateProfileString("Yu-Gi-Oh-PatchMeOut", "NoJanken", (DisableJanken_CB.Checked ? 1 : 0).ToString(), _Path);
            PInvoke.WritePrivateProfileString("Yu-Gi-Oh-GUI", "PluginsPath", PluginPath_TextBox.Text, _Path);
            PInvoke.WritePrivateProfileString("Yu-Gi-Oh-GUI", "AutoLoadPlugins", (AutoLoadPlugins_CheckBox.Checked ? 1 : 0).ToString(), _Path);
            PInvoke.WritePrivateProfileString("Yu-Gi-Oh-BetterLoad", "Archive", Archive_TextBox.Text, _Path);
            PInvoke.WritePrivateProfileString("Yu-Gi-Oh-BetterLoad", "AllowMultiInstance", (AllowMultiInstance_CB.Checked ? 1 : 0).ToString(), _Path);
            this.Close();
        }
    }
}
