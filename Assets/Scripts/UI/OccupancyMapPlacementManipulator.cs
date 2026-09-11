using System;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Reports pointer positions relative to an occupancy-map canvas.
/// Coordinate conversion remains the responsibility of the controller because it owns map bounds.
/// </summary>
public sealed class OccupancyMapPlacementManipulator : Manipulator
{
    private readonly Action<Vector2> _onPointerDown;
    private readonly Action<Vector2> _onPointerMove;
    private readonly Action _onPointerLeave;

    public OccupancyMapPlacementManipulator(
        Action<Vector2> onPointerDown,
        Action<Vector2> onPointerMove = null,
        Action onPointerLeave = null)
    {
        _onPointerDown = onPointerDown;
        _onPointerMove = onPointerMove;
        _onPointerLeave = onPointerLeave;
    }

    protected override void RegisterCallbacksOnTarget()
    {
        target.RegisterCallback<PointerDownEvent>(OnPointerDown);
        target.RegisterCallback<PointerMoveEvent>(OnPointerMove);
        target.RegisterCallback<PointerLeaveEvent>(OnPointerLeave);
    }

    protected override void UnregisterCallbacksFromTarget()
    {
        target.UnregisterCallback<PointerDownEvent>(OnPointerDown);
        target.UnregisterCallback<PointerMoveEvent>(OnPointerMove);
        target.UnregisterCallback<PointerLeaveEvent>(OnPointerLeave);
    }

    private void OnPointerDown(PointerDownEvent evt)
    {
        if (evt.button != (int)MouseButton.LeftMouse)
            return;

        _onPointerDown?.Invoke(target.WorldToLocal(evt.position));
        evt.StopPropagation();
    }

    private void OnPointerMove(PointerMoveEvent evt)
    {
        _onPointerMove?.Invoke(target.WorldToLocal(evt.position));
    }

    private void OnPointerLeave(PointerLeaveEvent evt)
    {
        _onPointerLeave?.Invoke();
    }
}
