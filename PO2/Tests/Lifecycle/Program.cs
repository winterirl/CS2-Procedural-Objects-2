using System;
using System.Linq;
using Colossal.Serialization.Entities;
using Game;
using Game.SceneFlow;
using PO2.Core;
using PO2.Persistence.Model;

internal static class Program
{
    private static int s_Checks;

    private static void Check(bool condition, string message)
    {
        s_Checks++;
        if (!condition) throw new Exception(message);
    }

    private static PersistenceObjectData[] City(int count) => Enumerable.Range(0, count)
        .Select(_ => new PersistenceObjectData { PersistentId = Guid.NewGuid() }).ToArray();

    private static void Empty(ProceduralObjectSystem runtime)
    {
        Check(runtime.ObjectCount == 0 && !runtime.Objects.Any(), "Previous-city POs survived the world transition.");
        Check(runtime.TestRenderer.Live.Count == 0, "Previous-city render instances survived.");
        Check(runtime.TestSelectionEmpty && runtime.TestNextId == 1, "Selection or runtime IDs survived.");
        Check(runtime.ActionHistory.Count == 0 && !runtime.ActionHistory.IsBusy, "Previous-city history survived.");
        Check(!runtime.HasClipboard && !runtime.ClipboardBusy, "Previous-city clipboard survived.");
        Check(!runtime.TestLodPending && !runtime.TestMirrorPending && !runtime.TestArrayPending, "Previous-city object work survived.");
        Check(runtime.TestTool.WorldStateEmpty, "Previous-city tool work survived.");
    }

    private static void Transition(Purpose purpose, GameMode destination)
    {
        GameManager.instance = new GameManager();
        var runtime = new ProceduralObjectSystem();
        runtime.TestCreate();
        try
        {
            var saved = City(3);
            runtime.RestoreSnapshot(saved);
            runtime.TestSeedState();
            var oldObject = runtime.Objects.First();
            int invalidations = runtime.TestTool.InvalidationCount;
            runtime.TestRenderer.BeforeClear = () =>
            {
                Check(runtime.TestTool.WorldStateEmpty, "Renderer disposal ran before tool cancellation.");
                Check(runtime.ObjectCount == 3, "Model was cleared before its renderer.");
            };

            // No main-menu frame or update occurs between these two Game modes.
            GameManager.instance.BeginLoad(purpose, destination);
            runtime.TestRenderer.BeforeClear = null;
            Empty(runtime);
            Check(runtime.TestTool.InvalidationCount == invalidations + 1, "Preload must invalidate old selections.");
            ulong listVersion = runtime.ObjectListVersion;
            oldObject.TestRename();
            Check(runtime.ObjectListVersion == listVersion, "Old object still notifies the current runtime.");
            GameManager.instance.CompleteLoad(purpose);
            runtime.UpdateRuntimeLifecycle();
            Empty(runtime);

            // The old saved data remains usable when returning to that city.
            GameManager.instance.BeginLoad(Purpose.LoadGame, GameMode.Game);
            runtime.RestoreSnapshot(saved);
            GameManager.instance.CompleteLoad(Purpose.LoadGame);
            runtime.UpdateRuntimeLifecycle();
            Check(runtime.Objects.Select(o => o.PersistentId).SequenceEqual(saved.Select(o => o.PersistentId)), "Returning to the original save lost its POs.");
            Check(runtime.TestRenderer.Live.Count == 3 && runtime.TestNextId == 4, "Loaded objects were cleared after restoration.");

            var other = City(2);
            GameManager.instance.BeginLoad(Purpose.LoadGame, GameMode.Game);
            runtime.RestoreSnapshot(other);
            GameManager.instance.CompleteLoad(Purpose.LoadGame);
            Check(runtime.Objects.Select(o => o.PersistentId).SequenceEqual(other.Select(o => o.PersistentId)), "Save-to-save transition mixed city objects.");
        }
        finally { runtime.TestRenderer.BeforeClear = null; runtime.TestDestroy(); }
    }

    private static void EmptyAndRepeatedTransitions()
    {
        GameManager.instance = new GameManager();
        var runtime = new ProceduralObjectSystem();
        runtime.TestCreate();
        try
        {
            // Even an empty world can have clipboard contents or detached tool work.
            runtime.TestSeedState();
            GameManager.instance.BeginLoad(Purpose.NewGame, GameMode.Game);
            Empty(runtime);
            GameManager.instance.BeginLoad(Purpose.NewGame, GameMode.Game);
            Empty(runtime);
            var saved = City(1);
            runtime.RestoreSnapshot(saved);
            runtime.UpdateRuntimeLifecycle();
            Check(runtime.ObjectCount == 1, "Ordinary Game updates must retain current objects.");
            GameManager.instance.gameMode = GameMode.MainMenu;
            runtime.UpdateRuntimeLifecycle();
            Check(runtime.ObjectCount == 0, "The existing menu-mode cleanup regressed.");
            GameManager.instance.BeginLoad(Purpose.NewGame, GameMode.Game);
            Empty(runtime);
        }
        finally { runtime.TestDestroy(); }
    }

    public static void Main()
    {
        Transition(Purpose.NewGame, GameMode.Game);
        Transition(Purpose.LoadGame, GameMode.Game);
        Transition(Purpose.Cleanup, GameMode.MainMenu);
        Transition(Purpose.NewMap, GameMode.Editor);
        Transition(Purpose.LoadMap, GameMode.Editor);
        EmptyAndRepeatedTransitions();
        Console.WriteLine($"PASS: {s_Checks} runtime lifecycle checks.");
    }
}
