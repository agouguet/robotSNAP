using System.Linq;
using System.Collections.Generic;
using System.Collections;
using UnityEngine;

/// <summary>
///     This script initializes the articulation bodies by 
///     setting stiffness, damping and force limit of
///     the non-fixed ones.
/// </summary>
public class ArticulationBodyInitialization : MonoBehaviour
{
    private ArticulationBody[] articulationChain;
    private HashSet<ArticulationBody> _driven;

    public GameObject robotRoot;
    public bool assignToAllChildren = true;
    public int robotChainLength = 0;
    public float stiffness = 10000f;
    public float damping = 100f;
    public float forceLimit = 1000f;

    void Start()
    {
        _driven = DrivenJoints();

        // Get non-fixed joints
        articulationChain = robotRoot.GetComponentsInChildren<ArticulationBody>();
        articulationChain = articulationChain.Where(
            joint => joint.jointType != ArticulationJointType.FixedJoint
        ).ToArray();

        // A driven wheel is not this component's business: the chassis controller gives it the drive type,
        // the gain and the friction a commanded wheel needs. Neither is a caster: the controller leaves it
        // free so it can swivel, and braking it here is what turned the casters of the Kuri into skids that
        // ate a third of the commanded turn. Both used to write the same joints as this loop, and which one
        // won depended on the order Unity happens to call two Start methods in - which is how a wheel ended
        // up commanded as a velocity and braked as if it were an arm.
        articulationChain = articulationChain.Where(joint => !_driven.Contains(joint)).ToArray();

        // Joint length to assign
        int assignLength = articulationChain.Length;
        if (!assignToAllChildren)
            assignLength = robotChainLength;

        // Setting stiffness, damping and force limit
        int defDyanmicVal = 100;
        for (int i = 0; i < assignLength; i++)
        {
            ArticulationBody joint = articulationChain[i];
            ArticulationDrive drive = joint.xDrive;

            joint.jointFriction = defDyanmicVal;
            joint.angularDamping = defDyanmicVal;

            drive.stiffness = stiffness;
            drive.damping = damping;
            drive.forceLimit = forceLimit;
            joint.xDrive = drive;
        }

        Debug.Log($"[ArticulationBodyInitialization] {assignLength} joint(s) of {robotRoot.name} set to " +
                  $"stiffness={stiffness}, damping={damping}, forceLimit={forceLimit}.");
    }

    /// <summary>
    /// The joints a wheel controller drives, so this component leaves them alone.
    /// </summary>
    private HashSet<ArticulationBody> DrivenJoints()
    {
        var driven = new HashSet<ArticulationBody>();
        if (robotRoot == null)
            return driven;

        foreach (ArticulationWheelController chassis in robotRoot.GetComponentsInChildren<ArticulationWheelController>(true))
        {
            AddIfPresent(driven, chassis.leftWheel);
            AddIfPresent(driven, chassis.rightWheel);

            if (chassis.leftWheels != null)
                foreach (ArticulationBody wheel in chassis.leftWheels)
                    AddIfPresent(driven, wheel);
            if (chassis.rightWheels != null)
                foreach (ArticulationBody wheel in chassis.rightWheels)
                    AddIfPresent(driven, wheel);

            if (chassis.freeRollers != null)
                foreach (ArticulationBody roller in chassis.freeRollers)
                    AddIfPresent(driven, roller);
        }

        return driven;
    }

    private static void AddIfPresent(HashSet<ArticulationBody> set, ArticulationBody wheel)
    {
        if (wheel != null)
            set.Add(wheel);
    }
    
    void Update()
    {
    }
}
