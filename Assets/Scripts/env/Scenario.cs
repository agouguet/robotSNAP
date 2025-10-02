using System;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;
using Random = UnityEngine.Random;

namespace ROS_DRL
{

    [Serializable]
    public class Scenario
    {
        [JsonProperty("scenarios")]
        public List<ScenarioCase> cases;

        public ScenarioCase GetRandomCase()
        {
            if (cases == null || cases.Count == 0)
                return null;

            int index = Random.Range(0, cases.Count);
            return cases[index];
        }
    }



    [Serializable]
    public class ScenarioCase
    {
        [JsonProperty("robot_tasks")]
        protected List<RobotTask> robot_tasks;

        [JsonIgnore]
        public bool HasRobotTasks => robot_tasks != null && robot_tasks.Count > 0;

        public RobotTask GetRandomRobotTask()
        {
            if (!HasRobotTasks)
            {
                Debug.LogWarning("No available robot task.");
                return null;
            }

            int index = Random.Range(0, robot_tasks.Count);
            return robot_tasks[index];
        }

        [JsonProperty("max_human")]
        protected int? _max_human;

        [JsonIgnore]
        public int max_human => _max_human ?? int.MaxValue;

        [JsonProperty("human_tasks")]
        protected List<HumanTask> human_tasks;

        [JsonIgnore]
        public bool HasHumanTasks => human_tasks != null && human_tasks.Count > 0;

        public HumanTask GetRandomHumanTask()
        {
            if (!HasHumanTasks)
            {
                Debug.LogWarning("No available human task.");
                return null;
            }

            int index = Random.Range(0, human_tasks.Count);
            return human_tasks[index];
        }

        public Vector3 GetRandomHumanStartPosition()
        {
            HumanTask humanTask = GetRandomHumanTask();
            if (humanTask != null)
                return humanTask.startPosition;

            return Vector3.zero;
        }

        public Vector3 GetRandomHumanEndPosition()
        {
            HumanTask humanTask = GetRandomHumanTask();
            if (humanTask != null)
                return humanTask.endPosition;

            return Vector3.zero;
        }
    }

    [Serializable]
    public class HumanTask
    {
        [JsonProperty("start")]
        public float[] startRaw;

        [JsonProperty("end")]
        public float[] endRaw;

        [JsonProperty("radius")]
        public float radius;

        [JsonIgnore]
        public Vector3 startPosition => new Vector3(startRaw[0], startRaw[2], startRaw[1]) + new Vector3(Random.Range(-radius, radius), 0, Random.Range(-radius, radius));

        [JsonIgnore]
        public Vector3 endPosition => new Vector3(endRaw[0], endRaw[2], endRaw[1]) + new Vector3(Random.Range(-radius, radius), 0, Random.Range(-radius, radius));
    }

    [Serializable]
    public class RobotTask
    {
        [JsonProperty("start")]
        public float[] startRaw;

        [JsonProperty("end")]
        public float[] endRaw;

        [JsonIgnore]
        public Vector3 startPosition3D => new Vector3(startRaw[0], startRaw[2], startRaw[1]);

        [JsonIgnore]
        public Vector3 endPosition3D => new Vector3(endRaw[0], endRaw[2], endRaw[1]);

        [JsonIgnore]
        public Quaternion startRotation => startRaw.Length > 3 ? Quaternion.Euler(0f, startRaw[3], 0f) : Quaternion.identity;

        [JsonIgnore]
        public Quaternion endRotation => endRaw.Length > 3 ? Quaternion.Euler(0f, endRaw[3], 0f) : Quaternion.identity;

        [JsonIgnore]
        public bool IsValid =>
            startRaw != null && endRaw != null &&
            startRaw.Length >= 3 && endRaw.Length >= 3 &&
            !float.IsNaN(startRaw[0]) && !float.IsNaN(startRaw[1]) && !float.IsNaN(startRaw[2]) &&
            !float.IsNaN(endRaw[0]) && !float.IsNaN(endRaw[1]) && !float.IsNaN(endRaw[2]);
    }
}
