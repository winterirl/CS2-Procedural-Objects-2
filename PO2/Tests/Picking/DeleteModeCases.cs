using System;
using System.Diagnostics;
using System.Threading;
using Game.Input;
using Game.Common;
using PO2.Tools.ObjectTool;
using UnityEngine;

internal static partial class Program {
    private static ProceduralObjectToolSystem DeleteTool(bool locked = false) {
        var tool = new ProceduralObjectToolSystem();
        tool.System.AddDeleteTarget(1, Square(2), WideBounds, locked);
        tool.System.AddDeleteTarget(2, Square(8), WideBounds);
        tool.NavigateToDeleteWorkspace();
        return tool;
    }

    private static void DeleteModeCases() {
        var tool = DeleteTool();
        for (int i = 0; i < 5 && tool.System.Hovered == null; i++) tool.DeleteAt(CenterRay, false);
        Check(tool.System.Hovered == 1 && tool.System.DeleteHovered, "delete highlights the closest PO");
        Check(tool.System.SelectedObjectId == null && tool.System.DeleteHistoryCount == 0, "hover neither selects nor deletes");
        tool.DeleteAt(CenterRay, true);
        Check(!tool.System.TryGetObject(1, out _) && tool.System.TryGetObject(2, out _), "click deletes only the closest PO");
        Check(tool.Mode == ObjectToolMode.Delete && tool.System.Hovered == null, "delete stays armed and clears removed hover");
        Check(tool.System.DeleteHistoryCount == 1 && tool.System.LastDelete.Deleted && tool.System.LastDelete.Object.Id == 1, "delete records the existing lifetime history action");
        tool.DeleteAt(CenterRay, false);
        Check(tool.System.TryGetObject(2, out _), "held button does not delete the object behind it");
        tool.DeleteAt(CenterRay, true);
        Check(!tool.System.TryGetObject(2, out _) && tool.System.DeleteHistoryCount == 2, "next click deletes the next PO");
        tool.DeleteAt(CenterRay, true);
        Check(tool.System.DeleteHistoryCount == 2, "empty-space click has no history entry");
        tool.NavigateToSelectWorkspace();
        Check(tool.Mode == ObjectToolMode.Select, "navigation exits delete mode");

        tool = DeleteTool(locked: true);
        for (int i = 0; i < 5; i++) tool.DeleteAt(CenterRay, true);
        Check(tool.System.Hovered == null && tool.System.TryGetObject(1, out _) && tool.System.TryGetObject(2, out _),
            "locked nearest PO is protected and does not allow deletion through it");
        Check(!tool.System.TryDeleteObject(1) && tool.System.DeleteHistoryCount == 0, "deletion itself also enforces locking");

        tool = DeleteTool();
        tool.System.RejectHistory = true;
        for (int i = 0; i < 5; i++) tool.DeleteAt(CenterRay, true);
        Check(tool.System.TryGetObject(1, out _) && tool.System.DeleteHistoryCount == 0, "failed history capture leaves the PO intact");
        tool.System.RejectHistory = false;
        tool.System.HistoryBusy = true;
        Check(!tool.System.TryDeleteObject(1), "history replay blocks deletion");
        tool.System.HistoryBusy = false;
        tool.MeshMutationBusy = true;
        Check(!tool.System.TryDeleteObject(1), "mesh work blocks deletion");
        tool.MeshMutationBusy = false;
        foreach (var mode in new[] { ObjectToolMode.MeshEdit, ObjectToolMode.Texturing, ObjectToolMode.CombineSource }) {
            tool.SetMode(mode);
            Check(!tool.System.TryDeleteObject(1), "editing mode blocks object deletion");
        }
        tool.SetMode(ObjectToolMode.Delete); tool.System.SelectedObjectId = 1;
        Check(tool.System.TryDeleteObject(1) && tool.Mode == ObjectToolMode.Delete && tool.System.SelectedObjectId == null,
            "selection restored by history does not disarm deletion");

        tool = DeleteTool();
        foreach (var flags in new[] { RaycastFlags.UIDisable, RaycastFlags.DebugDisable, RaycastFlags.ToolDisable, RaycastFlags.FreeCameraDisable }) {
            tool.DeleteFrame(true, flags);
            Check(!tool.DeleteCursorVisible && tool.System.DeleteHistoryCount == 0 && tool.System.Hovered == null,
                "blocked world input hides cursor and prevents deletion");
        }
        tool.Manage(true); tool.DeleteFrame(true);
        Check(!tool.DeleteCursorVisible && tool.System.DeleteHistoryCount == 0, "management panel blocks deletion");
        tool.Manage(false); InputManager.instance.hasInputFieldFocus = true; tool.DeleteFrame(true);
        Check(!tool.DeleteCursorVisible && tool.System.DeleteHistoryCount == 0, "text input blocks deletion");
        InputManager.instance.hasInputFieldFocus = false;
        CheckPendingDelete(false); CheckPendingDelete(true);
    }

    private static void CheckPendingDelete(bool cancel) {
        Grid(180, out var vertices, out var indices);
        var tool = new ProceduralObjectToolSystem();
        tool.System.AddDeleteTarget(1, Part(vertices, indices), WideBounds);
        tool.NavigateToDeleteWorkspace(); tool.DeleteAt(CenterRay, true);
        Check(tool.System.DeleteHistoryCount == 0, "cold delete waits for exact picking");
        if (cancel) tool.NavigateToSelectWorkspace();
        var wait = Stopwatch.StartNew();
        var moved = new Ray(new Vector3(300, 300, 0), CenterRay.direction);
        while (tool.System.DeleteHistoryCount == 0 && wait.Elapsed.TotalSeconds < 10) {
            // Warm the original query after cancellation to expose any stale queued click.
            tool.DeleteAt(cancel ? CenterRay : moved, false);
            if (cancel && tool.System.Hovered.HasValue) break;
            Thread.Sleep(1);
        }
        Check(tool.System.DeleteHistoryCount == (cancel ? 0 : 1),
            cancel ? "cancel discards a pending delete" : "pending delete uses the clicked ray despite pointer movement");
    }
}
