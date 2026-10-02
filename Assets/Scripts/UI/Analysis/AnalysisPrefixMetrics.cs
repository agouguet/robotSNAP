using RobotSNAP.Metrics;

/// <summary>
/// One episode read up to one instant instead of read whole.
///
/// Every field carries the same meaning as its twin on <see cref="EpisodeMetrics"/> - the same units, the
/// same definitions, the same no-human sentinel - because the two are meant to be compared side by side:
/// the detail panel shows the prefix and the final value of the same quantity, and a reader has to be able
/// to trust that the difference between them is the episode, not the arithmetic. Only the metrics a
/// trajectory can answer are here; the identity of the run, its control steps and its straight line to the
/// goal are properties of the whole episode and are not something a prefix can shorten.
/// </summary>
public readonly struct AnalysisPrefixMetrics
{
    public AnalysisPrefixMetrics(
        double seconds,
        double pathLengthMetres,
        double averageSpeedMetresPerSecond,
        double maxSpeedMetresPerSecond,
        double minHumanDistanceMetres,
        double averageHumanDistanceMetres,
        double minClearanceMetres,
        int personalSpaceIntrusions,
        double personalSpaceSeconds)
    {
        HasRobot = true;
        Seconds = seconds;
        PathLengthMetres = pathLengthMetres;
        AverageSpeedMetresPerSecond = averageSpeedMetresPerSecond;
        MaxSpeedMetresPerSecond = maxSpeedMetresPerSecond;
        MinHumanDistanceMetres = minHumanDistanceMetres;
        AverageHumanDistanceMetres = averageHumanDistanceMetres;
        MinClearanceMetres = minClearanceMetres;
        PersonalSpaceIntrusions = personalSpaceIntrusions;
        PersonalSpaceSeconds = personalSpaceSeconds;
    }

    /// <summary>
    /// False when the episode kept no robot path, which is the one case where nothing can be read up to an
    /// instant and a caller has to fall back to the recorded values instead of inventing a zero.
    /// </summary>
    public bool HasRobot { get; }

    /// <summary>Seconds covered by the prefix, never more than the episode's own world duration.</summary>
    public double Seconds { get; }

    /// <summary>Length of the polyline the robot walked in that time, in metres.</summary>
    public double PathLengthMetres { get; }

    /// <summary><see cref="PathLengthMetres"/> over <see cref="Seconds"/>.</summary>
    public double AverageSpeedMetresPerSecond { get; }

    /// <summary>Fastest instantaneous speed between two samples of the prefix, in metres per second.</summary>
    public double MaxSpeedMetresPerSecond { get; }

    /// <summary>Closest the robot came to a human so far, or <see cref="EpisodeMetrics.NoHumanDistance"/>.</summary>
    public double MinHumanDistanceMetres { get; }

    /// <summary>Mean over the prefix's samples of the distance to the nearest human.</summary>
    public double AverageHumanDistanceMetres { get; }

    /// <summary>
    /// Smallest gap between the two bodies so far, or <see cref="EpisodeMetrics.NoHumanDistance"/>: the human
    /// distance with the robot's and the crowd's footprints taken off, which is the quantity the intrusions
    /// are counted on.
    /// </summary>
    public double MinClearanceMetres { get; }

    /// <summary>Times the robot entered a human's personal space so far.</summary>
    public int PersonalSpaceIntrusions { get; }

    /// <summary>Seconds spent with at least one human inside that space, so far.</summary>
    public double PersonalSpaceSeconds { get; }
}
