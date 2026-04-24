[System.Serializable]
public class FullScenarioSettings
{
    // Agents
    public int environmentCount;
    public float environmentSpacing;
    public string datasetPath;
    public int minHumans, maxHumans;
    public float minAgentDistance;
    public float robotSpeed, robotSensorRange;
    public string robotBehavior;
    public float humanDefaultSpeed;
    public int humanControllerType;
    public float humanInteractionRadius;
    public float timeScale;
    public string rosPrefix;
    public bool rosEnable;
    public string scenarioId; // nom du fichier YAML

    // View
    public string cameraMode;
    public bool orthographic;
    public float fieldOfView;
    public float cameraDistance, cameraHeight, cameraRotation;
    public bool showGrid, showAxes, showFloor, showWalls, wireframeMode;
    public int renderQuality;
    // ... tous les autres paramètres de view, debug, ros
}