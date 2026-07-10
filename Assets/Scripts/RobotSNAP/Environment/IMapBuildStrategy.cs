using UnityEngine;
using System.Collections;

namespace RobotSNAP.Environment
{
    public interface IMapBuildStrategy
    {
        IEnumerator Build(EnvironmentBuilder builder, string mapName, Bounds? worldBounds);
        void Clear(EnvironmentBuilder builder);
    }
}