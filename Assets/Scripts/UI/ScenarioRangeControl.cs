using System;
using System.Collections.Generic;
using RobotSNAP.Core.Scenario;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// One scenario value the author can either fix or let the run draw from a <c>[min, max]</c> range.
///
/// The scenario wizard used to show one field per value. A value the run may draw needs two more pieces of
/// state than a fixed one - the two bounds - and the saved file has to keep saying which of the two it is,
/// so this control sits beside the field the wizard already had: the original field stays the fixed value, a
/// "Fixed / Range" menu says which one the author means, and the two bounds only appear once a range is
/// asked for. While the control is in Fixed mode nothing about the range is written into the scenario, which
/// is what leaves a route that randomizes nothing byte-identical to one written before ranges existed.
/// </summary>
public sealed class ScenarioRangeControl
{
    public const string FixedChoice = "Fixed";
    public const string RangeChoice = "Range";

    private readonly VisualElement _fixedRow;
    private readonly DropdownField _mode;
    private readonly VisualElement _boundsRow;
    private readonly FloatField _minField;
    private readonly FloatField _maxField;
    private readonly Label _hint;
    private readonly float _floor;
    private readonly float _ceiling;
    private bool _updating;

    /// <summary>Raised when the author changes the mode or a bound; never raised by a programmatic refresh.</summary>
    public event Action Changed;

    /// <summary>The row this control built, so a second control can be placed under it.</summary>
    public VisualElement Row { get; }

    /// <param name="host">Container the range row is inserted into.</param>
    /// <param name="anchor">Direct child of <paramref name="host"/> the range row is placed after.</param>
    /// <param name="fixedRow">Row of the existing value field, hidden while a range is being edited.</param>
    /// <param name="title">Label of the menu, naming which value it randomizes.</param>
    /// <param name="help">What the two bounds mean, shown under them while a range is set.</param>
    /// <param name="floor">Smallest bound the value can take, so a draw is never impossible.</param>
    /// <param name="ceiling">Largest bound the value can take.</param>
    /// <param name="defaultMin">Bound the editor starts from when the author switches to Range.</param>
    /// <param name="defaultMax">Other bound the editor starts from when the author switches to Range.</param>
    public ScenarioRangeControl(
        VisualElement host,
        VisualElement anchor,
        VisualElement fixedRow,
        string title,
        string help,
        float floor,
        float ceiling,
        float defaultMin,
        float defaultMax)
    {
        _fixedRow = fixedRow;
        _floor = floor;
        _ceiling = Mathf.Max(floor, ceiling);

        Row = new VisualElement();
        Row.AddToClassList("creation-field");
        // Named after the value it draws, so a test - and the debugging tree of the interface - can tell the
        // six of them apart without walking the layout.
        Row.name = title;

        var label = new Label(title);
        label.AddToClassList("creation-field-label");
        Row.Add(label);

        _mode = new DropdownField
        {
            choices = new List<string> { FixedChoice, RangeChoice }
        };
        _mode.AddToClassList("creation-dropdown");
        _mode.name = title + " mode";
        _mode.SetValueWithoutNotify(FixedChoice);
        Row.Add(_mode);

        _boundsRow = new VisualElement();
        _boundsRow.AddToClassList("coordinate-row");
        _boundsRow.style.display = DisplayStyle.None;
        _minField = MakeBound("Min");
        _maxField = MakeBound("Max");
        _minField.name = title + " min";
        _maxField.name = title + " max";
        _minField.SetValueWithoutNotify(Clamp(defaultMin));
        _maxField.SetValueWithoutNotify(Clamp(defaultMax));
        _boundsRow.Add(_minField.parent);
        _boundsRow.Add(_maxField.parent);
        Row.Add(_boundsRow);

        _hint = new Label(help);
        _hint.AddToClassList("field-help");
        _hint.style.display = DisplayStyle.None;
        Row.Add(_hint);

        int index = anchor != null ? host.IndexOf(anchor) : -1;
        if (index >= 0)
            host.Insert(index + 1, Row);
        else
            host.Add(Row);

        _mode.RegisterValueChangedCallback(_ =>
        {
            if (_updating)
                return;
            ApplyMode();
            Changed?.Invoke();
        });
        _minField.RegisterValueChangedCallback(evt =>
        {
            if (_updating)
                return;
            float value = Clamp(evt.newValue);
            if (!Mathf.Approximately(value, evt.newValue))
                _minField.SetValueWithoutNotify(value);
            Changed?.Invoke();
        });
        _maxField.RegisterValueChangedCallback(evt =>
        {
            if (_updating)
                return;
            float value = Clamp(evt.newValue);
            if (!Mathf.Approximately(value, evt.newValue))
                _maxField.SetValueWithoutNotify(value);
            Changed?.Invoke();
        });

        ApplyMode();
    }

