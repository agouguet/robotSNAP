using System.Collections.Generic;
using RobotSNAP.Agents;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds one body picture per robot type, as a prefab under <c>Assets/Resources/RobotBodies</c>.
///
/// A robot of a scenario drives the physical base the project was proven with - the articulation, the wheels,
/// the sensors and the streams it publishes - and only its picture is swapped for the one of its type. The
/// bodies are therefore visual only: they carry no collider, no articulation and no script, and a type that
/// has none keeps the base it was built from.
///
/// They are authored here from primitives so the project owns them outright and they can be re-generated at
/// any time. Dropping a real model in is a matter of replacing the prefab: keep its geometry above y = 0,
/// because that plane is the ground the body is stood on, and the runtime will do the rest.
/// </summary>
public static class RobotBodyPrefabs
{
    private const string BodyFolder = "Assets/Resources/RobotBodies";
    private const string MaterialFolder = "Assets/Resources/RobotBodies/Materials";
    private const string TemplateMaterialPath = "Assets/Prefabs/Robots/URDF/Fetch/Materials/FreightHDRP.mat";

    private static Material _dark;
    private static Material _sensor;
    /// <summary>Type whose body is being built, so its shell takes the colour the profile gives it.</summary>
    private static string _currentType;

    [MenuItem("RobotSNAP/Build the robot bodies")]
    public static void BuildAll()
    {
        EnsureFolders();
        CreateMaterials();

        Build("turtlebot4", BuildTurtlebot4);
        Build("jackal", BuildJackal);
        Build("husky", BuildHusky);
        Build("pioneer_p3dx", BuildPioneer);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[RobotBodyPrefabs] Wrote {BodyFolder}: one body per type that is not the default base.");
    }

    // ==========================================
    //          THE BODIES
    // ==========================================

    /// <summary>Small, round and low: a puck with a short mast, the shape people walk around without noticing.</summary>
    private static GameObject BuildTurtlebot4()
    {
        var root = new GameObject("turtlebot4");
        Cylinder(root, "base", new Vector3(0f, 0.085f, 0f), 0.34f, 0.07f, Shell());
        Cylinder(root, "skirt", new Vector3(0f, 0.035f, 0f), 0.30f, 0.02f, _dark);
        Cylinder(root, "top", new Vector3(0f, 0.155f, 0f), 0.24f, 0.01f, _dark);
        Cylinder(root, "mast", new Vector3(0f, 0.19f, 0f), 0.05f, 0.09f, _dark);
        Cylinder(root, "lidar", new Vector3(0f, 0.26f, 0f), 0.075f, 0.055f, _sensor);
        Wheel(root, "wheel_left", new Vector3(-0.13f, 0.035f, 0f), 0.07f, 0.045f, _dark);
        Wheel(root, "wheel_right", new Vector3(0.13f, 0.035f, 0f), 0.07f, 0.045f, _dark);
        return root;
    }

    /// <summary>Small and square, four wheels inside the body and a lidar on a short mast.</summary>
    private static GameObject BuildJackal()
    {
        var root = new GameObject("jackal");
        Box(root, "shell", new Vector3(0f, 0.20f, 0f), new Vector3(0.46f, 0.13f, 0.40f), Shell());
        Box(root, "deck", new Vector3(0f, 0.275f, 0f), new Vector3(0.30f, 0.02f, 0.26f), _dark);
        Cylinder(root, "mast", new Vector3(0f, 0.33f, 0f), 0.04f, 0.09f, _dark);
        Cylinder(root, "lidar", new Vector3(0f, 0.39f, 0f), 0.08f, 0.06f, _sensor);
        foreach (float x in new[] { -0.20f, 0.20f })
        {
            foreach (float z in new[] { -0.14f, 0.14f })
                Wheel(root, $"wheel_{(x < 0f ? "l" : "r")}{(z < 0f ? "b" : "f")}", new Vector3(x, 0.10f, z), 0.20f, 0.09f, _dark);
        }

        return root;
    }

