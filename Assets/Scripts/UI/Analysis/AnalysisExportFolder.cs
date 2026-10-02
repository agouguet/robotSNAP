using RobotSNAP.Metrics;
using UnityEngine;

/// <summary>
/// Where the Analysis tab writes an export.
///
/// The folder belongs to the user, not to the project: an export nobody can find afterwards is an export
/// that did not happen, and StreamingAssets is not a place a reader of a packaged build thinks to look. So
/// the folder is chosen once and remembered, the same way the visualization switches are remembered - a
/// preference, not configuration.
///
/// It never fails. A remembered path whose drive is gone, or a preference a previous version wrote in
/// another shape, falls back to the exporter's own default rather than making an export impossible.
/// </summary>
public static class AnalysisExportFolder
{
    private const string PreferenceKey = "robotsnap.analysis.export_folder";

    /// <summary>The folder the next export writes to; the exporter's default before one was ever chosen.</summary>
    public static string Last
    {
        get
        {
            string stored = PlayerPrefs.GetString(PreferenceKey, string.Empty);
            return string.IsNullOrWhiteSpace(stored) ? MetricsExporter.DefaultRoot : stored;
        }
        set
        {
            PlayerPrefs.SetString(PreferenceKey, value ?? string.Empty);
            PlayerPrefs.Save();
        }
    }

    /// <summary>Forgets the remembered folder, so the next export starts from the default again.</summary>
    public static void Forget()
    {
        PlayerPrefs.DeleteKey(PreferenceKey);
        PlayerPrefs.Save();
    }
}
