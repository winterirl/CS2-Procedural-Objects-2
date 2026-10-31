using System.IO;

namespace PO2.Persistence
{
    // Keep the disk boundary small so transaction failures can be exercised
    // without changing the writer, decoder, or replacement policy in tests.
    internal class SnapshotFileIO
    {
        internal virtual Stream CreateTemporary(string path) => new FileStream(path, FileMode.CreateNew,
            FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.WriteThrough);
        internal virtual void FlushToDisk(Stream stream) => ((FileStream)stream).Flush(true);
        internal virtual Stream OpenVerification(string path) => new FileStream(path, FileMode.Open,
            FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
        internal virtual void Replace(string temporary, string final) => File.Replace(temporary, final, null);
        internal virtual void Move(string temporary, string final) => File.Move(temporary, final);
    }
}
