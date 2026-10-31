using System;
using System.IO;
using System.Linq;
using System.Threading;
using PO2.Persistence;
using PO2.Persistence.Format;
using PO2.Persistence.Model;

internal static partial class LoadingTests
{
    private static void FileStoreChecks()
    {
        string root = Path.Combine(Path.GetTempPath(), "po2-pass2-" + Guid.NewGuid().ToString("N"));
        const string key = "00112233445566778899aabbccddeeff00";
        var metadata = new SnapshotMetadata("city", "save", "map", null, SnapshotSaveKind.Manual, null);
        var objects = Enumerable.Range(1, 6).Select(i => StableFixture(i, SnapshotFormat.kObjectSchemaVersion, true)).ToArray();
        var options = new SnapshotEncodingOptions(SaveCompression.Fast, 16);
        var ids = objects.Select(o => o.PersistentId).ToArray();
        try
        {
            var store = new SnapshotFileStore(root);
            var result = store.WriteTransactional(key, FixtureSave, FixtureSession, metadata, objects, options);
            var previous = File.ReadAllBytes(result.FinalPath);
            Check(previous.SequenceEqual(SnapshotWriter.Write(FixtureSave, FixtureSession, metadata, objects, options)), "disk and memory writer bytes");
            Check(!File.Exists(result.FinalPath + ".tmp"), "successful temp finalized");
            Check(store.ReadExact(key, FixtureSave, FixtureSession).Result.ObjectCount == objects.Length, "store round trip");
            store.WriteTransactional(key, FixtureSave, FixtureSession, metadata, objects, options);
            Check(previous.SequenceEqual(File.ReadAllBytes(result.FinalPath)), "replace existing snapshot");

            foreach (string stage in new[] { "write", "flush", "durable", "reopen", "read", "verify", "replace" })
            {
                var io = new FailingFileIO(stage);
                var failing = new SnapshotFileStore(root, io);
                if (stage == "verify") Throws<InvalidDataException>(() => failing.WriteTransactional(key, FixtureSave, FixtureSession, metadata, objects, options));
                else Throws<IOException>(() => failing.WriteTransactional(key, FixtureSave, FixtureSession, metadata, objects, options));
                Check(io.Triggered, "injected failure reached " + stage);
                Check(previous.SequenceEqual(File.ReadAllBytes(result.FinalPath)), "old final preserved: " + stage);
            }
            var bad = StableFixture(10, SnapshotFormat.kObjectSchemaVersion, true);
            bad.Parts[0].Triangles[0] = int.MaxValue;
            Throws<InvalidDataException>(() => store.WriteTransactional(key, FixtureSave, FixtureSession, metadata, new[] { bad }, options));
            Check(previous.SequenceEqual(File.ReadAllBytes(result.FinalPath)), "encoding failure preserves final");
            var firstFail = new SnapshotFileStore(root, new FailingFileIO("replace"));
            Throws<IOException>(() => firstFail.WriteTransactional("first", FixtureSave, FixtureSession, metadata, objects, options));
            Check(!File.Exists(Path.Combine(root, "first.po2")), "failed initial save unpublished");

            using (var input = new SmallReadStream(previous))
            {
                var summary = SnapshotReader.Verify(input, FixtureSave, FixtureSession, metadata, ids);
                Check(summary.ObjectCount == objects.Length && summary.DecodedBytes > 0, "streaming verification summary");
            }
            var wrongIds = (Guid[])ids.Clone(); wrongIds[1] = Guid.NewGuid();
            Throws<InvalidDataException>(() => SnapshotReader.Verify(new MemoryStream(previous), FixtureSave, FixtureSession, metadata, wrongIds));
            Throws<InvalidDataException>(() => SnapshotReader.Verify(new MemoryStream(previous), Guid.NewGuid(), FixtureSession, metadata, ids));
            Throws<InvalidDataException>(() => SnapshotReader.Verify(new MemoryStream(previous), FixtureSave, FixtureSession,
                new SnapshotMetadata("wrong", "save", "map", null, SnapshotSaveKind.Manual, null), ids));
            Throws<InvalidDataException>(() => SnapshotReader.Verify(new MemoryStream(previous), FixtureSave, FixtureSession, metadata, ids.Take(1).ToArray()));
            var corrupt = (byte[])previous.Clone(); corrupt[corrupt.Length - 1] ^= 1;
            Check(SnapshotReader.Read(new MemoryStream(corrupt), true).SkippedObjects.Count == 1, "normal load contains object damage");
            Throws<InvalidDataException>(() => SnapshotReader.Verify(new MemoryStream(corrupt), FixtureSave, FixtureSession, metadata, ids));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private sealed class SmallReadStream : MemoryStream
    {
        private readonly int m_Thread = Thread.CurrentThread.ManagedThreadId;
        internal SmallReadStream(byte[] bytes) : base(bytes, false) { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (Thread.CurrentThread.ManagedThreadId != m_Thread) throw new Exception("worker touched input stream");
            return base.Read(buffer, offset, Math.Min(count, 13));
        }
    }

    private sealed class FailingFileIO : SnapshotFileIO
    {
        private readonly string m_Stage;
        internal bool Triggered;
        internal FailingFileIO(string stage) { m_Stage = stage; }
        private void Fail() { Triggered = true; throw new IOException("injected " + m_Stage); }
        internal override Stream CreateTemporary(string path)
        {
            var file = base.CreateTemporary(path);
            return m_Stage == "write" || m_Stage == "flush" ? new FailureStream(file, m_Stage, Fail) : file;
        }
        internal override void FlushToDisk(Stream stream)
        {
            if (m_Stage == "durable") Fail();
            base.FlushToDisk(stream is FailureStream wrapped ? wrapped.Inner : stream);
        }
        internal override Stream OpenVerification(string path)
        {
            if (m_Stage == "reopen") Fail();
            if (m_Stage == "verify")
            {
                Triggered = true;
                using (var edit = new FileStream(path, FileMode.Open, FileAccess.ReadWrite))
                {
                    edit.Position = edit.Length - 1; int b = edit.ReadByte(); edit.Position--; edit.WriteByte((byte)(b ^ 1));
                }
            }
            var file = base.OpenVerification(path);
            return m_Stage == "read" ? new FailureStream(file, m_Stage, Fail) : file;
        }
        internal override void Replace(string temporary, string final) { if (m_Stage == "replace") Fail(); base.Replace(temporary, final); }
        internal override void Move(string temporary, string final) { if (m_Stage == "replace") Fail(); base.Move(temporary, final); }
    }

    private sealed class FailureStream : Stream
    {
        internal Stream Inner { get; }
        private readonly string m_Stage;
        private readonly Action m_Fail;
        internal FailureStream(Stream inner, string stage, Action fail) { Inner = inner; m_Stage = stage; m_Fail = fail; }
        public override bool CanRead => Inner.CanRead;
        public override bool CanSeek => Inner.CanSeek;
        public override bool CanWrite => Inner.CanWrite;
        public override long Length => Inner.Length;
        public override long Position { get => Inner.Position; set => Inner.Position = value; }
        public override long Seek(long offset, SeekOrigin origin) => Inner.Seek(offset, origin);
        public override void SetLength(long length) => Inner.SetLength(length);
        public override void Flush() { if (m_Stage == "flush") m_Fail(); Inner.Flush(); }
        public override int Read(byte[] buffer, int offset, int count) { if (m_Stage == "read") m_Fail(); return Inner.Read(buffer, offset, count); }
        public override void Write(byte[] buffer, int offset, int count)
        {
            if (m_Stage == "write") { Inner.Write(buffer, offset, count / 2); m_Fail(); }
            Inner.Write(buffer, offset, count);
        }
        protected override void Dispose(bool disposing) { if (disposing) Inner.Dispose(); base.Dispose(disposing); }
    }
}
