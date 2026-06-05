using UnityEngine;

/// <summary>
///     Converts linear/angular velocity to wheel joint velocities for a differential drive robot.
/// </summary>
public class ArticulationWheelController : MonoBehaviour
{
    [Header("Wheel References")]
    public ArticulationBody leftWheel;
    public ArticulationBody rightWheel;

    [Header("Robot Geometry")]
    public float wheelTrackLength; // distance between wheels (meters)
    public float wheelRadius;       // wheel radius (meters)

    // Current speeds (for UI / debug)
    private float _currentLinearSpeed;
    private float _currentAngularSpeed;

    public float CurrentLinearSpeed => _currentLinearSpeed;
    public float CurrentAngularSpeed => _currentAngularSpeed;

    // Threshold to consider angular speed as "straight line"
    private const float ANGULAR_THRESHOLD = 0.01f;

    private void Start()
    {
        // Appliquer un damping modéré pour un mouvement plus naturel
        SetWheelDamping(leftWheel, 0.5f);
        SetWheelDamping(rightWheel, 0.5f);
    }

    /// <summary>
    /// Sets target linear (m/s) and angular (rad/s) velocities.
    /// </summary>
    public void SetRobotVelocity(float targetLinearSpeed, float targetAngularSpeed)
    {
        _currentLinearSpeed = targetLinearSpeed;
        _currentAngularSpeed = targetAngularSpeed;

        if (targetLinearSpeed == 0 && targetAngularSpeed == 0)
        {
            StopWheels();
            return;
        }

        // Compute required wheel angular velocities (rad/s)
        float rightRad = (targetAngularSpeed * (wheelTrackLength / 2f) + targetLinearSpeed) / wheelRadius;
        float leftRad  = (-targetAngularSpeed * (wheelTrackLength / 2f) + targetLinearSpeed) / wheelRadius;

        // In straight line, enforce equal wheel speeds to prevent drifting
        if (Mathf.Abs(targetAngularSpeed) < ANGULAR_THRESHOLD)
        {
            float avg = (leftRad + rightRad) / 2f;
            leftRad = avg;
            rightRad = avg;
        }

        SetWheelVelocity(leftWheel, leftRad);
        SetWheelVelocity(rightWheel, rightRad);
    }

    private void SetWheelVelocity(ArticulationBody wheel, float angularVelocityRadPerSec)
    {
        if (wheel == null) return;
        // Convert rad/s → degrees/s for Unity's drive
        float targetDegPerSec = angularVelocityRadPerSec * Mathf.Rad2Deg;
        wheel.SetDriveTargetVelocity(ArticulationDriveAxis.X, targetDegPerSec);
    }

    private void StopWheels()
    {
        SetWheelVelocity(leftWheel, 0f);
        SetWheelVelocity(rightWheel, 0f);
    }

    /// <summary>
    /// Fully resets the drives (call after teleporting the robot).
    /// </summary>
    public void ResetDrives()
    {
        ResetDrive(leftWheel);
        ResetDrive(rightWheel);
    }

    private void ResetDrive(ArticulationBody wheel)
    {
        if (wheel == null) return;
        // Reset internal joint angle to zero
        wheel.jointPosition = new ArticulationReducedSpace(0f);
        // Reset drive target angle
        ArticulationDrive drive = wheel.xDrive;
        drive.target = 0f;
        drive.damping = 0.5f;
        wheel.xDrive = drive;
        // Reset velocity
        wheel.SetDriveTargetVelocity(ArticulationDriveAxis.X, 0f);
    }

    private void SetWheelDamping(ArticulationBody wheel, float damping)
    {
        if (wheel == null) return;
        ArticulationDrive drive = wheel.xDrive;
        drive.damping = damping;
        wheel.xDrive = drive;
    }
}