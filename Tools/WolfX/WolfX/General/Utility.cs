using PDLimits;
using System.Runtime.InteropServices;
using System.Text;

namespace WolfX
{
    internal static class PInvoke
    {
        [DllImport("kernel32.dll")]
        internal static extern int GetPrivateProfileString(string Section, string Key, string Default, StringBuilder RetVal, int Size, string FilePath);
        [DllImport("kernel32.dll")]
        internal static extern uint GetPrivateProfileInt(string lpAppName, string lpKeyName, int nDefault, string lpFileName);
        [DllImport("kernel32.dll")]
        internal static extern bool WritePrivateProfileString(string lpAppName, string lpKeyName, string lpString, string lpFileName);
    }

    internal static class Utility
    {
        private static readonly string[] suffixes = { "B", "KB", "MB", "GB", "TB", "PB" };
        internal static string FormatSize(Int64 bytes)
        {
            int counter = 0;
            decimal number = (decimal)bytes;
            while (Math.Round(number / 1024) >= 1)
            {
                number = number / 1024;
                counter++;
            }
            return string.Format("{0:n1}{1}", number, suffixes[counter]);
        }

        internal static string Get_UserSelectedFile(string Title, string Filter)
        {
            using (var OpenFile = new OpenFileDialog())
            {
                OpenFile.Filter = Filter;
                OpenFile.Title = Title;
                if (OpenFile.ShowDialog() == DialogResult.OK)
                {
                    return OpenFile.FileName;
                }
                else
                {
                    MessageBox.Show("Please Select the File", "Invalid File", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return "-1";
                }
            }
        }
    }
}