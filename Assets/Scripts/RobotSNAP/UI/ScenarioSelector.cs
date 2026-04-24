using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RobotSNAP.Core.Scenario;

namespace RobotSNAP.UI
{
    public class ScenarioSelector : MonoBehaviour
    {
        [Header("UI References")]
        public Transform contentContainer;          // Le parent qui contiendra les boutons des scénarios
        public GameObject buttonPrefab;             // Préfabriqué de bouton (avec TextMeshProUGUI)
        public TMP_InputField searchInput;          // Champ de recherche (optionnel)
        public Button closeButton;                  // Bouton pour fermer le panneau (si nécessaire)
        public TextMeshProUGUI titleText;           // Titre affichant le nombre de scénarios
        public TextMeshProUGUI currentScenarioText; // Affiche le scénario actuellement sélectionné

        [Header("Settings")]
        public int maxButtonsBeforeScroll = 10;     // Nombre max avant d'activer le scroll
        public Color selectedColor = new Color(0.3f, 0.7f, 0.3f); // Couleur du bouton sélectionné

        private ScenarioManager _scenarioSupervisor;
        private List<string> _allScenarioNames = new List<string>();
        private List<string> _filteredNames = new List<string>();
        private List<GameObject> _spawnedButtons = new List<GameObject>();
        private string _currentSelectedScenario;

        #region Unity Lifecycle

        private void Start()
        {
            FindReferences();
            SetupCallbacks();
            RefreshScenarioList();
        }

        private void OnDestroy()
        {
            if (_scenarioSupervisor != null)
                _scenarioSupervisor.OnScenarioLoaded -= OnScenarioLoaded;

            if (closeButton != null)
                closeButton.onClick.RemoveListener(ClosePanel);

            if (searchInput != null)
                searchInput.onValueChanged.RemoveListener(OnSearchChanged);
        }

        #endregion

        #region Initialization

        private void FindReferences()
        {
            _scenarioSupervisor = FindObjectOfType<ScenarioManager>();
            if (_scenarioSupervisor != null)
                _scenarioSupervisor.OnScenarioLoaded += OnScenarioLoaded;
        }

        private void SetupCallbacks()
        {
            if (closeButton != null)
                closeButton.onClick.AddListener(ClosePanel);

            if (searchInput != null)
                searchInput.onValueChanged.AddListener(OnSearchChanged);
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Rafraîchit la liste des scénarios depuis le ScenarioManager.
        /// </summary>
        public void RefreshScenarioList()
        {
            if (_scenarioSupervisor == null) return;

            _allScenarioNames = _scenarioSupervisor.GetAvailableScenarios();
            Debug.Log($"[ScenarioSelector] Scenarios found: {_allScenarioNames.Count}");
            UpdateCurrentScenarioDisplay();
            RefreshButtonList();
        }

        /// <summary>
        /// Ouvre le panneau (si le GameObject parent est désactivé, on l'active).
        /// </summary>
        public void OpenPanel()
        {
            gameObject.SetActive(true);
            RefreshScenarioList();
            if (searchInput != null)
            {
                searchInput.text = "";
                searchInput.Select();
                searchInput.ActivateInputField();
            }
        }

        /// <summary>
        /// Ferme le panneau.
        /// </summary>
        public void ClosePanel()
        {
            gameObject.SetActive(false);
        }

        /// <summary>
        /// Alterne l'état du panneau.
        /// </summary>
        public void TogglePanel()
        {
            if (gameObject.activeSelf)
                ClosePanel();
            else
                OpenPanel();
        }

        #endregion

        #region UI Management

        private void RefreshButtonList(string searchFilter = "")
        {
            // Nettoyer les anciens boutons
            foreach (var btn in _spawnedButtons)
                if (btn != null) Destroy(btn);
            _spawnedButtons.Clear();

            if (contentContainer == null || buttonPrefab == null) return;

            // Filtrer les noms
            _filteredNames = FilterNames(searchFilter);

            // Créer les boutons
            foreach (string name in _filteredNames)
                CreateScenarioButton(name);

            // Mettre à jour le titre
            if (titleText != null)
                titleText.text = $"Scénarios ({_filteredNames.Count})";

            // Ajuster la hauteur du conteneur
            AdjustContainerHeight(_filteredNames.Count);
        }

        private List<string> FilterNames(string filter)
        {
            if (string.IsNullOrEmpty(filter))
                return new List<string>(_allScenarioNames);

            string lowerFilter = filter.ToLower();
            return _allScenarioNames.Where(name => name.ToLower().Contains(lowerFilter)).ToList();
        }

        private void CreateScenarioButton(string scenarioName)
        {
            GameObject buttonObj = Instantiate(buttonPrefab, contentContainer);
            _spawnedButtons.Add(buttonObj);

            // Texte du bouton
            TextMeshProUGUI buttonText = buttonObj.GetComponentInChildren<TextMeshProUGUI>();
            if (buttonText != null)
                buttonText.text = scenarioName;

            // Couleur de sélection si c'est le scénario courant
            bool isCurrent = (scenarioName == _currentSelectedScenario);
            Button button = buttonObj.GetComponent<Button>();
            if (button != null)
            {
                button.onClick.AddListener(() => OnScenarioSelected(scenarioName));
                if (isCurrent)
                {
                    ColorBlock colors = button.colors;
                    colors.normalColor = selectedColor;
                    button.colors = colors;
                }
            }
        }

        private void AdjustContainerHeight(int buttonCount)
        {
            if (contentContainer == null) return;
            RectTransform rect = contentContainer.GetComponent<RectTransform>();
            if (rect != null && buttonPrefab != null)
            {
                RectTransform prefabRect = buttonPrefab.GetComponent<RectTransform>();
                if (prefabRect != null)
                {
                    float buttonHeight = prefabRect.rect.height;
                    float spacing = contentContainer.GetComponent<VerticalLayoutGroup>()?.spacing ?? 5f;
                    float totalHeight = buttonCount * (buttonHeight + spacing);
                    totalHeight = Mathf.Min(totalHeight, 400f);
                    rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, totalHeight);
                }
            }
        }

        private void UpdateCurrentScenarioDisplay()
        {
            if (_scenarioSupervisor != null)
                _currentSelectedScenario = _scenarioSupervisor.CurrentScenarioId;
            else
                _currentSelectedScenario = null;

            if (currentScenarioText != null)
            {
                if (!string.IsNullOrEmpty(_currentSelectedScenario))
                    currentScenarioText.text = $"Actuel: {_currentSelectedScenario}";
                else
                    currentScenarioText.text = "Aucun scénario chargé";
            }
        }

        #endregion

        #region Callbacks

        private void OnScenarioSelected(string scenarioName)
        {
            if (_scenarioSupervisor != null)
            {
                _scenarioSupervisor.LoadScenario(scenarioName);
                // La mise à jour de l'affichage se fera via l'événement OnScenarioLoaded
            }
            // Optionnel : fermer le panneau après sélection
            // ClosePanel();
        }

        private void OnScenarioLoaded(ScenarioData scenario)
        {
            UpdateCurrentScenarioDisplay();
            RefreshButtonList(searchInput?.text ?? "");
        }

        private void OnSearchChanged(string searchText)
        {
            RefreshButtonList(searchText);
        }

        #endregion
    }
}