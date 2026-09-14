using System.Collections.Generic;
using UnityEngine;

namespace RobotSNAP.Agents
{
    /// <summary>
    /// Runtime group of humans walking together.
    ///
    /// There is no leader. The group owns its route and a virtual reference point travels along it; every
    /// member — the first one included — steers towards its own slot around that reference. Two friends
    /// walking side by side have no leader either, and giving one of them the route is exactly what used to
    /// leave the others behind in a corner: the reference now slows down when a member falls behind, so the
    /// group waits for its laggards instead of stretching out of shape.
    /// </summary>
    public sealed class HumanGroup
    {
        /// <summary>Maximum rotation speed of the group frame, in degrees per second.</summary>
        private const float MaxTurnRateDegrees = 110f;

        /// <summary>
        /// Grip of the catch-up law: a member one metre behind its slot is asked for 1.2 m/s extra.
        /// Members are capped at their own physical speed, so this only closes a gap.
        /// </summary>
        private const float LagGain = 1.2f;

        /// <summary>Under this speed the member keeps its configured cruise speed.</summary>
        private const float MinCruiseOverride = 0.05f;

        /// <summary>Distance at which the reference point switches to the next waypoint of the route.</summary>
        private const float ArriveRadius = 0.2f;

        private readonly List<HumanAgent> _members = new();
        private readonly List<Vector2> _slots = new();
        private readonly List<Vector2> _route = new();

        private Vector2 _anchor;
        private float _heading;
        private float _speed;
        private float _desiredSpeed = 1f;
        private int _legIndex = 1;
        private int _lastTickFrame = -1;
        private bool _routeActive;
        private bool _awaitingPlacement;
        private bool _routeCompleted;

        public HumanGroup(string id, float spacing, string formation, float parameter = 0f)
        {
            Id = id;
            Formation = GroupFormation.Normalize(formation);
            // Never below what the formation itself can hold, never wide enough to break the group apart.
            Spacing = Mathf.Clamp(spacing, GroupFormation.MinSpacing(Formation), 3f);
            Parameter = parameter;
        }

        public string Id { get; }
        public float Spacing { get; }
        public string Formation { get; }

        /// <summary>The one value the formation tunes; zero means its natural default.</summary>
        public float Parameter { get; }

        public int Count => _members.Count;
        public IReadOnlyList<HumanAgent> Members => _members;

        /// <summary>True when the given agent walks in this group.</summary>
        public bool ContainsAgent(int agentId)
        {
            foreach (HumanAgent member in _members)
                if (member != null && member.agentId == agentId)
                    return true;

            return false;
        }

        /// <summary>Position of the virtual reference the whole formation is built around.</summary>
        public Vector2 Anchor => _anchor;

        /// <summary>Heading of the formation frame, in radians.</summary>
        public float Heading => _heading;

        public HumanEndBehavior EndBehavior { get; private set; } = HumanEndBehavior.Stay;
        public bool IsRouteActive => _routeActive;

        /// <summary>
        /// Shared route of the group: spawn anchor first, then every goal. The group walks it as one, so the
        /// members never need a route of their own.
        /// </summary>
        public void SetRoute(IReadOnlyList<Vector2> points, float speed, HumanEndBehavior endBehavior)
        {
            _route.Clear();
            if (points != null)
            {
                foreach (Vector2 point in points)
                    _route.Add(point);
            }

            EndBehavior = endBehavior;
            _desiredSpeed = Mathf.Max(0.1f, speed);
            _speed = 0f;
            _legIndex = 1;
            _routeActive = _route.Count >= 2;
            _routeCompleted = false;
            if (_route.Count == 0)
                return;

            _anchor = _route[0];
            _heading = HeadingTowards(_route, _legIndex, _heading);
            _awaitingPlacement = true;
        }

        public void Add(HumanAgent member)
        {
            if (member == null || _members.Contains(member))
                return;

            _members.Add(member);
            RebuildSlots();
            member.JoinGroup(this, _slots[_members.Count - 1]);
        }

        /// <summary>Removes a member, e.g. when it disappears on its own.</summary>
        public void Remove(HumanAgent member)
        {
            if (member == null || !_members.Remove(member))
                return;

            member.LeaveGroup();
            if (_members.Count > 0)
                RebuildSlots();
        }