    /// <summary>True while the author asked the run to draw this value.</summary>
    public bool IsRange => _mode.value == RangeChoice;

    public float Min => Mathf.Min(_minField.value, _maxField.value);
    public float Max => Mathf.Max(_minField.value, _maxField.value);

    /// <summary>
    /// The range to save, or <c>null</c> while the value is fixed. Two equal bounds are not something the run
    /// could draw from, so that case is written as the fixed value rather than as a range of one.
    /// </summary>
    public ScenarioRange ToRange()
    {
        if (!IsRange || Max <= Min)
            return null;
        return new ScenarioRange { Min = Min, Max = Max };
    }

    /// <summary>
    /// Shows the range a stored scenario carries, falling back on what the editor uses for a new route when
    /// it carries none. Programmatic, so no <see cref="Changed"/> is raised and no undo entry is pushed.
    /// </summary>
    public void SetFrom(ScenarioRange range, float fallbackMin, float fallbackMax)
    {
        _updating = true;
        if (range != null && range.IsUsable)
        {
            _mode.SetValueWithoutNotify(RangeChoice);
            _minField.SetValueWithoutNotify(Clamp(range.Min));
            _maxField.SetValueWithoutNotify(Clamp(range.Max));
        }
        else
        {
            _mode.SetValueWithoutNotify(FixedChoice);
            _minField.SetValueWithoutNotify(Clamp(fallbackMin));
            _maxField.SetValueWithoutNotify(Clamp(fallbackMax));
        }

        ApplyMode();
        _updating = false;
    }

    /// <summary>Shows the control only while its kind of route is the selected one.</summary>
    public void SetEnabled(bool enabled)
    {
        Row.SetEnabled(enabled);
    }

    /// <summary>
    /// Switches the value to a drawn range and sets its bounds, as the author typing them would, and raises
    /// <see cref="Changed"/> so the route is written back.
    /// </summary>
    public void SetRange(float minimum, float maximum)
    {
        _updating = true;
        _mode.SetValueWithoutNotify(RangeChoice);
        _minField.SetValueWithoutNotify(Clamp(minimum));
        _maxField.SetValueWithoutNotify(Clamp(maximum));
        ApplyMode();
        _updating = false;
        Changed?.Invoke();
    }

    /// <summary>Switches the value back to a fixed one, keeping the bounds for a later switch, and reports it.</summary>
    public void SetFixed()
    {
        _updating = true;
        _mode.SetValueWithoutNotify(FixedChoice);
        ApplyMode();
        _updating = false;
        Changed?.Invoke();
    }

    private void ApplyMode()
    {
        bool range = IsRange;
        if (_fixedRow != null)
            _fixedRow.style.display = range ? DisplayStyle.None : DisplayStyle.Flex;
        _boundsRow.style.display = range ? DisplayStyle.Flex : DisplayStyle.None;
        _hint.style.display = range ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private static FloatField MakeBound(string caption)
    {
        var container = new VisualElement();
        container.AddToClassList("creation-field");
        container.AddToClassList("half-field");

        var label = new Label(caption);
        label.AddToClassList("creation-field-label");
        container.Add(label);

        var field = new FloatField();
        field.AddToClassList("creation-text-field");
        container.Add(field);
        return field;
    }

    private float Clamp(float value) => Mathf.Clamp(value, _floor, _ceiling);
}
