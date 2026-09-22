using System.Collections.Generic;
using RobotSNAP.Agents;
using UnityEngine;

/// <summary>
/// Turns a linear/angular velocity pair into motion for a wheeled base.
///
/// A side may carry more than one wheel: the Jackal has four wheels driven by two motors, so its front
/// and rear wheel of a side always turn together. The pair of single wheels the base robot was built
/// with is still the first wheel of each side, which is why both are kept - an old prefab that only ever
/// named one wheel per side keeps driving exactly as it did.
///
/// Two chassis are not the same problem, and this component drives both:
///
/// <b>WheelVelocity</b> is for a differential drive with one wheel per side and casters. The two wheels
/// sit at the corners of a short baseline, the casters scrub freely, and friction at the two contact
/// patches turns the chassis exactly the way the model says. The wheels are therefore commanded directly
/// and the robot follows. This is the model the base robot shipped with and it is left untouched.
///
/// <b>SkidSteerBase</b> is for a four-wheel skid steer, whose wheels cannot scrub. PhysX has one friction
/// coefficient per contact - it cannot roll in one direction while sliding in the other - so a skid steer
/// whose wheels grip as hard as the floor simply goes straight however different its two sides are
/// commanded: the solver refuses the conflict and lets both sides settle on a common speed. Measured on
/// the Jackal, the wheels do reach the commanded 1.09 / 0.91 m/s differential while the base turns the
/// wrong way at a sixth of the commanded rate. What this model does about it is to stop asking friction
/// for something it cannot give: the base itself is driven by the force a differential drive's tyres could
/// pass, and the wheels are rolled at the speed that motion implies, so their contact never has to slide.
/// The force is capped by the tyre's grip, which is what keeps the contacts - a wall, a kerb, a person -
/// able to answer the drive instead of being walked over.
/// </summary>
public class ArticulationWheelController : MonoBehaviour, IRobotDrive
{
    /// <summary>Which of the two chassis the wheels belong to decides who carries the motion.</summary>
    public enum DriveModel
    {
        /// <summary>The wheels carry the robot through their contact with the ground.</summary>
        WheelVelocity,

        /// <summary>The base is driven to the commanded velocity; the wheels follow it.</summary>
        SkidSteerBase,

    }

    [Header("Wheel References")]
    public ArticulationBody leftWheel;
    public ArticulationBody rightWheel;

    [Tooltip("Every wheel of the left side. Empty means the single left wheel above.")]
    public List<ArticulationBody> leftWheels = new List<ArticulationBody>();

    [Tooltip("Every wheel of the right side. Empty means the single right wheel above.")]
    public List<ArticulationBody> rightWheels = new List<ArticulationBody>();

    [Tooltip(
        "The joints the model declares free - the casters of a Kuri or a Bibus. Nothing drives them, and " +
        "they must not be braked either: a caster that does not swivel is a skid, and a skid is a wheel " +
        "that fights every turn the chassis makes.")]
    public List<ArticulationBody> freeRollers = new List<ArticulationBody>();

    [Header("Robot Geometry")]
    public float wheelTrackLength; // distance between wheels (meters)
    public float wheelRadius;       // wheel radius (meters)

    [Header("Wheel drive")]
    [Tooltip("Gain of the wheel velocity drive. Kept at the value the base robot was tuned with.")]
    public float driveDamping = 0.5f;

    [Tooltip("Highest torque the wheel drive may apply, in N.m.")]
    public float driveForceLimit = 1000f;

    [Tooltip(
        "Torque the wheel drive is left with when the base carries the motion. The wheels of that model " +
        "roll to be seen and to keep the odometry honest; they must not have the strength to decide the " +
        "chassis. Measured on the Jackal, a wheel drive left at two newton-metres held the commanded turn " +
        "to 90 percent - four wheels pushing twenty newtons each against the ground - and the same turn " +
        "reaches 98 percent once the wheels are left with a fifth of that. Nothing here moves the robot: " +
        "the base does, and the wheels only have to turn fast enough to look right.")]
    public float rollingWheelForceLimit = 0.2f;