        /// <summary>
        /// Advances the whole group: the reference walks its route, then every member is given the slot it
        /// should hold. Any member calls this from its own update; the frame guard keeps it to once a frame,
        /// so the group does not depend on one of them to exist.
        /// </summary>
        public void UpdateGroup()
        {
            if (_members.Count == 0 || _lastTickFrame == Time.frameCount)
                return;

            _lastTickFrame = Time.frameCount;
            float deltaTime = Time.deltaTime;

            AdvanceReference(deltaTime);
            // Before the scenario has placed the group, the members stay on the destination they got when
            // they joined: laying them out is PlaceMembersAtSpawn's job. A group without a shared route
            // leaves its members on their own route instead of pinning them in place.
            if (_awaitingPlacement || (!_routeActive && !_routeCompleted))
                return;

            ApplySlots(place: false);
        }

        /// <summary>
        /// Lays every member out on its slot around the anchor, facing the direction the group is about to
        /// walk. Called once the scenario has spawned the whole group, so it starts already formed.
        /// </summary>
        public void PlaceMembersAtSpawn()
        {
            if (_members.Count == 0)
                return;

            _awaitingPlacement = false;
            _speed = 0f;
            ApplySlots(place: true);
        }

        /// <summary>Applies the group's end behavior to every member.</summary>
        public void ApplyEndBehavior(HumanEndBehavior behavior)
        {
            EndBehavior = behavior;
            foreach (HumanAgent member in _members)
                member?.SetGroupEndBehavior(behavior);
        }

        /// <summary>
        /// How far the projected spawn position of a member may sit from the anchor: half a spacing at most,
        /// so a group always appears as one compact bloc instead of being scattered by the NavMesh.
        /// </summary>
        public float SlotTolerance(int index)
        {
            float intended = index > 0 && index < _slots.Count ? _slots[index].magnitude : 0f;
            return intended + Mathf.Max(0.5f, Spacing * 0.5f);
        }

        /// <summary>Unit vector of the group frame's forward axis (local +y of the formation slots).</summary>
        private Vector2 Forward => new(Mathf.Sin(_heading), Mathf.Cos(_heading));

        private Vector2 SlotPosition(int index) =>
            _anchor + GroupFormation.Rotate(_slots[index], _heading);

        /// <summary>
        /// Moves the virtual reference along the shared route. Its speed comes from the cohesion law, so the
        /// group as a whole slows down when somebody trails, and the frame turns at a bounded rate instead of
        /// swinging every member across an arc mid-corner.
        /// </summary>
        private void AdvanceReference(float deltaTime)
        {
            if (!_routeActive || _awaitingPlacement)
            {
                _speed = 0f;
                return;
            }

            // Consume the waypoints already reached before steering towards the next one.
            _anchor = GroupProgress.AdvanceAlong(_route, ref _legIndex, _anchor, 0f, ArriveRadius, out bool completed);
            if (completed || _legIndex >= _route.Count)
            {
                _routeActive = false;
                _speed = 0f;
                CompleteRoute();
                return;
            }

            Vector2 target = _route[_legIndex];
            Vector2 toTarget = target - _anchor;
            float distance = toTarget.magnitude;
            Vector2 direction = distance > 0.0001f ? toTarget / distance : Forward;
            float targetHeading = Mathf.Atan2(direction.x, direction.y);
            float headingError = Mathf.Abs(Mathf.DeltaAngle(_heading * Mathf.Rad2Deg, targetHeading * Mathf.Rad2Deg));

            _speed = GroupProgress.AdvanceSpeed(
                _desiredSpeed,
                WorstSlotError(),
                SlowestMemberSpeed(),
                headingError);
            _anchor = GroupProgress.AdvanceAlong(
                _route,
                ref _legIndex,
                _anchor,
                _speed * deltaTime,
                ArriveRadius,
                out bool arrived);
            _heading = GroupFormation.SteadyHeading(
                _heading,
                targetHeading,
                MaxTurnRateDegrees * Mathf.Deg2Rad,
                deltaTime);

            if (!arrived)
                return;

            _routeActive = false;
            _speed = 0f;
            CompleteRoute();
        }

        /// <summary>What happens once the reference reached the last point of the shared route.</summary>
        private void CompleteRoute()
        {
            _routeCompleted = true;
            switch (EndBehavior)
            {
                case HumanEndBehavior.Loop:
                    // Index 0 is the leg back to the first point; once home the group walks the route again.
                    _legIndex = 0;
                    _routeActive = _route.Count >= 2;
                    return;
                case HumanEndBehavior.Disappear:
                    // Disappearing removes the member from the group, so walk the list backwards.
                    for (int index = _members.Count - 1; index >= 0; index--)
                        _members[index]?.Disappear();
                    return;
                default:
                    // Staying: the reference is parked on the last point, so the members gather on their
                    // final slots and the movement controller stops them there.
                    foreach (HumanAgent member in _members)
                        member?.SetGroupCruiseSpeed(0f);
                    return;
            }
        }

