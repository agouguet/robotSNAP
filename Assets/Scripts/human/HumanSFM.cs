using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace ROS_DRL
{
    public class SocialForce
    {
        public Vector3 force;
        public bool anyAgentInFront;
        public bool anyAgentApproaching;

        public SocialForce()
        {
            force = Vector3.zero;
            anyAgentInFront = false;
            anyAgentApproaching = false;
        }

        public bool pauseable => anyAgentInFront || anyAgentApproaching;
    }

    public class HumanSFM : MonoBehaviour
    {
        public ROS_DRL.EnvController controller;
        public string name;
        public bool staticAgent = false;

        private Animator animator;
        private Rigidbody rb;
        private CapsuleCollider collisionCapsule;

        private NavMeshPath nmPath;

        private Vector3 destPos;
        private bool navigating = false;

        public Vector3 velocity { get; private set; }

        private HashSet<GameObject> neighbors;
        private HashSet<GameObject> obstacles;

        private Dictionary<int, Vector3> closestPoints;

        private float robotRepulsion;

        // Constants
        private const float RADIUS = 0.25f;
        private const float ROBOT_RADIUS = 0.2f;
        private const float MASS = 80;
        private const float PERCEPTION_RADIUS_AGENT = 5.0f;
        private const float PERCEPTION_RADIUS_WALL = 0.1f;
        private const float ANGULAR_SPEED = 180;
        private const float ANIMATION_SMOOTHING = 0.6f;
        private const float IDLE_SPEED = 0.5f;
        private const int OBSTACLE_ANGLE_BINS = 6;
        private static readonly bool ShowDebug = true;
        private static readonly bool ApplyRootMotion = true;

        public float desiredSpeed = Parameters.DESIRED_SPEED;
        public float maxSpeed = Parameters.MAX_VEL;

        private Vector3 pausedVelocity = Vector3.zero;

        private void Start()
        {
            rb = GetComponent<Rigidbody>();
            if (!rb) rb = gameObject.AddComponent<Rigidbody>();
            animator = GetComponent<Animator>();
            nmPath = new NavMeshPath();
            neighbors = new HashSet<GameObject>();
            obstacles = new HashSet<GameObject>();


            // Rigidbody settings
            rb.useGravity = true;
            rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ | RigidbodyConstraints.FreezePositionY;
            rb.linearDamping = 2f;
            rb.angularDamping = 5f;

            var bounds = GetComponentInChildren<SkinnedMeshRenderer>().bounds;
            float height = bounds.extents.y * 2;

            // Collision settings
            collisionCapsule = gameObject.GetComponent<CapsuleCollider>();
            if (collisionCapsule == null)
            {
                collisionCapsule = gameObject.AddComponent<CapsuleCollider>();
            }
            collisionCapsule.radius = RADIUS;
            collisionCapsule.height = height;
            collisionCapsule.center = Vector3.up * height / 2f;

            // Animator settings
            animator.applyRootMotion = ApplyRootMotion;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            gameObject.layer = LayerMask.NameToLayer("Agent");
            gameObject.tag = "Agent";

            // var wallCollider = gameObject.AddComponent<SphereCollider>();
            // wallCollider.isTrigger = true;
            // wallCollider.radius = PERCEPTION_RADIUS_WALL;

            robotRepulsion = Random.Range(Parameters.ROBOT_REPULSION_DAMPENING_MIN, Parameters.ROBOT_REPULSION_DAMPENING_MAX);

            if (staticAgent)
            {
                animator.enabled = false;
            }

            StartCoroutine(Coroutine());
        }

        public void reset()
        {
            velocity = Vector3.zero;
            robotRepulsion = Random.Range(Parameters.ROBOT_REPULSION_DAMPENING_MIN, Parameters.ROBOT_REPULSION_DAMPENING_MAX);
            StartCoroutine(Coroutine());
        }

        private IEnumerator Coroutine()
        {
            while (true)
            {
                if (!controller.play)
                {
                    yield return null;
                    continue;
                }

                if (CloseEnough())
                {
                    StopNavigation();
                    navigating = false;
                }
                else
                {
                    PlanNavigation();
                    navigating = true;
                }

                yield return new WaitForSeconds(1f / 5f);
            }
        }

        public void InitDest(Vector3 pos)
        {
            destPos = SampledGoalPosition(pos);
            navigating = true;
            PlanNavigation();
        }

        private bool CloseEnough()
        {
            return Util.Geometry.GroundPlaneDist(destPos, transform.position) <= Parameters.CLOSE_ENOUGH_MIN_DIST;
        }

        private Vector3 SampledGoalPosition(Vector3 pos, float dist = 0.25f)
        {
            NavMesh.SamplePosition(pos, out NavMeshHit hit, dist, NavMesh.AllAreas);
            if (float.IsInfinity(hit.position.x))
            {
                return SampledGoalPosition(pos, dist + 0.25f);
            }
            return hit.position;
        }

        private void FixedUpdate()
        {
            if (!controller.play)
            {
                if (velocity != Vector3.zero)  // Première frame de la pause
                {
                    pausedVelocity = velocity;
                }

                rb.linearVelocity = Vector3.zero;
                velocity = Vector3.zero;  // Doit être temporaire
                if (animator != null && animator.enabled)
                {
                    animator.enabled = false;  // ❌ Stoppe l’animation complètement
                }
                return;
            }
            else
            {
                if (animator != null && !animator.enabled)
                {
                    animator.enabled = true;  // ✅ Reprend l’animation
                }
                
                if (velocity == Vector3.zero && pausedVelocity != Vector3.zero)
                {
                    velocity = pausedVelocity;
                    rb.linearVelocity = pausedVelocity;
                    pausedVelocity = Vector3.zero;
                }
            }
            UpdateNeighbors();
            UpdateAnimation();
            UpdateVelocity();
        }

        private void UpdateAnimation()
        {
            if (animator == null) return;
            if (!controller.play)
            {
                animator.speed = 0f;
                return;  // Empêche d'updater les paramètres
            }
            Vector3 localVelocity = transform.InverseTransformDirection(velocity);
            float forward = localVelocity.z / ANIMATION_SMOOTHING;
            float strafe = localVelocity.x / ANIMATION_SMOOTHING;

            bool isIdle = velocity.magnitude < IDLE_SPEED && !ApplyRootMotion;
            animator.speed = velocity.magnitude;
            animator.SetBool("Idling", isIdle);
            animator.SetFloat("Forward", forward);
            animator.SetFloat("Strafe", strafe);

        }

        private void StopNavigation()
        {
            if (nmPath != null) { nmPath.ClearCorners(); }
            // destPos = Vector3.zero;
            if (animator == null) return;
            animator.SetFloat("Forward", 0);
            animator.SetFloat("Strafe", 0);
        }

        private bool PlanNavigation()
        {
            if (float.IsInfinity(destPos.x)) return false;

            ComputePath(destPos);
            return true;
        }

        private void ComputePath(Vector3 destination)
        {
            destPos = destination;
            if (nmPath == null) { return; }
            NavMesh.CalculatePath(transform.position, destPos, NavMesh.AllAreas, nmPath);
        }

        private Vector3 nearestGoalPoint
        {
            get
            {
                foreach (Vector3 p in nmPath.corners)
                {
                    if (Util.Geometry.GroundPlaneDist(transform.position, p) > Parameters.NEXT_NAV_MIN_DIST)
                    {
                        return p;
                    }
                }
                return destPos;
            }
        }

        private void UpdateVelocity()
        {
            if (staticAgent) return;

            SocialForce totalForce = ComputeForce();
            Vector3 acceleration = totalForce.force / MASS;

            // Force minimal pour "décoller" à basse vitesse
            float minAcceleration = 0.5f;
            if (velocity.magnitude < IDLE_SPEED && acceleration.magnitude < minAcceleration)
            {
                Vector3 toGoal = (nearestGoalPoint - transform.position).normalized;
                acceleration = toGoal * minAcceleration;
            }

            Vector3 nextVelocity = velocity + acceleration * Time.fixedDeltaTime;

            Debug.DrawLine(transform.position, transform.position + nextVelocity, Color.magenta);

            float maxSpeed = 3f;
            if (nextVelocity.sqrMagnitude > maxSpeed * maxSpeed)
                nextVelocity = nextVelocity.normalized * maxSpeed;

            // Smooth velocity update
            velocity = Vector3.Lerp(velocity, nextVelocity, 0.5f);

            if (!navigating) velocity = Vector3.zero;

            // Apply velocity
            rb.linearVelocity = velocity;


            Vector3 goalDir = nearestGoalPoint - transform.position;
            goalDir.y = 0;
            if (goalDir.sqrMagnitude > 0.001f)
            {
                float goalWeight = 0.5f;
                Vector3 desiredDir = (goalWeight * goalDir.normalized + (1 - goalWeight) * velocity.normalized).normalized;

                float angle = -Vector3.SignedAngle(desiredDir, transform.forward, Vector3.up);

                float maxRotation = ANGULAR_SPEED * Time.fixedDeltaTime;
                if (Mathf.Abs(angle) > maxRotation)
                    angle = Mathf.Sign(angle) * maxRotation;

                transform.Rotate(Vector3.up, angle);
            }
        }

        private void UpdateNeighbors()
        {
            neighbors.Clear();
            foreach (var agent in controller.GetAgentList())
            {
                if (agent != this && Vector3.Distance(transform.position, agent.transform.position) < PERCEPTION_RADIUS_AGENT)
                {
                    neighbors.Add(agent.gameObject);
                }
            }

            foreach (ROS_DRL.Robot robot in controller.GetRobotList())
            {
                if (Vector3.Distance(transform.position, robot.transform.position) < PERCEPTION_RADIUS_AGENT)
                {
                    neighbors.Add(robot.transform.gameObject);
                }
            }
        }

        private SocialForce ComputeForce()
        {
            SocialForce total = CalculateAgentForce();
            total.force += CalculateGoalForce();
            total.force += CalculateWallForce();

            // print(CalculateAgentForce().force + "A   G" + CalculateGoalForce() + "  W " + CalculateWallForce());

            // Lateral damping
            Vector3 forwardProj = transform.forward * Vector3.Dot(transform.forward, total.force);
            Vector3 rightProj = transform.right * Vector3.Dot(transform.right, total.force);
            total.force -= forwardProj;
            total.force -= rightProj;
            total.force += rightProj / Parameters.LATERAL_DAMPENING;

            return total;
        }

        private Vector3 CalculateGoalForce()
        {
            Vector3 direction = nearestGoalPoint - transform.position;
            direction.y = 0;
            Vector3 desiredVelocity = direction.normalized * desiredSpeed;
            return MASS * (desiredVelocity - rb.linearVelocity) / Parameters.T;
        }

        private SocialForce CalculateAgentForce()
        {
            SocialForce force = new SocialForce();

            foreach (var neighbor in neighbors)
            {
                if (!neighbor.gameObject.activeSelf) { continue; }
                Vector3 dir = transform.position - neighbor.transform.position;
                dir.y = 0;
                float overlap = 0;
                float damp = 1f;

                if (!neighbor.CompareTag("Robot"))
                {
                    overlap = 2 * RADIUS - dir.magnitude;
                }
                else
                {
                    overlap = (RADIUS + ROBOT_RADIUS) - dir.magnitude;
                    Rigidbody robotRB = neighbor.GetComponent<Rigidbody>();
                    if (robotRB != null && robotRB.linearVelocity.magnitude > 0.1f)
                    {
                        damp = robotRepulsion;
                    }
                }

                dir.Normalize();
                overlap += 0.5f;

                force.force += Parameters.A * Mathf.Exp(overlap / Parameters.B) * dir * damp;

                Vector3 goalDir = (nearestGoalPoint - transform.position).normalized;
                Vector3 neighborVel = neighbor.GetComponent<HumanSFM>()?.velocity ?? neighbor.transform.forward;

                bool inFront = Vector3.Dot(-dir, goalDir) >= 0.5;
                bool approaching = Vector3.Dot(goalDir, neighborVel.normalized) < 0;

                if (inFront && approaching)
                {
                    float sideStepScale = -Vector3.Dot(-dir, goalDir);
                    force.force += sideStepScale * Parameters.A / 10 * Util.Geometry.Tangent(goalDir) * damp;

                    force.anyAgentInFront = true;
                    force.anyAgentApproaching = true;
                }
            }

            return force;
        }

        private Vector3 CalculateWallForce()
        {
            closestPoints = new Dictionary<int, Vector3>();
            foreach (var obs in obstacles)
            {
                if (obs == null) continue;

                var box = obs.GetComponent<BoxCollider>();
                if (!box) continue;

                var closest = box.ClosestPoint(transform.position);
                Vector3 dir = transform.position - closest;
                dir.y = 0;

                float overlap = RADIUS - dir.magnitude;
                int bin = (int)((Vector3.SignedAngle(transform.forward, dir, Vector3.up) + 180) / (360f / OBSTACLE_ANGLE_BINS)) % OBSTACLE_ANGLE_BINS;

                if (!closestPoints.ContainsKey(bin) || (closestPoints[bin] - transform.position).sqrMagnitude > dir.sqrMagnitude)
                {
                    closestPoints[bin] = closest;
                }
            }

            Vector3 totalForce = Vector3.zero;
            foreach (var p in closestPoints.Values)
            {
                Vector3 wallNorm = transform.position - p;
                wallNorm.y = 0;
                float overlap = RADIUS - wallNorm.magnitude;

                totalForce += Parameters.WALL_A * Mathf.Exp(overlap / Parameters.WALL_B) * wallNorm.normalized;
            }

            return totalForce;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other.isTrigger || other.CompareTag("Robot") || other.GetComponent<HumanSFM>() != null)
                return;

            float dist = Util.Geometry.GroundPlaneDist(other.transform.position, transform.position) - RADIUS;
            if (dist <= PERCEPTION_RADIUS_WALL)
            {
                obstacles.Add(other.gameObject);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (!other.isTrigger)
            {
                obstacles.Remove(other.gameObject);
            }
        }

        #region Gizmo

        protected void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying) return;

            Gizmos.color = Color.black;
            Vector3 lastPos = transform.position;
            foreach (Vector3 position in nmPath.corners)
            {
                if (Util.Geometry.GroundPlaneDist(transform.position, position) > Parameters.NEXT_NAV_MIN_DIST)
                {
                    Debug.DrawLine(lastPos, position);
                    Gizmos.DrawCube(position, new Vector3(0.15f, 0.15f, 0.15f));
                    lastPos = position;
                }
            }

            Gizmos.color = Color.blue;
            Gizmos.DrawCube(destPos, new Vector3(0.25f, 0.25f, 0.25f));

            // Display the explosion radius when selected
            // Red lines to people
            Gizmos.color = new Color(1, 0, 0, 0.75F);
            // foreach (GameObject neighbor in neighbors)
            // {
            //     if (neighbor != null)
            //     {
            //         Gizmos.DrawLine(transform.position, neighbor.transform.position);
            //     }

            // }
        }
        #endregion
    }
}