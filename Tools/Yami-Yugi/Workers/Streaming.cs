using System.Buffers;

namespace Yami_Yugi.Workers
{
    /// <summary>
    /// Copies between streams through one fixed buffer, so memory stays the same whatever the size of a file or of the whole archive (a
    /// heavily modded YGO_2020.dat is several GB; reading files whole ran Windows out of memory).
    /// </summary>
    internal static class Streaming
    {
        /// <summary>The copy buffer (1 MB): the most Yami-Yugi holds of any file at once.</summary>
        internal const int BufferSize = 1 << 20;

        /// <summary>Copies exactly <paramref name="count"/> bytes; throws when the source ends first.</summary>
        internal static void CopyExactly(Stream from, Stream to, long count)
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
            try
            {
                while (count > 0)
                {
                    int read = from.Read(buffer, 0, (int)Math.Min(buffer.Length, count));
                    if (read <= 0)
                        throw new EndOfStreamException($"The archive ended {count} bytes early.");
                    to.Write(buffer, 0, read);
                    count -= read;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        /// <summary>Files are 4-byte aligned in the archive.</summary>
        internal static long Padded(long size) => (size + 3) & ~3L;
    }
}
