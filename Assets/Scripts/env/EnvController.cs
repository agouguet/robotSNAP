using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

using UnityEngine.AI;
using Unity.AI.Navigation;


using Unity.Robotics.ROSTCPConnector;
using Unity.Robotics.ROSTCPConnector.ROSGeometry;
using RosMessageTypes.Std;
using RosMessageTypes.Geometry;
using RosMessageTypes.Simulation;

using NavMsgs = RosMessageTypes.Nav;
using StdMsgs = RosMessageTypes.Std;
using RosMessageTypes.Nav;
using GeometryMsgs = RosMessageTypes.Geometry;



namespace ROS_DRL
{
    public class EnvController : MonoBehaviour
    {
        static float NEXT_NAV_MIN_DIST = 0.1f;

        public int env_id = 0;

        [HideInInspector]
        public string prefix = "";
        [HideInInspector]
        public bool use_prefix = false;

        [Header("View")]
        public Camera camera;
        private EnvCreator envCreator;


        private Robot robot;
        public List<Robot> robots;
        private GameObject goal;

        [Header("Prefab")]
        public GameObject RobotPrefab;
        public GameObject GoalPrefab;
        public GameObject HumanPrefab;


        ROSConnection ros;

        [Header("Scenario")]
        public bool useTask = true;
        public bool useSpecificPosition = true;
        public Vector3 robotStartPosition;
        public Vector3 goalPosition;
        private Vector3 localGoal;
        private GameObject localGoalSphere;

        private int limitFixNumberOfHumans = 20;
        public int minNumberOfHumans = 0;
        public int maxNumberOfHumans = 0;
        private List<HumanSFM> agents;
        public GameObject humanPool;



        [Header("ROS")]
        public string globalPathTopicName = "/global_path";
        public string localGoalFromRobotTopicName = "/local_goal_from_robot";
        public string localGoalFromMapTopicName = "/local_goal_from_map";
        public float localGoalPublishFrequencyHz = 10f;
        private float localGoalTimeElapsed = 0f;
        private float localGoalPublishInterval => 1f / localGoalPublishFrequencyHz;

        public string serviceResetName = "/unity/reset";
        public string resetDoneTopicName = "/reset_done";

        public string servicePlayName = "/unity/play";

        public string mapTopicName = "/map";
        public float mapPublishFrequencyHz = 1f;
        private float mapTimeElapsed = 0f;
        private float mapPublishInterval => 1f / mapPublishFrequencyHz;


        [Header("NavMesh")]
        public NavMeshSurface navMeshSurfaceForNavigate, navMeshSurfaceForSpawn;
        private UnityEngine.AI.NavMeshPath path;
        private NavMeshQueryFilter navMeshQueryFilterForNavigate, navMeshQueryFilterForSpawn;


        private bool resetting = false;
        public bool play = true;

