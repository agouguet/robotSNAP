using System.Collections.Generic;
using UnityEngine;

namespace RobotSNAP.Agents
{
    /// <summary>
    /// Runtime group of humans walking together.
    /// The first member is the leader and owns the route; the others follow a formation slot
    /// expressed in the leader's frame, which keeps the group together without an extra steering rule.
    /// </summary>
    public sealed class HumanGroup
    {
        /// <summary>How fast the group frame latches onto the leader's position.</summary>
        private const float AnchorFollowRate = 12f;

        /// <summary>How fast the smoothed leader velocity tracks the real one.</summary>
        private const float VelocityFollowRate = 6f;

        /// <summary>Maximum rotation speed of the group frame, in degrees per second.</summary>
        private const float MaxTurnRateDegrees = 110f;

        /// <summary>Below this speed the leader counts as stopped, so the frame stops turning.</summary>
        private const float HeadingSpeedThreshold = 0.08f;

        /// <summary>
        /// Grip of the catch-up law: a follower one metre behind its slot is asked for 1.2 m/s extra.
        /// Followers are capped at their own physical speed, so this only closes a gap — it never
        /// launches the group ahead of its leader.
        /// </summary>
        private const float LagGain = 1.2f;

        /// <summary>Under this speed the follower keeps its configured cruise speed.</summary>
        private const float MinCruiseOverride = 0.05f;

        private readonly List<HumanAgent> _members = new();
        private readonly List<Vector2> _slots = new();
        private Vector2 _anchor;
        private Vector2 _velocity;
        private float _heading;
        private bool _frameInitialized;

        public HumanGroup(string id, float spacing, string formation)
        {
            Id = id;
            // Same window as the spawn layout: a group is meant to stay a compact bloc.
            Spacing = Mathf.Clamp(spacing, 0.4f, 3f);
            Formation = GroupFormation.Normalize(formation);
        }

        public string Id { get; }
        public float Spacing { get; }
        public string Formation { get; }
        public int Count => _members.Count;
        public IReadOnlyList<HumanAgent> Members => _members;
        public HumanAgent Leader => _members.Count > 0 ? _members[0] : null;

