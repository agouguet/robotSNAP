using UnityEngine;
using RobotSNAP.Core;
using System;

[ExecuteAlways]
public class DayNightCycle : MonoBehaviour
{
    [Header("Lumières")]
    public Light sunLight;
    public Light moonLight;

    [Header("Contrôle Éditeur (hors jeu)")]
    [Range(0, 24)]
    public float editorTimeOfDay = 12f;

    [Header("Courbes de Soleil")]
    public Gradient sunColorGradient;
    public AnimationCurve sunIntensityCurve;

    [Header("Courbes de Lune")]
    public Gradient moonColorGradient;
    public AnimationCurve moonIntensityCurve;

    [Header("Réglages Avancés")]
    public Vector3 sunRotationOffset = Vector3.zero;

    private float _previousAppliedHours = -1f;

    void Update()
    {
        float hoursToApply;

        if (Application.isPlaying)
        {
            if (Clock.Instance != null)
            {
                // ⬇️ DÉTERMINER L'HEURE SELON LE MODE DE LA CLOCK
                if (Clock.Instance.UseRealTime)
                {
                    // Mode Real Time : on prend l'heure UTC réelle
                    DateTime now = DateTime.UtcNow;
                    hoursToApply = (float)(now.Hour + now.Minute / 60.0 + now.Second / 3600.0);
                }
                else
                {
                    // Mode Simulation : on utilise le temps simulé (incluant l'offset)
                    double totalSeconds = Clock.Instance.CurrentTimeSeconds;
                    double secondsInDay = totalSeconds % 86400.0;
                    hoursToApply = (float)(secondsInDay / 3600.0);
                }
            }
            else
            {
                // Fallback si Clock n'existe pas
                Debug.LogWarning("Clock introuvable ! Utilisation de Time.time.");
                hoursToApply = (Time.time % 86400f) / 3600f;
            }
        }
        else
        {
            // Mode Édition : utilisation du slider
            hoursToApply = editorTimeOfDay;
        }

        // Application (seulement si l'heure a changé)
        if (!Mathf.Approximately(hoursToApply, _previousAppliedHours))
        {
            ApplyTimeOfDay(hoursToApply);
            _previousAppliedHours = hoursToApply;
        }
    }

    void ApplyTimeOfDay(float hours)
    {
        float sunAngleX = (hours / 24f) * 360f - 90f;

        if (sunLight != null)
        {
            sunLight.transform.rotation = Quaternion.Euler(sunAngleX, 0, 0) * Quaternion.Euler(sunRotationOffset);
            float t = hours / 24f;
            sunLight.color = sunColorGradient.Evaluate(t);
            sunLight.intensity = sunIntensityCurve.Evaluate(t);
        }

        if (moonLight != null)
        {
            float moonAngleX = sunAngleX + 180f;
            moonLight.transform.rotation = Quaternion.Euler(moonAngleX, 0, 0);
            float t = hours / 24f;
            moonLight.color = moonColorGradient.Evaluate(t);
            moonLight.intensity = moonIntensityCurve.Evaluate(t);
        }
    }
}