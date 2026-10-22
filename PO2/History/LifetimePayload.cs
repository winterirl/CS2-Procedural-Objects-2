using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using PO2.Core;
using PO2.Geometry;
using PO2.History.Actions;

namespace PO2.History
{
    internal sealed class LifetimePayload : IDisposable
    {
        private const int kVersion = 1;
        private const long kColdThreshold = 256 * 1024;
        private readonly HistoryIdentityManifest m_Manifest;
        private readonly bool m_IsPart;
        private ProceduralObject m_Object;
        private MeshPart m_Part;
        private HistoryResidentPayload m_Encoded;
        private byte[] m_Digest;
        internal long EstimatedBytes { get; }
        internal bool IsCold => m_Encoded != null && m_Object == null && m_Part == null;
        internal HistoryResidentPayload EncodedPayload => m_Encoded;
        internal long ManagedBytes => m_Object != null || m_Part != null ? EstimatedBytes : 128;
        internal long EncodedBytes => m_Encoded?.LongLength ?? 0;
        internal bool ShouldCool => EstimatedBytes > kColdThreshold;

        internal LifetimePayload(ProceduralObject obj)
        {
            m_Object = obj;
            m_Manifest = HistoryIdentityManifest.Capture(obj);
            EstimatedBytes = LifetimeModelCodec.Estimate(obj);
        }

        internal LifetimePayload(HistoryObjectKey owner, MeshPart part)
        {
            m_Part = part;
            m_IsPart = true;
            m_Manifest = new HistoryIdentityManifest(owner, new[] { part.PartHistoryId });
            EstimatedBytes = LifetimeModelCodec.Estimate(part);
        }

        internal LifetimePayload CopyForEncoding() => m_IsPart ? new LifetimePayload(m_Manifest.ObjectKey, m_Part) : new LifetimePayload(m_Object);

        internal async Task Prepare(CancellationToken token)
        {
            if (!IsCold) return;
            await m_Encoded.Prepare(token).ConfigureAwait(false);
            await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                var obj = Decode();
                token.ThrowIfCancellationRequested();
                if (m_IsPart) m_Part = obj.Parts[0];
                else m_Object = obj;
            }, token).ConfigureAwait(false);
        }

        internal void Cool()
        {
            if (IsCold) return;
            byte[] body = m_IsPart ? LifetimeModelCodec.EncodePart(m_Manifest.ObjectKey, m_Part) : LifetimeModelCodec.Encode(m_Object);
            byte[] encoded;
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
                {
                    writer.Write(kVersion);
                    writer.Write(m_IsPart);
                    m_Manifest.Write(writer);
                    int count = m_IsPart ? 0 : m_Object.CustomOrientations.Count;
                    writer.Write(count);
                    for (int i = 0; i < count; i++)
                    {
                        var value = m_Object.CustomOrientations[i];
                        writer.Write(value.Name);
                        writer.Write(value.LocalRotation.x);
                        writer.Write(value.LocalRotation.y);
                        writer.Write(value.LocalRotation.z);
                        writer.Write(value.LocalRotation.w);
                    }

                    writer.Write(body.Length);
                    writer.Write(body);
                }

                encoded = stream.ToArray();
            }

            byte[] digest;
            using (var sha = SHA256.Create()) digest = sha.ComputeHash(encoded);
            m_Encoded?.Dispose();
            m_Encoded = new HistoryResidentPayload(encoded, EstimatedBytes);
            m_Digest = digest;
            m_Object = null;
            m_Part = null;
        }

        private ProceduralObject Decode()
        {
            if (m_Encoded == null) throw new InvalidOperationException("Missing lifetime payload.");
            using (var sha = SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(m_Encoded.Bytes);
                for (int i = 0; i < digest.Length; i++)
                    if (digest[i] != m_Digest[i])
                        throw new InvalidDataException("Lifetime payload checksum failed.");
            }

            using (var stream = new MemoryStream(m_Encoded.Bytes, false))
            using (var reader = new BinaryReader(stream))
            {
                if (reader.ReadInt32() != kVersion || reader.ReadBoolean() != m_IsPart) throw new InvalidDataException("Lifetime payload type/version mismatch.");
                var manifest = HistoryIdentityManifest.Read(reader);
                if (!m_Manifest.Matches(manifest)) throw new InvalidDataException("Lifetime payload identity mismatch.");
                int count = reader.ReadInt32();
                if (count < 0 || count > ProceduralObject.kMaxCustomOrientationCount || (m_IsPart && count != 0)) throw new InvalidDataException("Invalid history orientation count.");
                var orientations = new CustomOrientation[count];
                for (int i = 0; i < count; i++) orientations[i] = CustomOrientation.RestoreHistory(reader.ReadString(), new Quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()));
                int size = reader.ReadInt32();
                if (size < 0 || size != stream.Length - stream.Position) throw new InvalidDataException("Invalid lifetime body size.");
                var obj = LifetimeModelCodec.Decode(reader.ReadBytes(size), manifest.ObjectKey.RuntimeId);
                if (!manifest.TryRestoreStaging(obj, out string reason)) throw new InvalidDataException(reason);
                obj.ApplyHistoryValue(ObjectValueKind.Orientations, new ObjectValueState { Orientations = orientations });
                return obj;
            }
        }

        internal ProceduralObject GetObject()
        {
            if (m_IsPart) throw new InvalidOperationException("Part payload requested as an object.");
            return m_Object ?? (m_Object = Decode());
        }

        internal MeshPart GetPart()
        {
            if (!m_IsPart) throw new InvalidOperationException("Object payload requested as a part.");
            return m_Part ?? (m_Part = Decode().Parts[0]);
        }

        public void Dispose()
        {
            m_Object = null;
            m_Part = null;
            m_Encoded?.Dispose();
            m_Encoded = null;
            m_Digest = null;
        }
    }
}