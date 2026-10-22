using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;

namespace PO2.History
{
    // Replay copies surviving records from the current state and inserts exact
    // literal bytes for changed records. No position-based identity guessing.
    internal sealed class ReversibleTopologyDelta
    {
        private readonly HistoryResidentPayload m_Undo, m_Redo;
        private readonly byte[] m_Before, m_After;
        private readonly byte[] m_UndoDigest, m_RedoDigest;
        private readonly long m_BeforeBytes, m_AfterBytes;
        internal bool UsesSnapshotFallback { get; }

        internal System.Collections.Generic.IEnumerable<HistoryResidentPayload> Payloads
        {
            get
            {
                yield return m_Undo;
                yield return m_Redo;
            }
        }

        internal HistoryFootprint Footprint => m_Undo.Footprint.Add(m_Redo.Footprint);

        internal void Dispose()
        {
            m_Undo.Dispose();
            m_Redo.Dispose();
        }

        internal long EncodedBytes => m_Undo.LongLength + m_Redo.LongLength;

        internal ReversibleTopologyDelta(TopologyState before, TopologyState after, int[] oldToNew)
        {
            int oldCount = before.Streams[0].Length / 12, newCount = after.Streams[0].Length / 12;
            if (oldToNew == null || oldToNew.Length != oldCount) throw new InvalidDataException("Missing editor vertex provenance.");
            var newToOld = new int[newCount];
            for (int i = 0; i < newCount; i++) newToOld[i] = -1;
            for (int i = 0; i < oldCount; i++)
            {
                int target = oldToNew[i];
                if (target < -1 || target >= newCount || (target >= 0 && newToOld[target] >= 0)) throw new InvalidDataException("Invalid editor vertex provenance.");
                if (target >= 0) newToOld[target] = i;
            }

            m_Before = before.Signature();
            m_After = after.Signature();
            m_Undo = new HistoryResidentPayload(EncodeBest(after, before, oldToNew, out bool undoSnapshot), before.Bytes);
            m_Redo = new HistoryResidentPayload(EncodeBest(before, after, newToOld, out bool redoSnapshot), after.Bytes);
            UsesSnapshotFallback = undoSnapshot || redoSnapshot;
            m_BeforeBytes = before.Bytes;
            m_AfterBytes = after.Bytes;
            using (var sha = SHA256.Create())
            {
                m_UndoDigest = sha.ComputeHash(m_Undo.Bytes);
                m_RedoDigest = sha.ComputeHash(m_Redo.Bytes);
            }
        }

        private static bool RecordEquals(byte[] source, int offset, byte[] target, int at, int width)
        {
            if (source == null || offset < 0 || offset > source.Length - width) return false;
            for (int i = 0; i < width; i++)
                if (source[offset + i] != target[at + i])
                    return false;
            return true;
        }

        private static byte[] EncodeBest(TopologyState source, TopologyState target, int[] map, out bool snapshot)
        {
            snapshot = false;
            var delta = Encode(source, target, map, false);
            // Only compare a whole affected-part literal when the patch is
            // already substantial. Local edits avoid a second compression pass.
            if (delta.LongLength < target.Bytes / 4) return delta;
            var literal = Encode(source, target, map, true);
            if (literal.Length >= delta.Length) return delta;
            snapshot = true;
            return literal;
        }

        private static byte[] Encode(TopologyState source, TopologyState target, int[] targetToSource, bool literalOnly)
        {
            using (var output = new MemoryStream())
            {
                using (var zip = new DeflateStream(output, System.IO.Compression.CompressionLevel.Fastest, true))
                using (var writer = new BinaryWriter(zip))
                {
                    for (int stream = 0; stream < TopologyState.s_Widths.Length; stream++)
                    {
                        byte[] from = source.Streams[stream], to = target.Streams[stream];
                        writer.Write(to == null ? -1 : to.Length);
                        if (to == null) continue;
                        int width = TopologyState.s_Widths[stream];
                        bool vertexStream = stream <= 5 || stream == 7 || stream == 11;
                        int cursor = 0;
                        while (cursor < to.Length)
                        {
                            int origin = vertexStream ? targetToSource[cursor / width] * width : cursor;
                            bool copy = !literalOnly && RecordEquals(from, origin, to, cursor, width);
                            int end = cursor + width;
                            while (end < to.Length)
                            {
                                int next = vertexStream ? targetToSource[end / width] * width : end;
                                bool nextCopy = !literalOnly && RecordEquals(from, next, to, end, width);
                                if (nextCopy != copy || (copy && next != origin + end - cursor)) break;
                                end += width;
                            }

                            writer.Write(copy);
                            writer.Write(end - cursor);
                            if (copy) writer.Write(origin);
                            else writer.Write(to, cursor, end - cursor);
                            cursor = end;
                        }
                    }
                }

                return output.ToArray();
            }
        }

        internal TopologyState Apply(TopologyState current, HistoryDirection direction)
        {
            bool undo = direction == HistoryDirection.Undo;
            if (!TopologyState.Equal(current.Signature(), undo ? m_After : m_Before)) throw new InvalidDataException("Topology history target no longer matches.");
            using (var sha = SHA256.Create())
            {
                if (!TopologyState.Equal(sha.ComputeHash((undo ? m_Undo : m_Redo).Bytes), undo ? m_UndoDigest : m_RedoDigest))
                    throw new InvalidDataException("Topology history program checksum failed.");
            }

            var streams = new byte[TopologyState.s_Widths.Length][];
            using (var input = new MemoryStream((undo ? m_Undo : m_Redo).Bytes, false))
            using (var zip = new DeflateStream(input, CompressionMode.Decompress))
            using (var reader = new BinaryReader(zip))
            {
                long total = 0;
                for (int stream = 0; stream < streams.Length; stream++)
                {
                    int length = reader.ReadInt32();
                    if (length == -1) continue;
                    if (length < 0 || length % TopologyState.s_Widths[stream] != 0 || (total += length) > (undo ? m_BeforeBytes : m_AfterBytes))
                        throw new InvalidDataException("Invalid topology history stream length.");
                    var target = streams[stream] = new byte[length];
                    int cursor = 0;
                    while (cursor < length)
                    {
                        bool copy = reader.ReadBoolean();
                        int count = reader.ReadInt32();
                        if (count <= 0 || count > length - cursor) throw new InvalidDataException("Invalid topology history run.");
                        if (copy)
                        {
                            int offset = reader.ReadInt32();
                            var source = current.Streams[stream];
                            if (source == null || offset < 0 || offset > source.Length - count) throw new InvalidDataException("Invalid topology history copy.");
                            Buffer.BlockCopy(source, offset, target, cursor, count);
                        }
                        else
                        {
                            int end = cursor + count;
                            for (int at = cursor; at < end;)
                            {
                                int read = reader.Read(target, at, end - at);
                                if (read == 0) throw new EndOfStreamException();
                                at += read;
                            }
                        }

                        cursor += count;
                    }
                }

                if (reader.BaseStream.ReadByte() != -1) throw new InvalidDataException("Trailing topology history data.");
            }

            var result = new TopologyState(streams);
            if (!TopologyState.Equal(result.Signature(), undo ? m_Before : m_After)) throw new InvalidDataException("Topology history result checksum failed.");
            return result;
        }
    }
}