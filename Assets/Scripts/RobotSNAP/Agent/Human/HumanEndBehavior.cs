using System;

namespace RobotSNAP.Agents
{
    /// <summary>What an agent does once it reached the last point of its route.</summary>
    public enum HumanEndBehavior
    {
        /// <summary>Stop where the route ended and stay there.</summary>
        Stay,

        /// <summary>Disappear (return to the pool) once the route is complete.</summary>
        Disappear,

        /// <summary>Walk the route again from its first point, forever.</summary>
        Loop
    }

    /// <summary>YAML values of <see cref="HumanEndBehavior"/>, with a few readable synonyms.</summary>
    public static class HumanEndBehaviorParser
    {
        public const string StayValue = "stay";
        public const string DisappearValue = "disappear";
        public const string LoopValue = "loop";

        public static HumanEndBehavior Parse(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return HumanEndBehavior.Stay;

            switch (value.Trim().ToLowerInvariant())
            {
                case "disappear":
                case "despawn":
                case "remove":
                case "leave":
                    return HumanEndBehavior.Disappear;
                case "loop":
                case "loop route":
                case "cycle":
                case "repeat":
                    return HumanEndBehavior.Loop;
                case "stay":
                case "stay in place":
                case "idle":
                case "wait":
                    return HumanEndBehavior.Stay;
                default:
                    return HumanEndBehavior.Stay;
            }
        }

        public static string ToYamlValue(HumanEndBehavior behavior)
        {
            switch (behavior)
            {
                case HumanEndBehavior.Disappear: return DisappearValue;
                case HumanEndBehavior.Loop: return LoopValue;
                default: return StayValue;
            }
        }

        /// <summary>Display labels used by the scenario editor, in enum order.</summary>
        public static string ToDisplayName(HumanEndBehavior behavior)
        {
            switch (behavior)
            {
                case HumanEndBehavior.Disappear: return "Disappear";
                case HumanEndBehavior.Loop: return "Loop route";
                default: return "Stay in place";
            }
        }
    }
}
