using System.Collections.Generic;
using Colossal.UI.Binding;
using PO2.Interface.Bindings;
using PO2.Localization;
using PO2.Persistence;

namespace PO2.Interface.Models
{
    internal sealed class SaveManagerPayload : IJsonWritable
    {
        internal static readonly SaveManagerPayload s_Empty = new SaveManagerPayload();
        private readonly bool m_Available;
        private readonly int m_Revision;
        private readonly SaveTile[] m_Saves;
        private readonly bool m_ScanSucceeded;
        private readonly bool m_CatalogComplete;
        private readonly string[] m_ProtectedKeys;
        private readonly LocalizedText m_Status;
        private readonly LocalizedText[] m_Issues;
        private readonly string m_ConfirmationToken;

        private SaveManagerPayload()
        {
        }

        internal SaveManagerPayload(SaveManager manager, IEnumerable<string> protectedKeys, LocalizedText status)
        {
            m_Available = true;
            m_Revision = manager.Revision;
            m_Saves = SaveTile.Capture(manager.Discovery.Saves);
            m_ScanSucceeded = manager.Discovery.ScanSucceeded;
            m_CatalogComplete = manager.CatalogComplete;
            m_ProtectedKeys = new List<string>(protectedKeys).ToArray();
            m_Status = status;
            m_Issues = new LocalizedText[manager.Discovery.RejectedFiles.Count];
            for (int i = 0; i < m_Issues.Length; i++)
            {
                SaveDiscoveryError issue = manager.Discovery.RejectedFiles[i];
                m_Issues[i] = issue.FileKey.Length > 0 ? LocalizedText.For("PO2.SaveManager.Scan.FileIssue", ("fileName", issue.FileKey + ".po2"), ("reason", issue.Message)) : issue.Message;
            }

            m_ConfirmationToken = manager.Pending?.Token;
        }

        public void Write(IJsonWriter writer)
        {
            if (!m_Available)
            {
                writer.WriteNull();
                return;
            }

            writer.TypeBegin("PO2.SaveManager");
            writer.PropertyName("revision");
            writer.Write(m_Revision);
            writer.PropertyName("saves");
            writer.Write<SaveTile>(m_Saves);
            writer.PropertyName("scanSucceeded");
            writer.Write(m_ScanSucceeded);
            writer.PropertyName("catalogComplete");
            writer.Write(m_CatalogComplete);
            writer.PropertyName("protectedKeys");
            WriteStrings(writer, m_ProtectedKeys);
            writer.PropertyName("status");
            LocalizedTextWriter.s_Instance.Write(writer, m_Status);
            writer.PropertyName("issues");
            writer.ArrayBegin((uint)m_Issues.Length);
            foreach (var issue in m_Issues) LocalizedTextWriter.s_Instance.Write(writer, issue);
            writer.ArrayEnd();
            writer.PropertyName("confirmation");
            if (m_ConfirmationToken == null) writer.WriteNull();
            else
            {
                writer.TypeBegin("PO2.SaveDeletionConfirmation");
                writer.PropertyName("token");
                writer.Write(m_ConfirmationToken);
                writer.TypeEnd();
            }

            writer.TypeEnd();
        }

        private static void WriteStrings(IJsonWriter writer, string[] values)
        {
            // Use the base writer so string arrays do not pull in the math writer overloads.
            writer.ArrayBegin((uint)values.Length);
            for (int i = 0; i < values.Length; i++) writer.Write(values[i]);
            writer.ArrayEnd();
        }
    }
}