        void Start()
        {
            if (use_prefix)
            { 
                prefix = "env_" + env_id;
            }
            serviceResetName = prefix + serviceResetName;
            servicePlayName = prefix + servicePlayName;
            globalPathTopicName = prefix + globalPathTopicName;
            localGoalFromRobotTopicName = prefix + localGoalFromRobotTopicName;
            localGoalFromMapTopicName = prefix + localGoalFromMapTopicName;
            resetDoneTopicName = prefix + resetDoneTopicName;
            mapTopicName = prefix + mapTopicName;

            ros = ROSConnection.GetOrCreateInstance();
            ros.ImplementService<RosMessageTypes.Simulation.ResetRequest, RosMessageTypes.Simulation.ResetResponse>(serviceResetName, CallbackReset);
            ros.ImplementService<RosMessageTypes.Simulation.PausePlayRequest, RosMessageTypes.Simulation.PausePlayResponse>(servicePlayName, CallbackPausePlay);
            ros.RegisterPublisher<BoolMsg>(resetDoneTopicName);
            ros.RegisterPublisher<NavMsgs.PathMsg>(globalPathTopicName);
            ros.RegisterPublisher<PointMsg>(localGoalFromRobotTopicName);
            ros.RegisterPublisher<PointMsg>(localGoalFromMapTopicName);
            ros.RegisterPublisher<NavMsgs.OccupancyGridMsg>(mapTopicName);

            navMeshQueryFilterForNavigate = new NavMeshQueryFilter();
            navMeshQueryFilterForNavigate.agentTypeID = navMeshSurfaceForNavigate.agentTypeID;
            navMeshQueryFilterForNavigate.areaMask = navMeshSurfaceForNavigate.layerMask;
            navMeshQueryFilterForSpawn = new NavMeshQueryFilter();
            navMeshQueryFilterForSpawn.agentTypeID = navMeshSurfaceForSpawn.agentTypeID;
            navMeshQueryFilterForSpawn.areaMask = navMeshSurfaceForSpawn.layerMask;
            path = new UnityEngine.AI.NavMeshPath();


            envCreator = GetComponent<EnvCreator>();

            robots = new List<Robot>();

            GameObject robotInstance = Instantiate(RobotPrefab, new Vector3(0, 0, 0), Quaternion.identity);
            robotInstance.transform.parent = transform;
            robotInstance.transform.localPosition = Vector3.zero;
            robotInstance.transform.localRotation = Quaternion.identity;

            robotInstance.tag = "Robot";
            // robot = robotInstance.GetComponent<Robot>();
            robot = Utils.FindScriptInChildren<Robot>(robotInstance);
            robots.Add(robot);


            goal = Instantiate(GoalPrefab, new Vector3(0, 0, 0), Quaternion.identity);
            goal.transform.parent = transform;
            goal.transform.localPosition = Vector3.zero;
            goal.transform.localRotation = Quaternion.identity;
            goal.tag = "Goal";

            if (camera != null)
            {
                SmoothFollow smoothFollowRobot = camera.GetComponent<SmoothFollow>();
                if (smoothFollowRobot != null)
                    smoothFollowRobot.target = robot.transform;
            }

            localGoalSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            localGoalSphere.transform.parent = transform;
            localGoalSphere.transform.localScale = Vector3.one * 0.2f;
            localGoalSphere.GetComponent<Collider>().enabled = false;  // Optionnel : éviter les collisions
            Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); // Ou "Legacy Shaders/Diffuse"
            mat.color = Color.green;
            localGoalSphere.GetComponent<Renderer>().material = mat;

            agents = new List<HumanSFM>();
            for (int i = 0; i < limitFixNumberOfHumans; i++)
            {
                HumanSFM agent = SpawnAgent("Agent", 0.8f, 1.0f, false, 5f);
                agent.gameObject.SetActive(false);
                agents.Add(agent);
            }

            // ResetScene();
            StartCoroutine(ResetScene());

            InvokeRepeating("PublishLocalGoal", 1.0f, (float)localGoalPublishInterval);

            InvokeRepeating("PublishMap", 1.0f, (float)mapPublishInterval);
        }

        #region Scene

        IEnumerator ResetScene()
        {
            if (resetting) { yield break; }
            resetting = true;
            robot.transform.gameObject.SetActive(false);
            goal.SetActive(false);

            yield return new WaitForSeconds(0.1f);

            setRobotPosAndRot(Vector3.zero, robot.transform.rotation);
            setGoalPosAndRot(new Vector3(10.0f, 0, 0), goal.transform.rotation);


            foreach (HumanSFM agent in agents)
            {
                agent.gameObject.SetActive(false);
            }

            if (envCreator != null)
            {
                yield return StartCoroutine(envCreator.CreateEnvironment());

                // Rebake le NavMesh si nécessaire
                if (navMeshSurfaceForSpawn)
                {
                    navMeshSurfaceForSpawn.BuildNavMesh();
                    yield return null;
                }
                if (navMeshSurfaceForNavigate)
                {
                    navMeshSurfaceForNavigate.BuildNavMesh();
                    yield return null;
                }

                

                var startPosRobot = getStartRobotPos();
                var startRot = getStartRobotRot();

                var targetPos = getGoalPos(startPosRobot);
                var targetRot = getGoalRot();

                setRobotPosAndRot(startPosRobot, startRot);
                setGoalPosAndRot(targetPos, targetRot);

                int numberOfHumans = GetRandomHumanNumber();
                for (int i = 0; i < numberOfHumans; i++)
                    {
                        HumanSFM agent = agents[i];
                        var humanPos = getHumanPos(startPosRobot, i);
                        var rot = getHumanRot();

                        var goalPos = getHumanGoal(startPosRobot, humanPos, i);
                        var goalRot = getHumanRot();

                        humanPos.y = 0.1f;
                        Pose humanStartPose = new Pose(humanPos, rot);
                        Pose humanTargetPose = new Pose(goalPos, goalRot);
                        agent.gameObject.SetActive(true);
                        agent.reset();
                        agent.transform.position = humanStartPose.position;
                        agent.transform.rotation = humanStartPose.rotation;
                        agent.InitDest(humanTargetPose.position);
                    }

                yield return new WaitForSeconds(0.1f);

                robot.transform.gameObject.SetActive(true);
                goal.SetActive(true);

                robot.Reset();
                PublishMap();
            }

            ros.Publish(resetDoneTopicName, new BoolMsg(true));
            resetting = false;

        }

