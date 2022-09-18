using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WolfX.Handler.Tools;
using WolfX.Types;

namespace WolfX.Handlers
{
    internal static class Archive_Handler
    {
        internal static void Load()
        {
            var Archives = new List<string>();
            Archives.AddRange(Directory.GetFiles(WolfX_UI_State.WorkingDirectory!, "*.zib"));

            foreach (var Archive in Archives)
            {
                WolfUI.Form.lv_ArchivePreviewer.Items.Add(new FileInfo(Archive).Name, 0);
            }
        }

        internal static void OpenArchive(string Archive)
        {
            if (WolfX_UI_State.HasArchiveOpen == true)
            {
                //if (new FileInfo(WolfUI.Form.lv_ArchivePreviewer.SelectedItems[0].Text).Extension == ".ydc")
                //   else;
                return;
            }

            WolfX_UI_State.HasArchiveOpen = true;
            WolfUI.Form.btn_CloseArchive.Enabled = true;
            WolfUI.Form.LBL_GameStatusLabel.Text = @"Loading Archive";
            WolfUI.Form.LBL_GameStatusLabel.ForeColor = Color.Orange;

            var Files = new List<Archive_File>();
            Files = Archive_Handler.GetFiles(Archive);

            WolfUI.Form.lbl_Name.Text = Archive;
            WolfUI.Form.lbl_Size.Text = Files.Sum(x => x.Size).ToString();
            WolfUI.Form.lbl_ItemCount.Text = Files.Count.ToString();

            WolfUI.Form.LBL_GameStatusLabel.Text = @"Loading Files";

            WolfUI.Form.lv_ArchivePreviewer.Items.Clear();
            switch (Archive)
            {
                case "2020.full.illust_a.jpg.zib":
                case "2020.full.illust_j.jpg.zib":
                case "busts.zib":
                    WolfUI.Form.lv_ArchivePreviewer.LargeImageList = WolfUI.Form.cb_ShowPicturePreview.Checked ? Preview_Generator.Get_ImagePreviewFromArchiveData(Files, Archive) : Preview_Generator.Get_DefaultImageFromDll();

                    for (var File = 0; File < Files.Count; File++)
                    {
                        switch (WolfUI.Form.cb_ShowPicturePreview.Checked)
                        {
                            case true when WolfUI.Form.cb_ShowFileName.Checked:
                                WolfUI.Form.lv_ArchivePreviewer.Items.Add(Files[File].Name, File);
                                break;

                            case true:
                                WolfUI.Form.lv_ArchivePreviewer.Items.Add("", File);
                                break;

                            default:
                                {
                                    WolfUI.Form.lv_ArchivePreviewer.Items.Add(
                                        WolfUI.Form.cb_ShowFileName.Checked ? Files[File].Name : "", 0);
                                    break;
                                }
                        }
                    }
                    break;

                case "decks.zib":
                case "packs.zib":
                    WolfUI.Form.lv_ArchivePreviewer.LargeImageList = Preview_Generator.Get_BriefcaseImageFromDll();

                    foreach (var File in Files)
                    {
                        WolfUI.Form.lv_ArchivePreviewer.Items.Add(File.Name, 0);
                    }
                    break;
            }
            WolfUI.Form.lv_ArchivePreviewer.ResumeLayout();
            WolfUI.Form.LBL_GameStatusLabel.Text = @"Archive Ready";
            WolfUI.Form.LBL_GameStatusLabel.ForeColor = Color.Green;
        }

        public static List<Archive_File> GetFiles(string Archive)
        {
            using var Reader = new BinaryReader(File.Open($"{WolfX_UI_State.WorkingDirectory}\\{Archive}",
                FileMode.Open, FileAccess.Read, FileShare.Read));

            var Files = new List<Archive_File>();
            var Size = 4;
            var DataStart = long.MaxValue;

            while (Reader.BaseStream.Position + 64 <= DataStart)
            {
                var FileStart = (BitConverter.ToUInt32(Reader.ReadBytes(Size).Reverse().ToArray(), 0) / 4) * 4;

                if (FileStart == 0 && Size == 4)
                {
                    Reader.BaseStream.Seek(0, SeekOrigin.Begin);
                    Size = 8;
                    continue;
                }

                var FileSize = BitConverter.ToUInt32(Reader.ReadBytes(Size).Reverse().ToArray(), 0);
                var FileName = Encoding.UTF8.GetString(Reader.ReadBytes(64 - Size * 2)).TrimEnd('\0');

                if (FileStart < DataStart)
                    DataStart = FileStart;

                Files.Add(new Archive_File(FileStart, FileSize, FileName));
            }

            return Files;
        }

        public static void CloseArchive()
        {
            WolfUI.Form.lv_ArchivePreviewer.LargeImageList = null;
            WolfUI.Form.lbl_Name.Text = "";
            WolfUI.Form.lbl_Size.Text = "";
            WolfUI.Form.lbl_ItemCount.Text = "";
            WolfUI.Form.LBL_GameStatusLabel.Text = @"Ready";

            WolfUI.Form.LBL_GameStatusLabel.ForeColor = Color.Green;

            WolfUI.Form.btn_CloseArchive.Enabled = false;

            WolfX_UI_State.HasArchiveOpen = false;

            WolfUI.Form.lv_ArchivePreviewer.Items.Clear();
            WolfUI.Form.lv_ArchivePreviewer.LargeImageList = Preview_Generator.Get_ArchiveImageFromDll();

            Load();
        }
    }
}