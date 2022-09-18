using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WolfX.Extensions
{
    internal static class Extensions
    {
        public static string ReadNullTerminatedString(this BinaryReader reader, Encoding encoding)
        {
            var Builder = new StringBuilder();
            var Reader = new StreamReader(reader.BaseStream, encoding);

            var Start = reader.BaseStream.Position;

            int X;
            while ((X = Reader.Read()) != -1)
            {
                var C = (char)X;
                if (C == '\0')
                {
                    break;
                }
                Builder.Append(C);
            }

            var Res = Builder.ToString();

            reader.BaseStream.Position = Start + encoding.GetByteCount(Res + '\0');

            return Res;
        }
    }
}