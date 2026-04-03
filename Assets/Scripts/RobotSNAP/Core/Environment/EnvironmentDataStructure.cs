using System;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;
using Random = UnityEngine.Random;

namespace RobotSNAP.Environment
{
    [Serializable]
    public class MapJsonData
    {
        [JsonProperty("verts")]
        public List<List<float>> rawVerts;
        
        [JsonProperty("id")]
        public string id;
        
        [JsonProperty("room_category")]
        public Dictionary<string, List<List<float>>> roomCategory;
        
        [JsonProperty("bbox")]
        public BoundingBoxData bbox;
        
        [JsonProperty("room_num")]
        public int roomNum;
        
        [JsonIgnore]
        public List<Vector2> verts;
        
        public void ConvertVerts()
        {
            verts = new List<Vector2>();
            if (rawVerts == null) return;
            
            foreach (var pair in rawVerts)
            {
                if (pair.Count == 2)
                    verts.Add(new Vector2(pair[0], pair[1]));
            }
        }
    }
    
    [Serializable]
    public class BoundingBoxData
    {
        [JsonProperty("min")]
        public List<float> min;
        
        [JsonProperty("max")]
        public List<float> max;
        
        [JsonIgnore]
        public Vector2 Min => min != null && min.Count >= 2 ? new Vector2(min[0], min[1]) : Vector2.zero;
        
        [JsonIgnore]
        public Vector2 Max => max != null && max.Count >= 2 ? new Vector2(max[0], max[1]) : Vector2.zero;
    }
    
    [Serializable]
    public class ScenarioData
    {
        [JsonProperty("scenarios")]
        public List<ScenarioCaseData> cases;
        
        public ScenarioCaseData GetRandomCase()
        {
            if (cases == null || cases.Count == 0) return null;
            return cases[Random.Range(0, cases.Count)];
        }
    }
    
    [Serializable]
    public class ScenarioCaseData
    {
        [JsonProperty("robot_tasks")]
        private List<RobotTaskData> robotTasks;
        
        [JsonIgnore]
        public bool HasRobotTasks => robotTasks != null && robotTasks.Count > 0;
        
        [JsonProperty("max_human")]
        private int? maxHuman;
        
        [JsonIgnore]
        public int MaxHuman => maxHuman ?? int.MaxValue;
        
        [JsonProperty("human_tasks")]
        private List<HumanTaskData> humanTasks;
        
        [JsonIgnore]
        public bool HasHumanTasks => humanTasks != null && humanTasks.Count > 0;
        
        public RobotTaskData GetRandomRobotTask()
        {
            if (!HasRobotTasks) return null;
            return robotTasks[Random.Range(0, robotTasks.Count)];
        }
        
        public HumanTaskData GetRandomHumanTask()
        {
            if (!HasHumanTasks) return null;
            return humanTasks[Random.Range(0, humanTasks.Count)];
        }
    }
    
    [Serializable]
    public class HumanTaskData
    {
        [JsonProperty("start")]
        public float[] startRaw;
        
        [JsonProperty("end")]
        public float[] endRaw;
        
        [JsonProperty("radius")]
        public float radius;
        
        [JsonIgnore]
        public Vector3 StartPosition => new Vector3(
            startRaw[0], 
            startRaw[2], 
            startRaw[1]
        ) + new Vector3(Random.Range(-radius, radius), 0, Random.Range(-radius, radius));
        
        [JsonIgnore]
        public Vector3 EndPosition => new Vector3(
            endRaw[0], 
            endRaw[2], 
            endRaw[1]
        ) + new Vector3(Random.Range(-radius, radius), 0, Random.Range(-radius, radius));
    }
    
    [Serializable]
    public class RobotTaskData
    {
        [JsonProperty("start")]
        public float[] startRaw;
        
        [JsonProperty("end")]
        public float[] endRaw;
        
        [JsonIgnore]
        public Vector3 StartPosition => new Vector3(startRaw[0], startRaw[2], startRaw[1]);
        
        [JsonIgnore]
        public Vector3 EndPosition => new Vector3(endRaw[0], endRaw[2], endRaw[1]);
        
        [JsonIgnore]
        public Quaternion StartRotation => startRaw.Length > 3 ? Quaternion.Euler(0f, startRaw[3], 0f) : Quaternion.identity;
        
        [JsonIgnore]
        public Quaternion EndRotation => endRaw.Length > 3 ? Quaternion.Euler(0f, endRaw[3], 0f) : Quaternion.identity;
        
        [JsonIgnore]
        public bool IsValid =>
            startRaw != null && endRaw != null &&
            startRaw.Length >= 3 && endRaw.Length >= 3 &&
            !float.IsNaN(startRaw[0]) && !float.IsNaN(startRaw[1]) && !float.IsNaN(startRaw[2]) &&
            !float.IsNaN(endRaw[0]) && !float.IsNaN(endRaw[1]) && !float.IsNaN(endRaw[2]);
    }
}