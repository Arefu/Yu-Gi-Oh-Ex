using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
