using System;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>Pointer position on the map canvas plus the modifier keys held at that moment.</summary>
public readonly struct MapPointerState
{
    public MapPointerState(Vector2 localPosition, bool control, bool shift, bool alt)
    {
        LocalPosition = localPosition;
        Control = control;
        Shift = shift;
        Alt = alt;
    }

    public Vector2 LocalPosition { get; }
    public bool Control { get; }
    public bool Shift { get; }
    public bool Alt { get; }
}

/// <summary>
/// Reports pointer positions relative to an occupancy-map canvas.
/// Coordinate conversion remains the responsibility of the controller because it owns map bounds.
/// </summary>
public sealed class OccupancyMapPlacementManipulator : Manipulator
{
    private readonly Action<MapPointerState> _onPointerDown;
    private readonly Action<MapPointerState> _onPointerMove;
    private readonly Action<MapPointerState> _onPointerUp;
    private readonly Action _onPointerLeave;

    public OccupancyMapPlacementManipulator(
        Action<MapPointerState> onPointerDown,
        Action<MapPointerState> onPointerMove = null,
        Action onPointerLeave = null,
        Action<MapPointerState> onPointerUp = null)
    {
        _onPointerDown = onPointerDown;
        _onPointerMove = onPointerMove;
        _onPointerLeave = onPointerLeave;
        _onPointerUp = onPointerUp;
    }

    protected override void RegisterCallbacksOnTarget()
    {
        target.RegisterCallback<PointerDownEvent>(OnPointerDown);
        target.RegisterCallback<PointerMoveEvent>(OnPointerMove);
        target.RegisterCallback<PointerUpEvent>(OnPointerUp);
        target.RegisterCallback<PointerLeaveEvent>(OnPointerLeave);
    }

    protected override void UnregisterCallbacksFromTarget()
    {
        target.UnregisterCallback<PointerDownEvent>(OnPointerDown);
        target.UnregisterCallback<PointerMoveEvent>(OnPointerMove);
        target.UnregisterCallback<PointerUpEvent>(OnPointerUp);
        target.UnregisterCallback<PointerLeaveEvent>(OnPointerLeave);
    }

    private void OnPointerDown(PointerDownEvent evt)
    {
        if (evt.button != (int)MouseButton.LeftMouse)
            return;

        // Capture the pointer so a drag keeps reporting moves outside the canvas.
        target.CapturePointer(evt.pointerId);
        _onPointerDown?.Invoke(ToState(evt));
        evt.StopPropagation();
    }

    private void OnPointerMove(PointerMoveEvent evt)
    {
        _onPointerMove?.Invoke(ToState(evt));
    }

    private void OnPointerUp(PointerUpEvent evt)
    {
        if (evt.button != (int)MouseButton.LeftMouse)
            return;

        if (target.HasPointerCapture(evt.pointerId))
            target.ReleasePointer(evt.pointerId);
        _onPointerUp?.Invoke(ToState(evt));
        evt.StopPropagation();
    }

    private void OnPointerLeave(PointerLeaveEvent evt)
    {
        if (target.HasPointerCapture(evt.pointerId))
            return;
        _onPointerLeave?.Invoke();
    }

    private MapPointerState ToState(IPointerEvent evt) =>
        new MapPointerState(target.WorldToLocal(evt.position), evt.ctrlKey, evt.shiftKey, evt.altKey);
}