        /// <summary>
        /// Worst distance between a member and its slot. Measuring the whole distance, not only the
        /// along-track part, is what makes the group slow down in a corner: followers cut the turn wide and
        /// the reference waits for them.
        /// </summary>
        private float WorstSlotError()
        {
            float worst = 0f;
            for (int index = 0; index < _members.Count && index < _slots.Count; index++)
            {
                HumanAgent member = _members[index];
                if (member == null || !member.gameObject.activeInHierarchy || member.IsYielding)
                    continue;

                float error = Vector2.Distance(member.Position2D, SlotPosition(index));
                if (error > worst)
                    worst = error;
            }

            return worst;
        }

        /// <summary>Speed of the slowest member: the group walks at that pace once somebody trails.</summary>
        private float SlowestMemberSpeed()
        {
            float slowest = float.PositiveInfinity;
            for (int index = 0; index < _members.Count; index++)
            {
                HumanAgent member = _members[index];
                if (member == null || !member.gameObject.activeInHierarchy || member.IsYielding)
                    continue;

                float speed = member.Velocity2D.magnitude;
                if (speed < slowest)
                    slowest = speed;
            }

            return float.IsPositiveInfinity(slowest) ? 0f : slowest;
        }

        /// <summary>
        /// Gives every member the destination that holds its slot: the slot itself, pushed ahead by the exact
        /// distance at which the movement controller regulates the group speed, so a member settles on its
        /// slot instead of trailing a full slow-down distance behind it.
        /// </summary>
        private void ApplySlots(bool place)
        {
            Vector2 forward = Forward;

            for (int index = 0; index < _members.Count && index < _slots.Count; index++)
            {
                HumanAgent member = _members[index];
                if (member == null)
                    continue;

                Vector2 slot = SlotPosition(index);
                if (place)
                {
                    Vector2 projected = SpawnPlacement.ProjectWithin(slot, _anchor, SlotTolerance(index));
                    member.transform.position = new Vector3(projected.x, member.transform.position.y, projected.y);
                    member.SetGroupDestination(projected);
                    // Start already looking where the group is about to walk.
                    member.FaceTowards(forward);
                    continue;
                }

                float reach = GroupFormation.RequiredGoalDistance(
                    _speed,
                    member.CruiseSpeed,
                    member.SlowDownDistance);

                if (member.IsYielding)
                {
                    // Stepping aside: the slot is shifted by the member's own avoidance offset, and the member
                    // keeps its own pace so the group cannot drag it back into the conflict.
                    member.SetGroupDestination(slot + forward * reach + member.YieldOffset);
                    member.SetGroupCruiseSpeed(0f);
                    continue;
                }

                member.SetGroupDestination(slot + forward * reach);

                // Feedback on the along-track error: a member that fell behind is allowed to walk faster than
                // the reference until it has caught up, one that drifted ahead slows down.
                float lag = Vector2.Dot(slot - member.Position2D, forward);
                float cruise = Mathf.Clamp(_speed + LagGain * lag, 0f, member.MaxSpeed);
                member.SetGroupCruiseSpeed(cruise > MinCruiseOverride ? cruise : 0f);
            }
        }

        /// <summary>Heading of the leg the reference is walking, with a fallback for a missing route.</summary>
        private static float HeadingTowards(IReadOnlyList<Vector2> route, int legIndex, float fallback)
        {
            if (route == null || route.Count < 2)
                return fallback;

            int index = Mathf.Clamp(legIndex, 1, route.Count - 1);
            Vector2 direction = route[index] - route[index - 1];
            return direction.sqrMagnitude > 0.0001f ? Mathf.Atan2(direction.x, direction.y) : fallback;
        }

        private void RebuildSlots()
        {
            _slots.Clear();
            _slots.AddRange(GroupFormation.CreateSlots(_members.Count, Spacing, Formation, Parameter));
            for (int index = 0; index < _members.Count && index < _slots.Count; index++)
                _members[index].SetFormationOffset(_slots[index]);
        }
    }
}
