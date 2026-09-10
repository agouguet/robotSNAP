using RobotSNAP.Core;
using UnityEngine;

namespace RobotSNAP.Core.Scenario
{
    /// <summary>
    /// Applies the global runtime settings required before a scenario starts.
    /// </summary>
    public static class SimulationRuntimeSettings
    {
        public static int Apply(SimulationConfig config, int fallbackSeed)
        {
            if (config == null) return fallbackSeed;

            int seed = config.RandomSeed == -1 ? fallbackSeed : config.RandomSeed;
            Random.InitState(seed);
            config.ApplyTimeSettings();
            return seed;
        }
    }
}
