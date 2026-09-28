using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Where the visualization switches of the overlay were left, kept from one run to the next.
///
/// A debugging panel that has to be set up again every session is a panel nobody sets up, so the value of
/// each switch is remembered under one key per switch. Only the switches somebody actually touched are
/// stored: one never touched keeps whatever the scene authored, which is what a hand-built environment or a
/// testbed expects on its very first run.
///
/// The player preferences are the store rather than a file of our own because that is where Unity already
/// keeps this kind of per-user preference, in the editor and on every platform alike, with no path to
/// create and no file to corrupt.
/// </summary>
public static class VisualizationSwitchMemory
{
    /// <summary>Prefix of the keys, so a switch cannot collide with a preference of another feature.</summary>
    private const string KeyPrefix = "robotsnap.visualization.";

    /// <summary>True when an earlier run left a value for this switch.</summary>
    public static bool Has(string toggleName) =>
        !string.IsNullOrEmpty(toggleName) && PlayerPrefs.HasKey(KeyPrefix + toggleName);

    /// <summary>The value this switch was left at, or <paramref name="fallback"/> when it was never touched.</summary>
    public static bool Load(string toggleName, bool fallback) =>
        Has(toggleName) ? PlayerPrefs.GetInt(KeyPrefix + toggleName) != 0 : fallback;

    /// <summary>Remembers the value of one switch.</summary>
    public static void Store(string toggleName, bool value)
    {
        if (string.IsNullOrEmpty(toggleName))
            return;

        PlayerPrefs.SetInt(KeyPrefix + toggleName, value ? 1 : 0);
        // Written at once rather than only at the exit of the editor: a session that ends in a crash is the
        // one whose switches would otherwise be forgotten, and a click on a toggle is rare enough to pay for.
        PlayerPrefs.Save();
    }

    /// <summary>
    /// Forgets the switches named, in one write, so they go back to whatever the scene authors.
    ///
    /// Nobody in the application calls this - the panel only ever remembers - and it is here for whoever
    /// needs a run that starts from the scene: a test that must not inherit the switches of the last one,
    /// and a session that wants the defaults back.
    /// </summary>
    public static void ForgetAll(IEnumerable<string> toggleNames)
    {
        if (toggleNames == null)
            return;

        foreach (string toggleName in toggleNames)
        {
            if (!string.IsNullOrEmpty(toggleName))
                PlayerPrefs.DeleteKey(KeyPrefix + toggleName);
        }

        PlayerPrefs.Save();
    }
}
