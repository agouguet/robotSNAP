#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using RobotSNAP.Agents;

namespace RobotSNAP.Human
{
    [CustomEditor(typeof(HumanConfig))]
    public class HumanConfigEditor : Editor
    {
        // Toggles (foldouts)
        private SerializedProperty showMovementParameters;
        private SerializedProperty showMovementController;
        private SerializedProperty showSFMParameters;
        private SerializedProperty showONNXParameters;
        private SerializedProperty showHybridParameters;
        private SerializedProperty showAnimationParameters;
        private SerializedProperty showNavigationParameters;
        private SerializedProperty showVisualizationParameters;

        // Movement
        private SerializedProperty desiredSpeed;
        private SerializedProperty maxSpeed;
        private SerializedProperty slowDownDistance;

        // Controller selection
        private SerializedProperty controllerType;

        // SFM - Physical
        private SerializedProperty agentMass;
        private SerializedProperty agentRadius;

        // SFM - Goal force
        private SerializedProperty relaxationTime;
        private SerializedProperty goalForceStrength;
        private SerializedProperty goalForceDistance;

        // SFM - Social forces
        private SerializedProperty perceptionRadiusAgent;
        private SerializedProperty socialForceA;
        private SerializedProperty socialForceB;
        private SerializedProperty alignmentStrength;

        // SFM - Contact forces
        private SerializedProperty contactStiffnessK;
        private SerializedProperty contactFrictionKappa;

        // SFM - Wall forces
        private SerializedProperty wallForceA;
        private SerializedProperty wallForceB;
        private SerializedProperty wallContactStiffnessK;
        private SerializedProperty wallContactFrictionKappa;

        // SFM - Obstacle forces
        private SerializedProperty obstaclePerceptionRadius;
        private SerializedProperty obstacleForceStrength;
        private SerializedProperty obstacleForceDistance;

        // SFM - Robot forces
        private SerializedProperty robotPerceptionRadius;
        private SerializedProperty robotRepulsionStrength;
        private SerializedProperty robotForceDistance;

        // SFM - Dampening
        private SerializedProperty backwardDampening;
        private SerializedProperty lateralDampening;
        private SerializedProperty robotRepulsionDampeningMin;
        private SerializedProperty robotRepulsionDampeningMax;

        // ONNX
        private SerializedProperty onnxModelConfig;

        // Hybrid
        private SerializedProperty predictionWeight;
        private SerializedProperty navigationWeight;
        private SerializedProperty useAdaptiveWeighting;
        private SerializedProperty hybridAdaptationRate;

        // Navigation
        private SerializedProperty goalReachedDistance;
        private SerializedProperty pathUpdateInterval;
        private SerializedProperty nextNavMinDistance;
        private SerializedProperty closeEnoughMinDistance;

        // Animation
        private SerializedProperty animationController;
        private SerializedProperty animationSmoothing;
        private SerializedProperty idleSpeedThreshold;
        private SerializedProperty angularSpeed;

        // Visualization
        private SerializedProperty showPredictionGizmos;
        private SerializedProperty predictionColor;
        private SerializedProperty trajectoryColor;

        private HumanConfig config;

