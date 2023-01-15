using System.Runtime.InteropServices;
using WolfX.Handlers;
using WolfX.Types;

namespace WolfX.Handler.Tools
{
    internal static class Preview_Generator
    {
        [DllImport("shell32.dll")]
        private static extern IntPtr ExtractIcon(IntPtr hInst, string lpszExeFileName, int nIconIndex);

        internal static ImageList Get_ArchiveImageFromDll()
        {
            var ImageList = new ImageList();
            var Shell = Icon.FromHandle(ExtractIcon(IntPtr.Zero, "C:\\Windows\\System32\\zipfldr.dll", 0));
            ImageList.Images.Add(Shell);
            ImageList.ImageSize = new Size(32, 32);
            return ImageList;
        }

        public static ImageList Get_BriefcaseImageFromDll()
        {
            var ImageList = new ImageList();
            var Shell = Icon.FromHandle(ExtractIcon(IntPtr.Zero, "C:\\Windows\\System32\\imageres.dll", 227));
            ImageList.Images.Add(Shell);
            ImageList.ImageSize = new Size(32, 32);
            return ImageList;
        }

        static readonly ImageConverter Converter = new();
        public static ImageList Get_ImagePreviewFromArchiveData(List<YGO_ArchiveFileEntry> Files, string Archive)
        {
            var ImageList = new ImageList();
            var ArchivePath = $"{WolfUI.State.WorkingDirectory}\\{Archive}";

            using var Reader = new BinaryReader(File.Open(ArchivePath, FileMode.Open, FileAccess.Read));

            foreach (var File in Files)
            {
                Reader.BaseStream.Position = File.Offset;
                ImageList.Images.Add(Converter.ConvertFrom(Reader.ReadBytes((int)File.Size)) as Bitmap);
            }
            ImageList.ImageSize = new Size(128, 128);
            ImageList.ColorDepth = ColorDepth.Depth32Bit;
            Reader.Close();
            Reader.Dispose();
            return ImageList;
        }

        public static byte[]? Get_ImageFromArchive(string Archive, string FileId)
        {
            var Image = Handlers.Files.Get_File(Archive, FileId);
            
            if (Image.Length == 0)
                return null;
            else
                return Image;
        }

        public static ImageList Get_DefaultImageFromDll()
        {
            var ImageList = new ImageList();
            var Shell = Icon.FromHandle(ExtractIcon(IntPtr.Zero, "C:\\Windows\\System32\\shell32.dll", 127));
            ImageList.Images.Add(Shell);
            ImageList.ImageSize = new Size(32, 32);
            return ImageList;
        }


        /// <summary>
        /// Get Card's Image From "2020.full.illust_j.jpg.zib", appends 'jpg' to Name
        /// </summary>
        /// <param name="Name">The CardID Of The Card</param>
        /// <param name="Censored">Load "2020.full.illust_a.jpg.zib" Instead.</param>
        /// <returns></returns>
        internal static Image Get_CardImageFromArchive(string Name, bool Censored = false)
        {
            if (Censored)
            {
                var Image = Get_ImageFromArchive("2020.full.illust_j.jpg.zib", $"{Name}.jpg");
                Image ??= Get_ImageFromArchive("2020.full.illust_a.jpg.zib", $"{Name}.jpg");

                return Extensions.Convert.ToImage(Image);
            }
            else
            {
                var Image = Get_ImageFromArchive("2020.full.illust_a.jpg.zib", $"{Name}.jpg");
                Image ??= Get_ImageFromArchive("2020.full.illust_j.jpg.zib", $"{Name}.jpg");

                return Extensions.Convert.ToImage(Image);
            }
        }
    }
}