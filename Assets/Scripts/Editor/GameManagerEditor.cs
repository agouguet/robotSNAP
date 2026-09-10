using UnityEditor;
using UnityEngine;
using RobotSNAP.Core;
using RobotSNAP.ROS;

namespace RobotSNAP
{
    [CustomEditor(typeof(GameManager))]
    public class GameManagerEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            GameManager gameManager = (GameManager)target;

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Runtime Controls", EditorStyles.boldLabel);
            
            GUI.enabled = Application.isPlaying;
            
            // Reset button
            if (GUILayout.Button("Reset Environment", GUILayout.Height(30)))
            {
                if (gameManager != null)
                {
                    gameManager.EditorReset();
                    Debug.Log("[GameManagerEditor] Reset triggered");
                }
            }
            
            // Pause/Play button
            if (GUILayout.Button("Pause/Play", GUILayout.Height(30)))
            {
                if (gameManager != null)
                {
                    // Toggle pause via Clock
                    if (Clock.Instance != null)
                    {
                        Clock.Instance.TogglePause();
                        Debug.Log($"[GameManagerEditor] Play state toggled. IsPlaying: {!Clock.Instance.IsPaused}");
                    }
                    else
                    {
                        Debug.LogWarning("[GameManagerEditor] Clock instance not found!");
                    }
                }
            }
            
            // Force publish button (optional)
            // if (GUILayout.Button("Force Publish Map", GUILayout.Height(25)))
            // {
            //     if (gameManager != null)
            //     {
            //         var mapPublisher = gameManager.GetComponent<MapPublisher>();
            //         if (mapPublisher != null)
            //         {
            //             mapPublisher.PublishMap();
            //             Debug.Log("[GameManagerEditor] Map publish forced");
            //         }
            //         else
            //         {
            //             Debug.LogWarning("[GameManagerEditor] MapPublisher not found!");
            //         }
            //     }
            // }
            
            GUI.enabled = true;
            
            // Display runtime info if in play mode
            if (Application.isPlaying)
            {
                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField("Runtime Info", EditorStyles.boldLabel);
                
                EditorGUI.BeginDisabledGroup(true);
                
                // Clock info
                if (Clock.Instance != null)
                {
                    EditorGUILayout.LabelField("Simulation Time", $"{Clock.Instance.CurrentTimeSeconds:F2}s");
                    EditorGUILayout.LabelField("Paused", Clock.Instance.IsPaused ? "Yes" : "No");
                }
                else
                {
                    EditorGUILayout.LabelField("Clock", "Not found");
                }
                
                EditorGUI.EndDisabledGroup();
            }
        }
    }
}