        public void Add(HumanAgent member)
        {
            if (member == null || _members.Contains(member))
                return;

            bool isLeader = _members.Count == 0;
            _members.Add(member);
            RebuildSlots();
            member.JoinGroup(this, _slots[_members.Count - 1], isLeader);
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

        /// <summary>Called every frame by the leader so members keep their slot around it.</summary>
        public void UpdateMemberDestinations()
        {
            HumanAgent leader = Leader;
            if (leader == null || !leader.gameObject.activeInHierarchy)
                return;

            AdvanceFrame(leader, Time.deltaTime);

            for (int index = 1; index < _members.Count && index < _slots.Count; index++)
            {
                HumanAgent member = _members[index];
                if (member == null)
                    continue;

                Vector2 slot = _anchor + GroupFormation.Rotate(_slots[index], _heading);
                float groupSpeed = _velocity.magnitude;

                // The slot is where the member should stand; the destination is pushed ahead of it by
                // exactly the distance at which the movement controller regulates the group speed, so
                // the member settles on the slot instead of trailing a full slow-down distance behind it.
                float reach = GroupFormation.RequiredGoalDistance(
                    groupSpeed,
                    member.CruiseSpeed,
                    member.SlowDownDistance);
                member.SetGroupDestination(slot + Forward * reach);

                // Feedback on the along-track error: a follower that fell behind is allowed to walk
                // faster than the leader until it has caught up, one that drifted ahead slows down.
                float lag = Vector2.Dot(slot - member.Position2D, Forward);
                float cruise = Mathf.Clamp(groupSpeed + LagGain * lag, 0f, member.MaxSpeed);
                member.SetGroupCruiseSpeed(cruise > MinCruiseOverride ? cruise : 0f);
            }
        }

        /// <summary>Applies the leader's end behavior to every member of the group.</summary>
        public void ApplyEndBehavior(HumanEndBehavior behavior)
        {
            for (int index = 1; index < _members.Count; index++)
                _members[index]?.SetGroupEndBehavior(behavior);
        }

        /// <summary>
        /// Lays every member out on its slot around the leader, rotated by the walking heading.
        /// Called right after the scenario spawns the group so it starts already formed.
        /// </summary>
        public void PlaceMembersAtSpawn(float headingRadians)
        {
            HumanAgent leader = Leader;
            if (leader == null)
                return;

            Vector2 anchor = leader.Position2D;
            _anchor = anchor;
            _velocity = leader.Velocity2D;
            _heading = headingRadians;
            _frameInitialized = true;

            for (int index = 1; index < _members.Count && index < _slots.Count; index++)
            {
                HumanAgent member = _members[index];
                if (member == null)
                    continue;

                Vector2 slot = anchor + GroupFormation.Rotate(_slots[index], headingRadians);
                slot = SpawnPlacement.ProjectWithin(slot, anchor, SlotTolerance(index));
                member.transform.position = new Vector3(slot.x, member.transform.position.y, slot.y);
                member.SetGroupDestination(slot);
            }
        }

        /// <summary>
        /// How far the projected spawn position of a member may sit from the leader.
        /// A member may not drift further than half a spacing away from its intended slot,
        /// so a group always appears as one compact bloc instead of being scattered by the NavMesh.
        /// </summary>
        public float SlotTolerance(int index)
        {
            float intended = index > 0 && index < _slots.Count ? _slots[index].magnitude : 0f;
            return intended + Mathf.Max(0.5f, Spacing * 0.5f);
        }

        /// <summary>Unit vector of the group frame's forward axis (local +y of the formation slots).</summary>
        private Vector2 Forward => new(Mathf.Sin(_heading), Mathf.Cos(_heading));

        /// <summary>
        /// Advances the smoothed group frame: the anchor follows the leader, the velocity is filtered and
        /// the heading turns at a bounded rate. Without that smoothing a leader turning on the spot spins
        /// the whole formation frame in one frame and throws every follower at once.
        /// </summary>
        private void AdvanceFrame(HumanAgent leader, float deltaTime)
        {
            Vector2 leaderPosition = leader.Position2D;
            Vector2 leaderVelocity = leader.Velocity2D;

            if (!_frameInitialized)
            {
                _anchor = leaderPosition;
                _velocity = leaderVelocity;
                _heading = HeadingOf(leader, leaderVelocity, 0f);
                _frameInitialized = true;
                return;
            }

            _anchor = Vector2.Lerp(_anchor, leaderPosition, Blend(AnchorFollowRate, deltaTime));
            _velocity = Vector2.Lerp(_velocity, leaderVelocity, Blend(VelocityFollowRate, deltaTime));

            if (_velocity.magnitude < HeadingSpeedThreshold)
                return;

            float targetHeading = HeadingOf(leader, _velocity, _heading);
            _heading = GroupFormation.SteadyHeading(
                _heading,
                targetHeading,
                MaxTurnRateDegrees * Mathf.Deg2Rad,
                deltaTime);
        }

        private static float HeadingOf(HumanAgent leader, Vector2 velocity, float fallback)
        {
            if (velocity.sqrMagnitude >= HeadingSpeedThreshold * HeadingSpeedThreshold)
                return Mathf.Atan2(velocity.x, velocity.y);

            Vector3 forward = leader != null ? leader.transform.forward : Vector3.forward;
            Vector2 planar = new(forward.x, forward.z);
            return planar.sqrMagnitude > 0.0001f ? Mathf.Atan2(planar.x, planar.y) : fallback;
        }

        /// <summary>Frame-rate independent blend factor of an exponential follow.</summary>
        private static float Blend(float rate, float deltaTime) =>
            1f - Mathf.Exp(-Mathf.Max(0.01f, rate) * Mathf.Max(0f, deltaTime));

        private void RebuildSlots()
        {
            _slots.Clear();
            _slots.AddRange(GroupFormation.CreateSlots(_members.Count, Spacing, Formation));
            for (int index = 0; index < _members.Count && index < _slots.Count; index++)
                _members[index].SetFormationOffset(_slots[index]);
        }
    }
}