    /// <summary>Large, long and heavy: a low slab on four wide wheels, with the sensor pushed up a mast.</summary>
    private static GameObject BuildHusky()
    {
        var root = new GameObject("husky");
        Box(root, "shell", new Vector3(0f, 0.315f, 0f), new Vector3(0.94f, 0.20f, 0.64f), Shell());
        Box(root, "deck", new Vector3(0f, 0.425f, 0f), new Vector3(0.66f, 0.02f, 0.44f), _dark);
        Box(root, "mast", new Vector3(0f, 0.63f, 0f), new Vector3(0.06f, 0.40f, 0.06f), _dark);
        Cylinder(root, "lidar", new Vector3(0f, 0.86f, 0f), 0.10f, 0.07f, _sensor);
        foreach (float x in new[] { -0.38f, 0.38f })
        {
            foreach (float z in new[] { -0.24f, 0.24f })
                Wheel(root, $"wheel_{(x < 0f ? "l" : "r")}{(z < 0f ? "b" : "f")}", new Vector3(x, 0.165f, z), 0.33f, 0.10f, _dark);
        }

        return root;
    }

    /// <summary>
    /// A round base of the old school: two wheels, a rear caster and a ring of sonar, which is why its
    /// profile sees so little of the world.
    /// </summary>
    private static GameObject BuildPioneer()
    {
        var root = new GameObject("pioneer_p3dx");
        Cylinder(root, "deck_low", new Vector3(0f, 0.10f, 0f), 0.42f, 0.08f, _dark);
        Cylinder(root, "deck_high", new Vector3(0f, 0.19f, 0f), 0.40f, 0.10f, Shell());
        Cylinder(root, "top", new Vector3(0f, 0.245f, 0f), 0.34f, 0.01f, _dark);
        Cylinder(root, "mast", new Vector3(0f, 0.30f, 0f), 0.04f, 0.07f, _dark);
        Cylinder(root, "lidar", new Vector3(0f, 0.355f, 0f), 0.085f, 0.055f, _sensor);
        Wheel(root, "wheel_left", new Vector3(-0.20f, 0.10f, 0.02f), 0.20f, 0.09f, _dark);
        Wheel(root, "wheel_right", new Vector3(0.20f, 0.10f, 0.02f), 0.20f, 0.09f, _dark);
        Sphere(root, "caster", new Vector3(0f, 0.04f, -0.19f), 0.08f, _dark);
        for (int index = 0; index < 8; index++)
        {
            float angle = -50f + index * (100f / 7f);
            var direction = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
            Cylinder(root, $"sonar_{index}", new Vector3(direction.x * 0.185f, 0.19f, direction.z * 0.185f),
                0.035f, 0.045f, _sensor, direction);
        }

        return root;
    }

    // ==========================================
    //          PRIMITIVES
    // ==========================================

    private static void Build(string typeId, System.Func<GameObject> factory)
    {
        _currentType = typeId;
        GameObject root = factory();
        PrefabUtility.SaveAsPrefabAsset(root, $"{BodyFolder}/{typeId}.prefab");
        Object.DestroyImmediate(root);
    }

    private static void Box(GameObject parent, string name, Vector3 centre, Vector3 size, Material material)
    {
        GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
        part.name = name;
        Finish(part, parent, centre, size, material);
    }

    private static void Cylinder(
        GameObject parent, string name, Vector3 centre, float diameter, float height, Material material,
        Vector3? forward = null)
    {
        GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        part.name = name;
        // Unity's cylinder is two units tall and lies along its own up axis.
        part.transform.localScale = new Vector3(diameter, height * 0.5f, diameter);
        // A cylinder that stands up keeps the rotation it was created with: asking for a look rotation whose
        // forward and up are the same vector is degenerate, and Unity answers it with a tilted body whose
        // rim ends up in the floor.
        part.transform.localRotation = forward.HasValue
            ? Quaternion.LookRotation(forward.Value, Vector3.up)
            : Quaternion.identity;
        Finish(part, parent, centre, Vector3.one, material);
    }

