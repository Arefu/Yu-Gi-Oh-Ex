using System.IO;
using System.Text;

namespace WolfX.Handlers
{
    internal static class Files
    {
        /// <summary>
        /// How Many Bytes To Read Per File
        /// </summary>
        private static readonly int FILE_INDEX_SIZE = 0x40;

        /// <summary>
        /// How Many Bytes Per Entry
        /// </summary>
        private static readonly int FILE_SIZE = 0x4;
        
        /// <summary>
        /// Get A File From Archive.
        /// </summary>
        /// <param name="Archive">ZIB File To Read From</param>
        /// <param name="Name">File To Get</param>
        /// <returns>Byte Array of File, or Empty Array If Not Found</returns>
        internal static byte[] Get_File(string Archive, string Name)
        {
            //Open Archive
            using var Reader = new BinaryReader(File.Open($"{WolfUI.State.WorkingDirectory}\\{Archive}", FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
            
            //Where Does The Contents End And Files Start
            var DataStart = Reader.BaseStream.Length;

            while (Reader.BaseStream.Position + FILE_INDEX_SIZE <= DataStart)
            {
                var FileOffset = (BitConverter.ToUInt32(Reader.ReadBytes(FILE_SIZE).Reverse().ToArray(), 0) / FILE_SIZE) * FILE_SIZE;

                var FileSize = BitConverter.ToUInt32(Reader.ReadBytes(FILE_SIZE).Reverse().ToArray(), 0);
                var FileName = Encoding.UTF8.GetString(Reader.ReadBytes(FILE_INDEX_SIZE - FILE_SIZE * 2)).TrimEnd('\0');
                 
                if (FileOffset < DataStart)
                    DataStart = FileOffset;

                if (FileName == Name)
                {
                    Reader.BaseStream.Position = FileOffset;
                    return Reader.ReadBytes((int)FileSize);
                }
            }

            Reader.Close();

            return Array.Empty<byte>();
        }
    }
}
