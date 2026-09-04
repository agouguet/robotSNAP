using UnityEngine;
using UnityEngine.UIElements;
using RobotSNAP.UI;
using RobotSNAP.Core;
using RobotSNAP.Core.Scenario;

public class SidebarController : MonoBehaviour
{
    [Header("UI Document")]
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private ScenarioDataService _scenarioDataService;

    public System.Action<string> OnViewChanged;
    public System.Action<bool> OnDarkModeToggled;

    private VisualElement _sidebar;
    private Button _collapseButton;
    private VisualElement _collapseContainer;
    private SlideToggle _darkModeToggle;
    private bool _isSidebarCollapsed = false;

    // === Éléments du ScenarioMenuGroup ===
    private Label _scenarioNameLabel;
    private Label _scenarioLocationLabel;
    private Label _scenarioRobotLabel;
    private Image _scenarioThumbnail;
    private VisualElement _scenarioStatusDot;

    private void OnEnable()
    {
        if (uiDocument == null)
            uiDocument = GetComponent<UIDocument>();

        var root = uiDocument.rootVisualElement;

        _sidebar = root.Q<VisualElement>("Sidebar");
        _collapseButton = root.Q<Button>("CollapseButton");
        _collapseContainer = root.Q<VisualElement>("CollapseContainer");
        _darkModeToggle = root.Q<SlideToggle>("DarkModeToggle");


        var scenarioGroup = root.Q<VisualElement>("ScenarioMenuGroup");
        if (scenarioGroup != null)
        {
            _scenarioNameLabel = scenarioGroup.Q<Label>(className: "scenario-name");
            _scenarioLocationLabel = scenarioGroup.Q<Label>(className: "scenario-info-value");
            var infoValues = scenarioGroup.Query<Label>(className: "scenario-info-value").ToList();
            if (infoValues.Count >= 2)
            {
                _scenarioLocationLabel = infoValues[0];
                _scenarioRobotLabel = infoValues[1];
            }
            _scenarioThumbnail = scenarioGroup.Q<Image>(className: "scenario-thumbnail-preview-image");
            _scenarioStatusDot = scenarioGroup.Q<VisualElement>(className: "scenario-dot");
        }

        // Collapse
        if (_collapseButton != null)
            _collapseButton.clicked += ToggleSidebar;
        if (_collapseContainer != null)
            _collapseContainer.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.target == _collapseButton) return;
                ToggleSidebar();
            });

        // Dark Mode
        if (_darkModeToggle != null)
        {
            _darkModeToggle.RegisterValueChangedCallback(evt => ToggleDarkMode(evt.newValue));
            bool isDark = PlayerPrefs.GetInt("DarkMode", 0) == 1;
            _darkModeToggle.SetValueWithoutNotify(isDark);
            ToggleDarkMode(isDark);
        }

        EventBus.Instance.Subscribe<SimulationStateChangedEvent>(OnSimulationStateChanged);

        if (_scenarioDataService == null)
            _scenarioDataService = FindObjectOfType<ScenarioDataService>();

        if (_scenarioDataService != null)
        {
            _scenarioDataService.OnScenarioLoaded += UpdateScenarioInfo;
            UpdateScenarioInfo(_scenarioDataService.LoadedScenarioId);
        }
        else
        {
            Debug.LogWarning("[SidebarController] ScenarioDataService not found.");
        }

        RegisterMenuItems();
    }

    private void OnDisable()
    {
        EventBus.Instance.Unsubscribe<SimulationStateChangedEvent>(OnSimulationStateChanged);
        if (_scenarioDataService != null)
            _scenarioDataService.OnScenarioLoaded -= UpdateScenarioInfo;
    }

    private void OnSimulationStateChanged(SimulationStateChangedEvent evt)
    {
        UpdateDotColor(evt.NewState);
    }


    private void UpdateScenarioInfo(string scenarioId)
    {
        Debug.Log($"[SidebarController] UpdateScenarioInfo called with scenarioId: {scenarioId}");
        if (string.IsNullOrEmpty(scenarioId))
        {
            var manager = FindObjectOfType<ScenarioManager>();
            if (manager != null && manager.HasScenarioLoaded)
            {
                scenarioId = manager.CurrentScenarioId;
            }
        }

        if (string.IsNullOrEmpty(scenarioId))
        {
            // Aucun scénario chargé
            if (_scenarioNameLabel != null)
                _scenarioNameLabel.text = "No scenario loaded";
            if (_scenarioLocationLabel != null)
                _scenarioLocationLabel.text = "—";
            if (_scenarioRobotLabel != null)
                _scenarioRobotLabel.text = "—";
            if (_scenarioStatusDot != null)
                _scenarioStatusDot.RemoveFromClassList("active");
            if (_scenarioThumbnail != null)
                _scenarioThumbnail.style.backgroundImage = StyleKeyword.Null;
            return;
        }

        // Récupérer les infos du scénario
        var info = _scenarioDataService.GetScenarioInfo(scenarioId);
        if (info == null)
        {
            Debug.LogWarning($"[SidebarController] Scenario info not found for ID: {scenarioId}");
            return;
        }

        // Mettre à jour les labels
        if (_scenarioNameLabel != null)
            _scenarioNameLabel.text = info.Name;

        if (_scenarioLocationLabel != null)
            _scenarioLocationLabel.text = info.Location ?? "Unknown Location";

        if (_scenarioRobotLabel != null)
            _scenarioRobotLabel.text = info.RobotType ?? "Unknown Robot";

        // Dot actif
        if (_scenarioStatusDot != null)
            _scenarioStatusDot.AddToClassList("active");

        // Thumbnail
        LoadThumbnail(info);
    }

    private void UpdateDotColor(SimulationState state)
    {
        if (_scenarioStatusDot == null) return;

        // Retirer toutes les classes d'état existantes
        _scenarioStatusDot.RemoveFromClassList("dot-idle");
        _scenarioStatusDot.RemoveFromClassList("dot-ready");
        _scenarioStatusDot.RemoveFromClassList("dot-running");
        _scenarioStatusDot.RemoveFromClassList("dot-paused");

        // Ajouter la classe correspondante
        switch (state)
        {
            case SimulationState.Idle:
                _scenarioStatusDot.AddToClassList("dot-idle");
                break;
            case SimulationState.Ready:
                _scenarioStatusDot.AddToClassList("dot-ready");
                break;
            case SimulationState.Running:
                _scenarioStatusDot.AddToClassList("dot-running");
                break;
            case SimulationState.Paused:
                _scenarioStatusDot.AddToClassList("dot-paused");
                break;
        }
    }

    private void LoadThumbnail(ScenarioInfo info)
    {
        if (_scenarioThumbnail == null || info == null) return;
        Texture2D previewTexture = _scenarioDataService.GetScenarioPreviewImage(info);
        _scenarioThumbnail.image = previewTexture;
        _scenarioThumbnail.style.backgroundImage = StyleKeyword.Null;
    }

    private void ToggleDarkMode(bool isOn)
    {
        var root = uiDocument.rootVisualElement;
        if (isOn)
            root.AddToClassList("dark-mode");
        else
            root.RemoveFromClassList("dark-mode");

        PlayerPrefs.SetInt("DarkMode", isOn ? 1 : 0);
        OnDarkModeToggled?.Invoke(isOn);
    }

    private void RegisterMenuItems()
    {
        var root = uiDocument.rootVisualElement;
        var allMenuButtons = root.Query<Button>(className: "menu-item").ToList();
        foreach (var btn in allMenuButtons)
        {
            if (btn.name == "DarkModeToggle") continue;
            var label = btn.Q<Label>(className: "menu-label");
            string viewName = label?.text ?? btn.name;
            btn.clicked += () => SetActiveView(btn, viewName);
        }
    }

    private void SetActiveView(Button activeButton, string viewName)
    {
        var allMenuItems = activeButton.panel.visualTree.Query<Button>(className: "menu-item").ToList();
        if (allMenuItems != null)
        {
            foreach (var item in allMenuItems)
                item.RemoveFromClassList("active");
        }
        activeButton.AddToClassList("active");
        OnViewChanged?.Invoke(viewName);
    }

    private void ToggleSidebar()
    {
        _isSidebarCollapsed = !_isSidebarCollapsed;
        if (_isSidebarCollapsed)
        {
            _sidebar.AddToClassList("collapsed");
            _collapseButton.text = "▶";
        }
        else
        {
            _sidebar.RemoveFromClassList("collapsed");
            _collapseButton.text = "◀";
        }
    }

    public void SetSidebarCollapsed(bool collapsed)
    {
        if (collapsed == _isSidebarCollapsed) return;
        ToggleSidebar();
    }
}