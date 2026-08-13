using UnityEngine;
using UnityEngine.UIElements;
using RobotSNAP.Core.Scenario;
using System;
using System.Collections.Generic;
using System.Linq;

public class ScenariosTabController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private ScenarioDataService _dataService;
    [SerializeField] private VisualTreeAsset _cardTemplate;
    [SerializeField] private ScenarioLoader _scenarioLoader;

    [Header("Grid View settings")]
    [SerializeField] private int _columns = 4;
    [SerializeField] private float _cardSpacingPercent = 0.2f;

    [Header("Debug")]
    [SerializeField] private bool _logEvents = true;

    // === Composants UI ===
    private ScenarioListView _listView;
    private ScenarioDetailsView _detailsView;
    private Button _newButton;
    private Button _useButton;
    private Button _editButton;
    private Button _cancelEditButton;
    private Button _previousArrowButton;
    private Button _saveEditButton;

    // === Éléments du formulaire d'édition ===
    private Label _editTitleLabel;
    private TextField _editNameField;
    private TextField _editTypeField;
    private TextField _editLocationField;
    private TextField _editDescriptionField;
    private TextField _editTagsField;
    private TextField _editRobotTypeField;
    private FloatField _editDurationField;
    private DropdownField _editMapDropdown;
    private DropdownField _editPreviewDropdown;

    // === Conteneurs ===
    private VisualElement _listContainer;
    private VisualElement _editorContainer;
    private VisualElement _editorPanel;

    // === État ===
    private bool _isInitialized = false;
    private bool _isEditing = false;
    private ScenarioInfo _editingInfo = null;

    // ==========================================
    //          CYCLE DE VIE
    // ==========================================

    private void Awake()
    {
        if (_dataService == null)
            _dataService = FindObjectOfType<ScenarioDataService>();
        if (_scenarioLoader == null)
            _scenarioLoader = FindObjectOfType<ScenarioLoader>();

        MainViewController.OnViewLoaded += OnViewLoaded;
    }

    private void OnDestroy()
    {
        MainViewController.OnViewLoaded -= OnViewLoaded;
        _listView?.Dispose();
        if (_newButton != null) _newButton.clicked -= OnNewClicked;
        if (_editButton != null) _editButton.clicked -= OnEditClicked;
        if (_useButton != null) _useButton.clicked -= OnUseClicked;
        if (_cancelEditButton != null) _cancelEditButton.clicked -= OnEditCancel;
        if (_saveEditButton != null) _saveEditButton.clicked -= OnEditSave;
    }

    private void OnViewLoaded(string viewName)
    {
        if (viewName == "Scenarios" && !_isInitialized)
        {
            InitializeUI();
            _dataService?.EnsureLoaded();
        }
    }

    private void OnEnable()
    {
        if (_isInitialized && !_isEditing)
        {
            _listView?.Refresh();
        }
    }

    // ==========================================
    //          INITIALISATION DE L'UI
    // ==========================================

    private void InitializeUI()
    {
        if (_isInitialized) return;
        if (uiDocument == null) uiDocument = GetComponent<UIDocument>();
        var root = uiDocument.rootVisualElement;

        // --- Récupérer les conteneurs ---
        _listContainer = root.Q<VisualElement>("ScenarioListView");
        _editorContainer = root.Q<VisualElement>("ScenarioEditorView");

        // Si les conteneurs n'existent pas, on les crée ? (fallback)
        _listContainer = root.Q<VisualElement>("ScenarioContent");
        if (_listContainer == null)
        {
            // Fallback : essayer de trouver un parent commun
            var browser = root.Q<VisualElement>("ScenarioBrowser");
            var details = root.Q<VisualElement>("ScenarioDetails");
            if (browser != null && details != null && browser.parent == details.parent)
                _listContainer = browser.parent;
        }

        // --- Construire la vue liste ---
        BuildListView(root);

        // --- Construire la vue éditeur ---
        BuildEditorView(root);

        // --- Bouton Nouveau ---
        _newButton = root.Q<Button>("NewScenarioButton");
        if (_newButton != null)
            _newButton.clicked += OnNewClicked;

        // --- État initial ---
        ShowListView();
        _isInitialized = true;
        Log("[ScenariosTabController] UI initialized");
    }

    private void BuildListView(VisualElement root)
    {
        _listView = new ScenarioListView(_cardTemplate, _columns, _cardSpacingPercent);
        _listView.Initialize(_dataService);
        _listView.OnScenarioSelected += OnScenarioSelected;

       

        // Récupérer les conteneurs existants
        var browserContainer = root.Q<VisualElement>("ScenarioBrowser");
        if (browserContainer != null)
        {
            browserContainer.Clear();
            browserContainer.Add(_listView);
        }
        else
        {
            // Fallback : ajouter directement
            root.Q<VisualElement>("ScenarioContent")?.Insert(0, _listView);
        }


        _detailsView = new ScenarioDetailsView(_dataService);

        var detailsContainer = root.Q<VisualElement>("ScenarioDetailsContainer");
        if (detailsContainer != null)
        {
            detailsContainer.Clear();
            detailsContainer.Add(_detailsView);

            var actions = new VisualElement();
            actions.AddToClassList("scenario-details-actions");
            _useButton = new Button(OnUseClicked) { text = "Utiliser le scénario" };
            _useButton.AddToClassList("action-button");
            _useButton.AddToClassList("primary");
            _editButton = new Button(OnEditClicked) { text = "Modifier" };
            _editButton.AddToClassList("action-button");
            _editButton.AddToClassList("secondary");
            actions.Add(_editButton);
            actions.Add(_useButton);
            detailsContainer.Add(actions);
        }

        _isInitialized = true;
        Log("[ScenariosTabController] UI initialized");
    }

    private void BuildEditorView(VisualElement root)
    {
        // Récupérer l'éditeur depuis l'UXML (si présent)
        if (_editorContainer == null)
        {
            // Fallback : créer le formulaire depuis zéro (ou ignorer)
            LogWarning("[ScenariosTabController] EditorContainer not found, editor will not work.");
            return;
        }

        // Récupérer les éléments du formulaire
        _editTitleLabel = _editorContainer.Q<Label>("EditorTitle");
        _editNameField = _editorContainer.Q<TextField>("NameField");
        _editTypeField = _editorContainer.Q<TextField>("TypeField");
        _editLocationField = _editorContainer.Q<TextField>("LocationField");
        _editDescriptionField = _editorContainer.Q<TextField>("DescriptionField");
        _editTagsField = _editorContainer.Q<TextField>("TagsField");
        _editRobotTypeField = _editorContainer.Q<TextField>("RobotTypeField");
        _editDurationField = _editorContainer.Q<FloatField>("DurationField");
        _editMapDropdown = _editorContainer.Q<DropdownField>("MapDropdown");
        _editPreviewDropdown = _editorContainer.Q<DropdownField>("PreviewDropdown");
        _cancelEditButton = _editorContainer.Q<Button>("CancelEditorButton");
        _previousArrowButton = _editorContainer.Q<Button>("BackToScenariosButton");
        _saveEditButton = _editorContainer.Q<Button>("SaveEditorButton");

        // Remplir les dropdowns
        RefreshMapOptions();
        RefreshPreviewOptions();

        // Abonnements
        if (_cancelEditButton != null)
            _cancelEditButton.clicked += OnEditCancel;
        if (_saveEditButton != null)
            _saveEditButton.clicked += OnEditSave;
        if (_previousArrowButton != null)
            _previousArrowButton.clicked += OnEditCancel;

        // Cacher l'éditeur par défaut
        _editorContainer.style.display = DisplayStyle.None;
    }

    // ==========================================
    //          BASCOULEMENT ENTRE VUES
    // ==========================================

    private void ShowListView()
    {
        _isEditing = false;
        if (_listContainer != null)
            _listContainer.style.display = DisplayStyle.Flex;
        if (_editorContainer != null)
            _editorContainer.style.display = DisplayStyle.None;
        // Mettre à jour le titre de la page
        var title = uiDocument.rootVisualElement.Q<Label>("ScenarioPageTitle");
        if (title != null) title.text = "Scénarios";
        var subtitle = uiDocument.rootVisualElement.Q<Label>("ScenarioPageSubtitle");
        if (subtitle != null) subtitle.text = "Gérez et configurez vos scénarios de simulation.";
        // Réactiver le bouton "Nouveau"
        if (_newButton != null) _newButton.text = "+  Nouveau scénario";
        _listView?.Refresh();
    }

    private void ShowEditor(ScenarioInfo info = null)
    {
        _isEditing = true;
        _editingInfo = info;

        if (_listContainer != null)
            _listContainer.style.display = DisplayStyle.None;
        if (_editorContainer != null)
            _editorContainer.style.display = DisplayStyle.Flex;

        // Mettre à jour le titre de la page
        var title = uiDocument.rootVisualElement.Q<Label>("ScenarioPageTitle");
        if (title != null) title.text = info == null ? "Nouveau scénario" : "Modifier le scénario";
        var subtitle = uiDocument.rootVisualElement.Q<Label>("ScenarioPageSubtitle");
        if (subtitle != null) subtitle.text = info == null ? "Remplissez les informations du nouveau scénario." : "Modifiez les informations du scénario.";

        // Désactiver le bouton "Nouveau" (ou changer son texte)
        if (_newButton != null) _newButton.text = "← Retour";

        // Remplir le formulaire
        FillEditorForm(info);
    }

    // ==========================================
    //          ÉDITEUR : FORMULAIRE
    // ==========================================

    private void FillEditorForm(ScenarioInfo info)
    {
        if (info == null)
        {
            _editTitleLabel.text = "Nouveau scénario";
            _editNameField.value = "";
            _editTypeField.value = "";
            _editLocationField.value = "";
            _editDescriptionField.value = "";
            _editTagsField.value = "";
            _editRobotTypeField.value = "TurtleBot4";
            _editDurationField.value = 0f;
            _editMapDropdown.value = "Aucune";
            _editPreviewDropdown.value = "Aucune";
        }
        else
        {
            _editTitleLabel.text = $"Modifier : {info.Name}";
            _editNameField.value = info.Name ?? "";
            _editTypeField.value = info.Type ?? "";
            _editLocationField.value = info.Location ?? "";
            _editDescriptionField.value = info.Description ?? "";
            _editTagsField.value = info.Tags != null ? string.Join(", ", info.Tags) : "";
            _editRobotTypeField.value = info.RobotType ?? "TurtleBot4";
            _editDurationField.value = info.Duration;

            // Map
            string mapName = info.MapImage ?? "";
            if (!string.IsNullOrEmpty(mapName) && _editMapDropdown.choices.Contains(mapName))
                _editMapDropdown.value = mapName;
            else
                _editMapDropdown.value = "Aucune";

            // Preview
            string previewName = info.PreviewImage ?? "";
            if (!string.IsNullOrEmpty(previewName) && _editPreviewDropdown.choices.Contains(previewName))
                _editPreviewDropdown.value = previewName;
            else
                _editPreviewDropdown.value = "Aucune";
        }
    }

    private void RefreshMapOptions()
    {
        if (_editMapDropdown == null || _scenarioLoader == null) return;
        var maps = _scenarioLoader.GetAvailableMaps();
        var choices = new List<string> { "Aucune" };
        choices.AddRange(maps);
        _editMapDropdown.choices = choices;
        _editMapDropdown.value = "Aucune";
    }

    private void RefreshPreviewOptions()
    {
        if (_editPreviewDropdown == null) return;
        var previews = Resources.LoadAll<Texture2D>("ScenarioPreviews");
        var choices = new List<string> { "Aucune" };
        foreach (var tex in previews)
        {
            choices.Add(tex.name);
        }
        _editPreviewDropdown.choices = choices;
        _editPreviewDropdown.value = "Aucune";
    }

    // ==========================================
    //          ÉDITEUR : SAUVEGARDE / ANNULATION
    // ==========================================

    private void OnEditSave()
    {
        if (string.IsNullOrWhiteSpace(_editNameField.value))
        {
            Debug.LogWarning("[ScenariosTabController] Le nom est obligatoire.");
            return;
        }

        var info = new ScenarioInfo
        {
            Name = _editNameField.value.Trim(),
            Type = _editTypeField.value.Trim(),
            Location = _editLocationField.value.Trim(),
            Description = _editDescriptionField.value.Trim(),
            Tags = _editTagsField.value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(t => t.Trim()).ToArray(),
            RobotType = _editRobotTypeField.value.Trim(),
            Duration = _editDurationField.value,
            MapImage = _editMapDropdown.value != "Aucune" ? _editMapDropdown.value : "",
            PreviewImage = _editPreviewDropdown.value != "Aucune" ? _editPreviewDropdown.value : ""
        };

        // Créer un ScenarioData complet (minimal)
        var scenarioData = new ScenarioData
        {
            Info = info,
            Points = new Dictionary<string, RefPoint>(),
            Robot = new RobotScenarioConfig { StartRef = "start", GoalRef = "goal", Speed = 1.2f, Behavior = "normal" },
            Humans = new List<HumanScenarioConfig>()
        };

        if (_scenarioLoader != null)
        {
            string fileName = info.Name + ".yaml";
            bool success = _scenarioLoader.ExportScenario(scenarioData, fileName);
            if (success)
            {
                Debug.Log($"[ScenariosTabController] Scenario '{info.Name}' saved.");
                // Recharger les données et revenir à la liste
                _dataService?.EnsureLoaded();
                ShowListView();
                // Optionnel : sélectionner le nouveau scénario ?
            }
            else
            {
                Debug.LogError("[ScenariosTabController] Failed to save scenario.");
            }
        }
        else
        {
            Debug.LogError("[ScenariosTabController] ScenarioLoader not available.");
        }
    }

    private void OnEditCancel()
    {
        ShowListView();
    }

    // ==========================================
    //          ÉVÉNEMENTS DE LA LISTE
    // ==========================================

    private void OnScenarioSelected(string scenarioId)
    {
        var info = _dataService?.GetScenarioInfo(scenarioId);
        _detailsView?.ShowScenario(info);
        Log($"[ScenariosTabController] Scenario selected: {scenarioId}");
    }

    // ==========================================
    //          BOUTONS
    // ==========================================

    private void OnNewClicked()
    {
        if (_isEditing)
        {
            // Si on est en mode édition, "Retour" -> annuler
            OnEditCancel();
        }
        else
        {
            // Sinon, ouvrir le formulaire pour un nouveau scénario
            ShowEditor(null);
        }
    }

    private void OnEditClicked()
    {
        if (_listView?.SelectedScenarioInfo == null)
        {
            LogWarning("[ScenariosTabController] No scenario selected to edit.");
            return;
        }
        ShowEditor(_listView.SelectedScenarioInfo);
    }

    private void OnUseClicked()
    {
        if (_listView?.SelectedScenarioInfo == null) return;
        _dataService?.LoadScenario(_listView.SelectedScenarioId);
        Log($"[ScenariosTabController] Using scenario: {_listView.SelectedScenarioId}");
    }

    // ==========================================
    //          LOGS
    // ==========================================

    private void Log(string msg)
    {
        if (_logEvents) Debug.Log(msg);
    }

    private void LogWarning(string msg)
    {
        if (_logEvents) Debug.LogWarning(msg);
    }

    private void LogError(string msg)
    {
        if (_logEvents) Debug.LogError(msg);
    }
}