        public void EditorResetScene()
        {
            StartCoroutine(ResetScene());
        }

        public void EditorPausePlayScene()
        {
            // StartCoroutine(ResetScene());
            play = !play;
        }


        private RosMessageTypes.Simulation.ResetResponse CallbackReset(RosMessageTypes.Simulation.ResetRequest request)
        {
            if (request.dataset != null)
            {
                minNumberOfHumans = request.min_human;
                maxNumberOfHumans = request.max_human;
                ((EnvCreatorFromDataset)envCreator).folderPath = "Dataset/" + request.dataset;
                // ((EnvCreatorFromDataset)envCreator).folderPath = Utils.GetRelativePath(request.dataset, Application.streamingAssetsPath);
                
            }
        
            StartCoroutine(ResetScene());

            RosMessageTypes.Simulation.ResetResponse response = new RosMessageTypes.Simulation.ResetResponse();
            response.success = true;
            return response;
        }

        private RosMessageTypes.Simulation.PausePlayResponse CallbackPausePlay(RosMessageTypes.Simulation.PausePlayRequest request)
        {
            play = request.play;

            RosMessageTypes.Simulation.PausePlayResponse response = new RosMessageTypes.Simulation.PausePlayResponse();
            response.success = true;
            return response;
        }

        protected HumanSFM SpawnAgent(string name, float desiredSpeed, float maxSpeed, bool staticAgent, float perception)
        {
            var sfRandom = Instantiate(HumanPrefab, Vector3.zero, Quaternion.identity);

            HumanSFM agent = sfRandom.GetComponentInChildren<HumanSFM>();
            agent.controller = this;
            agent.name = name;
            agent.desiredSpeed = desiredSpeed;
            agent.maxSpeed = maxSpeed;
            agent.staticAgent = staticAgent;
            agent.transform.parent = humanPool.transform;
            agent.gameObject.layer = LayerMask.NameToLayer("Agent");
            agent.gameObject.tag = "Agent";
            GameObject.Destroy(sfRandom.gameObject);
            return agent;
        }

        #endregion


        #region ROS

        void PublishLocalGoal()
        {
            if (!goal.activeSelf || !envCreator.isEnvironmentReady) { return; }
            UnityEngine.AI.NavMesh.CalculatePath(robot.transform.position, goal.transform.position, navMeshQueryFilterForNavigate, path);

            PublishPath(path);

            localGoal = FindPointAlongPathAtDistance(path, 1.0f);
            // localGoal = FindVisiblePointAlongPathAtDistance(path, 2.0f);
            var localGoalFromMapMsg = Util.Geometry.GetGeometryPoint(localGoal.To<FLU>());
            ros.Publish(localGoalFromMapTopicName, localGoalFromMapMsg);

            Vector3 localDirection = robot.transform.InverseTransformPoint(localGoal);
            var localGoalFromRobotMsg = Util.Geometry.GetGeometryPoint(localDirection.To<FLU>());
            ros.Publish(localGoalFromRobotTopicName, localGoalFromRobotMsg);

            if (localGoalSphere != null)
            {
                localGoalSphere.transform.position = localGoal;
            }
        }

