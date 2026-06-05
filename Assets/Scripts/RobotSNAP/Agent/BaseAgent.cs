using UnityEngine;

namespace RobotSNAP.Agents
{
    /// <summary>
    /// Classe abstraite de base pour tous les agents (humains, robots, etc.).
    /// Implémente l'interface IAgent et factorise les propriétés et méthodes communes.
    /// </summary>
    public abstract class BaseAgent : MonoBehaviour, IAgent
    {
        [Header("Base Agent Settings")]
        [SerializeField] protected string agentName;
        [SerializeField] protected float radius = 0.25f;
        [SerializeField] protected float mass = 80f;

        [Header("Social Parameters")]
        [SerializeField] protected float interactionRadius = 1.5f;
        [SerializeField] protected float personalSpace = 0.8f;
        [SerializeField] protected float assertiveness = 0.5f;
        [SerializeField] protected float reactionTime = 0.3f;
        [SerializeField] protected float desiredSpeed = 1.2f;

        // État interne
        protected Vector3 _currentGoal;
        protected string _currentBehavior = "normal";
        protected float _currentSpeed = 1.2f;
        protected bool _hasGoal = false;

        // ==================== IAgent Implementation ====================
        public virtual string AgentName => agentName ?? gameObject.name;
        public abstract Vector3 Position { get; }
        public virtual Vector2 Position2D => new Vector2(Position.x, Position.z);
        public abstract Quaternion Rotation { get; }
        public abstract Vector3 Forward { get; }
        public abstract Vector3 Velocity { get; }
        public virtual Vector2 Velocity2D => new Vector2(Velocity.x, Velocity.z);
        public virtual float Speed => Velocity2D.magnitude;
        public virtual float AngularSpeed => 0f; // À surcharger pour les robots
        public virtual float Radius => radius;
        public virtual float Mass => mass;
        public virtual bool HasGoal => _hasGoal;
        public virtual Vector3 Goal => _currentGoal;
        public virtual string Behavior => _currentBehavior;
        public virtual float DesiredSpeed => desiredSpeed;
        public virtual float InteractionRadius => interactionRadius;
        public virtual float PersonalSpace => personalSpace;
        public virtual float Assertiveness => assertiveness;
        public virtual float ReactionTime => reactionTime;
        public virtual bool IsActive => gameObject.activeInHierarchy;

        // ==================== Méthodes communes ====================
        public virtual void SetGoal(Vector3 goal)
        {
            _currentGoal = goal;
            _hasGoal = true;
        }

        public virtual void ClearGoal()
        {
            _hasGoal = false;
            _currentGoal = Vector3.zero;
        }

        public virtual void SetBehavior(string behavior)
        {
            _currentBehavior = behavior;
        }

        public virtual void SetSpeed(float speed)
        {
            desiredSpeed = Mathf.Clamp(speed, 0.5f, 5f);
            _currentSpeed = desiredSpeed;
        }

        public virtual void SetInteractionRadius(float radius)
        {
            interactionRadius = Mathf.Clamp(radius, 0.5f, 5f);
        }

        public virtual void SetPersonalSpace(float space)
        {
            personalSpace = Mathf.Clamp(space, 0.3f, 2f);
        }

        public virtual void SetAssertiveness(float value)
        {
            assertiveness = Mathf.Clamp(value, 0f, 1f);
        }

        public virtual void SetReactionTime(float time)
        {
            reactionTime = Mathf.Clamp(time, 0.1f, 1f);
        }

        public virtual void SetActive(bool active)
        {
            gameObject.SetActive(active);
            if (!active) Stop();
        }

        public abstract void Stop();
        public abstract void Reset();
    }
}