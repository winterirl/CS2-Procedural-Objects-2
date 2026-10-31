using System;
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
        private readonly SaveManagerPage m_Page;
        private readonly int m_IssueCount;

        private SaveManagerPayload()
        {
        }

        internal SaveManagerPayload(SaveManager manager, SaveManagerPage page, IEnumerable<string> protectedKeys, LocalizedText status)
        {
            m_Available = true;
            m_Revision = manager.Revision;
            m_Page = page;
            m_IssueCount = manager.Discovery.RejectedFileCount;
            m_Saves = SaveTile.Capture(page.Saves);
            m_ScanSucceeded = manager.Discovery.ScanSucceeded;
            m_CatalogComplete = manager.CatalogComplete;
            var protectedSet = new HashSet<string>(protectedKeys, StringComparer.OrdinalIgnoreCase);
            var visibleProtected = new List<string>();
            foreach (SaveRecord save in page.Saves)
                if (protectedSet.Contains(save.FileKey))
                    visibleProtected.Add(save.FileKey);
            m_ProtectedKeys = visibleProtected.ToArray();
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
            writer.PropertyName("page");
            writer.Write(m_Page.Index);
            writer.PropertyName("pageCount");
            writer.Write(Math.Max(1, (m_Page.MatchCount + SaveManagerPage.Size - 1) / SaveManagerPage.Size));
            writer.PropertyName("matchCount");
            writer.Write(m_Page.MatchCount);
            writer.PropertyName("totalCount");
            writer.Write(m_Page.TotalCount);
            writer.PropertyName("totalBytes");
            writer.Write(m_Page.TotalBytes);
            writer.PropertyName("pageBytes");
            writer.Write(m_Page.PageBytes);
            writer.PropertyName("session");
            writer.Write(m_Page.Session);
            writer.PropertyName("filter");
            writer.Write(m_Page.Status);
            writer.PropertyName("oldest");
            writer.Write(m_Page.Oldest);
            writer.PropertyName("issueCount");
            writer.Write(m_IssueCount);
            writer.PropertyName("lineages");
            writer.ArrayBegin((uint)m_Page.Lineages.Length);
            foreach (SaveLineage group in m_Page.Lineages)
            {
                writer.TypeBegin("PO2.SaveLineage");
                writer.PropertyName("id");
                writer.Write(group.Id.ToString("D"));
                writer.PropertyName("name");
                writer.Write(group.Name ?? "");
                writer.PropertyName("count");
                writer.Write(group.Count);
                writer.TypeEnd();
            }

            writer.ArrayEnd();
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