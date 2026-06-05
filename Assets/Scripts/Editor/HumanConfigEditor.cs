#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using RobotSNAP.Agents;

namespace RobotSNAP.Human
{
    [CustomEditor(typeof(HumanConfig))]
    public class HumanConfigEditor : Editor
    {
        private SerializedProperty showMovementParameters;
        private SerializedProperty showMovementController;
        private SerializedProperty showSFMParameters;
        private SerializedProperty showONNXParameters;
        private SerializedProperty showHybridParameters;
        private SerializedProperty showAnimationParameters;
        private SerializedProperty showNavigationParameters;
        private SerializedProperty showVisualizationParameters;

        private HumanConfig config;

        private void OnEnable()
        {
            showMovementParameters = serializedObject.FindProperty("showMovementParameters");
            showMovementController = serializedObject.FindProperty("showMovementController");
            showSFMParameters = serializedObject.FindProperty("showSFMParameters");
            showONNXParameters = serializedObject.FindProperty("showONNXParameters");
            showHybridParameters = serializedObject.FindProperty("showHybridParameters");
            showAnimationParameters = serializedObject.FindProperty("showAnimationParameters");
            showNavigationParameters = serializedObject.FindProperty("showNavigationParameters");
            showVisualizationParameters = serializedObject.FindProperty("showVisualizationParameters");
            
            config = (HumanConfig)target;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // Movement Parameters
            showMovementParameters.boolValue = EditorGUILayout.Foldout(showMovementParameters.boolValue, "Movement Parameters", true);
            if (showMovementParameters.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(serializedObject.FindProperty("desiredSpeed"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("maxSpeed"));
                EditorGUI.indentLevel--;
                EditorGUILayout.Space();
            }

            // Movement Controller Selection
            showMovementController.boolValue = EditorGUILayout.Foldout(showMovementController.boolValue, "Movement Controller Selection", true);
            if (showMovementController.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(serializedObject.FindProperty("controllerType"));
                EditorGUI.indentLevel--;
                EditorGUILayout.Space();
            }

            // SFM Parameters
            showSFMParameters.boolValue = EditorGUILayout.Foldout(showSFMParameters.boolValue, "SFM Parameters", true);
            if (showSFMParameters.boolValue)
            {
                EditorGUI.indentLevel++;
                
                // Perception
                EditorGUILayout.LabelField("Perception", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("perceptionRadiusAgent"));
                EditorGUILayout.Space();
                
                // Social Forces
                EditorGUILayout.LabelField("Social Forces", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("relaxationTime"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("socialForceA"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("socialForceB"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("contactStiffnessK"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("contactFrictionKappa"));
                EditorGUILayout.Space();
                
                // Wall Forces
                EditorGUILayout.LabelField("Wall Forces", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("wallForceA"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("wallForceB"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("wallContactStiffnessK"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("wallContactFrictionKappa"));
                EditorGUILayout.Space();
                
                // Tangential Forces
                // EditorGUILayout.LabelField("Tangential Forces", EditorStyles.boldLabel);
                // EditorGUILayout.PropertyField(serializedObject.FindProperty("tangentialForceA"));
                // EditorGUILayout.PropertyField(serializedObject.FindProperty("tangentialForceB"));
                // EditorGUILayout.Space();
                
                // Navigation
                EditorGUILayout.LabelField("Navigation", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("nextNavMinDistance"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("closeEnoughMinDistance"));
                EditorGUILayout.Space();
                
                // Dampening
                EditorGUILayout.LabelField("Dampening", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("backwardDampening"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("lateralDampening"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("robotRepulsionDampeningMin"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("robotRepulsionDampeningMax"));
                
                EditorGUI.indentLevel--;
                EditorGUILayout.Space();
            }

            // ONNX Model Parameters
            showONNXParameters.boolValue = EditorGUILayout.Foldout(showONNXParameters.boolValue, "ONNX Model Parameters", true);
            if (showONNXParameters.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(serializedObject.FindProperty("onnxModelConfig"));
                EditorGUI.indentLevel--;
                EditorGUILayout.Space();
            }

            // Hybrid Parameters
            showHybridParameters.boolValue = EditorGUILayout.Foldout(showHybridParameters.boolValue, "Hybrid Parameters", true);
            if (showHybridParameters.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(serializedObject.FindProperty("predictionWeight"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("navigationWeight"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("useAdaptiveWeighting"));
                EditorGUI.indentLevel--;
                EditorGUILayout.Space();
            }

            // Animation Parameters
            showAnimationParameters.boolValue = EditorGUILayout.Foldout(showAnimationParameters.boolValue, "Animation Parameters", true);
            if (showAnimationParameters.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(serializedObject.FindProperty("animationController"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("animationSmoothing"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("idleSpeedThreshold"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("angularSpeed"));
                EditorGUI.indentLevel--;
                EditorGUILayout.Space();
            }

            // Navigation
            showNavigationParameters.boolValue = EditorGUILayout.Foldout(showNavigationParameters.boolValue, "Navigation", true);
            if (showNavigationParameters.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(serializedObject.FindProperty("goalReachedDistance"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("pathUpdateInterval"));
                EditorGUI.indentLevel--;
                EditorGUILayout.Space();
            }

            // Visualization
            showVisualizationParameters.boolValue = EditorGUILayout.Foldout(showVisualizationParameters.boolValue, "Visualization", true);
            if (showVisualizationParameters.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(serializedObject.FindProperty("showPredictionGizmos"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("predictionColor"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("trajectoryColor"));
                EditorGUI.indentLevel--;
                EditorGUILayout.Space();
            }

            // Read-only properties
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Read-only Properties", EditorStyles.boldLabel);
            
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.TextField("ONNX Model Path", config.onnxModelPath);
            EditorGUILayout.Toggle("ONNX Use GPU", config.onnxUseGPU);
            EditorGUILayout.IntField("Prediction Samples", config.predictionSamples);
            EditorGUILayout.FloatField("Prediction Update Rate", config.predictionUpdateRate);
            EditorGUI.EndDisabledGroup();

            serializedObject.ApplyModifiedProperties();
        }
    }
}
#endif