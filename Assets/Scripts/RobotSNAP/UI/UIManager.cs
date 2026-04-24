// Scripts/RobotSNAP/UI/UIManager.cs
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.IO;
using RobotSNAP.Core;
using RobotSNAP.Core.Scenario;
using VYaml.Serialization; // pour sérialiser en YAML

namespace RobotSNAP.UI
{
    public class UIManager : MonoBehaviour
    {
        [Header("Main Panel")]
        public GameObject settingsPanel;
        public Button toggleButton;

        [Header("Tabs")]
        public List<Button> tabButtons;
        public List<GameObject> tabPanels;

        [Header("Global Actions")]
        public Button applyButton;
        public Button resetAllButton;
        public Button saveScenarioButton;   // Nouveau : sauvegarder tous les paramètres en YAML
        public Button resetScenarioButton;  // Nouveau : restaurer les paramètres du scénario courant

        private int _currentTabIndex = -1;
        private bool _isPanelOpen = false;
        private ScenarioManager _scenarioSupervisor;
        private Supervisor _supervisor;

        private void Start()
        {
            _supervisor = Supervisor.Instance;
            _scenarioSupervisor = FindObjectOfType<ScenarioManager>();

            if (settingsPanel != null)
                settingsPanel.SetActive(false);

            if (toggleButton != null)
                toggleButton.onClick.AddListener(TogglePanel);

            for (int i = 0; i < tabButtons.Count; i++)
            {
                int index = i;
                tabButtons[i].onClick.AddListener(() => ShowTab(index));
            }

            if (applyButton != null)
                applyButton.onClick.AddListener(ApplyAllTabs);

            if (resetAllButton != null)
                resetAllButton.onClick.AddListener(ResetAllEnvironments);

            if (saveScenarioButton != null)
                saveScenarioButton.onClick.AddListener(SaveFullScenario);

            if (resetScenarioButton != null)
                resetScenarioButton.onClick.AddListener(ResetToCurrentScenario);

            if (tabPanels.Count > 0)
                ShowTab(0);
        }

        private void TogglePanel()
        {
            _isPanelOpen = !_isPanelOpen;
            if (settingsPanel != null)
                settingsPanel.SetActive(_isPanelOpen);
        }

        private void ShowTab(int index)
        {
            if (index < 0 || index >= tabPanels.Count) return;
            foreach (var panel in tabPanels)
                if (panel != null) panel.SetActive(false);
            tabPanels[index].SetActive(true);
            _currentTabIndex = index;
        }

        private void ApplyAllTabs()
        {
            foreach (var panel in tabPanels)
            {
                if (panel == null) continue;
                var tab = panel.GetComponent<ISettingsTab>();
                tab?.ApplySettings();
            }
            Debug.Log("[UIManager] All settings applied");
        }

        public void RefreshAllTabs()
        {
            foreach (var panel in tabPanels)
            {
                if (panel == null) continue;
                var tab = panel.GetComponent<ISettingsTab>();
                tab?.RefreshUI();
            }
        }

        private void ResetAllEnvironments()
        {
            if (_supervisor != null)
            {
                // _supervisor.RebuildAllEnvironments();
                Debug.Log("[UIManager] Reset all environments (rebuild)");
                RefreshAllTabs();
            }
            else
                Debug.LogWarning("[UIManager] Supervisor not found, cannot reset environments");
        }

        private void SaveFullScenario()
        {
            if (_scenarioSupervisor == null || _scenarioSupervisor.CurrentScenarioData == null)
            {
                Debug.LogWarning("[UIManager] No active scenario to save");
                return;
            }

            var fullSettings = new FullScenarioSettings();
            fullSettings.scenarioId = _scenarioSupervisor.CurrentScenarioId;

            // Récupérer les paramètres de chaque onglet
            foreach (var panel in tabPanels)
            {
                if (panel == null) continue;
                var tab = panel.GetComponent<ISettingsTab>();
                if (tab != null)
                {
                    var settings = tab.GetSettings();
                    if (settings != null)
                        MergeIntoSettings(fullSettings, settings, tab.GetType().Name);
                }
            }

            byte[] yamlBytes = YamlSerializer.Serialize(fullSettings).ToArray(); // ← correction
            string path = GetScenarioFilePath(fullSettings.scenarioId);
            File.WriteAllBytes(path, yamlBytes);
            Debug.Log($"[UIManager] Scenario saved to {path}");

            // Recharger le scénario pour que le ScenarioSupervisor prenne en compte le nouveau YAML
            _scenarioSupervisor.LoadScenario(fullSettings.scenarioId);
        }

        private void ResetToCurrentScenario()
        {
            if (_scenarioSupervisor == null || _scenarioSupervisor.CurrentScenarioData == null)
            {
                Debug.LogWarning("[UIManager] No active scenario to reset to");
                return;
            }

            // Lire le fichier YAML actuel
            string path = GetScenarioFilePath(_scenarioSupervisor.CurrentScenarioId);
            if (!File.Exists(path))
            {
                Debug.LogError($"[UIManager] Scenario file not found: {path}");
                return;
            }

            byte[] yamlBytes = File.ReadAllBytes(path);
            var fullSettings = YamlSerializer.Deserialize<FullScenarioSettings>(yamlBytes);
            if (fullSettings == null)
            {
                Debug.LogError("[UIManager] Failed to deserialize scenario settings");
                return;
            }

            // Restaurer chaque onglet
            foreach (var panel in tabPanels)
            {
                if (panel == null) continue;
                var tab = panel.GetComponent<ISettingsTab>();
                if (tab != null)
                {
                    var part = ExtractPart(fullSettings, tab.GetType().Name);
                    if (part != null)
                        tab.SetSettings(part);
                }
            }

            Debug.Log("[UIManager] UI reset to current scenario");
        }

        private void MergeIntoSettings(FullScenarioSettings full, object part, string tabName)
        {
            // Utiliser la réflexion pour fusionner les propriétés du part dans le full
            // On suppose que part a des propriétés nommées comme dans FullScenarioSettings
            var partType = part.GetType();
            var fullType = full.GetType();
            foreach (var prop in partType.GetProperties())
            {
                var fullProp = fullType.GetProperty(prop.Name);
                if (fullProp != null && fullProp.CanWrite)
                {
                    fullProp.SetValue(full, prop.GetValue(part));
                }
            }
        }

        private object ExtractPart(FullScenarioSettings full, string tabName)
        {
            // Pour simplifier, on pourrait retourner full directement si chaque onglet ne gère qu'une partie
            // Mais idéalement, on extrait les propriétés correspondant à l'onglet.
            // Ici on retourne full, mais chaque onglet devra filtrer.
            return full;
        }

        private string GetScenarioFilePath(string scenarioId)
        {
            string basePath = Path.Combine(Application.streamingAssetsPath, "Scenarios");
            return Path.Combine(basePath, scenarioId + ".yaml");
        }
    }
}