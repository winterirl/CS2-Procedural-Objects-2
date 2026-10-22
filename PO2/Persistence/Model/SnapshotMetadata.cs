using System;
using System.Collections.Generic;
using System.Text;
using PO2.Persistence.Format;

namespace PO2.Persistence.Model
{
    internal enum SnapshotSaveKind : byte
    {
        Unknown = 0,
        Manual = 1,
        AutoSave = 2,
    }

    internal enum SnapshotMetadataStatus
    {
        Missing,
        Available,
        Invalid,
        Unsupported,
    }

    internal readonly struct SnapshotCityDate : IEquatable<SnapshotCityDate>
    {
        internal readonly int Year;

        // SaveInfo calls this "month", but CS2 writes DayOfYear - 1 here.
        internal readonly int Month;
        internal readonly int Hour;
        internal readonly int Minute;

        internal SnapshotCityDate(int year, int month, int hour, int minute)
        {
            Year = year;
            Month = month;
            Hour = hour;
            Minute = minute;
        }

        internal bool IsValid => Year >= 1 && Year <= 9999 && Month >= 0 && Month <= 365 && Hour >= 0 && Hour <= 23 && Minute >= 0 && Minute <= 59;

        public bool Equals(SnapshotCityDate other) =>
            Year == other.Year && Month == other.Month && Hour == other.Hour && Minute == other.Minute;

        public override bool Equals(object obj) =>
            obj is SnapshotCityDate other && Equals(other);

        public override int GetHashCode() =>
            ((Year * 397 ^ Month) * 397 ^ Hour) * 397 ^ Minute;
    }

    // Copied at the successful-save event, before a write can be deferred.
    // No game/asset references or localized display strings belong here.
    internal sealed class SnapshotMetadata : IEquatable<SnapshotMetadata>
    {
        internal static readonly UTF8Encoding s_Utf8 = new UTF8Encoding(false, true);

        internal string CityName { get; }
        internal string SaveName { get; }
        internal string MapName { get; }
        internal DateTime? SavedAtUtc { get; }
        internal SnapshotSaveKind SaveKind { get; }
        internal SnapshotCityDate? CityDate { get; }

        internal SnapshotMetadata(string cityName, string saveName, string mapName, DateTime? savedAtUtc, SnapshotSaveKind saveKind, SnapshotCityDate? cityDate)
        {
            CityName = cityName ?? string.Empty;
            SaveName = saveName ?? string.Empty;
            MapName = mapName ?? string.Empty;
            SavedAtUtc = savedAtUtc;
            SaveKind = saveKind;
            CityDate = cityDate;
        }

        internal static SnapshotMetadata Capture(string cityName, string saveName, string mapName, DateTime savedAt, bool autoSave, SnapshotCityDate cityDate, out string warning)
        {
            var omitted = new List<string>();
            DateTime? savedAtUtc = null;
            if (savedAt != DateTime.MinValue)
            {
                try
                {
                    // CS2 supplies local wall time, including on metadata reload.
                    savedAtUtc = savedAt.Kind == DateTimeKind.Utc ? savedAt : savedAt.ToUniversalTime();
                }
                catch (ArgumentException)
                {
                    omitted.Add("save timestamp");
                }
            }

            SnapshotCityDate? capturedDate = cityDate.IsValid ? cityDate : (SnapshotCityDate?)null;
            if (!capturedDate.HasValue) omitted.Add("city date");
            var result = new SnapshotMetadata(CaptureLabel(cityName, "city name", omitted), CaptureLabel(saveName, "save name", omitted), CaptureLabel(mapName, "map name", omitted), savedAtUtc, autoSave ? SnapshotSaveKind.AutoSave : SnapshotSaveKind.Manual, capturedDate);
            warning = omitted.Count == 0 ? string.Empty : "Unavailable save metadata: " + string.Join(", ", omitted) + ".";
            return result;
        }

        private static string CaptureLabel(string value, string field, List<string> omitted)
        {
            if (value == null) return string.Empty;
            try
            {
                if (s_Utf8.GetByteCount(value) <= SnapshotFormat.kMaxMetadataStringBytes)
                {
                    return value;
                }
            }
            catch (EncoderFallbackException)
            {
            }

            // A broken label must not prevent the POs from being saved.
            omitted.Add(field);
            return string.Empty;
        }

        public bool Equals(SnapshotMetadata other) => other != null && CityName == other.CityName && SaveName == other.SaveName && MapName == other.MapName && SavedAtUtc == other.SavedAtUtc && SaveKind == other.SaveKind && Nullable.Equals(CityDate, other.CityDate);

        public override bool Equals(object obj) => Equals(obj as SnapshotMetadata);

        public override int GetHashCode() =>
            CityName.GetHashCode() ^ SaveName.GetHashCode() ^ MapName.GetHashCode() ^ SavedAtUtc.GetHashCode() ^ SaveKind.GetHashCode() ^ CityDate.GetHashCode();
    }
}