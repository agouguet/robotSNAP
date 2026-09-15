using System.Collections.Generic;
using RobotSNAP.CameraControl;
using UnityEngine.UIElements;

/// <summary>
/// The rail of tools in the top-left corner of the view. It decides what the left mouse button
/// does in the camera view — pick an agent, slide, turn or zoom — and offers a shortcut to
/// re-frame the view on the agent the camera already works with.
/// </summary>
public sealed class SimulationViewToolbar
{
    private readonly CameraController _camera;
    private readonly Dictionary<CameraController.CameraTool, Button> _buttons = new();
    private readonly List<SimulationToolIcon> _icons = new();
    private readonly Button _centerButton;

    public SimulationViewToolbar(VisualElement root, CameraController camera)
    {
        _camera = camera;

        _buttons[CameraController.CameraTool.Select] = root.Q<Button>("ToolSelectButton");
        _buttons[CameraController.CameraTool.Move] = root.Q<Button>("ToolMoveButton");
        _buttons[CameraController.CameraTool.Rotate] = root.Q<Button>("ToolRotateButton");
        _buttons[CameraController.CameraTool.Zoom] = root.Q<Button>("ToolZoomButton");
        _centerButton = root.Q<Button>("CenterToolButton");

        if (_camera == null)
        {
            // Without a camera the rail would lie about the active tool, so it stays inert.
            SetVisible(false);
            return;
        }

        foreach (KeyValuePair<CameraController.CameraTool, Button> pair in _buttons)
        {
            if (pair.Value == null) continue;

            CameraController.CameraTool tool = pair.Key;
            pair.Value.clicked += () => _camera.SetTool(tool);
        }

        Attach(_buttons[CameraController.CameraTool.Select], SimulationToolIcon.Shape.Pointer);
        Attach(_buttons[CameraController.CameraTool.Move], SimulationToolIcon.Shape.Move);
        Attach(_buttons[CameraController.CameraTool.Rotate], SimulationToolIcon.Shape.Rotate);
        Attach(_buttons[CameraController.CameraTool.Zoom], SimulationToolIcon.Shape.Zoom);
        Attach(_centerButton, SimulationToolIcon.Shape.Target);

        if (_centerButton != null)
            _centerButton.clicked += FocusCurrentTarget;

        _camera.OnToolChanged += _ => Refresh();
        _camera.OnFollowTargetChanged += _ => Refresh();

        Refresh();
    }

    private void FocusCurrentTarget()
    {
        UnityEngine.Transform target = _camera.GetCurrentFollowTarget();
        if (target != null)
            _camera.FocusAgent(target);
    }

    private void Refresh()
    {
        foreach (KeyValuePair<CameraController.CameraTool, Button> pair in _buttons)
        {
            if (pair.Value == null) continue;

            pair.Value.EnableInClassList("is-active", _camera.ActiveTool == pair.Key);
        }

        if (_centerButton != null)
            _centerButton.SetEnabled(_camera.GetCurrentFollowTarget() != null);

        // The icons take their colour from the button, so they have to be redrawn when the active
        // tool changes colour.
        foreach (SimulationToolIcon icon in _icons)
            icon.MarkDirtyRepaint();
    }

    /// <summary>Replaces the button's text with a drawn pictogram.</summary>
    private void Attach(Button button, SimulationToolIcon.Shape shape)
    {
        if (button == null) return;

        button.text = string.Empty;

        var icon = new SimulationToolIcon(shape);
        button.Add(icon);
        _icons.Add(icon);
    }

    private void SetVisible(bool visible)
    {
        foreach (Button button in _buttons.Values)
        {
            if (button != null)
                button.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        if (_centerButton != null)
            _centerButton.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }
}