        void PublishPath(NavMeshPath navPath)
        {
            HeaderMsg header = new HeaderMsg();
            header.frame_id = (prefix+"/map").TrimStart('/');
            Supervisor.instance.clock.UpdateMHeader(header);

            PoseStampedMsg[] poses = new PoseStampedMsg[navPath.corners.Length];
            for (int i = 0; i < navPath.corners.Length; i++)
            {
                Vector3 p = navPath.corners[i];

                // Pose
                // PointMsg position = new PointMsg(p.x, p.y, p.z);
                PointMsg position = Util.Geometry.GetGeometryPoint(p.To<FLU>());
                Quaternion orientationQuat = Quaternion.identity;
                QuaternionMsg orientation = new QuaternionMsg(
                    orientationQuat.x,
                    orientationQuat.y,
                    orientationQuat.z,
                    orientationQuat.w
                );

                PoseMsg pose = new PoseMsg(position, orientation);

                poses[i] = new PoseStampedMsg(header, pose);
            }

            NavMsgs.PathMsg pathMsg = new NavMsgs.PathMsg(header, poses);
            ros.Publish(globalPathTopicName, pathMsg);
        }

        private Vector3 FindPointAlongPathAtDistance(UnityEngine.AI.NavMeshPath path, float targetDistance)
        {
            float accumulatedDistance = 0f;

            if (!robot.gameObject.activeSelf || path.corners.Length == 0) { return Vector3.zero; }

            Vector3 previousPoint = robot.position;

            for (int i = 0; i < path.corners.Length; i++)
            {
                Vector3 currentPoint = path.corners[i];
                float segmentLength = Vector3.Distance(previousPoint, currentPoint);

                if (accumulatedDistance + segmentLength >= targetDistance)
                {
                    float distanceNeeded = targetDistance - accumulatedDistance;
                    Vector3 direction = (currentPoint - previousPoint).normalized;
                    return previousPoint + direction * distanceNeeded;
                }

                accumulatedDistance += segmentLength;
                previousPoint = currentPoint;
            }
            return path.corners[^1];
        }
        

        void PublishMap()
        {

            if (envCreator == null) { return; }

            Texture2D texture = envCreator.texture;
            float resolution = envCreator.resolution;

            if (texture == null) { return; }
            if (resolution == null) { return; }

            int width = texture.width;
            int height = texture.height;

            var msg = new NavMsgs.OccupancyGridMsg();

            // Header
            msg.header = new StdMsgs.HeaderMsg();
            msg.header.frame_id = mapTopicName.TrimStart('/');
            Supervisor.instance.clock.UpdateMHeader(msg.header);

            // Info
            msg.info = new NavMsgs.MapMetaDataMsg();
            msg.info.resolution = resolution; // en mètres/pixel
            msg.info.width = (uint)width;
            msg.info.height = (uint)height;
            msg.info.origin = Util.Geometry.GetMapOriginPose(Vector2.zero, resolution, width, height);

            // Convertir la texture en tableau d'occupation (int8)
            List<sbyte> mapData = new List<sbyte>(width * height);
            Color32[] pixels = texture.GetPixels32();
            for (int y = height - 1; y >= 0; y--) // inverser Y pour coord ROS
            {
                for (int x = 0; x < width; x++)
                {
                    Color32 pixel = pixels[y * width + x];
                    // Exemple : blanc = libre, noir = occupé, gris = inconnu
                    if (pixel.r < 50) // noir → occupé
                        mapData.Add(100);
                    else if (pixel.r > 200) // blanc → libre
                        mapData.Add(0);
                    else // gris → inconnu
                        mapData.Add(-1);
                }
            }

            msg.data = mapData.ToArray();
            // msg.data = FlipHorizontal(msg.data, width, height);
            msg.data = FlipVertical(msg.data, width, height);
            msg.data = Rotate90Clockwise(msg.data, width, height);

            msg.info.width = (uint)height;
            msg.info.height = (uint)width;

            // Publier sur /map
            ros.Publish(mapTopicName, msg);
        }