        private void OnEnable()
        {
            // Toggles
            showMovementParameters = serializedObject.FindProperty("showMovementParameters");
            showMovementController = serializedObject.FindProperty("showMovementController");
            showSFMParameters = serializedObject.FindProperty("showSFMParameters");
            showONNXParameters = serializedObject.FindProperty("showONNXParameters");
            showHybridParameters = serializedObject.FindProperty("showHybridParameters");
            showAnimationParameters = serializedObject.FindProperty("showAnimationParameters");
            showNavigationParameters = serializedObject.FindProperty("showNavigationParameters");
            showVisualizationParameters = serializedObject.FindProperty("showVisualizationParameters");

            // Movement
            desiredSpeed = serializedObject.FindProperty("desiredSpeed");
            maxSpeed = serializedObject.FindProperty("maxSpeed");
            slowDownDistance = serializedObject.FindProperty("slowDownDistance");

            // Controller
            controllerType = serializedObject.FindProperty("controllerType");

            // SFM - Physical
            agentMass = serializedObject.FindProperty("agentMass");
            agentRadius = serializedObject.FindProperty("agentRadius");

            // SFM - Goal
            relaxationTime = serializedObject.FindProperty("relaxationTime");
            goalForceStrength = serializedObject.FindProperty("goalForceStrength");
            goalForceDistance = serializedObject.FindProperty("goalForceDistance");

            // SFM - Social
            perceptionRadiusAgent = serializedObject.FindProperty("perceptionRadiusAgent");
            socialForceA = serializedObject.FindProperty("socialForceA");
            socialForceB = serializedObject.FindProperty("socialForceB");
            alignmentStrength = serializedObject.FindProperty("alignmentStrength");

            // SFM - Contact
            contactStiffnessK = serializedObject.FindProperty("contactStiffnessK");
            contactFrictionKappa = serializedObject.FindProperty("contactFrictionKappa");

            // SFM - Wall
            wallForceA = serializedObject.FindProperty("wallForceA");
            wallForceB = serializedObject.FindProperty("wallForceB");
            wallContactStiffnessK = serializedObject.FindProperty("wallContactStiffnessK");
            wallContactFrictionKappa = serializedObject.FindProperty("wallContactFrictionKappa");

            // SFM - Obstacles
            obstaclePerceptionRadius = serializedObject.FindProperty("obstaclePerceptionRadius");
            obstacleForceStrength = serializedObject.FindProperty("obstacleForceStrength");
            obstacleForceDistance = serializedObject.FindProperty("obstacleForceDistance");

            // SFM - Robot
            robotPerceptionRadius = serializedObject.FindProperty("robotPerceptionRadius");
            robotRepulsionStrength = serializedObject.FindProperty("robotRepulsionStrength");
            robotForceDistance = serializedObject.FindProperty("robotForceDistance");

            // SFM - Dampening
            backwardDampening = serializedObject.FindProperty("backwardDampening");
            lateralDampening = serializedObject.FindProperty("lateralDampening");
            robotRepulsionDampeningMin = serializedObject.FindProperty("robotRepulsionDampeningMin");
            robotRepulsionDampeningMax = serializedObject.FindProperty("robotRepulsionDampeningMax");

            // ONNX
            onnxModelConfig = serializedObject.FindProperty("onnxModelConfig");

            // Hybrid
            predictionWeight = serializedObject.FindProperty("predictionWeight");
            navigationWeight = serializedObject.FindProperty("navigationWeight");
            useAdaptiveWeighting = serializedObject.FindProperty("useAdaptiveWeighting");
            hybridAdaptationRate = serializedObject.FindProperty("hybridAdaptationRate");

            // Navigation
            goalReachedDistance = serializedObject.FindProperty("goalReachedDistance");
            pathUpdateInterval = serializedObject.FindProperty("pathUpdateInterval");
            nextNavMinDistance = serializedObject.FindProperty("nextNavMinDistance");
            closeEnoughMinDistance = serializedObject.FindProperty("closeEnoughMinDistance");

            // Animation
            animationController = serializedObject.FindProperty("animationController");
            animationSmoothing = serializedObject.FindProperty("animationSmoothing");
            idleSpeedThreshold = serializedObject.FindProperty("idleSpeedThreshold");
            angularSpeed = serializedObject.FindProperty("angularSpeed");

            // Visualization
            showPredictionGizmos = serializedObject.FindProperty("showPredictionGizmos");
            predictionColor = serializedObject.FindProperty("predictionColor");
            trajectoryColor = serializedObject.FindProperty("trajectoryColor");

            config = (HumanConfig)target;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // ==================== MOVEMENT ====================
            showMovementParameters.boolValue = EditorGUILayout.Foldout(showMovementParameters.boolValue, "Movement Parameters", true);
            if (showMovementParameters.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(desiredSpeed);
                EditorGUILayout.PropertyField(maxSpeed);
                EditorGUILayout.PropertyField(slowDownDistance);
                EditorGUI.indentLevel--;
                EditorGUILayout.Space();
            }

            // ==================== CONTROLLER SELECTION ====================
            showMovementController.boolValue = EditorGUILayout.Foldout(showMovementController.boolValue, "Movement Controller Selection", true);
            if (showMovementController.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(controllerType);
                EditorGUI.indentLevel--;
                EditorGUILayout.Space();
            }

            // ==================== SFM PARAMETERS ====================
            showSFMParameters.boolValue = EditorGUILayout.Foldout(showSFMParameters.boolValue, "SFM Parameters", true);
            if (showSFMParameters.boolValue)
            {
                EditorGUI.indentLevel++;

                // --- Physical Properties ---
                EditorGUILayout.LabelField("Physical Properties", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(agentMass);
                EditorGUILayout.PropertyField(agentRadius);
                EditorGUILayout.Space();

                // --- Goal Force ---
                EditorGUILayout.LabelField("Goal Force", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(relaxationTime);
                EditorGUILayout.PropertyField(goalForceStrength);
                EditorGUILayout.PropertyField(goalForceDistance);
                EditorGUILayout.Space();

                // --- Social Forces ---
                EditorGUILayout.LabelField("Social Forces", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(perceptionRadiusAgent);
                EditorGUILayout.PropertyField(socialForceA);
                EditorGUILayout.PropertyField(socialForceB);
                EditorGUILayout.PropertyField(alignmentStrength);
                EditorGUILayout.Space();

                // --- Contact Forces ---
                EditorGUILayout.LabelField("Contact Forces", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(contactStiffnessK);
                EditorGUILayout.PropertyField(contactFrictionKappa);
                EditorGUILayout.Space();

                // --- Wall Forces ---
                EditorGUILayout.LabelField("Wall Forces", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(wallForceA);
                EditorGUILayout.PropertyField(wallForceB);
                EditorGUILayout.PropertyField(wallContactStiffnessK);
                EditorGUILayout.PropertyField(wallContactFrictionKappa);
                EditorGUILayout.Space();

                // --- Obstacle Forces ---
                EditorGUILayout.LabelField("Obstacle Forces", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(obstaclePerceptionRadius);
                EditorGUILayout.PropertyField(obstacleForceStrength);
                EditorGUILayout.PropertyField(obstacleForceDistance);
                EditorGUILayout.Space();

                // --- Robot Forces ---
                EditorGUILayout.LabelField("Robot Forces", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(robotPerceptionRadius);
                EditorGUILayout.PropertyField(robotRepulsionStrength);
                EditorGUILayout.PropertyField(robotForceDistance);
                EditorGUILayout.Space();

                // --- Dampening ---
                EditorGUILayout.LabelField("Dampening", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(backwardDampening);
                EditorGUILayout.PropertyField(lateralDampening);
                EditorGUILayout.PropertyField(robotRepulsionDampeningMin);
                EditorGUILayout.PropertyField(robotRepulsionDampeningMax);

                EditorGUI.indentLevel--;
                EditorGUILayout.Space();
            }

            // ==================== ONNX ====================
            showONNXParameters.boolValue = EditorGUILayout.Foldout(showONNXParameters.boolValue, "ONNX Model Parameters", true);
            if (showONNXParameters.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(onnxModelConfig);
                EditorGUI.indentLevel--;
                EditorGUILayout.Space();
            }

            // ==================== HYBRID ====================
            showHybridParameters.boolValue = EditorGUILayout.Foldout(showHybridParameters.boolValue, "Hybrid Parameters", true);
            if (showHybridParameters.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(predictionWeight);
                EditorGUILayout.PropertyField(navigationWeight);
                EditorGUILayout.PropertyField(useAdaptiveWeighting);
                EditorGUILayout.PropertyField(hybridAdaptationRate);
                EditorGUI.indentLevel--;
                EditorGUILayout.Space();
            }

            // ==================== ANIMATION ====================
            showAnimationParameters.boolValue = EditorGUILayout.Foldout(showAnimationParameters.boolValue, "Animation Parameters", true);
            if (showAnimationParameters.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(animationController);
                EditorGUILayout.PropertyField(animationSmoothing);
                EditorGUILayout.PropertyField(idleSpeedThreshold);
                EditorGUILayout.PropertyField(angularSpeed);
                EditorGUI.indentLevel--;
                EditorGUILayout.Space();
            }

            // ==================== NAVIGATION ====================
            showNavigationParameters.boolValue = EditorGUILayout.Foldout(showNavigationParameters.boolValue, "Navigation", true);
            if (showNavigationParameters.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(goalReachedDistance);
                EditorGUILayout.PropertyField(pathUpdateInterval);
                EditorGUILayout.PropertyField(nextNavMinDistance);
                EditorGUILayout.PropertyField(closeEnoughMinDistance);
                EditorGUI.indentLevel--;
                EditorGUILayout.Space();
            }

            // ==================== VISUALIZATION ====================
            showVisualizationParameters.boolValue = EditorGUILayout.Foldout(showVisualizationParameters.boolValue, "Visualization", true);
            if (showVisualizationParameters.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(showPredictionGizmos);
                EditorGUILayout.PropertyField(predictionColor);
                EditorGUILayout.PropertyField(trajectoryColor);
                EditorGUI.indentLevel--;
                EditorGUILayout.Space();
            }

            // ==================== READ-ONLY PROPERTIES ====================
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Read-only Properties (from ONNX config)", EditorStyles.boldLabel);
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