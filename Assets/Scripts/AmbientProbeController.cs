using UnityEngine;
using UnityEngine.Rendering;

public class AmbientProbeController : MonoBehaviour
{
    [Header("Contrôle de l'Ambiance")]
    [Tooltip("Couleur ambiante (Noir pour une pièce fermée sans fenêtre)")]
    public Color ambientColor = Color.black; // Noir par défaut

    [Tooltip("Intensité de la lumière ambiante (1 = normal, 0 = éteint)")]
    [Range(0f, 1f)]
    public float ambientIntensity = 1f;

    [Header("Réglages de Secours")]
    [Tooltip("Si activé, force le mode 'Flat' pour désactiver l'influence du ciel")]
    public bool forceFlatMode = true;

    private void Awake()
    {
        // Applique les réglages dès que l'objet est instancié (même à l'exécution)
        ApplyAmbientSettings();
    }

    /// <summary>
    /// Applique les paramètres d'ambiance à la scène.
    /// </summary>
    public void ApplyAmbientSettings()
    {
        // 1. Passer en mode "Flat" (couleur unie) pour ignorer le ciel HDRI
        if (forceFlatMode)
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
        }

        // 2. Appliquer la couleur choisie (multipliée par l'intensité)
        Color finalColor = ambientColor * ambientIntensity;
        
        // Ces 3 paramètres contrôlent les 3 axes de la sonde ambiante
        RenderSettings.ambientSkyColor = finalColor;
        RenderSettings.ambientEquatorColor = finalColor;
        RenderSettings.ambientGroundColor = finalColor;

        Debug.Log($"[AmbientProbe] Ambiance mise à jour : Couleur = {finalColor}, Intensité = {ambientIntensity}");
    }

    /// <summary>
    /// Méthode publique pour changer la couleur à la volée (ex: allumer une lumière de secours)
    /// </summary>
    public void SetAmbientColor(Color newColor, float newIntensity = 1f)
    {
        ambientColor = newColor;
        ambientIntensity = newIntensity;
        ApplyAmbientSettings();
    }

    /// <summary>
    /// Méthode pour basculer entre jour/nuit (test rapide)
    /// </summary>
    public void ToggleDayNight()
    {
        if (ambientColor == Color.black)
        {
            SetAmbientColor(new Color(0.3f, 0.3f, 0.4f), 0.5f); // Gris bleuté (clair)
        }
        else
        {
            SetAmbientColor(Color.black, 1f); // Noir
        }
    }

    // --- Pour les tests : Appuyez sur la touche "Espace" pour basculer ---
    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            ToggleDayNight();
        }
    }
}