using System.Text;

using WolfX.Handler.Tools;
using WolfX.Types;

namespace WolfX.Handlers
{
    internal static class Archive
    {
        /// <summary>
        /// Get The List Of Files In The Specified Archive
        /// </summary>
        /// <param name="Archive">Archive To Read From</param>
        /// <returns>A List Full Of YGO_ArchiveFileEntry Objects That Contain The Information About The Files</returns>
        internal static List<YGO_ArchiveFileEntry> Get_FilesInArchive(string Archive)
        {
            using var Reader = new BinaryReader(File.Open($"{WolfUI.State.WorkingDirectory}\\{Archive}", FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
            var Files = new List<YGO_ArchiveFileEntry>();
            var DataStart = long.MaxValue;
            
            while (Reader.BaseStream.Position + 64 <= DataStart)
            {
                var FileStart = (BitConverter.ToUInt32(Reader.ReadBytes(4).Reverse().ToArray(), 0) / 4) * 4;

           

                var FileSize = BitConverter.ToUInt32(Reader.ReadBytes(4).Reverse().ToArray(), 0);
                var FileName = Encoding.UTF8.GetString(Reader.ReadBytes(64 - 4 * 2)).TrimEnd('\0');

                if (FileStart < DataStart)
                    DataStart = FileStart;

                Files.Add(new YGO_ArchiveFileEntry(FileStart, FileSize, FileName, Reader.ReadBytes((int)FileSize)));
            }

            Reader.Close();
            return Files;
        }

        internal static void Load()
        {
            var Archives = new List<string>();
            Archives.AddRange(Directory.GetFiles(WolfUI.State.WorkingDirectory!, "*.zib"));

            foreach (var Archive in Archives)
            {
                WolfUI.Form.lv_ArchivePreviewer.Items.Add(new FileInfo(Archive).Name, 0);
            }
        }
        
        internal static void OpenArchive(string Archive)
        {
            WolfUI.State.HasArchiveOpen = true;
            WolfUI.Form.btn_CloseArchive.Enabled = true;
            WolfUI.Form.LBL_GameStatusLabel.Text = @"Loading Archive";
            WolfUI.Form.LBL_GameStatusLabel.ForeColor = Color.Orange;

            var Files = new List<YGO_ArchiveFileEntry>();
            Files = Get_FilesInArchive(Archive);

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
                    WolfUI.Form.lv_ArchivePreviewer.SmallImageList = WolfUI.Form.cb_ShowPicturePreview.Checked ? Preview_Generator.Get_ImagePreviewFromArchiveData(Files, Archive) : Preview_Generator.Get_DefaultImageFromDll();

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
                    WolfUI.Form.lv_ArchivePreviewer.SmallImageList = Preview_Generator.Get_BriefcaseImageFromDll();

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
        
        public static uint GetFileLocation(string Archive, string Name)
        {
            using var Reader = new BinaryReader(File.Open($"{WolfUI.State.WorkingDirectory}\\{Archive}",
                FileMode.Open, FileAccess.Read, FileShare.ReadWrite));

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

                if (Path.GetFileNameWithoutExtension(FileName) == Name)
                {
                    Reader.BaseStream.Position = FileStart;
                    return FileStart;
                }
            }

            Reader.Close();

            return -0;
        }

        public static void CloseArchive()
        {
            WolfUI.Form.lv_ArchivePreviewer.SmallImageList = null;
            WolfUI.Form.lbl_Name.Text = "";
            WolfUI.Form.lbl_Size.Text = "";
            WolfUI.Form.lbl_ItemCount.Text = "";
            WolfUI.Form.LBL_GameStatusLabel.Text = @"Ready";

            WolfUI.Form.LBL_GameStatusLabel.ForeColor = Color.Green;

            WolfUI.Form.btn_CloseArchive.Enabled = false;

            WolfUI.State.HasArchiveOpen = false;

            WolfUI.Form.lv_ArchivePreviewer.Items.Clear();
            WolfUI.Form.lv_ArchivePreviewer.SmallImageList = Preview_Generator.Get_ArchiveImageFromDll();

            Load();
        }
    }
}