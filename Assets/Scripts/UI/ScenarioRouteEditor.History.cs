using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using RobotSNAP.Agents;
using RobotSNAP.Core.Scenario;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Undo and redo for the wizard: the snapshots it takes of the whole editor, the stack that
/// holds them, and the restore that puts an old state back.</summary>
public sealed partial class ScenarioRouteEditor
{

    private void ResetHistory()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        _lastEditKey = null;
        _lastEditTime = float.NegativeInfinity;
        EndPointDrag();
    }

    private EditorSnapshot PushUndo(string coalesceKey = null)
    {
        if (coalesceKey != null && coalesceKey == _lastEditKey &&
            Time.realtimeSinceStartup - _lastEditTime < FieldEditCoalesceSeconds)
        {
            _lastEditTime = Time.realtimeSinceStartup;
            return null;
        }

        EditorSnapshot snapshot = CaptureSnapshot();
        _lastEditKey = coalesceKey;
        _lastEditTime = Time.realtimeSinceStartup;
        _undoStack.Add(snapshot);
        if (_undoStack.Count > UndoLimit)
            _undoStack.RemoveAt(0);
        _redoStack.Clear();
        return snapshot;
    }

    private void Undo()
    {
        if (_undoStack.Count == 0)
        {
            _instructionLabel.text = "Nothing left to undo.";
            return;
        }

        EditorSnapshot snapshot = _undoStack[^1];
        _undoStack.RemoveAt(_undoStack.Count - 1);
        _redoStack.Add(CaptureSnapshot());
        RestoreSnapshot(snapshot);
        _instructionLabel.text = "Undone.";
    }

    private void Redo()
    {
        if (_redoStack.Count == 0)
        {
            _instructionLabel.text = "Nothing left to redo.";
            return;
        }

        EditorSnapshot snapshot = _redoStack[^1];
        _redoStack.RemoveAt(_redoStack.Count - 1);
        _undoStack.Add(CaptureSnapshot());
        RestoreSnapshot(snapshot);
        _instructionLabel.text = "Redone.";
    }

    private EditorSnapshot CaptureSnapshot()
    {
        return new EditorSnapshot
        {
            Routes = _routes.Select(CaptureRoute).ToList(),
            ActiveRouteIndex = _activeRouteIndex,
            PendingPointIndex = _pendingPointIndex
        };
    }

    private static RouteSnapshot CaptureRoute(RouteDraft route)
    {
        return new RouteSnapshot
        {
            Id = route.Id,
            IsRobot = route.IsRobot,
            RobotType = route.RobotType,
            StartYaw = route.StartYaw,
            Count = route.Count,
            Speed = route.Speed,
            EndBehavior = route.EndBehavior,
            Group = route.Group,
            MovementController = route.MovementController,
            Formation = route.Formation,
            GroupSpacing = route.GroupSpacing,
            FormationParameter = route.FormationParameter,
            SpawnWindow = route.SpawnWindow,
            CountRange = route.CountRange?.Clone(),
            SpeedRange = route.SpeedRange?.Clone(),
            StartYawRange = route.StartYawRange?.Clone(),
            SpacingRange = route.SpacingRange?.Clone(),
            SpawnWindowRange = route.SpawnWindowRange?.Clone(),
            Points = new List<Vector2>(route.Points),
            SpawnRandom = route.SpawnRandom,
            SpawnZone = route.SpawnZone,
            PointZones = new List<Rect?>(route.PointZones),
            Source = route.Source,
            RobotSource = route.RobotSource,
            RouteModified = route.RouteModified,
            HasNonSpatialGoal = route.HasNonSpatialGoal
        };
    }

    private static RouteDraft CreateDraftFromSnapshot(RouteSnapshot route)
    {
        var draft = new RouteDraft
        {
            Id = route.Id,
            IsRobot = route.IsRobot,
            RobotType = string.IsNullOrWhiteSpace(route.RobotType) ? RobotProfiles.DefaultId : route.RobotType,
            StartYaw = route.StartYaw,
            Count = route.Count,
            Speed = route.Speed,
            EndBehavior = route.EndBehavior,
            Group = route.Group,
            MovementController = route.MovementController,
            Formation = route.Formation,
            GroupSpacing = route.GroupSpacing,
            FormationParameter = route.FormationParameter,
            SpawnWindow = route.SpawnWindow,
            CountRange = route.CountRange?.Clone(),
            SpeedRange = route.SpeedRange?.Clone(),
            StartYawRange = route.StartYawRange?.Clone(),
            SpacingRange = route.SpacingRange?.Clone(),
            SpawnWindowRange = route.SpawnWindowRange?.Clone(),
            SpawnRandom = route.SpawnRandom,
            SpawnZone = route.SpawnZone,
            Source = route.Source,
            RobotSource = route.RobotSource,
            RouteModified = route.RouteModified,
            HasNonSpatialGoal = route.HasNonSpatialGoal
        };
        draft.Points.AddRange(route.Points);
        draft.PointZones.AddRange(route.PointZones ?? new List<Rect?>());
        NormalizeZones(draft);
        return draft;
    }

    private void RestoreSnapshot(EditorSnapshot snapshot)
    {
        if (snapshot == null)
            return;

        _routes.Clear();
        foreach (RouteSnapshot route in snapshot.Routes)
            _routes.Add(CreateDraftFromSnapshot(route));

        _activeRouteIndex = Mathf.Clamp(snapshot.ActiveRouteIndex, 0, Mathf.Max(0, _routes.Count - 1));
        _pendingPointIndex = Mathf.Max(0, snapshot.PendingPointIndex);
        _lastEditKey = null;
        EndPointDrag();
        RebuildRouteList();
        RefreshActiveRoute();
    }
}
