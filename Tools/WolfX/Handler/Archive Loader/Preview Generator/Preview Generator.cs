using System;
using System.Collections.Generic;
using System.Configuration.Internal;
using System.DirectoryServices;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
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

        public static ImageList Get_ImagePreviewFromArchiveData(List<Archive_File> Files, string Archive)
        {
            var ImageList = new ImageList();
            var ArchivePath = $"{WolfX_UI_State.WorkingDirectory}\\{Archive}";

            using var Reader = new BinaryReader(File.Open(ArchivePath, FileMode.Open, FileAccess.Read));
            var Converter = new ImageConverter();
            foreach (var File in Files)
            {
                Reader.BaseStream.Position = File.Offset;
                ImageList.Images.Add((Bitmap)((Converter.ConvertFrom(Reader.ReadBytes((int)File.Size)))));
            }
            ImageList.ImageSize = new Size(128, 128);
            ImageList.ColorDepth = ColorDepth.Depth32Bit;
            return ImageList;
        }

        public static ImageList Get_DefaultImageFromDll()
        {
            var ImageList = new ImageList();
            var Shell = Icon.FromHandle(ExtractIcon(IntPtr.Zero, "C:\\Windows\\System32\\shell32.dll", 127));
            ImageList.Images.Add(Shell);
            ImageList.ImageSize = new Size(32, 32);
            return ImageList;
        }
    }
}