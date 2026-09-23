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
///
/// <b>TyreForces</b> takes the opposite road to the same place, and it is the one that keeps the wheels
/// carrying the robot. The contact of a driven wheel is emptied of shear - what a contact cannot separate,
/// this model separates itself - and the tyre of each wheel is computed from the slip it runs at, in the
/// two directions at once, so that what a wheel spends scrubbing across a turn is taken out of what it has
/// left to drive with. The robot is then moved by its tyres and by nothing else: the wheels still hold it
/// up through their contacts, and a wall, a kerb or a person still answers the drive with the grip the
/// tyres have. The wheel drive of that model is its motor, and the torque it is left with -
/// <see cref="rollingWheelForceLimit"/> - is therefore also the pull one wheel can put into the floor, which
/// is what a robot asked for more than its motors can give is held by.
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

        /// <summary>
        /// The tyres carry the robot, through the slip model below rather than through the contact:
        /// the shape of a tyre, which a single friction coefficient cannot express.
        /// </summary>
        TyreForces,

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
        "Torque the wheel drive is left with when the base carries the motion, in N.m. The wheels of that " +
        "model roll to be seen and to keep the odometry honest; they must not have the strength to decide " +
        "the chassis, and they cannot: a driven wheel meets the world through the skid contact the wiring " +
        "tool gives it, which passes almost nothing. What the figure does have to clear is the effort of " +
        "spinning the wheel up. Measured on this project, a Jackal and a Bibus left at the two tenths of a " +
        "newton-metre they used to carry never turned their wheels at all - zero revolutions over four " +
        "seconds while the robot slid a metre per second across the floor - and at three newton-metres the " +
        "four robots roll at the speed their motion implies, a Jackal's 573 degrees per second against the " +
        "574 asked for. The commanded turn answers at 99 percent either way, so nothing here moves the " +
        "robot: the base does, and the wheels only have to turn fast enough to look right.")]
    public float rollingWheelForceLimit = 3f;

    [Tooltip("Internal friction of the wheel joint. A wheel that is commanded must not be braked by it.")]
    public float jointFriction = 0f;

    [Header("Tyre model (the TyreForces chassis)")]
    [Tooltip(
        "What a tyre can pass along its rolling direction, as a friction coefficient. This is the drive: it is " +
        "the force that makes the robot move, stop and turn, and it is the one a real rubber wheel holds.")]
    public float tyreLongitudinalGrip = 0.9f;

    [Tooltip(
        "What a tyre can pass sideways, as a friction coefficient. This is the number a wheeled robot's " +
        "behaviour in a turn comes from: high enough that the robot is not on ice - a tyre that slides " +
        "sideways that easily is a tyre that cannot push a pedestrian - and low enough that a four-wheel skid " +
        "steer can still scrub the half of its contact patch a turn demands. It is the figure Gazebo's users " +
        "tune by hand as a contact's second friction direction, for exactly this reason.")]
    public float tyreLateralGrip = 0.55f;

    [Tooltip(
        "How much a tyre is allowed to slip before it answers with its grip, as a fraction of the speed it " +
        "rolls at: the longitudinal stiffness of the tyre, written as the slip it reaches three quarters of " +
        "what it can pass at. It is what decides how much of a commanded turn a skid steer gets, because the " +
        "yaw of a four-wheel chassis is driven by the difference between two sides and a slip on each side " +
        "eats the difference: measured on the Jackal at a metre per second and half a radian per second, five " +
        "hundredths of a metre per second of slip per side - what this figure gives at eight hundredths - " +
        "costs thirteen percent of the commanded turn. A rubber tyre reaches three quarters of its grip " +
        "between five and twelve percent of slip, and this model reads it at eight.")]
    public float tyreLongitudinalSlip = 0.08f;

    [Tooltip(
        "How far a tyre is allowed to run sideways before it answers with its grip, written as the slip " +
        "angle it reaches three quarters of what it can pass at, in radians. It is the cornering stiffness " +
        "of the tyre, and it is the other half of what a skid steer pays for a turn: the scrub of a turning " +
        "chassis is resisted by exactly this force, so a stiffer tyre here does not hold the robot better, " +
        "it loses more of the turn it was given - measured on the Jackal, the same arc came out at " +
        "seventy-one percent of its command at fifteen hundredths and eighty-seven percent at a quarter. A " +
        "wheel of this size and load has a real cornering stiffness between these two, and the scrubbing " +
        "contact of a skid steer is softer than the pure cornering one, which is why a quarter is kept.")]
    public float tyreLateralSlip = 0.25f;

    [Tooltip(
        "The speed at which a slip is read, in metres per second: the relaxation length of a tyre in disguise, " +
        "and what keeps the model from dividing by a velocity of nothing when the robot is standing still.")]
    public float tyreRelaxationSpeed = 0.4f;

    [Tooltip(
        "What the rolling of a loaded tyre costs, as a fraction of the grip it has: the figure that takes the " +
        "last of a robot's speed away instead of letting it coast for ever. A rubber tyre on a hard floor is " +
        "between one and two hundredths.")]
    public float tyreRollingResistance = 0.015f;

    [Tooltip(
        "Whether the driven wheels are taken off the shear of their contact in this model. They have to be: a " +
        "PhysX contact passes one friction coefficient in every direction, so a wheel on one either grips or " +
        "slides, and a four-wheel skid steer has to roll along its wheels while it scrubs across them. What " +
        "the contact keeps is the normal force, so the wheels still hold the robot up, and everything the " +
        "robot does with the floor is asked of the tyres, which know the difference between the two " +
        "directions. Switch it off to see what a contact that answers both of them does instead.")]
    public bool freeWheelContact = true;

    /// <summary>What a wheel is commanded to roll at, in metres per second: the speed the tyres are read against.</summary>
    private float _leftSurfaceSpeed;
    private float _rightSurfaceSpeed;

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
        "How hard the drive may take speed off the robot, in m/s per second. Zero - the figure every robot of " +
        "this project carries - lets the tyres do it: a robot asked to stop brakes with the grip it has and " +
        "comes to rest in a tenth of a second from two radians per second. A figure here is what a platform " +
        "whose motors are the limit does instead, and it is how the Jackal is built: " +
        "see maxAngularBraking.")]
    public float maxLinearBraking = 0f;

    [Tooltip(
        "How hard the drive may take yaw off the robot, in rad/s per second. Zero lets the tyres decide, as " +
        "above. The Jackal carries 2.5: released from the top of its commanded turn it comes to " +
        "rest in 1.6 seconds and in about a radian of rotation, where the tyre-braked one stops in 0.13 " +
        "seconds and a tenth of a radian - and only the second of those is a robot that could exist.")]
    public float maxAngularBraking = 0f;

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

    [Range(0f, 1f)]
    [Tooltip(
        "How much the tyres are allowed to decide, from 0 to 1. At 1 - the setting every robot is wired " +
        "with - the drive may never ask for more force than its tyres can pass, so a wall, a kerb or a " +
        "person can slow the robot down and stop it. Lower the figure and the drive is allowed to push " +
        "past what its tyres could ever grip with: 0.5 lets it ask for twice its traction, 0.25 for four " +
        "times, and the robot then follows its command whatever it is leaning against. It changes nothing " +
        "on an empty floor - every robot here follows its command to the percent either way - and shows " +
        "only in what it is up against.")]
    public float controlRealism = 1f;

    // Current speeds (for UI / debug)
    private float _currentLinearSpeed;
    private float _currentAngularSpeed;

    /// <summary>The body the articulation hangs from, resolved once, for the drives that act on the base.</summary>
    private ArticulationBody _articulationRoot;

    /// <summary>
    /// Every collider of this robot. The ground a wheel looks for under itself must not be one of its own:
    /// the chassis of a robot whose wheels sit inside its body is the first thing a ray from a wheel meets,
    /// and a wheel that reads its own chassis as ground pushes the robot off a floor it never touched.
    /// </summary>
    private readonly HashSet<Collider> _ownColliders = new HashSet<Collider>();

    /// <summary>The material each wheel collider carried before the tyre model took its shear away.</summary>
    private readonly Dictionary<Collider, PhysicsMaterial> _wheelMaterials =
        new Dictionary<Collider, PhysicsMaterial>();

    /// <summary>
    /// The model the wheels were last configured for. A model chosen after this component woke up - which is
    /// how a scenario, a tool or a test picks one - leaves the wheels wired for the model before it, and a
    /// robot whose wheels are wired for another chassis behaves like neither.
    /// </summary>
    private DriveModel _configuredModel = DriveModel.WheelVelocity;

    /// <summary>
    /// Mass of the whole chassis, in kilograms, and its moment of inertia about the vertical, in kilogram
    /// square metres. Read from the articulated chain, and not from the base link; see
    /// <see cref="MeasureChassis"/> for why that distinction is the whole of whether a robot stops when its
    /// command is let go.
    /// </summary>
    private float _chassisMass;
    private float _chassisYawInertia;

    /// <summary>Mass the base link carried when the chassis was last measured, to notice a profile written on it.</summary>
    private float _measuredRootMass = -1f;

    /// <summary>
    /// Effort the drive has had to add to hold its command, in newtons and in newton-metres. It is what the
    /// force and the torque would otherwise have to ask for all at once, and it is bounded by the traction.
    /// </summary>
    private Vector3 _forceIntegral;
    private float _torqueIntegral;

    public float CurrentLinearSpeed => _currentLinearSpeed;
    public float CurrentAngularSpeed => _currentAngularSpeed;

    /// <summary>Mass of the chassis this drive moves, in kilograms.</summary>
    public float ChassisMass => _chassisMass;

    /// <summary>Moment of inertia of the chassis about the vertical, in kilogram square metres.</summary>
    public float ChassisYawInertia => _chassisYawInertia;

    // Threshold to consider angular speed as "straight line"
    private const float ANGULAR_THRESHOLD = 0.01f;

    private void Awake()
    {
        ConfigureDrive();
        MeasureChassis();
        CacheOwnColliders();
    }

    private void Start()
    {
        // Done again here, and not only in Awake, because another component of the prefab configures every
        // joint of the chain at its own start: whichever of the two runs last is the one that counts, and
        // the order Unity picks between two Start methods is not something a prefab should depend on.
        ConfigureDrive();
        MeasureChassis();
        CacheOwnColliders();
    }

    /// <summary>
    /// Wires the wheels for the chassis model this robot carries. Done together, and not in pieces, because
    /// the model decides all of it: which body carries the robot, how strong a wheel drive may be, and
    /// whether a contact is allowed to pass a force across the direction a wheel rolls.
    ///
    /// The articulation is wired first and the body the chain hangs from is read after it, and that order is
    /// not a detail: writing the <c>immovable</c> or the gravity flag of a base rebuilds the articulation,
    /// which replaces the bodies of the chain, so a reference taken before that write is a reference to
    /// nothing and a robot whose every drive then returns on its first line - which is exactly what happened
    /// to the Bibus, the Jackal and the Freight when this method was called after the root had been read.
    /// </summary>
    private void ConfigureDrive()
    {
        ConfigureArticulation();

        foreach (ArticulationBody wheel in AllWheels())
            ConfigureWheel(wheel);

        foreach (ArticulationBody roller in AllRollers())
            ConfigureRoller(roller);

        FreeWheelContacts();

        // After the articulation, never before: see the remark above.
        _articulationRoot = ArticulationRoot();
        _configuredModel = driveModel;
    }

    /// <summary>
    /// Reads every collider of this robot, so that a wheel looking for the ground underneath it can tell the
    /// floor from its own chassis.
    /// </summary>
    private void CacheOwnColliders()
    {
        _ownColliders.Clear();

        if (_articulationRoot != null)
        {
            foreach (Collider collider in _articulationRoot.GetComponentsInChildren<Collider>(true))
                _ownColliders.Add(collider);
        }

        foreach (Collider collider in GetComponentsInChildren<Collider>(true))
            _ownColliders.Add(collider);
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

        // A model picked at runtime is a robot whose wheels are still wired for the model before it.
        if (driveModel != _configuredModel)
            ConfigureDrive();

        if (driveModel == DriveModel.SkidSteerBase)
            DriveBase();
        else if (driveModel == DriveModel.TyreForces)
            DriveTyreForces();
    }

    /// <summary>
    /// Drives the robot the way a tyre does, with the two directions a contact cannot separate.
    ///
    /// A tyre passes a force along the direction it rolls and a force sideways, and the two are not the same
    /// figure: a wheel grips its road when it rolls and slides when the road asks it to go sideways. PhysX
    /// gives a contact a single friction coefficient for the pair, which is why this project's wheels carry a
    /// contact of a hundredth of the floor's - so that a skid steer can turn at all - and why the robot then
    /// reads as if the floor were ice: its yaw comes from a torque written on its chassis, and its wheels
    /// could be lifted off the ground without anything changing.
    ///
    /// This chassis computes the two forces itself, from the slip a tyre actually runs at, and leaves the
    /// contact nothing to decide. Each driven wheel reads the velocity of its own contact patch - the
    /// chassis' velocity carried out to the contact by the rotation, which is what a wheel at the corner of
    /// a turning chassis does - splits it into the part along its rolling direction and the part across it,
    /// and asks the tyre for:
    ///
    ///   along: grip times the weight it carries, for a slip ratio of (wheel speed - ground speed) / speed,
    ///   across: grip times the weight it carries, for a slip angle of atan(across / speed),
    ///
    /// each saturating the way a tyre does, over the slip it reaches its grip at. The two forces are applied
    /// at the contact patch, so the moment they make about the chassis - which is how a turn happens, and
    /// what a skid steer pays for it - is applied too.
    ///
    /// The normal load is the weight of the robot split evenly between its driven wheels, and the wheel is
    /// turned by the same velocity drive as everywhere else: it is how the platform these models come from
    /// is commanded, and what a real one does with its own wheel-speed loop. What is not modelled is load
    /// transfer in a turn and the relaxation length of a tyre in time; what is, is the direction a contact
    /// cannot know about.
    /// </summary>
    private void DriveTyreForces()
    {
        ArticulationBody body = _articulationRoot;
        body.WakeUp();

        if (!Mathf.Approximately(_measuredRootMass, body.mass))
            MeasureChassis();

        float step = Time.fixedDeltaTime;
        Vector3 velocity = body.linearVelocity;
        Vector3 spin = body.angularVelocity;

        Vector3 forward = Flattened(body.transform.forward);
        Vector3 side = Vector3.Cross(Vector3.up, forward);

        // The wheels on the ground carry the robot between them, and a wheel in the air carries nothing at
        // all: that is the whole of what a kerb, a ramp, or a robot put down on one side asks of the model.
        int grounded = 0;
        foreach (ArticulationBody wheel in AllWheels())
        {
            if (ContactOf(wheel, out _, out _))
                grounded++;
        }

        float load = grounded > 0
            ? Mathf.Max(1f, _chassisMass) * Mathf.Abs(Physics.gravity.y) / grounded
            : 0f;

        ApplyTyres(body, WheelsOf(leftWheel, leftWheels), _leftSurfaceSpeed, load, forward, side, velocity, spin);
        ApplyTyres(body, WheelsOf(rightWheel, rightWheels), _rightSurfaceSpeed, load, forward, side, velocity, spin);

        // The lean comes back the way it does on the driven base, and for the same reason: a four-wheel
        // chassis has no suspension, so without it a contact with a kerb leaves the robot tilted.
        float inertia = InertiaAboutUp(body);
        float tiltLimit = Mathf.Max(0f, tiltRecovery) * step;
        body.AddTorque(new Vector3(
            Mathf.Clamp(-spin.x, -tiltLimit, tiltLimit) / step * inertia,
            0f,
            Mathf.Clamp(-spin.z, -tiltLimit, tiltLimit) / step * inertia));
    }

    /// <summary>
    /// The tyre of every wheel of one side, applied where that wheel meets the ground.
    ///
    /// A tyre passes a force along the direction it rolls and a force sideways, and it has one grip to spend
    /// between the two: a wheel that is sliding across the floor has that much less left to drive with. That
    /// is the whole of this model, and it is the whole of what a PhysX contact cannot say, because a contact
    /// has a single coefficient and answers both directions with all of it. Its cost is exacted where a skid
    /// steer pays it: on the four wheels of a robot in a turn, whose scrub is what a contact would either
    /// refuse to give (and the robot goes straight) or give in full (and the robot slides).
    ///
    /// The two slips are put on one scale - the slip ratio the tyre reaches its grip at, and the slip angle
    /// it reaches it at - so that they can be compared at all, and the tyre then answers along the direction
    /// the pair points at, with the grip of an ellipse between the two extremes. The force is applied at the
    /// contact patch, so the moment it makes about the chassis - which is how a turn happens and what a skid
    /// steer pays for it - is applied with it.
    /// </summary>
    private void ApplyTyres(
        ArticulationBody body,
        IEnumerable<ArticulationBody> wheels,
        float surfaceSpeed,
        float load,
        Vector3 forward,
        Vector3 side,
        Vector3 velocity,
        Vector3 spin)
    {
        foreach (ArticulationBody wheel in wheels)
        {
            if (wheel == null || !ContactOf(wheel, out Vector3 contact, out Vector3 normal))
                continue;

            // The ground a wheel stands on decides both how much of the robot that wheel carries and which
            // way its tyre may push: a force is passed along the floor, never into it.
            float carried = load * Mathf.Clamp01(Vector3.Dot(normal, Vector3.up));
            if (carried <= 0f)
                continue;

            Vector3 lever = contact - body.transform.position;
            Vector3 atContact = velocity + Vector3.Cross(spin, lever);

            float along = Vector3.Dot(atContact, forward);
            float across = Vector3.Dot(atContact, side);
            float reference = Mathf.Max(tyreRelaxationSpeed, Mathf.Abs(along));

            float slipRatio = (surfaceSpeed - along) / reference / Mathf.Max(1e-3f, tyreLongitudinalSlip);
            // The slip angle is read against the speed the tyre is travelling at and not against the
            // relaxation speed: a tyre leaning five centimetres sideways per second while it rolls at one
            // metre per second is at a slip angle of three degrees and holds with a third of its grip, where
            // the same five centimetres read against the relaxation speed would ask for three quarters of it.
            // That reading is what a cornering stiffness is, and getting it wrong is what made the first
            // version of this model turn a metre-per-second arc at three percent of its command.
            float slipAngle = Mathf.Atan2(-across, reference) / Mathf.Max(1e-3f, tyreLateralSlip);

            float combined = Mathf.Sqrt(slipRatio * slipRatio + slipAngle * slipAngle);
            if (combined <= 1e-4f)
                continue;

            // One budget, two directions: the grip of the direction the tyre is actually sliding in, read off
            // the ellipse the two coefficients make between them, then spent along that same direction.
            float grip = Mathf.Sqrt(
                Mathf.Pow(slipRatio * tyreLongitudinalGrip, 2f) +
                Mathf.Pow(slipAngle * tyreLateralGrip, 2f)) / combined;

            float answer = carried * grip * (float)System.Math.Tanh(combined) / combined;
            float drive = slipRatio * answer;
            float scrub = slipAngle * answer;

            // A tyre passes a force its motor can turn - the load it carries is the ceiling the floor puts on
            // that force, and the motor is the ceiling the robot puts on it. Below the motor's figure nothing
            // changes; at it, the wheel spins a little more and the robot is a little slower to answer,
            // which is what a robot whose wheels are asked for more than they can pull does.
            float motor = MotorForce(wheel);
            if (motor > 0f)
                drive = Mathf.Clamp(drive, -motor, motor);

            Vector3 force = forward * drive + side * scrub;

            // The last of a robot's speed goes the way a real one's does, into the rolling of its loaded
            // tyres rather than into a coast that never ends.
            if (tyreRollingResistance > 0f)
            {
                float rolling = carried * tyreRollingResistance
                    * (float)System.Math.Tanh(along / 0.05f);
                force -= forward * rolling;
            }

            force -= normal * Vector3.Dot(force, normal);

            body.AddForce(force);
            body.AddTorque(Vector3.Cross(lever, force));
        }
    }

    /// <summary>
    /// The force one wheel's motor can turn into a push, in newtons: the torque its drive is allowed to
    /// apply, over the radius it applies it at. Zero for a wheel whose drive is left unbounded, which is a
    /// motor this model has no figure for and must therefore not invent one for.
    /// </summary>
    private float MotorForce(ArticulationBody wheel)
    {
        float torque = wheel.xDrive.forceLimit;
        float radius = Mathf.Max(0.01f, wheelRadius);
        return torque > 0f ? torque / radius : 0f;
    }

    /// <summary>
    /// Where a wheel meets the ground, and the normal of that ground: the point a tyre's force acts at, and
    /// what decides how much of the robot that wheel carries. False when the wheel is in the air, where it
    /// passes nothing at all.
    ///
    /// The ground is looked for from the wheel's own centre, one radius down and no further, and the
    /// robot's own colliders are skipped - see <see cref="_ownColliders"/> for why that is not a detail.
    /// </summary>
    private bool ContactOf(ArticulationBody wheel, out Vector3 point, out Vector3 normal)
    {
        float radius = Mathf.Max(0.01f, wheelRadius);
        Vector3 origin = wheel.transform.position;
        point = origin - Vector3.up * radius;
        normal = Vector3.up;

        int found = Physics.RaycastNonAlloc(
            origin, Vector3.down, GroundHits, radius * 1.5f, ~0, QueryTriggerInteraction.Ignore);

        float nearest = float.MaxValue;
        bool onGround = false;

        for (int i = 0; i < found; i++)
        {
            Collider collider = GroundHits[i].collider;
            if (collider == null || _ownColliders.Contains(collider))
                continue;

            if (GroundHits[i].distance >= nearest)
                continue;

            nearest = GroundHits[i].distance;
            point = GroundHits[i].point;
            normal = GroundHits[i].normal;
            onGround = true;
        }

        return onGround;
    }

    /// <summary>The hits of the ground under the wheels, reused every step so that driving a robot allocates nothing.</summary>
    private static readonly RaycastHit[] GroundHits = new RaycastHit[8];

    /// <summary>A direction with its climb taken out, since a chassis drives along the floor and not up it.</summary>
    private static Vector3 Flattened(Vector3 direction)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude < 1e-6f)
            return Vector3.forward;

        return direction.normalized;
    }

    private void DriveBase()
    {
        ArticulationBody body = _articulationRoot;
        body.WakeUp();

        // The robot type is written on the base link after this prefab woke up, and the profile carries a
        // mass, so a chassis read at Awake can be a chassis the robot no longer has.
        if (!Mathf.Approximately(_measuredRootMass, body.mass))
            MeasureChassis();

        float step = Time.fixedDeltaTime;
        Vector3 forward = Flattened(body.transform.forward);
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
        float mass = Mathf.Max(0.001f, _chassisMass > 0f ? _chassisMass : body.mass);
        float traction = TractionForce(mass);
        Vector3 error = wanted - planar;

        // Slowing down is where a robot's ramps and its tyres can be told apart. A robot being asked to stop
        // uses its tyres, and the demand is left whole so that the traction budget is the only thing that
        // caps it - which is what every robot here does, and what makes them all come to rest together. A
        // platform whose motors are the limit instead brakes at the rate it can command, and that is what
        // maxLinearBraking asks for: it is the figure that separates a robot that stops as its motors allow
        // from one that stops as its grip would, and only the first of those exists.
        bool braking = Vector3.Dot(error, planar) < 0f;
        float linearLimit = braking && maxLinearBraking > 0f ? maxLinearBraking : maxLinearAcceleration;
        Vector3 change = braking && maxLinearBraking <= 0f
            ? error
            : Vector3.ClampMagnitude(error, Mathf.Max(0f, linearLimit) * step);
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
        float inertia = _chassisYawInertia > 0f ? _chassisYawInertia : InertiaAboutUp(body);
        Vector3 spin = body.angularVelocity;
        float yawError = -_currentAngularSpeed - spin.y;
        bool slowingDown = yawError * spin.y < 0f;
        float yawRate = slowingDown && maxAngularBraking > 0f ? maxAngularBraking : maxAngularAcceleration;
        float yawLimit = Mathf.Max(0f, yawRate) * step;
        float yawChange = slowingDown && maxAngularBraking <= 0f
            ? yawError
            : Mathf.Clamp(yawError, -yawLimit, yawLimit);
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

        // The lean is brought back by a torque of the same shape, through the inertia of the base link and
        // not through the yaw inertia above: it is a stabiliser and not a tyre force, so it is not asked of
        // the traction budget, and sizing it on the inertia of the whole chassis - which this same robot
        // reads as thirty times the base link's - would have it slam the body back at the first kerb.
        float tiltInertia = InertiaAboutUp(body);
        float tiltLimit = Mathf.Max(0f, tiltRecovery) * step;
        float pitchChange = Mathf.Clamp(-spin.x, -tiltLimit, tiltLimit);
        float rollChange = Mathf.Clamp(-spin.z, -tiltLimit, tiltLimit);
        body.AddTorque(new Vector3(pitchChange / step * tiltInertia, 0f, rollChange / step * tiltInertia));
    }

    /// <summary>True when the command asks for no motion at all.</summary>
    private static bool Stops(float command) => Mathf.Abs(command) <= 0.001f;

    /// <summary>True when the two commands ask for opposite directions, one of them being a real one.</summary>
    private static bool Opposite(float wanted, float current)
    {
        return !Stops(wanted) && !Stops(current) && wanted * current < 0f;
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
        float traction = maxDriveForce > 0f
            ? maxDriveForce
            : tractionGrip > 0f ? tractionGrip * mass * Mathf.Abs(Physics.gravity.y) : 0f;

        // Zero is the drive asking for no limit at all, and that is what a realism of zero means: the
        // controller is then free to push as hard as it likes, which is the idealised drive this project
        // started with - exact tracking, and a collision that cannot be felt.
        if (traction <= 0f || controlRealism <= 0.01f)
            return 0f;

        return traction / Mathf.Clamp01(controlRealism);
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
        if (driveModel != DriveModel.WheelVelocity)
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
        foreach (ArticulationBody wheel in AllWheels())
            return TopOf(wheel);
        return null;
    }

    /// <summary>
    /// The body an articulation hangs from, given any one of its bodies: the topmost one, the body that has
    /// no other above it.
    /// </summary>
    private static ArticulationBody TopOf(ArticulationBody body)
    {
        ArticulationBody candidate = body;
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
    /// Reads the mass and the moment of inertia about the vertical of the whole chassis, from the chain of
    /// bodies the base carries.
    ///
    /// Reading them off the base link alone - which is what this drive did before - sizes the effort on the
    /// one body the joint sits on instead of on the robot that has to be stopped by it. Stepped by hand
    /// through the physics, a Kuri turning under five newton-metres answers with the acceleration of a
    /// 1.27 kg.m2 chassis while its base link declares 0.037, and a Jackal's 11.5 against the 0.39 of its
    /// chassis_link; the same measurement on the Freight gives 5.96 against 2.81 and on the Bibus 2.24
    /// against 1.31. The two robots whose base link carries most of the robot are exactly the two that come
    /// to rest the moment their command is let go, and the two that coast for a second and more are the two
    /// whose base link carries a fifth of the robot. The same reading sizes the linear drive and the
    /// traction budget, and it is what the wiring tool already assumed when it wrote that a robot pushes
    /// what it weighs: a Kuri does not weigh the seven kilograms of its base link, it weighs twenty-two and
    /// a half.
    ///
    /// The figure is a floor rather than an exact value: the inertia of a body whose shape PhysX derives for
    /// itself is read from the body, which reports a stand-in, and only the distance to the axis is added on
    /// top of it. A floor is the safe direction - the drive asks for less than the chassis needs and never
    /// more, so nothing overshoots, and the traction budget stays the ceiling on all of it.
    /// </summary>
    public void MeasureChassis()
    {
        ArticulationBody root = _articulationRoot != null ? _articulationRoot : ArticulationRoot();
        _chassisMass = 0f;
        _chassisYawInertia = 0f;
        _measuredRootMass = root != null ? root.mass : -1f;

        if (root == null)
            return;

        float mass = 0f;
        float inertia = 0f;

        foreach (ArticulationBody body in root.GetComponentsInChildren<ArticulationBody>(true))
        {
            // Only the chain that hangs from this base. A prefab can carry a second articulation of its own -
            // the Kuri ships a gyro_link that sits at the origin of the model rather than under its base -
            // and a body standing five hundred metres away would otherwise be counted for the square of that
            // distance, which is the whole of the figure rather than a rounding of it.
            if (TopOf(body) != root)
                continue;

            mass += body.mass;

            Vector3 offset = body.transform.position - root.transform.position;
            float radius = new Vector2(offset.x, offset.z).magnitude;
            inertia += body.mass * radius * radius;

            Vector3 own = body.inertiaTensor;
            inertia += own.x * Mathf.Pow(Vector3.Dot(body.transform.right, Vector3.up), 2f)
                     + own.y * Mathf.Pow(Vector3.Dot(body.transform.up, Vector3.up), 2f)
                     + own.z * Mathf.Pow(Vector3.Dot(body.transform.forward, Vector3.up), 2f);
        }

        _chassisMass = mass;
        _chassisYawInertia = inertia;
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
        if (driveModel != DriveModel.WheelVelocity && rollingWheelForceLimit > 0f)
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
    /// Takes the driven wheels off the shear of their contact, which is what makes the tyres the only thing
    /// that can move this robot.
    ///
    /// The normal force of the contact is left alone: the wheels still carry the robot, a kerb is still
    /// something to climb, and a robot whose wheels leave the ground still falls. What is removed is the
    /// part of a contact that answers a force across the direction it rolls - which is a skid steer's turn.
    /// </summary>
    private void FreeWheelContacts()
    {
        if (driveModel != DriveModel.TyreForces || !freeWheelContact)
            return;

        foreach (ArticulationBody wheel in AllWheels())
        {
            foreach (Collider collider in wheel.GetComponents<Collider>())
            {
                if (!_wheelMaterials.ContainsKey(collider))
                    _wheelMaterials.Add(collider, collider.sharedMaterial);

                collider.sharedMaterial = Slipless;
            }
        }
    }

    /// <summary>
    /// The material of a contact that passes nothing along the ground: the normal force of the floor is all a
    /// wheel with tyres of its own should ever read from it.
    ///
    /// It is made once and shared, and it is made here rather than written as an asset so that a project that
    /// never drives a robot this way never carries the file.
    /// </summary>
    private static PhysicsMaterial Slipless
    {
        get
        {
            if (_slipless == null)
            {
                _slipless = new PhysicsMaterial("TyreModelNoShear")
                {
                    dynamicFriction = 0f,
                    staticFriction = 0f,
                    bounciness = 0f,
                    frictionCombine = PhysicsMaterialCombine.Minimum,
                    bounceCombine = PhysicsMaterialCombine.Minimum,
                };
            }

            return _slipless;
        }
    }

    private static PhysicsMaterial _slipless;

    /// <summary>Gives the wheels back the material they were built with when this component is switched off.</summary>
    private void OnDisable()
    {
        foreach (KeyValuePair<Collider, PhysicsMaterial> wheel in _wheelMaterials)
        {
            if (wheel.Key != null)
                wheel.Key.sharedMaterial = wheel.Value;
        }

        _wheelMaterials.Clear();
    }

    /// <summary>
    /// Sets target linear (m/s) and angular (rad/s) velocities.
    /// </summary>
    public void SetRobotVelocity(float targetLinearSpeed, float targetAngularSpeed)
    {
        // A command that stops or reverses drops the effort the drive had built up against the resistance.
        // Keeping it is what made a robot keep turning after its key was let go - measured on the Kuri, two
        // and a half seconds to come to rest, against nine tenths of a second for the same robot turning
        // the other way - and what made a robot asked to turn the other way answer at 46 percent of its
        // command for the first half second.
        //
        // The comparison is made on the magnitudes and not on Unity's sign, because Mathf.Sign answers 1 for
        // zero: "turn" and "stop" came out as the same sign, so the effort of the turn was kept for a stop
        // and dropped for a reversal, which is the exact opposite of what the robot needed in both cases.
        if (Stops(targetLinearSpeed) || Opposite(targetLinearSpeed, _currentLinearSpeed))
            _forceIntegral = Vector3.zero;
        if (Stops(targetAngularSpeed) || Opposite(targetAngularSpeed, _currentAngularSpeed))
            _torqueIntegral = 0f;

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

        // What the tyres of each side are rolling at, in metres per second: the figure the tyre model reads
        // its slip against, and the one a wheel's own contact would have carried had it been left to decide.
        _leftSurfaceSpeed = leftRad * radius;
        _rightSurfaceSpeed = rightRad * radius;
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
