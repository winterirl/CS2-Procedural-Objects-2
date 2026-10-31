using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using PO2.Persistence.Format;
using UnityEngine;

internal static partial class LoadingTests
{
    private static uint ScalarCrc(byte[] bytes, int offset, int count)
    {
        uint crc = 0xffffffff;
        for (int i = offset; i < offset + count; i++)
        {
            crc ^= bytes[i];
            for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0);
        }
        return ~crc;
    }

    private static void EqualStream(EncodedStream a, EncodedStream b, string label)
    {
        Check(a.Codec == b.Codec && a.Semantic == b.Semantic && a.ElementCount == b.ElementCount &&
            a.DecodedByteLength == b.DecodedByteLength && a.Payload.ToArray().SequenceEqual(b.Payload.ToArray()), label);
    }

    private static void CodecCompatibility()
    {
        Check(Crc32.Compute(Encoding.ASCII.GetBytes("123456789")) == 0xcbf43926u, "known CRC32 vector");
        var random = new Random(391);
        var bytes = new byte[2048]; random.NextBytes(bytes);
        for (int offset = 0; offset < 32; offset++)
        for (int count = 0; count < 150; count++)
            Check(Crc32.Compute(bytes, offset, count) == ScalarCrc(bytes, offset, count), "CRC offset/tail");
        Check(Crc32.Compute(Array.Empty<byte>()) == 0, "empty CRC");
        Throws<ArgumentOutOfRangeException>(() => Crc32.Compute(bytes, 1, bytes.Length));

        foreach (int count in new[] { 0, 1, 21, 22, 1023, 1024, 1025, 1365, 1366, 4099 })
        foreach (bool patterned in new[] { false, true })
        {
            float Value(int i) => patterned ? (i % 7) * 0.1f : BitConverter.Int32BitsToSingle(random.Next(int.MinValue, int.MaxValue));
            var v3 = Enumerable.Range(0, count).Select(i => new Vector3(Value(i), Value(i + 1), Value(i + 2))).ToArray();
            var v4 = Enumerable.Range(0, count).Select(i => new Vector4(Value(i), Value(i + 1), Value(i + 2), Value(i + 3))).ToArray();
            foreach (bool shuffled in new[] { false, true })
            {
                var raw3 = FloatStreamPacking.Pack(v3, shuffled, false);
                var raw4 = FloatStreamPacking.Pack(v4, shuffled, false);
                Check(raw3.SequenceEqual(FloatStreamPacking.Pack(v3, shuffled)), "bulk/plane vector3 bits");
                Check(raw4.SequenceEqual(FloatStreamPacking.Pack(v4, shuffled)), "bulk/plane vector4 bits");
                var padded3 = new byte[raw3.Length + 19]; Array.Copy(raw3, 0, padded3, 7, raw3.Length);
                var padded4 = new byte[raw4.Length + 23]; Array.Copy(raw4, 0, padded4, 11, raw4.Length);
                foreach (bool bulk in new[] { false, true })
                {
                    var out3 = FloatStreamPacking.Read3(new ArraySegment<byte>(padded3, 7, raw3.Length), count, shuffled, bulk);
                    var out4 = FloatStreamPacking.Read4(new ArraySegment<byte>(padded4, 11, raw4.Length), count, shuffled, bulk);
                    Check(raw3.SequenceEqual(FloatStreamPacking.Pack(out3, shuffled, false)), "decoded vector3 bits");
                    Check(raw4.SequenceEqual(FloatStreamPacking.Pack(out4, shuffled, false)), "decoded vector4 bits");
                }
            }
            foreach (var mode in new[] { SaveCompression.Compact, SaveCompression.Fast })
            {
                var context = new EncodingContext(mode);
                EqualStream(ObjectBlockCodec.EncodeVector3(SnapshotStreamSemantic.Positions, v3, context),
                    ReferenceObjectBlockCodec.EncodeVector3(SnapshotStreamSemantic.Positions, v3, context), "reference vector3 codec");
                EqualStream(ObjectBlockCodec.EncodeVector4(SnapshotStreamSemantic.UV0, v4, context),
                    ReferenceObjectBlockCodec.EncodeVector4(SnapshotStreamSemantic.UV0, v4, context), "reference vector4 codec");
            }
        }
        for (int trial = 0; trial < 100; trial++)
        {
            int[] values = Enumerable.Range(0, trial * 21).Select(i => i % 17 < 8 ? 7 : random.Next()).ToArray();
            Check(SnapshotBinary.EncodeDeltaZigZagVarInt(values).SequenceEqual(ReferenceSnapshotBinary.EncodeDeltaZigZagVarInt(values)), "exact delta bytes");
            Check(SnapshotBinary.EncodeRleVarUInt(values).SequenceEqual(ReferenceSnapshotBinary.EncodeRleVarUInt(values)), "exact RLE bytes");
        }
        Throws<InvalidDataException>(() => SnapshotBinary.EncodeRleVarUInt(new[] { 0, -1 }));
        var uniform = Enumerable.Repeat(new Color32(1, 2, 3, 4), 4000).ToArray();
        EqualStream(ObjectBlockCodec.EncodeColors(uniform), ReferenceObjectBlockCodec.EncodeColors(uniform), "uniform color bytes");
        uniform[3999] = new Color32(9, 8, 7, 6);
        EqualStream(ObjectBlockCodec.EncodeColors(uniform), ReferenceObjectBlockCodec.EncodeColors(uniform), "nonuniform color bytes");

        if (Environment.GetEnvironmentVariable("PO2_CODEC_BENCHMARK") == "1")
        {
            int[] values = Enumerable.Range(0, 500000).Select(i => i % 100 < 30 ? 5 : i % 1000).ToArray();
            double Time(Func<int[], byte[]> encode)
            {
                encode(values); var timer = Stopwatch.StartNew();
                for (int i = 0; i < 15; i++) encode(values);
                return timer.Elapsed.TotalMilliseconds;
            }
            Console.WriteLine($"Integer comparison ms (old/new): delta {Time(ReferenceSnapshotBinary.EncodeDeltaZigZagVarInt):0.0}/{Time(SnapshotBinary.EncodeDeltaZigZagVarInt):0.0}; RLE {Time(ReferenceSnapshotBinary.EncodeRleVarUInt):0.0}/{Time(SnapshotBinary.EncodeRleVarUInt):0.0}");
        }
    }
}
