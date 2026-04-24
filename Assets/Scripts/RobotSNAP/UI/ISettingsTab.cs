// Scripts/RobotSNAP/UI/ISettingsTab.cs
namespace RobotSNAP.UI
{
    public interface ISettingsTab
    {
        void ApplySettings();                // applique à la simulation (déjà existant)
        void RefreshUI();                    // recharge depuis la config (déjà existant)
        object GetSettings();                // retourne un objet sérialisable (Dictionnaire, struct, etc.)
        void SetSettings(object settings);   // restaure l’UI à partir de settings
    }
}