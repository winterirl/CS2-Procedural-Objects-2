using System;
using System.Collections.Generic;
using System.Linq;
using Colossal.Serialization.Entities;
using Game;
using PO2.Core;
using PO2.Geometry;
using PO2.History;
using PO2.Rendering;
using PO2.Rendering.Objects;
using PO2.Tools.ObjectTool;
using UnityEngine;

// The runtime owner, its snapshot publication, and the tool's world-change method
// are linked production code. These stand in for Unity and the surrounding systems.
namespace Colossal.Serialization.Entities
{
    public enum Purpose { NewGame, NewMap, LoadGame, LoadMap, Cleanup }
}
namespace Colossal.IO.AssetDatabase.VirtualTexturing
{
    public sealed class TextureStreamingSystem { }
}
namespace Game
{
    public enum GameMode { Other, Game, Editor, MainMenu }
    public sealed class TestWorld
    {
        private readonly Dictionary<Type, object> m_Systems = new Dictionary<Type, object>();
        public T GetOrCreateSystemManaged<T>() where T : new()
        {
            if (!m_Systems.TryGetValue(typeof(T), out var value))
                m_Systems.Add(typeof(T), value = new T());
            return (T)value;
        }
    }
    public abstract class GameSystemBase
    {
        public readonly TestWorld World = new TestWorld();
        protected virtual void OnCreate()
        {
            SceneFlow.GameManager.instance.onGamePreload += OnGamePreload;
            SceneFlow.GameManager.instance.onGameLoadingComplete += OnGameLoadingComplete;
        }
        protected virtual void OnDestroy()
        {
            SceneFlow.GameManager.instance.onGamePreload -= OnGamePreload;
            SceneFlow.GameManager.instance.onGameLoadingComplete -= OnGameLoadingComplete;
        }
        protected virtual void OnUpdate() { }
        protected virtual void OnGamePreload(Purpose purpose, GameMode mode) { }
        protected virtual void OnGameLoadingComplete(Purpose purpose, GameMode mode) { }
        internal void TestCreate() => OnCreate();
        internal void TestDestroy() => OnDestroy();
    }
}
namespace Game.SceneFlow
{
    public sealed class GameManager
    {
        public static GameManager instance = new GameManager();
        public GameMode gameMode = GameMode.Game;
        public event Action<Purpose, GameMode> onGamePreload;
        public event Action<Purpose, GameMode> onGameLoadingComplete;
        internal void BeginLoad(Purpose purpose, GameMode mode)
        {
            // The game assigns the destination mode before raising preload.
            gameMode = mode;
            onGamePreload?.Invoke(purpose, mode);
        }
        internal void CompleteLoad(Purpose purpose) => onGameLoadingComplete?.Invoke(purpose, gameMode);
    }
}
namespace Game.Tools
{
    public sealed class ToolSystem { public object activeTool; }
}
namespace UnityEngine
{
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => default;
        public static Vector3 Scale(Vector3 a, Vector3 b) => new Vector3(a.x * b.x, a.y * b.y, a.z * b.z);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
    }
    public struct Quaternion { public float x, y, z, w; }
    public struct Bounds
    {
        public Vector3 center, extents;
        public Bounds(Vector3 center, Vector3 size) { this.center = center; extents = size; }
        public void Encapsulate(Vector3 point) { }
    }
}
namespace PO2
{
    internal static class Mod
    {
        internal static string ModRootPath = "";
        internal static TestSettings Settings = new TestSettings();
        internal static readonly TestLog log = new TestLog();
    }
    internal sealed class TestSettings { internal int UndoHistorySteps = 20; }
    internal sealed class TestLog
    {
        internal void Info(string message) { }
        internal void Warn(string message) { }
        internal void Error(string message) => throw new Exception(message);
    }
}
namespace PO2.Geometry
{
    public enum GeometryChange { None }
    public sealed class MeshPart { public const int kDefaultSurfaceAreaId = 0; }
}
namespace PO2.Rendering
{
    public sealed class DeformationRendererWorkItem { }
    public sealed class DeformationRendererPreparation { }
}
namespace PO2.Rendering.Objects
{
    public sealed class ProceduralObjectRenderer : IDisposable
    {
        internal readonly List<ProceduralObject> Live = new List<ProceduralObject>();
        internal Action BeforeClear;
        internal int ClearCount;
        public ProceduralObjectRenderer(Colossal.IO.AssetDatabase.VirtualTexturing.TextureStreamingSystem streaming, string root) { }
        public void Clear() { BeforeClear?.Invoke(); Live.Clear(); ClearCount++; }
        public void Dispose() => Clear();
        public bool TryCaptureDeformationWork(ProceduralObject obj, int[] parts, out DeformationRendererWorkItem work) { work = null; return false; }
        public bool TryPublishDeformationPreparation(ProceduralObject obj, DeformationRendererPreparation preparation, out string failure) { failure = ""; return false; }
        public void NoticeGeometryChange(ProceduralObject obj, GeometryChange change) { }
        public bool HasLodMetricScaleChanged(int id, Vector3 scale) => false;
        public void InvalidateLod(int id) { }
        public void Sync(ProceduralObject obj) { }
        public void PollTopologyCleanup() { }
        public void RestoreObjects(List<ProceduralObject> objects, Action<ProceduralObject> commit, Action<ProceduralObject, Exception> failure)
        {
            foreach (var obj in objects) { Live.Add(obj); commit(obj); }
        }
    }
}
namespace PO2.History
{
    public static class HistoryLimits { public const int kDefaultCapacity = 20; }
    public sealed class ActionHistory : IDisposable
    {
        public int Count;
        public bool IsBusy;
        public void Clear() { Count = 0; IsBusy = false; }
        public void Dispose() => Clear();
        public void Poll(int capacity) { }
    }
}
namespace PO2.Tools.ObjectTool
{
    public partial class ProceduralObjectToolSystem
    {
        private bool m_ManagementWorkspaceActive;
        internal bool SelectionActive, TopologyPending, DeformationPending, ConversionPending, ArrayPending;
        internal int InvalidationCount;
        internal bool WorldStateEmpty => !SelectionActive && !TopologyPending && !DeformationPending && !ConversionPending && !ArrayPending && !m_ManagementWorkspaceActive;
        internal void TestSeed()
        {
            SelectionActive = TopologyPending = DeformationPending = ConversionPending = ArrayPending = m_ManagementWorkspaceActive = true;
        }
        public void PrepareRuntimeSelectionInvalidation() { SelectionActive = false; InvalidationCount++; }
        private void ResetTopologyEdit() { TopologyPending = false; }
        private void ResetCageDrag() { DeformationPending = false; }
        private void AbandonPendingAssetConversion() { ConversionPending = false; }
        public void CancelArray() { ArrayPending = false; }
        public void ProcessHistoryInput() { }
        public void ProcessClipboardInput() { }
        public void PollArrayPreview() { }
    }
}
namespace PO2.Core
{
    [Flags]
    public enum ProceduralObjectDirtyFlags { None = 0, Geometry = 1, Topology = 2, Material = 4, Transform = 8 }
    public sealed class TestTransform
    {
        public Vector3 Scale;
        public Vector3 TransformPoint(Vector3 point) => point;
    }
    public sealed class ProceduralObject
    {
        public int Id;
        public Guid PersistentId;
        public event Action NameChanged;
        public readonly TestTransform Transform = new TestTransform();
        public ProceduralObjectDirtyFlags DirtyFlags;
        public bool TryGetLocalBounds(out Bounds bounds) { bounds = default; return false; }
        public int ValidateLibraryReferences() => 0;
        internal void TestRename() => NameChanged?.Invoke();
    }
    public partial class ProceduralObjectSystem
    {
        private int m_NextId = 1;
        private int? m_SelectedObjectId;
        private int m_SelectedPartIndex, m_SelectedSurfaceAreaId;
        internal ActionHistory ActionHistory;
        internal bool HasClipboard, ClipboardBusy, TestLodPending, TestMirrorPending, TestArrayPending;
        internal ProceduralObjectRenderer TestRenderer => m_Renderer;
        internal ProceduralObjectToolSystem TestTool => m_ProceduralObjectToolSystem;
        internal bool TestSelectionEmpty => m_SelectedObjectId == null && m_SelectedPartIndex == 0 && m_SelectedSurfaceAreaId == MeshPart.kDefaultSurfaceAreaId;
        internal int TestNextId => m_NextId;
        internal void TestSeedState()
        {
            HasClipboard = ClipboardBusy = TestLodPending = TestMirrorPending = TestArrayPending = true;
            ActionHistory.Count = 5;
            ActionHistory.IsBusy = true;
            m_SelectedObjectId = 1;
            m_SelectedPartIndex = 7;
            m_SelectedSurfaceAreaId = 8;
            TestTool.TestSeed();
        }
        private void InitializeActionHistory() { ActionHistory = new ActionHistory(); }
        private void InitializeLodGeneration() { }
        private void ShutdownLodGeneration() { }
        private void ResetLodWorld() { TestLodPending = false; }
        private void UpdateLodGeneration() { }
        private void ObserveLodObject(ProceduralObject obj, ProceduralObjectDirtyFlags flags) { }
        private void ResetMirrorEdit() { TestMirrorPending = false; }
        private void ResetArrayEdit() { TestArrayPending = false; }
        private void PollArrayEdit() { }
        private void ClearClipboard() { HasClipboard = ClipboardBusy = false; }
        private void PollClipboardOperation() { }
    }
}
namespace PO2.Persistence.Model
{
    public sealed class PersistenceObjectData
    {
        public Guid PersistentId;
        public ProceduralObject Restore(int id) => new ProceduralObject { Id = id, PersistentId = PersistentId };
    }
    public static class SnapshotModelRestorer
    {
        public static List<ProceduralObject> Restore(PO2.Persistence.Format.OwnedSnapshotObjects objects) => objects.Items.Select((o, i) => o.Restore(i + 1)).ToList();
    }
}
namespace PO2.Persistence.Format
{
    public sealed class OwnedSnapshotObjects
    {
        public PO2.Persistence.Model.PersistenceObjectData[] Items;
    }
}
