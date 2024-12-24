using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Types;

namespace WolfX
{
    public partial class WolfUI : Form
    {
        public WolfUI()
        {
            // CheckForIllegalCrossThreadCalls = false;

            InitializeComponent();
        }

        private void Tools_OnLoadGame_Click(object sender, EventArgs e)
        {
            using var OpenFolder = new FolderBrowserDialog();
            OpenFolder.Description = "Select Extracted YGO_2020 Folder";
            if (OpenFolder.ShowDialog() != DialogResult.OK) return;


            if (OpenFolder.SelectedPath.EndsWith("YGO_2020") == false)
            {
                MessageBox.Show("Please Select the YGO_2020 Folder", "Invalid Folder", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            else
                State.Path = OpenFolder.SelectedPath;
        }

      
    }
}