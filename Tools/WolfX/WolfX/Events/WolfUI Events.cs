﻿using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text.RegularExpressions;

namespace WolfX
{
    public partial class WolfUI
    {
        //Handling Game Context Events
        private void WOLFUI_TOOLITEM_Extract_Click(object sender, EventArgs e)
        {
            using var OpenFile = new OpenFileDialog();
            if (!File.Exists("Yami-Yugi.exe"))
            {
                OpenFile.Title = "Select Yami-Yugi.exe";
                OpenFile.Filter = "Yami-Yugi.exe|Yami-Yugi.exe";
                if (OpenFile.ShowDialog() != DialogResult.OK)
                {
                    MessageBox.Show("Please Re-Download WolfX", "Yami-Yugi Not Found", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }

            OpenFile.Title = "Select Yu-Gi-Oh! Legacy of the Duelist Link Evolution YGO_2020.toc File";
            OpenFile.Filter = "YGO_2020.toc|YGO_2020.toc";
            if (OpenFile.ShowDialog() != DialogResult.OK)
            {
                MessageBox.Show("Please Select a YGO_2020.toc File", "No YGO_2020.toc File Selected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var Process = new Process();
            Process.StartInfo.FileName = "Yami-Yugi.exe";
            Process.StartInfo.Arguments = $"\"{OpenFile.FileName}\"";

            Process.Start();
        }

        private void WOLFUI_TOOLITEM_Pack_Click(object sender, EventArgs e)
        {
            var FolderBrowser = new Microsoft.Win32.OpenFolderDialog();
            using var OpenFile = new OpenFileDialog();
            if (!File.Exists("Yami-Yugi.exe"))
            {
                OpenFile.Title = "Select Yami-Yugi.exe";
                OpenFile.Filter = "Yami-Yugi.exe|Yami-Yugi.exe";
                if (OpenFile.ShowDialog() != DialogResult.OK)
                {
                    MessageBox.Show("Please Re-Download WolfX", "Yami-Yugi Not Found", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }

            if (State.Path == null || State.Path == "")
            {
                FolderBrowser.Title = "Select Yu-Gi-Oh! Legacy of the Duelist Link Evolution YGO_2020 Folder";
                if (FolderBrowser.ShowDialog() != true)
                {
                    MessageBox.Show("Please Select a YGO_2020 Folder", "No YGO_2020 Folder Selected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            if (FolderBrowser.FolderName == "")
                FolderBrowser.FolderName = State.Path;

            var Process = new Process();
            Process.StartInfo.FileName = "Yami-Yugi.exe";
            Process.StartInfo.Arguments = $"\"{FolderBrowser.FolderName}\"";

            Process.Start();
        }

        private void WOLFUI_TOOLITEM_SetPath_Click(object sender, EventArgs e)
        {
            //Check for Administrator rights
            if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
            {
                MessageBox.Show("Please Run WolfX as Administrator", "Administrator Rights Required", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var FolderBrowser = new Microsoft.Win32.OpenFolderDialog();
            FolderBrowser.Title = "Select Yu-Gi-Oh! Legacy of the Duelist Link Evolution YGO_2020 Folder";
            if (FolderBrowser.ShowDialog() != true)
            {
                MessageBox.Show("Please Select a YGO_2020 Folder", "No YGO_2020 Folder Selected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var RegistryKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\Steam App 1150640", true);

            if (RegistryKey == null)
            {
                Microsoft.Win32.Registry.LocalMachine.CreateSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\Steam App 1150640");
                RegistryKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\Steam App 1150640", true);
            }

            if (RegistryKey.GetValue("InstallLocation") == null)
                RegistryKey.SetValue("InstallLocation", FolderBrowser.FolderName);

            RegistryKey.Close();
        }

        internal void WOLFUI_TOOLITEM_VerifyFiles_Click(object sender, EventArgs e)
        {
            var Missing = false;
            using var OpenFile = new OpenFileDialog();
            OpenFile.Title = "Select Yu-Gi-Oh! Legacy of the Duelist Link Evolution YGO_2020.toc File";
            OpenFile.Filter = "YGO_2020.toc|YGO_2020.toc";
            if (OpenFile.ShowDialog() != DialogResult.OK) return;
            if (State.Path == string.Empty || State.Path == null)
                State.Path = $"{Directory.GetCurrentDirectory()}\\YGO_2020\\";

            foreach (var Line in File.ReadLines(OpenFile.FileName))
            {
                if (Line == "UT") continue;

                var CLine = Line.TrimStart(' ');
                CLine = Regex.Replace(CLine, @"  +", " ", RegexOptions.Compiled);
                var Information = CLine.Split(' ', 3);

                if (File.Exists($"{State.Path}/{Information[2]}") == false)
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

        //Handling Language Events
        private void Language_english_Click(object sender, EventArgs e)
        {
            foreach (var Item in languageToolStripMenuItem.DropDownItems)
            {
                var Language = (ToolStripMenuItem)Item;
                Language.Checked = false;
            }

            Language_english.Checked = true;
            State.Language = Language.English;
        }

        private void Language_french_Click(object sender, EventArgs e)
        {
            foreach (var Item in languageToolStripMenuItem.DropDownItems)
            {
                var Language = (ToolStripMenuItem)Item;
                Language.Checked = false;
            }

            Language_french.Checked = true;
            State.Language = Language.French;
        }

        private void Language_german_Click(object sender, EventArgs e)
        {
            foreach (var Item in languageToolStripMenuItem.DropDownItems)
            {
                var Language = (ToolStripMenuItem)Item;
                Language.Checked = false;
            }

            Language_german.Checked = true;
            State.Language = Language.German;
        }

        private void Language_italian_Click(object sender, EventArgs e)
        {
            foreach (var Item in languageToolStripMenuItem.DropDownItems)
            {
                var Language = (ToolStripMenuItem)Item;
                Language.Checked = false;
            }

            Language_italian.Checked = true;
            State.Language = Language.Italian;
        }

        private void Language_spanish_Click(object sender, EventArgs e)
        {
            foreach (var Item in languageToolStripMenuItem.DropDownItems)
            {
                var Language = (ToolStripMenuItem)Item;
                Language.Checked = false;
            }

            Language_spanish.Checked = true;
            State.Language = Language.Spanish;
        }

        private void Language_japanese_Click(object sender, EventArgs e)
        {
            foreach (var Item in languageToolStripMenuItem.DropDownItems)
            {
                var Language = (ToolStripMenuItem)Item;
                Language.Checked = false;
            }

            Language_japanese.Checked = true;
            State.Language = Language.Japanese;
        }

        private void Language_russian_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("The game doesn't seemingly support Russian, do you wan't to continue?", "Russian Not Supported", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.No)
                return;

            foreach (var Item in languageToolStripMenuItem.DropDownItems)
            {
                var Language = (ToolStripMenuItem)Item;
                Language.Checked = false;
            }

            Language_russian.Checked = true;
            State.Language = Language.Russian;
        }
    }
}