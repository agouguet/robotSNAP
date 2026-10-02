using System;
using System.Collections.Generic;

namespace RobotSNAP.Metrics
{
    /// <summary>
    /// One agent's path through one episode: the samples <c>[t, x, z]</c> a top-down view draws, spaced at the
    /// control step, held inside a fixed point budget.
    ///
    /// A trajectory an hour of training would collect is unbounded, and an episode document that grows with
    /// the episode is a document a dashboard cannot open and an exporter cannot rewrite after every episode.
    /// So the buffer keeps at most <c>capacity</c> points and, once full, halves its resolution: every second
    /// point is dropped and the stride doubles. The path keeps its full extent - the first and the last sample
    /// survive every halving, so the drawn line still starts and ends where the robot did - and its shape
    /// stays representative, at the cost of the detail between two kept points. A caller that needs the exact
    /// count of dropped points reads <see cref="Stride"/>: it is the factor between what was measured and what
    /// is kept, and it is written into the episode document for exactly that reason.
    ///
    /// Samples are stored at millimetre and millisecond precision. A trajectory is what a dashboard draws and
    /// what a reader plots, and seventeen significant digits of a position the robot never stood at to the
    /// nanometre are bytes nothing consumes: the metrics themselves are computed from the raw samples, never
    /// from what this buffer kept.
    /// </summary>
    public sealed class TrajectoryBuffer
    {
        private const int Decimals = 3;
        private const int MinimumCapacity = 2;

        private readonly List<double[]> _points;
        private readonly int _capacity;
        private int _stride = 1;
        private int _sinceLastKept;

        /// <summary>A buffer that keeps at most <paramref name="capacity"/> points, floored at two.</summary>
        public TrajectoryBuffer(int capacity)
        {
            _capacity = capacity < MinimumCapacity ? MinimumCapacity : capacity;
            _points = new List<double[]>(_capacity);
        }

        /// <summary>Samples measured for every one kept; 1 while nothing has been dropped.</summary>
        public int Stride => _stride;

        /// <summary>The kept samples, oldest first, each one <c>{ t, x, z }</c>.</summary>
        public IReadOnlyList<double[]> Points => _points;

        /// <summary>Number of samples kept.</summary>
        public int Count => _points.Count;

        /// <summary>
        /// Records one sample, halving the resolution when the budget is reached. The phase is reset on a
        /// halving, so every kept point sits on a multiple of the current stride from the halving point
        /// onwards instead of drifting by one every time.
        /// </summary>
        public void Add(double worldSeconds, double x, double z)
        {
            if (_sinceLastKept == 0)
                _points.Add(new[] { Round(worldSeconds), Round(x), Round(z) });

            _sinceLastKept++;
            if (_sinceLastKept >= _stride)
                _sinceLastKept = 0;

            if (_points.Count >= _capacity)
                Halve();
        }

        private void Halve()
        {
            for (int read = 2, write = 1; read < _points.Count; read += 2, write++)
                _points[write] = _points[read];

            int kept = (_points.Count + 1) / 2;
            _points.RemoveRange(kept, _points.Count - kept);
            _stride *= 2;
            _sinceLastKept = 0;
        }

        private static double Round(double value) => Math.Round(value, Decimals, MidpointRounding.AwayFromZero);
    }
}
