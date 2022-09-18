using System;
using System.Text;

namespace WolfX.Types
{
    internal class Archive_File
    {
        public string Name { get; set; }
        public long Size { get; set; }
        public long Offset { get; set; }

        public Archive_File(uint Offset, uint Size, string Name)
        {
            this.Offset = Offset;
            this.Size = Size;
            this.Name = Name;
        }
    }
}