    [Tooltip("Internal friction of the wheel joint. A wheel that is commanded must not be braked by it.")]
    public float jointFriction = 0f;

    [Tooltip("Passive angular drag of the wheel. Near zero: the drive, not the drag, decides its speed.")]
    public float angularDamping = 0.05f;

    [Header("PhysX solver")]
    [Tooltip("Position iterations of the solver for this articulation. Unity's default of 6 is kept.")]
    public int solverIterations = 6;

    [Tooltip(
        "Velocity iterations of the solver for this articulation. Unity defaults to 1, which is not enough " +
        "for the contacts of a wheel that has to scrub while it rolls. 16 is where the behaviour of this " +
        "chassis stops changing.")]
    public int solverVelocityIterations = 16;

    [Header("Chassis model")]
    [Tooltip(
        "WheelVelocity for a differential drive with one wheel per side, SkidSteerBase for a four-wheel " +
        "skid steer. The chassis model is a property of the wheel layout, not a taste.")]
    public DriveModel driveModel = DriveModel.WheelVelocity;

    [Tooltip(
        "How hard the skid steer base may change its speed, in m/s per second. The command is reached at " +
        "this rate instead of instantly, which is what a real chassis does and what keeps the impulse the " +
        "controller asks for inside what the solver can resolve.")]
    public float maxLinearAcceleration = 6f;

    [Tooltip("How hard the skid steer base may change its yaw rate, in rad/s per second.")]
    public float maxAngularAcceleration = 10f;

    [Tooltip(
        "How much of the sideways velocity the skid steer base removes in one step, from 0 (free to slide) " +
        "to 1 (never slides). A wheeled base does not slide sideways: that constraint is what makes the " +
        "commanded turn happen instead of the robot being carried outwards.")]
    [Range(0f, 1f)]
    public float lateralGrip = 1f;

    [Tooltip(
        "Rate at which the lean of the chassis is brought back, in rad/s per second. A four-wheel chassis " +
        "has no suspension, so without this a contact with a kerb leaves it tilted.")]
    public float tiltRecovery = 6f;

    [Tooltip(
        "Highest force the base drive may ever apply, in N. Zero derives it from the tyres: grip times the " +
        "weight of the robot, which is all a wheel can pass before it slips. This ceiling is what a " +
        "collision pushes against - a wall stops the robot because the drive cannot out-ask the contact, " +
        "and a person leaning on a moving robot slows it down for the same reason.")]
    public float maxDriveForce = 0f;

    [Tooltip(
        "Grip of the tyres on the ground, as a friction coefficient. It is the traction the drive is " +
        "allowed to use, and it is read as a property of the robot, not of the floor.")]
    public float tractionGrip = 0.7f;

    [Tooltip(
        "Arm the traction turns the chassis on, in metres. Zero means half the wheel track, which is what " +
        "the two sides of a differential drive actually sit at.")]
    public float tractionLever = 0f;

    [Tooltip(
        "How fast the drive makes up the effort a resistance is taking from it, in 1/s^2. The drive itself " +
        "is deadbeat - it asks for exactly the force that would put the chassis at its commanded speed this " +
        "step - and a deadbeat drive has no answer to a contact that eats part of that force every step: " +
        "measured on the Freight, its wheels scrubbing sideways took thirty-nine newton-metres while the " +
        "controller asked for thirty-five and the robot simply stood still. This term is the integral every " +
        "wheel controller has, bounded by the same traction budget as everything else so that a robot held " +
        "against a wall pushes with its tyres and no more.")]
    public float driveIntegralGain = 30f;

    [Tooltip(
        "The error the drive stops accumulating effort for, in m/s and rad/s. Below it the robot is already " +
        "where it was told to be, and continuing to add effort there is how a robot ends up driving faster " +
        "than its command.")]
    public float driveIntegralDeadband = 0.02f;

