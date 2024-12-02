using System.Runtime.InteropServices;

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

    }
}