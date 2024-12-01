using System.IO;
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
                WolfUI.Form.ARCHIVE_LV_ArchiveItems.Items.Add(new FileInfo(Archive).Name, 0);
            }
        }
        
        internal static void OpenArchive(string Archive)
        {
        
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
            WolfUI.Form.ARCHIVE_LV_ArchiveItems.SmallImageList = null;
            WolfUI.Form.ARCHIVE_LBL_ArchiveName.Text = "";
            WolfUI.Form.ARCHIVE_LBL_ArchiveSize.Text = "";
            WolfUI.Form.ARCHIVE_LBL_ArchiveItems.Text = "";
            WolfUI.Form.LBL_GameStatusLabel.Text = @"Ready";

            WolfUI.Form.LBL_GameStatusLabel.ForeColor = Color.Green;

            WolfUI.Form.ARCHIVE_BTN_SaveZIB.Enabled = false;

            WolfUI.State.HasArchiveOpen = false;

            WolfUI.Form.ARCHIVE_LV_ArchiveItems.Items.Clear();
            WolfUI.Form.ARCHIVE_LV_ArchiveItems.SmallImageList = Preview_Generator.Get_ArchiveImageFromDll();

            Load();
        }
    }
}