    /// <summary>A wheel stands on its rim: the cylinder is laid on its side, along the axle of the robot.</summary>
    private static void Wheel(GameObject parent, string name, Vector3 centre, float diameter, float width, Material material)
    {
        GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        part.name = name;
        part.transform.localScale = new Vector3(diameter, width * 0.5f, diameter);
        part.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        Finish(part, parent, centre, Vector3.one, material);
    }

    private static void Sphere(GameObject parent, string name, Vector3 centre, float diameter, Material material)
    {
        GameObject part = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        part.name = name;
        Finish(part, parent, centre, Vector3.one * diameter, material);
    }

    private static void Finish(GameObject part, GameObject parent, Vector3 centre, Vector3 scale, Material material)
    {
        part.transform.SetParent(parent.transform, false);
        part.transform.localPosition = centre;
        if (scale != Vector3.one)
            part.transform.localScale = Vector3.Scale(part.transform.localScale, scale);

        var renderer = part.GetComponent<MeshRenderer>();
        if (renderer != null)
            renderer.sharedMaterial = material;

        // The body is a picture. Only the base the robot actually drives keeps colliders, so a type cannot
        // push the crowd, the walls or the planner around with geometry the physics never agreed to.
        Collider collider = part.GetComponent<Collider>();
        if (collider != null)
            Object.DestroyImmediate(collider);
    }

    // ==========================================
    //          MATERIALS
    // ==========================================

    private static void EnsureFolders()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder(BodyFolder))
            AssetDatabase.CreateFolder("Assets/Resources", "RobotBodies");
        if (!AssetDatabase.IsValidFolder(MaterialFolder))
            AssetDatabase.CreateFolder(BodyFolder, "Materials");
    }

    /// <summary>
    /// The three surfaces a body is made of. They are variants of the material the robot of the project
    /// already uses, so a body built here renders in the render pipeline of the project without anyone having
    /// to remember which shader that is.
    /// </summary>
    private static Material _template;

    private static void CreateMaterials()
    {
        _template = AssetDatabase.LoadAssetAtPath<Material>(TemplateMaterialPath);
        if (_template == null)
            Debug.LogWarning($"[RobotBodyPrefabs] No template material at {TemplateMaterialPath}; " +
                             "the bodies reuse the material of the prefab they come from.");

        _dark = CreateMaterial("Trim", new Color(0.16f, 0.17f, 0.19f));
        _sensor = CreateMaterial("Sensor", new Color(0.10f, 0.11f, 0.14f));
    }

    /// <summary>
    /// The shell of the body being built, coloured the way the profile of that type says. A person reads the
    /// colour before they read the shape, so two robots of the same scenario are told apart at a glance even
    /// from above.
    /// </summary>
    private static Material Shell()
    {
        Color colour = RobotProfiles.Find(_currentType).BodyColor;
        if (colour.a <= 0.01f)
            colour = new Color(0.82f, 0.84f, 0.87f);
        colour.a = 1f;

        return CreateMaterial($"Shell_{_currentType}", colour);
    }

    private static Material CreateMaterial(string name, Color colour)
    {
        string path = $"{MaterialFolder}/{name}.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = _template != null ? new Material(_template) : new Material(Shader.Find("HDRP/Lit"));
            AssetDatabase.CreateAsset(material, path);
        }

        // Written under both names because a body may end up on either pipeline: the project renders through
        // HDRP, and a test or a player without it finds the built-in property.
        material.SetColor("_BaseColor", colour);
        material.SetColor("_Color", colour);
        material.color = colour;
        EditorUtility.SetDirty(material);
        return material;
    }
}
