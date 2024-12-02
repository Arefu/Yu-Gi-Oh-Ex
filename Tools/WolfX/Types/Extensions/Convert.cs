using System.IO;

namespace WolfX.Extensions
{
    internal static partial class Convert
    {
        public static Image ToImage(byte[] Bytes)
        {
            return Image.FromStream(new MemoryStream(Bytes));
        }
    }
}