    [Tooltip(
        "Rate at which the accumulated effort is let go when the robot is already past its command, in 1/s. " +
        "It is what keeps the effort that broke a robot free of its wheels from staying on once it is " +
        "rolling, without taking that effort away while it is still needed: a plain leak, applied whether " +
        "the robot is behind or ahead, cost the Bibus its spin - it fell from 94 to 5 percent of the " +
        "commanded turn, because the effort it takes to break its casters loose was bled off as fast as it " +
        "was built.")]
    public float driveIntegralUnwind = 6f;

    // Current speeds (for UI / debug)
    private float _currentLinearSpeed;
    private float _currentAngularSpeed;

    /// <summary>The body the articulation hangs from, resolved once, for the drives that act on the base.</summary>
    private ArticulationBody _articulationRoot;

    /// <summary>
    /// Effort the drive has had to add to hold its command, in newtons and in newton-metres. It is what the
    /// force and the torque would otherwise have to ask for all at once, and it is bounded by the traction.
    /// </summary>
    private Vector3 _forceIntegral;
    private float _torqueIntegral;

    public float CurrentLinearSpeed => _currentLinearSpeed;
    public float CurrentAngularSpeed => _currentAngularSpeed;

    // Threshold to consider angular speed as "straight line"
    private const float ANGULAR_THRESHOLD = 0.01f;

    private void Awake()
    {
        ConfigureArticulation();
        _articulationRoot = ArticulationRoot();

        foreach (ArticulationBody wheel in AllWheels())
            ConfigureWheel(wheel);

        foreach (ArticulationBody roller in AllRollers())
            ConfigureRoller(roller);
    }

    private void Start()
    {
        // Done again here, and not only in Awake, because another component of the prefab configures every
        // joint of the chain at its own start: whichever of the two runs last is the one that counts, and
        // the order Unity picks between two Start methods is not something a prefab should depend on.
        ConfigureArticulation();
        _articulationRoot = ArticulationRoot();

        foreach (ArticulationBody wheel in AllWheels())
            ConfigureWheel(wheel);

        foreach (ArticulationBody roller in AllRollers())
            ConfigureRoller(roller);
    }

    /// <summary>
    /// Drives the base with the force a wheeled chassis could really apply, once per physics step.
    ///
    /// The command is asked of the base rather than of its wheels, and that is the whole point of the
    /// model. Isotropic friction cannot both hold a wheel to its path and let it scrub, so a four-wheel
    /// skid steer whose motion is left to its contacts turns at roughly two thirds of what it is told no
    /// matter how much torque it is given: measured on the Jackal, the wheels reach their commanded
    /// 1.09 / 0.91 m/s differential while the chassis delivers 69% of the commanded yaw at any torque up to
    /// twenty times what the drive was asking for. Twenty times the torque changing nothing is the proof
    /// that the limit is kinematic, not dynamic. The base is therefore driven directly, and the wheels are
    /// rolled at the speed that motion implies for them, so their contact never has to slide.
    ///
    /// What the base is given is a <b>force</b> and not a velocity. The difference is the whole of the
    /// collision behaviour: a velocity written on the body every step cannot be slowed by anything, so a
    /// robot driven that way walks through a crowd - it is put back at its commanded speed after every
    /// contact the solver resolves, and pushes with an unbounded force. A force can be answered. The drive
    /// computes the change of velocity it wants this step, turns it into the force that would produce it,
    /// and clamps that force to what the tyres can pass before they slip
    /// (<see cref="tractionGrip"/> times the weight, or <see cref="maxDriveForce"/> when it is set). Against
    /// a wall, or against a person who leans on the robot, the contact now wins because the drive has
    /// nothing left to answer with: the robot is stopped or slowed by the collision instead of overriding
    /// it, and the wheels keep turning while the body does not.
    ///
    /// The same ceiling is put on the yaw torque, with the traction acting at <see cref="tractionLever"/>.
    /// In free space the budgets are far above what the command needs - the acceleration limits
    /// <see cref="maxLinearAcceleration"/> and <see cref="maxAngularAcceleration"/> bind first - so a robot
    /// alone on the floor still tracks what it is told to the percent. Gravity is left alone: the chassis
    /// is held up by its contacts and not by this controller, and it is never teleported.
    /// </summary>
    private void FixedUpdate()
    {
        if (_articulationRoot == null)
            return;

        if (driveModel != DriveModel.SkidSteerBase)
            return;

        DriveBase();
    }

