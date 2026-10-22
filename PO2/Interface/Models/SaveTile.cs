using System;
using System.Collections.Generic;
using System.Globalization;
using Colossal.UI.Binding;
using PO2.Persistence;
using PO2.Persistence.Model;

namespace PO2.Interface.Models
{
    internal sealed class SaveTile : IJsonWritable
    {
        private readonly string m_FileKey;
        private readonly string m_SessionGuid;
        private readonly int m_ObjectCount;
        private readonly long m_Bytes;
        private readonly string m_SavedAtUtc;
        private readonly bool m_DateEstimated;
        private readonly string m_CityName;
        private readonly string m_SaveName;
        private readonly string m_MapName;
        private readonly string m_SaveKind;
        private readonly string m_GameSaveStatus;
        private readonly SnapshotCityDate? m_CityDate;

        private SaveTile(SaveRecord save)
        {
            SnapshotMetadata metadata = save.DisplayMetadata;
            m_FileKey = save.FileKey;
            m_SessionGuid = save.SessionGuid.ToString("D");
            m_ObjectCount = save.ObjectCount;
            m_Bytes = save.FileBytes;
            m_SavedAtUtc = save.DisplayTimeUtc.ToString("O", CultureInfo.InvariantCulture);
            m_DateEstimated = save.DateEstimated;
            m_CityName = metadata.CityName;
            m_SaveName = metadata.SaveName;
            m_MapName = metadata.MapName;
            m_SaveKind = metadata.SaveKind.ToString();
            m_GameSaveStatus = save.GameSaveStatus.ToString();
            m_CityDate = metadata.CityDate;
        }

        internal static SaveTile[] Capture(IReadOnlyList<SaveRecord> saves)
        {
            if (saves.Count == 0) return Array.Empty<SaveTile>();
            var entries = new SaveTile[saves.Count];
            for (int i = 0; i < entries.Length; i++) entries[i] = new SaveTile(saves[i]);
            return entries;
        }

        public void Write(IJsonWriter writer)
        {
            writer.TypeBegin("PO2.SaveTile");
            writer.PropertyName("fileKey");
            writer.Write(m_FileKey);
            writer.PropertyName("sessionGuid");
            writer.Write(m_SessionGuid);
            writer.PropertyName("objectCount");
            writer.Write(m_ObjectCount);
            writer.PropertyName("bytes");
            writer.Write(m_Bytes);
            writer.PropertyName("savedAtUtc");
            writer.Write(m_SavedAtUtc);
            writer.PropertyName("dateEstimated");
            writer.Write(m_DateEstimated);
            writer.PropertyName("cityName");
            writer.Write(m_CityName);
            writer.PropertyName("saveName");
            writer.Write(m_SaveName);
            writer.PropertyName("mapName");
            writer.Write(m_MapName);
            writer.PropertyName("saveKind");
            writer.Write(m_SaveKind);
            writer.PropertyName("gameSaveStatus");
            writer.Write(m_GameSaveStatus);
            writer.PropertyName("cityDate");
            if (m_CityDate.HasValue)
            {
                SnapshotCityDate date = m_CityDate.Value;
                writer.TypeBegin("PO2.CityDate");
                writer.PropertyName("year");
                writer.Write(date.Year);
                writer.PropertyName("month");
                writer.Write(date.Month);
                writer.PropertyName("hour");
                writer.Write(date.Hour);
                writer.PropertyName("minute");
                writer.Write(date.Minute);
                writer.TypeEnd();
            }
            else writer.WriteNull();

            writer.TypeEnd();
        }
    }
}