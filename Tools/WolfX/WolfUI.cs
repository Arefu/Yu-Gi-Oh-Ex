using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Types;
using WolfX.Handler.Tools;
using WolfX.Handlers;

namespace WolfX
{
    public partial class WolfUI : Form
    {
        public WolfUI()
        {
            CheckForIllegalCrossThreadCalls = false;
      
            InitializeComponent();
        }

        internal void Tools_Verify_Click(object sender, EventArgs e)
        {
            var Missing = false;
            using var OpenFile = new OpenFileDialog();
            OpenFile.Title = "Select Yu-Gi-Oh! Legacy of the Duelist Link Evolution YGO_2020.toc File";
            OpenFile.Filter = "YGO_2020.toc|YGO_2020.toc";
            if (OpenFile.ShowDialog() != DialogResult.OK) return;

            foreach (var Line in File.ReadLines(OpenFile.FileName))
            {
                if (Line == "UT") continue;

                var CLine = Line.TrimStart(' ');
                CLine = Regex.Replace(CLine, @"  +", " ", RegexOptions.Compiled);
                var Information = CLine.Split(' ', 3);

                if (File.Exists($"{State.WorkingDirectory}/{Information[2]}") == false)
                {
                    Missing = true;
                    File.AppendAllLines("Missing.txt", new[] { Information[2] });
                }
            }

            if (Missing)
                MessageBox.Show("Missing Files Found, Please Check Missing.txt and Re-Run Yami-Yugi.exe", "Missing Files", MessageBoxButtons.OK, MessageBoxIcon.Error);
            else
                MessageBox.Show("No Missing Files Found", "No Missing Files", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

    }
}