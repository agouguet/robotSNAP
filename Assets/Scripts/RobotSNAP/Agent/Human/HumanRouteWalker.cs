using System.Collections.Generic;
using UnityEngine;

namespace RobotSNAP.Agents
{
    /// <summary>
    /// Pure ordered-route cursor: walks a list of points from the first to the last one.
    /// Kept free of Unity state so the progression (including looping) can be unit tested.
    /// </summary>
    public sealed class HumanRouteWalker
    {
        private readonly List<Vector3> _points = new();

        public HumanEndBehavior EndBehavior { get; set; } = HumanEndBehavior.Stay;
        public IReadOnlyList<Vector3> Points => _points;
        public int Index { get; private set; } = -1;
        public int Loops { get; private set; }
        public bool IsActive { get; private set; }
        public bool HasCurrent => IsActive && Index >= 0 && Index < _points.Count;
        public Vector3 Current => HasCurrent ? _points[Index] : Vector3.zero;

        /// <summary>Starts the route again from its first point.</summary>
        public void SetRoute(IEnumerable<Vector3> points)
        {
            _points.Clear();
            if (points != null)
            {
                foreach (Vector3 point in points)
                    _points.Add(point);
            }

            Loops = 0;
            Index = -1;
            IsActive = _points.Count > 0;
            if (IsActive)
                Index = 0;
        }

        public void Clear()
        {
            _points.Clear();
            Index = -1;
            Loops = 0;
            IsActive = false;
        }

        /// <summary>
        /// Moves to the next point.
        /// Returns true when the route is finished, false when there is still a point to reach
        /// (a looping route wraps around instead of finishing).
        /// </summary>
        public bool AdvanceNext()
        {
            if (!IsActive)
                return true;

            Index++;
            if (Index < _points.Count)
                return false;

            if (EndBehavior == HumanEndBehavior.Loop && _points.Count > 0)
            {
                Loops++;
                Index = 0;
                return false;
            }

            IsActive = false;
            Index = _points.Count;
            return true;
        }
    }
}
