using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RobotSNAP.Metrics;
using UnityEngine;

/// <summary>
/// One colour and one caption per agent track of an episode. The trajectory view and its legend both read
/// this, so the colour a line was drawn in is the colour the key names - they cannot drift apart.
///
/// The robots are separated from the crowd the way the scenario editor already separates them: the project's
/// robot green for the robot the episode follows, a family of greens for the rest of the fleet, and a curated
/// palette for the humans. A different robot must never read as a crowd member, which is why the fleet keeps
/// the green family and the crowd never does. The colour of an agent depends on its rank in the episode's
/// ordinal order of keys, not on the order a dictionary happened to enumerate in, so the same episode always
/// paints the same picture.
/// </summary>
public static class AnalysisAgentPalette
{
    /// <summary>Prefix every human track key carries; anything else is a robot of the fleet.</summary>
    private const string HumanPrefix = "human_";

    /// <summary>One agent track as the map and its legend read it: an identity, a colour and a caption.</summary>
    public readonly struct TrackColour
    {
        public TrackColour(string key, string label, Color colour, bool isRobot)
        {
            Key = key;
            Label = label;
            Colour = colour;
            IsRobot = isRobot;
        }

        /// <summary>Key the track is filed under in the episode: <c>robot_1</c>, <c>human_3</c>.</summary>
        public string Key { get; }

        /// <summary>Short caption of the legend entry: "Robot", "Robot 2", "Human 3".</summary>
        public string Label { get; }

        /// <summary>Colour the trajectory is drawn in.</summary>
        public Color Colour { get; }

        /// <summary>True for any robot of the fleet, false for a crowd member.</summary>
        public bool IsRobot { get; }
    }

    /// <summary>The followed robot's colour: the green the scenario editor already gives a robot marker.</summary>
    public static readonly Color RobotColour = new Color32(55, 200, 115, 255);

    /// <summary>
    /// Fleet colours, handed out to the robots the episode does not follow. They stay in the robot's green
    /// family so a fleet never reads as a crowd, but each is far enough from its neighbours - and from the
    /// followed robot's green - to tell two robots apart on the map.
    /// </summary>
    private static readonly Color32[] RobotCompanionColours =
    {
        new Color32(150, 215, 70, 255),   // lime
        new Color32(25, 150, 95, 255),    // deep green
        new Color32(95, 200, 165, 255),   // mint
        new Color32(190, 210, 95, 255),   // olive-lime
    };

    /// <summary>
    /// Crowd colours, in the order they are handed out. The first eight are a fixed palette; a crowd past
    /// that keeps the same hues and rotates each one, which is unique for as long as a crowd realistically
    /// stays smaller than a few dozen.
    /// </summary>
    private static readonly Color32[] HumanColours =
    {
        new Color32(55, 145, 230, 255),   // blue
        new Color32(235, 150, 40, 255),   // orange
        new Color32(155, 90, 220, 255),   // violet
        new Color32(225, 78, 130, 255),   // pink
        new Color32(70, 190, 200, 255),   // teal
        new Color32(205, 185, 60, 255),   // yellow
        new Color32(170, 120, 90, 255),   // brown
        new Color32(150, 160, 180, 255),  // slate
    };

    /// <summary>
    /// Assigns one entry per track of the episode, the fleet first - the followed robot, then the other
    /// robots - and the crowd after it. A track whose key is missing from the episode's dictionary is simply
    /// not listed, which is what lets a caller hand over the whole document without filtering it first.
    /// </summary>
    public static IReadOnlyList<TrackColour> Assign(IEnumerable<string> trackKeys, string robotKey)
        => Assign(trackKeys, robotKey, null);

    /// <summary>
    /// Assigns one entry per track, using <paramref name="robotIds"/> as the fleet roster when the episode
    /// carries one. The roster is what the recorder wrote - the followed robot first - so a robot the fleet
    /// never sampled is still named as a robot instead of being mistaken for a crowd member. With no roster
    /// the prefix of the key is the only signal: <c>human_&lt;id&gt;</c> is a human, anything else a robot.
    /// </summary>
    public static IReadOnlyList<TrackColour> Assign(
        IEnumerable<string> trackKeys,
        string robotKey,
        IReadOnlyList<string> robotIds)
    {
        var keys = (trackKeys ?? Enumerable.Empty<string>())
            .Where(key => !string.IsNullOrEmpty(key))
            .Distinct()
            .ToList();
        if (keys.Count == 0)
            return new List<TrackColour>();

        var result = new List<TrackColour>(keys.Count);

        // The followed robot is the track the episode names as its own; with no such key the single robot
        // entry stands in for it, which is what an episode recorded before the key was written looks like.
        string resolvedRobot = keys.Contains(robotKey)
            ? robotKey
            : keys.Count(key => IsRobotKey(key, robotIds)) == 1
                ? keys.First(key => IsRobotKey(key, robotIds))
                : null;

        if (resolvedRobot != null)
            result.Add(new TrackColour(resolvedRobot, "Robot", RobotColour, true));

        // The rest of the fleet, roster order first so the numbering follows the recorder's own list, then
        // ordinal for whatever the roster did not name. They are all robots, so none of them may fall into
        // the crowd palette below.
        int companion = 0;
        foreach (string key in Ordered(keys.Where(key =>
                     !string.Equals(key, resolvedRobot, System.StringComparison.Ordinal) &&
                     IsRobotKey(key, robotIds)), robotIds))
        {
            companion++;
            result.Add(new TrackColour(key, "Robot " + (companion + 1),
                RobotCompanionColour(companion - 1), true));
        }

        int rank = 0;
        foreach (string key in keys.Where(key => !string.Equals(key, resolvedRobot, System.StringComparison.Ordinal))
                     .Where(key => !IsRobotKey(key, robotIds))
                     .OrderBy(key => key, HumanKeyComparer.Instance))
        {
            result.Add(new TrackColour(key, HumanLabel(key), HumanColour(rank), false));
            rank++;
        }

        return result;
    }

