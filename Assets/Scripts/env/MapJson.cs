using System;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;


[Serializable]
public class MapJson
{
    [JsonProperty("verts")]
    public List<List<float>> rawVerts;

    [JsonProperty("id")]
    public string id;

    [JsonProperty("room_category")]
    public Dictionary<string, List<List<float>>> room_category;

    [JsonProperty("bbox")]
    public BoundingBox bbox;

    [JsonProperty("room_num")]
    public int room_num;

    [JsonIgnore]
    public List<Vector2> verts;

    public void ConvertVerts()
    {
        verts = new List<Vector2>();
        if (rawVerts != null)
        {
            foreach (var pair in rawVerts)
            {
                if (pair.Count == 2)  // Vérifie qu'on a bien des paires X, Y
                    verts.Add(new Vector2(pair[0], pair[1]));
                else
                    Debug.LogWarning("Une entrée de rawVerts n'a pas exactement 2 valeurs.");
            }
        }
        else
        {
            Debug.LogWarning("rawVerts est null après la désérialisation.");
        }
    }
}

[Serializable]
public class BoundingBox
{
    [JsonProperty("min")]
    public List<float> min;

    [JsonProperty("max")]
    public List<float> max;

    public Vector2 Min => new Vector2(min[0], min[1]);
    public Vector2 Max => new Vector2(max[0], max[1]);
}

// [Serializable]
// public class Vertices
// {
//     public List<float> values;


//     public float x => values[0];
//     public float y => values[1];
// }