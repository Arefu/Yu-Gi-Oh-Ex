namespace WolfX.Types
{
    internal class YGO_ArchiveFileEntry
    {
        public string Name { get; set; }
        public long Size { get; set; }
        public long Offset { get; set; }
        public byte[] Data { get; set; }

        public YGO_ArchiveFileEntry(uint Offset, uint Size, string Name, byte[] Data)
        {
            this.Offset = Offset;
            this.Size = Size;
            this.Name = Name;

            this.Data = Data;
        }
    }
}