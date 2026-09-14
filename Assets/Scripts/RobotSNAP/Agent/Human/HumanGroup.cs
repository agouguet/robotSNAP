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
        private readonly List<HumanAgent> _members = new();
        private readonly List<Vector2> _slots = new();

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

            Vector2 forward = leader.Velocity2D;
            if (forward.sqrMagnitude < 0.01f)
            {
                Vector3 leaderForward = leader.transform.forward;
                forward = new Vector2(leaderForward.x, leaderForward.z);
            }
            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector2.up;

            forward.Normalize();
            float yaw = Mathf.Atan2(forward.x, forward.y);
            Vector2 leaderPosition = leader.Position2D;
            // Small lead term: without it the followers trail several metres behind a moving leader.
            Vector2 lead = leader.Velocity2D * 0.35f;

            for (int index = 1; index < _members.Count && index < _slots.Count; index++)
            {
                HumanAgent member = _members[index];
                if (member == null)
                    continue;
                member.SetGroupDestination(leaderPosition + lead + GroupFormation.Rotate(_slots[index], yaw));
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

        private void RebuildSlots()
        {
            _slots.Clear();
            _slots.AddRange(GroupFormation.CreateSlots(_members.Count, Spacing, Formation));
            for (int index = 0; index < _members.Count && index < _slots.Count; index++)
                _members[index].SetFormationOffset(_slots[index]);
        }
    }
}
