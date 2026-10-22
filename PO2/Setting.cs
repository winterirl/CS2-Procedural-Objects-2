using Colossal.IO.AssetDatabase;
using Game.Modding;
using Game.Settings;
using PO2.Core.Lod;
using PO2.History;
using PO2.Persistence.Format;

namespace PO2
{
    [FileLocation(nameof(PO2))]
    [SettingsUIGroupOrder(kControlsGroup, kMainModeHotkeysGroup, kHistoryGroup, kLodGroup, kSavingGroup, "Modes", "Shared", "Select", "Create", "Object", "Edit", "Texturing", "AxesSnapping", "Mirror", "Deform", "Array", "Workflow")]
    [SettingsUIShowGroupName(kControlsGroup, kMainModeHotkeysGroup, kHistoryGroup, kLodGroup, kSavingGroup, "Modes", "Shared", "Select", "Create", "Object", "Edit", "Texturing", "AxesSnapping", "Mirror", "Deform", "Array", "Workflow")]
    public sealed partial class Setting : ModSetting
    {
        internal const float kMinimumLodDistanceMultiplier = LodTransitionSettings.kMinimumDistanceMultiplier;
        internal const float kMaximumLodDistanceMultiplier = LodTransitionSettings.kMaximumDistanceMultiplier;

        public const string kSection = "Main";
        public const string kLodGroup = "LevelOfDetail";
        public const string kControlsGroup = "Controls";
        public const string kMainModeHotkeysGroup = "MainModeHotkeys";
        public const string kHistoryGroup = "History";
        public const string kSavingGroup = "Saving";
        private int m_UndoHistorySteps = HistoryLimits.kDefaultCapacity;

        public Setting(IMod mod) : base(mod)
        {
        }

        [SettingsUISection(kSection, kHistoryGroup)]
        [SettingsUISlider(min = 1, max = 40, step = 1, updateOnDragEnd = true)]
        public int UndoHistorySteps
        {
            get => m_UndoHistorySteps;
            set => m_UndoHistorySteps = HistoryLimits.ClampCapacity(value);
        }

        [SettingsUISection(kSection, kLodGroup)]
        [SettingsUISlider(min = 50.0f, max = 200.0f, step = 5.0f, unit = "percentage", scalarMultiplier = 100.0f, updateOnDragEnd = true)]
        public float LOD1Distance { get; set; } = 1.0f;

        [SettingsUISection(kSection, kLodGroup)]
        [SettingsUISlider(min = 50.0f, max = 200.0f, step = 5.0f, unit = "percentage", scalarMultiplier = 100.0f, updateOnDragEnd = true)]
        public float LOD2Distance { get; set; } = 1.0f;

        [SettingsUISection(kSection, kSavingGroup)]
        public SaveCompression SaveCompression { get; set; } = SaveCompression.Compact;

        public override void SetDefaults()
        {
            SaveCompression = SaveCompression.Compact;
            UndoHistorySteps = HistoryLimits.kDefaultCapacity;
            LOD1Distance = 1.0f;
            LOD2Distance = 1.0f;
            ResetKeyBindings();
        }
    }
}