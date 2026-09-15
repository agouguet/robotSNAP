using UnityEngine;
using RobotSNAP.Agents;
using RobotSNAP.Agents.Movement.Interfaces;

namespace RobotSNAP.Agents.Movement.Controllers
{
    /// <summary>
    /// Controller of a human whose velocity is decided outside Unity, by the Python API.
    ///
    /// The controller owns no local behaviour and no goal: it replays the last velocity it was given, in world
    /// units per second, in the X/Z plane — the same contract the SFM controller returns. Steering, avoidance
    /// and route following are therefore the caller's responsibility, which is exactly what an external brain
    /// wants; collisions with the scene are still resolved by the physics of the agent.
    ///
    /// A command stays active for <see cref="CommandTimeout"/> seconds and is expected to be refreshed at the
    /// rate of the external driver. A bridge that stops sending — a crashed script, a paused loop — lets the
    /// humans stop instead of walking on forever. A timeout of zero latches the command until it changes.
    /// </summary>
    public sealed class ExternalControlController : IMovementController
    {
        /// <summary>How long a command stays active without being refreshed, in seconds.</summary>
        public const float DefaultCommandTimeout = 1f;

        private HumanConfig _config;
        private Vector2 _command;
        private float _commandTime = float.NegativeInfinity;
        private bool _hasCommand;

        public ExternalControlController(HumanConfig config = null, float commandTimeout = DefaultCommandTimeout)
        {
            _config = config;
            CommandTimeout = Mathf.Max(0f, commandTimeout);
        }

        /// <summary>Seconds a command stays active without being refreshed. Zero latches it indefinitely.</summary>
        public float CommandTimeout { get; set; }

        /// <summary>True when a command was given and is still fresh.</summary>
        public bool HasCommand => _hasCommand && IsCommandFresh(_commandTime, Time.time, CommandTimeout);

        /// <summary>The last velocity asked for, whether or not it is still fresh.</summary>
        public Vector2 Command => _command;

        /// <summary>
        /// Desired velocity of the human, in world units per second. Call it again to refresh the command; the
        /// last one expires after <see cref="CommandTimeout"/> seconds.
        /// </summary>
        public void SetCommand(Vector2 velocity)
        {
            _command = velocity;
            _commandTime = Time.time;
            _hasCommand = true;
        }

        /// <summary>Drops the command: the human stops on the spot.</summary>
        public void ClearCommand()
        {
            _command = Vector2.zero;
            _commandTime = float.NegativeInfinity;
            _hasCommand = false;
        }

        public Vector2 ComputeVelocity(
            Vector2 currentPosition,
            Vector2 currentVelocity,
            Vector2 goalPosition,
            System.Collections.Generic.IReadOnlyList<Vector2> neighbors,
            System.Collections.Generic.IReadOnlyList<Vector2> neighborVelocities,
            System.Collections.Generic.IReadOnlyList<Vector2> staticObstacles,
            RobotObservation robot,
            float deltaTime,
            float cruiseSpeedOverride = 0f)
        {
            if (!HasCommand)
                return Vector2.zero;

            // Safety net, not a behaviour: the external driver owns the speed, the configuration owns the limit.
            float maxSpeed = _config != null ? Mathf.Max(0.1f, _config.maxSpeed) : 1.4f;
            return Vector2.ClampMagnitude(_command, maxSpeed);
        }

        public void Reset() => ClearCommand();

        public float GetConfidence() => HasCommand ? 1f : 0f;

        public void UpdateParameters(HumanConfig config) => _config = config;

        /// <summary>
        /// Freshness of a command, as a pure function so the rule can be read and tested on its own.
        /// A timeout of zero or less means the command never expires.
        /// </summary>
        public static bool IsCommandFresh(float commandTime, float now, float timeout) =>
            timeout <= 0f || now - commandTime <= timeout;
    }
}