        private sbyte[] Rotate90Clockwise(sbyte[] data, int width, int height)
        {
            sbyte[] rotated = new sbyte[data.Length];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int srcIndex = y * width + x;
                    int dstIndex = x * height + (height - y - 1);
                    rotated[dstIndex] = data[srcIndex];
                }
            }

            return rotated;
        }

        private sbyte[] FlipVertical(sbyte[] original, int width, int height)
        {
            sbyte[] flipped = new sbyte[original.Length];
            for (int y = 0; y < height; y++)
            {
                int flippedY = height - 1 - y;
                for (int x = 0; x < width; x++)
                {
                    flipped[flippedY * width + x] = original[y * width + x];
                }
            }
            return flipped;
        }

        private sbyte[] FlipHorizontal(sbyte[] original, int width, int height)
        {
            sbyte[] flipped = new sbyte[original.Length];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int flippedX = width - 1 - x;
                    flipped[y * width + flippedX] = original[y * width + x];
                }
            }
            return flipped;
        }

        #endregion


        #region GetSetAndGizmo

        public bool ArePointsConnected(Vector3 start, Vector3 end)
        {
            UnityEngine.AI.NavMeshPath path = new UnityEngine.AI.NavMeshPath();
            if (UnityEngine.AI.NavMesh.CalculatePath(start, end, navMeshQueryFilterForNavigate, path))
            {
                return path.status == UnityEngine.AI.NavMeshPathStatus.PathComplete;
            }
            return false;
        }

        private bool IsVisible(Vector3 from, Vector3 to)
        {
            NavMeshHit hit;
            return !NavMesh.Raycast(from, to, out hit, NavMesh.AllAreas);
        }

        public float GetDistanceFromGoal()
        {
            if (!goal.activeSelf)
                return float.PositiveInfinity; // ou 0 ou -1 selon ton choix

            UnityEngine.AI.NavMeshPath path = new UnityEngine.AI.NavMeshPath();
            bool foundPath = UnityEngine.AI.NavMesh.CalculatePath(
                robot.transform.position,
                goal.transform.position,
                navMeshQueryFilterForNavigate, // ou ton navMeshQueryFilter.areaMask si tu veux filtrer
                path
            );

            if (!foundPath || path.status != UnityEngine.AI.NavMeshPathStatus.PathComplete)
                return float.PositiveInfinity;

            float totalLength = 0f;
            for (int i = 1; i < path.corners.Length; i++)
            {
                totalLength += Vector3.Distance(path.corners[i - 1], path.corners[i]);
            }

            return totalLength;
        }

        Quaternion GetRandomRot()
        {
            return Quaternion.Euler(0, Random.Range(0.0f, 360.0f), 0);
        }

        public Vector3 GetRandomNavMeshPointOnNavigateMesh()
        {
            int maxAttempts = 10;
            float radius = envCreator.GetFloorRadius();
            for (int i = 0; i < maxAttempts; i++)
            {
                Vector3 randomPoint = transform.position + UnityEngine.Random.insideUnitSphere * radius;
                if (UnityEngine.AI.NavMesh.SamplePosition(randomPoint, out UnityEngine.AI.NavMeshHit hit, radius, navMeshQueryFilterForNavigate))
                {
                    return hit.position;
                }
            }
            return transform.position;
        }

        public Vector3 GetRandomNavMeshPointOnSpawnMesh()
        {
            int maxAttempts = 10;
            float radius = envCreator.GetFloorRadius();
            for (int i = 0; i < maxAttempts; i++)
            {
                Vector3 randomPoint = transform.position + UnityEngine.Random.insideUnitSphere * radius;
                if (UnityEngine.AI.NavMesh.SamplePosition(randomPoint, out UnityEngine.AI.NavMeshHit hit, radius, navMeshQueryFilterForSpawn))
                {
                    return hit.position;
                }
            }
            return transform.position;
        }

        public void setRobotPosAndRot(Vector3 pos, Quaternion rot)
        {
            robot.transform.position = pos;
            robot.transform.rotation = rot;
            // robot.transform.localPosition = pos;
            // robot.transform.localRotation = rot;
        }

        public void setGoalPosAndRot(Vector3 pos, Quaternion rot)
        {
            goal.transform.position = pos;
            goal.transform.rotation = rot;
            // goal.transform.localPosition = pos;
            // goal.transform.localRotation = rot;
        }

        public void SetLaserSample(int laserSample)
        {
            foreach(Robot r in robots)
            {
                r.SetLaserSample(laserSample);
            }
        }


        protected Vector3 getStartRobotPos()
        {
            Debug.LogWarning(envCreator.hasValidTask);
            if (useSpecificPosition) { return transform.position + robotStartPosition; }
            if (envCreator.hasValidTask && useTask){ return transform.position + envCreator.robotTask.startPosition3D; }
            int maxAttempts = 10;
            float radius = envCreator.GetFloorRadius();
            for (int i = 0; i < maxAttempts; i++)
            {
                Vector3 randomPoint = transform.position + UnityEngine.Random.insideUnitSphere * radius;
                if (UnityEngine.AI.NavMesh.SamplePosition(randomPoint, out UnityEngine.AI.NavMeshHit hit, radius, navMeshQueryFilterForSpawn))
                {
                    return hit.position;
                }
            }
            return transform.position;
        }

        protected Quaternion getStartRobotRot()
        {
            if (envCreator.hasValidTask && useTask){ return envCreator.robotTask.startRotation; }
            return GetRandomRot();
        }

        protected Vector3 getGoalPos(Vector3 robotStartPos)
        {
            if (useSpecificPosition) { return transform.position + goalPosition; }
            if (envCreator.hasValidTask && useTask){ return transform.position + envCreator.robotTask.endPosition3D; }
            var targetPos = GetRandomNavMeshPointOnSpawnMesh();
            int maxAttempts = 100;
            for (int i = 0; i < maxAttempts; i++)
            {
                if (Vector3.Distance(robotStartPos, targetPos) >= 3.0f && ArePointsConnected(robotStartPos, targetPos))
                {
                    break;
                }
                targetPos = GetRandomNavMeshPointOnSpawnMesh();
            }
            return targetPos;
        }

        protected Quaternion getGoalRot()
        {
            if (envCreator.hasValidTask && useTask){ return envCreator.robotTask.endRotation; }
            return GetRandomRot();
        }

        protected int GetRandomHumanNumber()
        {
            if (envCreator.hasValidScenario && useTask)
            {
                int upperLimit = Mathf.Min(maxNumberOfHumans, envCreator.scenario.max_human);
                return Mathf.Min(envCreator.scenario.max_human, Random.Range(minNumberOfHumans, upperLimit + 1));
            }
            return Random.Range(minNumberOfHumans, maxNumberOfHumans + 1);
        }

        protected Vector3 getHumanPos(Vector3 robotStartPos, int idx)
        {
            if (envCreator.hasValidScenario && useTask){
                HumanTask humanTask = envCreator.scenario.GetRandomHumanTask();
                if(humanTask != null) {return transform.position + humanTask.startPosition;}
            }
            var targetPos = GetRandomNavMeshPointOnNavigateMesh();
            int maxAttempts = 100;
            for (int i = 0; i < maxAttempts; i++)
            {
                if (Vector3.Distance(robotStartPos, targetPos) >= 3.0f && ArePointsConnected(robotStartPos, targetPos))
                {
                    break;
                }
                targetPos = GetRandomNavMeshPointOnNavigateMesh();
            }
            return targetPos;
        }

        protected Vector3 getHumanGoal(Vector3 robotStartPos, Vector3 humanStartPos, int idx)
        {
            if (envCreator.hasValidScenario && useTask){
                int maxAttemptsHumanTasks = 100;
                for (int i = 0; i < maxAttemptsHumanTasks; i++)
                {
                    HumanTask humanTask = envCreator.scenario.GetRandomHumanTask();
                    if (humanTask == null) { continue; }
                    var targetPosHumanTask = transform.position + humanTask.endPosition;
                    if (Vector3.Distance(humanStartPos, targetPosHumanTask) >= 3.0f)
                    {
                        return targetPosHumanTask;
                    }
                }
            }
            var targetPos = GetRandomNavMeshPointOnNavigateMesh();
            int maxAttempts = 100;
            for (int i = 0; i < maxAttempts; i++)
            {
                if (Vector3.Distance(robotStartPos, targetPos) >= 3.0f && ArePointsConnected(robotStartPos, targetPos))
                {
                    break;
                }
                targetPos = GetRandomNavMeshPointOnNavigateMesh();
            }
            return targetPos;
        }

        protected Quaternion getHumanRot()
        {
            return GetRandomRot();
        }

        public List<HumanSFM> GetAgentList()
        {
            return agents;
        }

        public List<Robot> GetRobotList()
        {
            return robots;
        }

        void OnDrawGizmos()
        {
            if (localGoal != null)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawSphere(localGoal, 0.2f);

            }
        }
        #endregion
    }
}