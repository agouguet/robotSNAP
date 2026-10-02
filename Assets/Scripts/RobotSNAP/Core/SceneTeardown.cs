using UnityEngine;

namespace RobotSNAP.Core
{
    /// <summary>
    /// Removes a GameObject in whichever context is running.
    ///
    /// <see cref="UnityEngine.Object.Destroy"/> is refused outside play mode, and this application runs the
    /// same tear-down from the editor: the EditMode suite stops a simulation, and the environment builder
    /// rebuilds the map while a scenario is being authored. In both places the plain call only logged an
    /// error and left the object standing, so a stop emptied the roster while its robots stayed in the
    /// scene. The immediate form is the only one that acts there; it is also the wrong one in play mode,
    /// where a body about to be reached by a physics step has to stay until the frame is over.
    /// </summary>
    public static class SceneTeardown
    {
        /// <summary>Destroys a GameObject now in the editor and at the end of the frame in play mode.</summary>
        public static void Destroy(GameObject target)
        {
            if (target == null)
                return;

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                Object.DestroyImmediate(target);
                return;
            }
#endif
            Object.Destroy(target);
        }
    }
}
