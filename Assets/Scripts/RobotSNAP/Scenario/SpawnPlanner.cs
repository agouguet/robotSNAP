using System;
using System.Collections.Generic;
using RobotSNAP.Agents;
using UnityEngine;

namespace RobotSNAP.Core.Scenario
{
    /// <summary>
    /// Decides where the agents of a scenario start, without touching a scene.
    ///
    /// Keeping the policy here rather than inside <see cref="ScenarioApplier"/> makes it unit testable:
    /// the applier resolves scenario references and applies the returned positions, while every rule
    /// (group layout, spacing window, heading, formation slots) stays a pure function of its inputs.
    /// </summary>
    public static class SpawnPlanner
    {
        public const float DefaultSpacing = 1.5f;
        public const float MinSpacing = 0.4f;
        public const float MaxSpacing = 3f;

        /// <summary>Formation and spacing shared by every human config declaring the same group id.</summary>
        public readonly struct GroupLayout
        {
            public GroupLayout(
                string formation,
                float spacing,
                bool hasFormation,
                bool hasSpacing = true,
                float parameter = 0f)
            {
                Formation = formation;
                Spacing = ClampSpacing(spacing, formation);
                HasFormation = hasFormation;
                HasSpacing = hasSpacing;
                Parameter = parameter;
            }

            public string Formation { get; }
            public float Spacing { get; }
            public bool HasFormation { get; }
            public bool HasSpacing { get; }
            public float Parameter { get; }
        }

        /// <summary>A group is meant to stay a compact bloc, so its spacing window is bounded.</summary>
        public static float ClampSpacing(float spacing) =>
            Mathf.Clamp(spacing <= 0f ? DefaultSpacing : spacing, MinSpacing, MaxSpacing);

        /// <summary>
        /// Same window, raised to the smallest spacing the formation itself can hold: a single file
        /// below one metre walks into its own leader, a loose cluster can stay tighter.
        /// </summary>
        public static float ClampSpacing(float spacing, string formation)
        {
            float floor = Mathf.Max(MinSpacing, GroupFormation.MinSpacing(formation));
            return Mathf.Clamp(spacing <= 0f ? DefaultSpacing : spacing, floor, MaxSpacing);
        }

        /// <summary>
        /// Resolves one layout per group id. The first config that sets a formation or a spacing wins,
        /// so hand-written YAML stays valid even when only one entry of the group carries the layout.
        /// </summary>
        public static Dictionary<string, GroupLayout> ResolveGroupLayouts(IEnumerable<HumanScenarioConfig> humans)
        {
            var layouts = new Dictionary<string, GroupLayout>();
            if (humans == null)
                return layouts;

            foreach (HumanScenarioConfig config in humans)
            {
                if (config == null || string.IsNullOrWhiteSpace(config.Group))
                    continue;

                string id = config.Group.Trim();
                if (!layouts.TryGetValue(id, out GroupLayout layout))
                    layout = new GroupLayout(null, DefaultSpacing, hasFormation: false, hasSpacing: false);

                string formation = layout.Formation;
                bool hasFormation = layout.HasFormation;
                float parameter = layout.Parameter;
                if (!hasFormation && !string.IsNullOrWhiteSpace(config.Spawn?.Formation))
                {
                    formation = config.Spawn.Formation;
                    hasFormation = true;
                }

                if (parameter <= 0f && config.Spawn != null && config.Spawn.FormationParameter > 0f)
                    parameter = config.Spawn.FormationParameter;

                float spacing = layout.Spacing;
                bool hasSpacing = layout.HasSpacing;
                if (!hasSpacing && config.Spawn != null)
                {
                    spacing = config.Spawn.Spacing;
                    hasSpacing = true;
                }

                layouts[id] = new GroupLayout(formation, spacing, hasFormation, hasSpacing, parameter);
            }

            return layouts;
        }

        /// <summary>
        /// Walking direction at spawn: from the anchor towards the first reachable objective.
        /// Falls back on the second objective when the agent already stands on the first one.
        /// </summary>
        public static float HeadingTowards(Vector2 anchor, IList<Vector3> goals)
        {
            if (goals == null || goals.Count == 0)
                return 0f;

            var direction = new Vector2(goals[0].x - anchor.x, goals[0].z - anchor.y);
            if (direction.sqrMagnitude < 0.0001f && goals.Count > 1)
                direction = new Vector2(goals[1].x - goals[0].x, goals[1].z - goals[0].z);

            return direction.sqrMagnitude > 0.0001f ? Mathf.Atan2(direction.x, direction.y) : 0f;
        }

        /// <summary>
        /// Leader-local formation slot of one member, already rotated into world axes.
        /// The leader always owns the first slot and stays on the anchor.
        /// </summary>
        public static Vector2 SlotOffset(
            int index,
            int count,
            float spacing,
            string formation,
            float headingRadians,
            float parameter = 0f)
        {
            List<Vector2> slots = GroupFormation.CreateSlots(
                Mathf.Max(0, count),
                ClampSpacing(spacing, formation),
                formation,
                parameter);
            if (slots.Count == 0)
                return Vector2.zero;

            Vector2 slot = slots[Mathf.Clamp(index, 0, slots.Count - 1)];
            return GroupFormation.Rotate(slot, headingRadians);
        }

        /// <summary>
        /// Places one slot around an anchor, bounded so a member cannot be pushed metres away
        /// from the group by the walkable-area projection.
        /// </summary>
        public static Vector3 PlaceSlot(Vector3 anchor, Vector2 offset, float spacing)
        {
            Vector2 anchor2D = new(anchor.x, anchor.z);
            float tolerance = offset.magnitude + Mathf.Max(0.5f, ClampSpacing(spacing) * 0.5f);
            Vector2 projected = SpawnPlacement.ProjectWithin(anchor2D + offset, anchor2D, tolerance);
            return new Vector3(projected.x, anchor.y, projected.y);
        }

        /// <summary>
        /// Snaps a spawn anchor onto walkable ground. A group member's anchor is shared by the whole
        /// group, so this runs once per route instead of once per agent.
        /// </summary>
        public static Vector3 PlaceAnchor(Vector3 anchor) => SpawnPlacement.SnapToNavMesh(anchor);
    }
}
