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

        internal static string Get_UserSelectedZIBFile()
        {
            using (var OpenDialog = new OpenFileDialog())
            {
                OpenDialog.Title = "Open ZIB Archive";
                OpenDialog.Filter = "ZIB Archive (*.zib)|*.zib";
                if (OpenDialog.ShowDialog() != DialogResult.OK)
                {
                    MessageBox.Show("No ZIB Archive Selected", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);

                    return String.Empty;
                }

                return OpenDialog.FileName;

            }
        }
        internal static string Get_UserSelectedIndxFile()
        {
            using (var OpenFile = new OpenFileDialog())
            {
                OpenFile.Filter = $"{State.Language} Card Indx File|CarD_Indx_{State.Language.ToString()[0]}.bin|All Indx Files (*.bin)|*.bin";
                OpenFile.Title = "Open Cards Indx File";
                OpenFile.Multiselect = false;
                OpenFile.InitialDirectory = State.Path;
                var Res = OpenFile.ShowDialog();
                if (Res != DialogResult.OK || OpenFile.SafeFileName.StartsWith("CARD_Indx") != true)
                {
                    MessageBox.Show("Please Select a Cards Indx File", "No Cards Indx File Selected", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return String.Empty;
                }

                return OpenFile.FileName;
            }
        }
        internal static string Get_UserSelectedPDLimitsFile()
        {
            var OFD = new OpenFileDialog();
            OFD.Title = "Select PDLimits.bin";
            OFD.Filter = "pd_limits.bin|pd_limits.bin";
            if (OFD.ShowDialog() == DialogResult.OK)
            {
                return OFD.FileName;
            }
            else
            {
                MessageBox.Show("Please Select the PDLimits.bin", "Invalid File", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return String.Empty;
            }
        }
    }
}