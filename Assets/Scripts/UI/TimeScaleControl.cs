using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RobotSNAP.ROS;
using UnityEngine.UIElements;

/// <summary>
/// The speed control of the simulation view: a dropdown of the speeds a session is run at, which
/// shows the scale the configuration actually holds and writes a new one through the same command a
/// ROS or Python client sends, <c>{"command":"set_time_scale"}</c>.
///
/// It is a list of exact presets rather than a slider because a session's speed has to be the same
/// number every time it is set - a client says ten, not 9.7 - and the speed a session actually runs
/// at is read back from the configuration, which is what clamps it. A scale that is not one of the
/// presets (a client set seven) is shown as it is rather than rounded to a neighbour, so the control
/// never claims a speed the session is not running at.
/// </summary>
public sealed class TimeScaleControl
{
    /// <summary>
    /// The speeds the dropdown offers, in multiples of real time. One hundred is the ceiling of
    /// <c>SimulationConfig.TimeScale</c>, so the list stops there.
    /// </summary>
    public static readonly IReadOnlyList<float> Presets = new[] { 1f, 2f, 5f, 10f, 25f, 50f, 100f };

    private readonly DropdownField _dropdown;
    private readonly SimulationCommandRouter _router;
    private readonly Func<float?> _appliedScale;
    private readonly Action<string> _log;

    /// <summary>The scale the dropdown currently shows, so a refresh that changes nothing writes nothing.</summary>
    private float _shown = float.NaN;

    /// <summary>
    /// Builds the control over <paramref name="dropdown"/>. <paramref name="appliedScale"/> reads the
    /// scale the session is running at, which is what the control mirrors; it returns null while the
    /// scene carries nothing to read, and the control then leaves the dropdown alone rather than
    /// inventing a speed. <paramref name="router"/> runs the command, and <paramref name="log"/> is
    /// where a refused one is reported.
    /// </summary>
    public TimeScaleControl(
        DropdownField dropdown,
        SimulationCommandRouter router,
        Func<float?> appliedScale,
        Action<string> log = null)
    {
        _router = router ?? new SimulationCommandRouter();
        _appliedScale = appliedScale ?? (() => null);
        _log = log ?? (message => UnityEngine.Debug.LogWarning(message));

        if (dropdown == null)
        {
            UnityEngine.Debug.LogWarning("[TimeScaleControl] The top bar has no speed dropdown; the control stays inert.");
            return;
        }

        _dropdown = dropdown;
        _dropdown.choices = new List<string>(LabelsFor(null));
        _dropdown.RegisterValueChangedCallback(OnValueChanged);
        Refresh();
    }

    /// <summary>
    /// Reads the scale the session holds and mirrors it in the dropdown, a value a client set and a
    /// clamp the configuration applied to one included. It is read on a tick rather than on a
    /// configuration event because the router writes the configuration in place on purpose, so that
    /// changing the speed does not make the scenario loader drop its caches.
    /// </summary>
    public void Refresh()
    {
        if (_dropdown == null) return;

        float? applied = _appliedScale();
        if (applied == null) return;
        if (!float.IsNaN(_shown) && UnityEngine.Mathf.Approximately(applied.Value, _shown)) return;

        _shown = applied.Value;
        // The list is rebuilt rather than appended to, so a scale that is no longer the session's
        // stops being offered as a choice.
        _dropdown.choices = new List<string>(LabelsFor(applied.Value));
        _dropdown.SetValueWithoutNotify(FormatScale(applied.Value));
    }

    /// <summary>
    /// What a selection from the dropdown does: the dropdown hands its new label to this, and a label
    /// that is not a speed is ignored rather than guessed at.
    /// </summary>
    public void Choose(string label)
    {
        if (TryParseScale(label, out float scale))
            Apply(scale);
    }

    /// <summary>
    /// Asks the session to run at <paramref name="scale"/>, through the command a ROS or Python
    /// client sends, so the two ways of setting the speed cannot diverge. The configuration clamps
    /// what it is given, so the dropdown is refreshed from it rather than from the request.
    /// </summary>
    public void Apply(float scale)
    {
        if (_dropdown == null || !IsUsable(scale)) return;

        JObject body = new JObject
        {
            ["command"] = "set_time_scale",
            ["time_scale"] = scale
        };

        CommandResult result = _router.Execute(body.ToString(Formatting.None));
        if (!result.Ok)
        {
            _log($"[TimeScaleControl] {result.Message}");
            return;
        }

        Refresh();
    }

    /// <summary>
    /// The label a scale is shown under. A whole number keeps no decimal part, so the presets read
    /// "x1" and "x100", and a scale a client chose reads as itself: "x7".
    /// </summary>
    public static string FormatScale(float scale)
    {
        return "x" + scale.ToString("0.###", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Reads a label back, so a selection can be turned into the number the command carries.
    /// </summary>
    public static bool TryParseScale(string label, out float scale)
    {
        scale = 0f;
        if (string.IsNullOrWhiteSpace(label)) return false;

        string text = label.Trim();
        if (text.Length > 0 && (text[0] == 'x' || text[0] == 'X'))
            text = text.Substring(1);

        return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out scale) && IsUsable(scale);
    }

    private static bool IsUsable(float scale) => scale > 0f && !float.IsNaN(scale) && !float.IsInfinity(scale);

    private void OnValueChanged(ChangeEvent<string> evt) => Choose(evt.newValue);

    /// <summary>
    /// The labels of the list: the presets in order, with <paramref name="extra"/> in front of them
    /// while the session runs at a scale that is not one of them.
    /// </summary>
    private static IEnumerable<string> LabelsFor(float? extra)
    {
        if (extra.HasValue && !IsPreset(extra.Value))
            yield return FormatScale(extra.Value);

        foreach (float preset in Presets)
            yield return FormatScale(preset);
    }

    private static bool IsPreset(float scale)
    {
        foreach (float preset in Presets)
        {
            if (UnityEngine.Mathf.Approximately(preset, scale)) return true;
        }

        return false;
    }
}
