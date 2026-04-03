using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

namespace RobotSNAP
{
    [CustomEditor(typeof(AgentDetector))]
    public class FieldOfViewEditor : Editor
    {
        private void OnSceneGUI()
        {
            AgentDetector detector = (AgentDetector)target;
            
            if (detector == null) return;
            
            // Couleurs
            Color defaultColor = Handles.color;
            
            // Dessiner le rayon de détection
            Handles.color = new Color(1f, 1f, 1f, 0.3f);
            Handles.DrawWireArc(detector.transform.position, Vector3.up, Vector3.forward, 360, detector.Radius);
            
            // Dessiner le champ de vision
            Vector3 viewAngleLeft = DirectionFromAngle(detector.transform.eulerAngles.y, -detector.Angle / 2);
            Vector3 viewAngleRight = DirectionFromAngle(detector.transform.eulerAngles.y, detector.Angle / 2);
            
            Handles.color = Color.yellow;
            Handles.DrawLine(detector.transform.position, detector.transform.position + viewAngleLeft * detector.Radius);
            Handles.DrawLine(detector.transform.position, detector.transform.position + viewAngleRight * detector.Radius);
            
            // Dessiner un arc pour représenter le champ de vision
            Handles.DrawWireArc(detector.transform.position, Vector3.up, viewAngleLeft, detector.Angle, detector.Radius);
            
            // Dessiner les agents visibles
            if (Application.isPlaying && detector.AgentsView != null)
            {
                Handles.color = Color.green;
                foreach (var agent in detector.AgentsView)
                {
                    if (agent.Key != null && agent.Value)
                    {
                        Vector3 targetPosition = agent.Key.transform.position + Vector3.up * 0.5f;
                        Handles.DrawLine(detector.transform.position, targetPosition);
                        
                        // Dessiner un petit cercle autour de l'agent visible
                        Handles.DrawWireDisc(targetPosition, Vector3.up, 0.3f);
                    }
                }
            }
            
            // Dessiner les agents non visibles (optionnel)
            if (Application.isPlaying && detector.AgentsView != null)
            {
                Handles.color = Color.red;
                foreach (var agent in detector.AgentsView)
                {
                    if (agent.Key != null && !agent.Value)
                    {
                        Vector3 targetPosition = agent.Key.transform.position + Vector3.up * 0.5f;
                        Handles.DrawWireDisc(targetPosition, Vector3.up, 0.2f);
                    }
                }
            }
            
            // Restaurer la couleur par défaut
            Handles.color = defaultColor;
        }
        
        private Vector3 DirectionFromAngle(float eulerY, float angleInDegrees)
        {
            angleInDegrees += eulerY;
            return new Vector3(Mathf.Sin(angleInDegrees * Mathf.Deg2Rad), 0, Mathf.Cos(angleInDegrees * Mathf.Deg2Rad));
        }
        
        // Override pour afficher des infos supplémentaires dans l'inspecteur
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            
            AgentDetector detector = (AgentDetector)target;
            
            if (Application.isPlaying && detector != null)
            {
                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField("Runtime Info", EditorStyles.boldLabel);
                
                EditorGUI.BeginDisabledGroup(true);
                
                // Afficher le nombre d'agents visibles
                int visibleCount = 0;
                if (detector.AgentsView != null)
                {
                    foreach (var agent in detector.AgentsView)
                    {
                        if (agent.Value) visibleCount++;
                    }
                }
                EditorGUILayout.LabelField("Visible Agents", visibleCount.ToString());
                EditorGUILayout.LabelField("Total Agents", detector.AgentsView?.Count.ToString() ?? "0");
                
                EditorGUI.EndDisabledGroup();
                
                // Bouton pour forcer un rafraîchissement
                if (GUILayout.Button("Refresh Agent List"))
                {
                    detector.RefreshAgentList();
                    SceneView.RepaintAll();
                }
                
                if (GUILayout.Button("Force Detection Check"))
                {
                    detector.ForceDetectionCheck();
                    SceneView.RepaintAll();
                }
                
                EditorGUILayout.Space(5);
                
                // Afficher la liste des agents visibles
                if (detector.AgentsView != null && detector.AgentsView.Count > 0)
                {
                    EditorGUILayout.LabelField("Visible Agents List", EditorStyles.miniBoldLabel);
                    foreach (var agent in detector.AgentsView)
                    {
                        if (agent.Key != null && agent.Value)
                        {
                            float distance = Vector3.Distance(detector.transform.position, agent.Key.transform.position);
                            EditorGUILayout.LabelField($"  - {agent.Key.name}", $"{distance:F1}m");
                        }
                    }
                }
            }
        }
    }
}