    private void DriveBase()
    {
        ArticulationBody body = _articulationRoot;
        body.WakeUp();

        float step = Time.fixedDeltaTime;
        Vector3 forward = body.transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 1e-6f)
            forward = Vector3.forward;
        forward.Normalize();
        Vector3 side = Vector3.Cross(Vector3.up, forward);

        // Where the robot should be going: forward at the commanded speed, with the sideways part of its
        // present velocity removed to the degree lateralGrip asks for. A chassis with wheels does not slide.
        Vector3 velocity = body.linearVelocity;
        Vector3 planar = new Vector3(velocity.x, 0f, velocity.z);
        float sideways = Vector3.Dot(planar, side);
        Vector3 wanted = forward * _currentLinearSpeed + side * (sideways * (1f - lateralGrip));

        // The change of velocity this step is allowed to ask for, and the force that produces it on the
        // mass of the robot. Traction then caps it: whatever the command, a tyre cannot pass more force
        // than its grip times the weight it carries, and the difference is exactly what a collision is.
        float mass = Mathf.Max(0.001f, body.mass);
        float traction = TractionForce(mass);
        Vector3 error = wanted - planar;
        Vector3 change = Vector3.ClampMagnitude(error, Mathf.Max(0f, maxLinearAcceleration) * step);
        Vector3 force = change / step * mass;

        bool ahead = Vector3.Dot(_forceIntegral, error) < 0f;
        if (ahead)
            _forceIntegral *= Mathf.Clamp01(1f - (driveIntegralUnwind * step));

        if (!ahead && error.magnitude > driveIntegralDeadband)
            _forceIntegral += error * (driveIntegralGain * mass * step);
        _forceIntegral = traction > 0f
            ? Vector3.ClampMagnitude(_forceIntegral, traction)
            : _forceIntegral;
        force += _forceIntegral;

        if (traction > 0f)
            force = Vector3.ClampMagnitude(force, traction);

        body.AddForce(force);

        // Yaw, then the lean: the same bounded correction, on the two rotations that make a chassis move
        // like a chassis instead of like a body dragged along the floor.
        //
        // The sign is the one the rest of the application uses, and it is not the sign Unity puts on its Y
        // axis: a positive angular command means a turn to the left, which is a *decreasing* Unity yaw,
        // since Unity turns clockwise around +Y. Reading the yaw rate without that flip made the base pull
        // against its own wheels, and the two of them settled at half the commanded rate.
        float inertia = InertiaAboutUp(body);
        float yawLimit = Mathf.Max(0f, maxAngularAcceleration) * step;
        Vector3 spin = body.angularVelocity;
        float yawError = -_currentAngularSpeed - spin.y;
        float yawChange = Mathf.Clamp(yawError, -yawLimit, yawLimit);
        float torque = yawChange / step * inertia;

        bool turningTooFar = _torqueIntegral * yawError < 0f;
        if (turningTooFar)
            _torqueIntegral *= Mathf.Clamp01(1f - (driveIntegralUnwind * step));

        if (!turningTooFar && Mathf.Abs(yawError) > driveIntegralDeadband)
            _torqueIntegral += yawError * (driveIntegralGain * inertia * step);
        if (traction > 0f)
        {
            float torqueBudget = traction * TractionLever();
            _torqueIntegral = Mathf.Clamp(_torqueIntegral, -torqueBudget, torqueBudget);
            torque += _torqueIntegral;
            torque = Mathf.Clamp(torque, -torqueBudget, torqueBudget);
        }
        else
        {
            torque += _torqueIntegral;
        }

        body.AddTorque(Vector3.up * torque);