    /// <summary>Colour of a single key on its own, for a caller that has no episode to rank it in.</summary>
    public static Color ColourFor(string trackKey)
    {
        if (string.IsNullOrEmpty(trackKey) || !trackKey.StartsWith(HumanPrefix, System.StringComparison.Ordinal))
            return RobotColour;
        return HumanColour(System.Math.Abs(trackKey.GetHashCode()) % HumanColours.Length);
    }

    /// <summary>Caption of a track: "Human 3" for the third crowd member, or the key for anything else.</summary>
    public static string HumanLabel(string trackKey)
    {
        if (string.IsNullOrEmpty(trackKey))
            return "Agent";
        if (!trackKey.StartsWith(HumanPrefix, System.StringComparison.Ordinal))
            return trackKey;
        return "Human " + trackKey.Substring(HumanPrefix.Length);
    }

    /// <summary>
    /// Whether a track key names a robot. The roster decides it when the episode carries one; without it the
    /// key's own prefix does - every human is written as <c>human_&lt;id&gt;</c> and every robot under its
    /// roster id.
    /// </summary>
    public static bool IsRobotKey(string trackKey, IReadOnlyList<string> robotIds)
    {
        if (string.IsNullOrEmpty(trackKey))
            return false;

        if (robotIds != null)
        {
            foreach (string robot in robotIds)
            {
                if (string.Equals(MetricsContract.RobotTrackKey(robot), trackKey, System.StringComparison.Ordinal))
                    return true;
            }
        }

        return !trackKey.StartsWith(HumanPrefix, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// Orders robot keys by the recorder's roster when it holds them, so "Robot 2" is the second robot the
    /// fleet listed rather than whichever key sorted first; the keys the roster missed keep the tail.
    /// </summary>
    private static IEnumerable<string> Ordered(IEnumerable<string> keys, IReadOnlyList<string> robotIds)
    {
        var remaining = new List<string>(keys);
        var ordered = new List<string>();

        if (robotIds != null)
        {
            foreach (string robot in robotIds)
            {
                string key = MetricsContract.RobotTrackKey(robot);
                int index = remaining.IndexOf(key);
                if (index >= 0)
                {
                    ordered.Add(key);
                    remaining.RemoveAt(index);
                }
            }
        }

        remaining.Sort(System.StringComparer.Ordinal);
        ordered.AddRange(remaining);
        return ordered;
    }

    private static Color RobotCompanionColour(int rank)
    {
        Color32 baseColour = RobotCompanionColours[rank % RobotCompanionColours.Length];
        int cycle = rank / RobotCompanionColours.Length;
        if (cycle == 0)
            return baseColour;

        Color.RGBToHSV(baseColour, out float hue, out float saturation, out float value);
        return Color.HSVToRGB(Mathf.Repeat(hue + 0.07f * cycle, 1f), saturation, value);
    }

    private static Color HumanColour(int rank)
    {
        Color32 baseColour = HumanColours[rank % HumanColours.Length];
        int cycle = rank / HumanColours.Length;
        if (cycle == 0)
            return baseColour;

        Color.RGBToHSV(baseColour, out float hue, out float saturation, out float value);
        return Color.HSVToRGB(Mathf.Repeat(hue + 0.11f * cycle, 1f), saturation, value);
    }

    /// <summary>
    /// Orders human keys by the number in them when they both carry one, so <c>human_2</c> precedes
    /// <c>human_10</c>; an id that is not a number falls back to a plain ordinal comparison.
    /// </summary>
    private sealed class HumanKeyComparer : IComparer<string>
    {
        public static readonly HumanKeyComparer Instance = new HumanKeyComparer();

        public int Compare(string left, string right)
        {
            bool leftNumber = TryNumber(left, out int leftValue);
            bool rightNumber = TryNumber(right, out int rightValue);
            if (leftNumber && rightNumber && leftValue != rightValue)
                return leftValue.CompareTo(rightValue);
            return string.CompareOrdinal(left, right);
        }

        private static bool TryNumber(string key, out int value)
        {
            value = 0;
            if (string.IsNullOrEmpty(key))
                return false;
            int separator = key.LastIndexOf('_');
            string tail = separator >= 0 ? key.Substring(separator + 1) : key;
            return int.TryParse(tail, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }
    }
}