        // The lean is brought back by a torque of the same shape, through the same inertia. It is a
        // stabiliser and not a tyre force, so it is not asked of the traction budget.
        float tiltLimit = Mathf.Max(0f, tiltRecovery) * step;
        float pitchChange = Mathf.Clamp(-spin.x, -tiltLimit, tiltLimit);
        float rollChange = Mathf.Clamp(-spin.z, -tiltLimit, tiltLimit);
        body.AddTorque(new Vector3(pitchChange / step * inertia, 0f, rollChange / step * inertia));
    }

    /// <summary>
    /// The force the tyres of this robot can pass before they slip: the grip written on the robot times
    /// the weight it carries, or the explicit ceiling when one is given.
    ///
    /// A robot with no mass and no ceiling asks for no limit at all, which is what a prefab built without
    /// physics gets: it is then driven the way the model always drove it, and the collision behaviour is
    /// the solver's business alone.
    /// </summary>
    private float TractionForce(float mass)
    {
        if (maxDriveForce > 0f)
            return maxDriveForce;

        if (tractionGrip <= 0f)
            return 0f;

        return tractionGrip * mass * Mathf.Abs(Physics.gravity.y);
    }

    /// <summary>Half the wheel track: where the two driven sides of a differential chassis sit.</summary>
    private float TractionLever()
    {
        if (tractionLever > 0f)
            return tractionLever;

        return wheelTrackLength > 0.001f ? wheelTrackLength * 0.5f : 0.2f;
    }

    /// <summary>
    /// Moment of inertia of the base around the vertical axis, read from the body and falling back on the
    /// disc a chassis of this radius would be. It is what turns a wanted change of yaw rate into a torque.
    /// </summary>
    private float InertiaAboutUp(ArticulationBody body)
    {
        float about = Mathf.Abs(body.inertiaTensor.y);
        if (about > 1e-4f)
            return about;

        float radius = Mathf.Max(0.05f, wheelTrackLength * 0.5f);
        return Mathf.Max(0.01f, body.mass * radius * radius * 0.5f);
    }

    /// <summary>
    /// Gives the whole articulation the solver budget a driven vehicle needs.
    ///
    /// A wheeled base turns by asking its left and right wheels for different speeds, and the contacts are
    /// what lets the chassis follow. PhysX solves those contacts with a fixed number of velocity iterations
    /// per step, and Unity ships one. It is written on every body of the articulation rather than on the
    /// wheels alone because the solver budget is read from the articulation, and a prefab that nests its
    /// wheels under a mount must not depend on which body happens to be visited first.
    /// </summary>
    private void ConfigureArticulation()
    {
        ArticulationBody root = ArticulationRoot();
        if (root == null)
            return;

        // A base this component drives itself has to be a body the solver is free to move: an immovable
        // base ignores both the forces of the drive and the push of a contact, and the robot would then be
        // a wall of its own. The kinematic drive of a model without wheels is the one that asks for the
        // opposite, and it keeps doing so on its own.
        //
        // The two flags are only written when they are wrong, and that is not a micro-optimisation: writing
        // either one of them rebuilds the articulation, which puts every body of the chain back at the pose
        // it was authored with. Written from Start - which is called after the scenario has placed the
        // robot - a needless write teleports the robot back to the origin of the scene, wheels included,
        // which is exactly what it did before this comment existed.
        if (driveModel == DriveModel.SkidSteerBase)
        {
            if (root.immovable)
                root.immovable = false;
            if (!root.useGravity)
                root.useGravity = true;
        }

        int positionIterations = Mathf.Max(1, solverIterations);
        int velocityIterations = Mathf.Max(1, solverVelocityIterations);

        foreach (ArticulationBody body in root.GetComponentsInChildren<ArticulationBody>(true))
        {
            body.solverIterations = positionIterations;
            body.solverVelocityIterations = velocityIterations;
        }
    }

    /// <summary>
    /// The body the articulation hangs from, found by walking up from a wheel and stopping at the first
    /// body that has no ArticulationBody above it.
    ///
    /// The wheel is the starting point, and not this component, because the controller sits beside the
    /// articulation and not inside it - the project keeps its scripts on a separate object - so asking
    /// from here would look up a branch that carries no physics at all and find nothing.
    ///
    /// Null when this chassis drives no articulation, which is a body somebody built without physics.
    /// </summary>
    private ArticulationBody ArticulationRoot()
    {
        ArticulationBody candidate = null;
        foreach (ArticulationBody wheel in AllWheels())
        {
            candidate = wheel;
            break;
        }

        if (candidate == null)
            return null;

        while (candidate != null)
        {
            ArticulationBody above = candidate.transform.parent != null
                ? candidate.transform.parent.GetComponentInParent<ArticulationBody>()
                : null;

            if (above == null)
                return candidate;

            candidate = above;
        }

        return null;
    }

    /// <summary>
    /// Makes one joint behave like a driven wheel.
    ///
    /// A joint imported from a URDF comes out with the wrong settings for this: its drive type is Force,
    /// so a velocity handed to it does nothing at all, and the body initialiser leaves it with a joint
    /// friction and an angular damping of 100, which brake it permanently. A wheel that is commanded is
    /// therefore given the drive it needs and freed of the drag that fights it.
    /// </summary>
    private void ConfigureWheel(ArticulationBody wheel)
    {
        if (wheel == null)
            return;

        ArticulationDrive drive = wheel.xDrive;
        if (drive.driveType != ArticulationDriveType.Velocity)
            drive.driveType = ArticulationDriveType.Velocity;

        // A joint imported from a URDF arrives with both limits at zero, which describes a joint locked at
        // its rest angle. A wheel turns freely, so it is given a full turn of travel: whatever mode the
        // joint was left in, nothing can hold it inside a range that has no width.
        drive.lowerLimit = -360f;
        drive.upperLimit = 360f;
        drive.damping = driveDamping;
        drive.forceLimit = driveForceLimit > 0f ? driveForceLimit : drive.forceLimit;

        // A wheel that drives the robot has to be strong. A wheel that only rolls while the base is driven
        // must not be: at full strength its contact pins the chassis to the speed of the contact, and the
        // base then turns as little as the contact allows rather than as much as it was told.
        if (driveModel == DriveModel.SkidSteerBase && rollingWheelForceLimit > 0f)
            drive.forceLimit = rollingWheelForceLimit;

        wheel.xDrive = drive;

        wheel.jointFriction = jointFriction;
        wheel.angularDamping = angularDamping;
    }

    /// <summary>
    /// Leaves a joint the model declares free free.
    ///
    /// A caster is a small wheel on a swivel, and the importer hands it to the articulation initialiser as
    /// just another joint - which brakes it with a joint friction and an angular damping of a hundred, the
    /// values an arm is given. Measured on the Kuri, whose two casters came out that way, the braked
    /// casters cost a third of the commanded turn once the drive was an honest force instead of a velocity
    /// written on the chassis: the drive had to spend its traction dragging two joints nobody asked to be
    /// braked in the first place. A caster is given no drive and no drag, so it follows.
    /// </summary>
    private void ConfigureRoller(ArticulationBody roller)
    {
        if (roller == null)
            return;

        ArticulationDrive drive = roller.xDrive;
        drive.driveType = ArticulationDriveType.Velocity;
        drive.stiffness = 0f;
        drive.damping = 0f;
        drive.forceLimit = 0f;
        drive.target = 0f;
        roller.xDrive = drive;

        roller.jointFriction = 0f;
        roller.angularDamping = 0.05f;
    }

    /// <summary>
    /// Sets target linear (m/s) and angular (rad/s) velocities.
    /// </summary>
    public void SetRobotVelocity(float targetLinearSpeed, float targetAngularSpeed)
    {
        _currentLinearSpeed = targetLinearSpeed;
        _currentAngularSpeed = targetAngularSpeed;

        // A chassis built before the geometry was written down would otherwise divide by zero, and a robot
        // that cannot compute a wheel speed is a robot that never moves: fall back on the values of the base.
        float track = wheelTrackLength > 0.001f ? wheelTrackLength : 0.37476f;
        float radius = wheelRadius > 0.001f ? wheelRadius : 0.0605f;

        // Compute required wheel angular velocities (rad/s)
        float rightRad = (targetAngularSpeed * (track / 2f) + targetLinearSpeed) / radius;
        float leftRad  = (-targetAngularSpeed * (track / 2f) + targetLinearSpeed) / radius;

        // In straight line, enforce equal wheel speeds to prevent drifting
        if (Mathf.Abs(targetAngularSpeed) < ANGULAR_THRESHOLD)
        {
            float avg = (leftRad + rightRad) / 2f;
            leftRad = avg;
            rightRad = avg;
        }

        foreach (ArticulationBody wheel in WheelsOf(leftWheel, leftWheels))
            SetWheelVelocity(wheel, leftRad);
        foreach (ArticulationBody wheel in WheelsOf(rightWheel, rightWheels))
            SetWheelVelocity(wheel, rightRad);
    }

    private void SetWheelVelocity(ArticulationBody wheel, float angularVelocityRadPerSec)
    {
        if (wheel == null) return;

        // The drive target velocity of a revolute joint is read in degrees per second, which is what was
        // measured on this project: a target of 573.9 rolls a wheel of radius 0.0998 m at 1 m/s.
        float targetDegPerSec = angularVelocityRadPerSec * Mathf.Rad2Deg;
        wheel.SetDriveTargetVelocity(ArticulationDriveAxis.X, targetDegPerSec);
    }

    /// <summary>
    /// Fully resets the drives (call after teleporting the robot).
    ///
    /// The base is stopped as well as the wheels: a teleport leaves the body carrying the velocity it had,
    /// and a robot that was moving when it was put down must be at rest the moment it lands.
    /// </summary>
    public void ResetDrives()
    {
        _forceIntegral = Vector3.zero;
        _torqueIntegral = 0f;

        foreach (ArticulationBody wheel in AllWheels())
            ResetDrive(wheel);

        if (_articulationRoot != null)
        {
            _articulationRoot.linearVelocity = Vector3.zero;
            _articulationRoot.angularVelocity = Vector3.zero;
        }
    }

    private void ResetDrive(ArticulationBody wheel)
    {
        if (wheel == null) return;
        // Reset internal joint angle to zero
        wheel.jointPosition = new ArticulationReducedSpace(0f);
        // Reset drive target angle
        ArticulationDrive drive = wheel.xDrive;
        drive.target = 0f;
        drive.damping = driveDamping;
        wheel.xDrive = drive;
        // Reset velocity
        wheel.SetDriveTargetVelocity(ArticulationDriveAxis.X, 0f);
    }

    /// <summary>
    /// Every wheel of the chassis, both sides, without ever handing the same joint over twice: a prefab that
    /// names its wheels in both places - which is what the wiring tool writes - still resets each one once.
    /// </summary>
    private IEnumerable<ArticulationBody> AllWheels()
    {
        foreach (ArticulationBody wheel in WheelsOf(leftWheel, leftWheels))
            yield return wheel;
        foreach (ArticulationBody wheel in WheelsOf(rightWheel, rightWheels))
            yield return wheel;
    }

    /// <summary>The casters of the chassis, without ever handing the same joint over twice.</summary>
    private IEnumerable<ArticulationBody> AllRollers()
    {
        if (freeRollers == null)
            yield break;

        var seen = new HashSet<ArticulationBody>();
        foreach (ArticulationBody roller in freeRollers)
        {
            if (roller != null && seen.Add(roller))
                yield return roller;
        }
    }

    /// <summary>
    /// The wheels of one side: the list when it holds any, and the single reference otherwise. The single
    /// reference is the first wheel of the list, so a prefab that carries both does not drive the same joint
    /// on two different commands.
    /// </summary>
    private static IEnumerable<ArticulationBody> WheelsOf(
        ArticulationBody single,
        List<ArticulationBody> list)
    {
        if (list != null && list.Count > 0)
        {
            foreach (ArticulationBody wheel in list)
            {
                if (wheel != null)
                    yield return wheel;
            }

            yield break;
        }

        if (single != null)
            yield return single;
    }
}
