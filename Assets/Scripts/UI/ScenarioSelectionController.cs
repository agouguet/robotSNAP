using UnityEngine;
using System.Linq;
using UnityEngine.UIElements;
using RobotSNAP.Core;
using RobotSNAP.Core.Scenario;
using System;

public class ScenarioSelectionController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private ScenarioDataService _dataService;
    [SerializeField] private VisualTreeAsset _cardTemplate;

    [Header("Pop-up settings")]
    [SerializeField] private int _columns = 4;
    [SerializeField] private float _cardSpacingPercent = 1f;
    [SerializeField] private string _searchPlaceholder = "Search a scenario...";

    // Éléments UI du popup
    private VisualElement _root;
    private VisualElement _popupOverlay;
    private Button _cancelButton;
    private Button _loadButton;
    private Button _newScenarioButton;

    private ScenarioListView _listView;

    // État
    private string _selectedScenarioId = null;

    public event Action<string> OnScenarioSelected;
    public event Action OnPopupClosed;

    // ==========================================
    //          CYCLE DE VIE
    // ==========================================

    private void Start()
    {
        if (_dataService == null)
            _dataService = FindObjectOfType<ScenarioDataService>();
        if (_dataService == null)
            Debug.LogError("[ScenarioSelectionController] ScenarioDataService not found!");

        InitializeUI();
    }

    private void OnDestroy()
    {
        _listView?.Dispose();
        UnsubscribeUI();
    }

    // ==========================================
    //          INITIALISATION
    // ==========================================

    private void InitializeUI()
    {
        if (uiDocument == null)
            uiDocument = GetComponent<UIDocument>();
        if (uiDocument == null) return;

        _root = uiDocument.rootVisualElement;
        _popupOverlay = _root.Q<VisualElement>("PopupOverlay");
        if (_popupOverlay == null)
            _popupOverlay = uiDocument.rootVisualElement.Q<VisualElement>("PopupOverlay");

        if (_popupOverlay == null)
        {
            Debug.LogError("[ScenarioSelectionController] PopupOverlay not found!");
            return;
        }

        _cancelButton = _popupOverlay.Q<Button>("CancelPopupButton");
        _loadButton = _popupOverlay.Q<Button>("LoadScenarioConfirmButton");
        _newScenarioButton = _popupOverlay.Q<Button>("NewScenarioButton");

        var panel = _popupOverlay.Q<VisualElement>(className: "popup-panel");
        if (panel == null) return;

        var title = panel.Q<Label>(className: "popup-title");
        var subtitle = panel.Q<Label>(className: "popup-subtitle");
        var footer = panel.Q<VisualElement>(className: "popup-footer");

        var children = panel.Children().ToList();
        bool startRemoving = false;
        foreach (var child in children)
        {
            if (child == subtitle)
            {
                startRemoving = true;
                continue;
            }
            if (child == footer)
            {
                break;
            }
            if (startRemoving)
            {
                child.RemoveFromHierarchy();
            }
        }

        _listView = new ScenarioListView(_cardTemplate, _columns, _cardSpacingPercent);
        _listView.Initialize(_dataService);
        _listView.style.flexGrow = 1;
        _listView.style.flexShrink = 1;
        _listView.style.width = Length.Percent(100);
        _listView.style.marginTop = 8;
        _listView.style.marginBottom = 8;
        

        int insertIndex = panel.IndexOf(subtitle) + 1;
        panel.Insert(insertIndex, _listView);

        _listView.OnScenarioSelected += OnListViewSelectionChanged;

        SubscribeUI();

        if (_popupOverlay != null)
        {
            VisualElement popup_panel = _popupOverlay.Q<VisualElement>(className: "popup-panel");
            _popupOverlay.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (popup_panel != null && popup_panel.worldBound.Contains(evt.position))
                    return;
                ClosePopup();
            });
        }


        // Fermé par défaut
        ClosePopup();
    }

    private void SubscribeUI()
    {
        if (_cancelButton != null) _cancelButton.clicked += ClosePopup;
        if (_loadButton != null) _loadButton.clicked += OnConfirmLoadScenario;
        if (_newScenarioButton != null) _newScenarioButton.clicked += OnNewScenarioClicked;
    }

    private void UnsubscribeUI()
    {
        if (_cancelButton != null) _cancelButton.clicked -= ClosePopup;
        if (_loadButton != null) _loadButton.clicked -= OnConfirmLoadScenario;
        if (_newScenarioButton != null) _newScenarioButton.clicked -= OnNewScenarioClicked;
        if (_listView != null) _listView.OnScenarioSelected -= OnListViewSelectionChanged;
    }

    // ==========================================
    //          GESTION DU POPUP
    // ==========================================

    public void OpenPopup()
    {
        if (_popupOverlay == null) return;

        _selectedScenarioId = null;
        _loadButton?.SetEnabled(false);

        _dataService?.EnsureLoaded();
        _listView?.Refresh();

        _popupOverlay.style.display = DisplayStyle.Flex;
    }

    public void ClosePopup()
    {
        if (_popupOverlay == null) return;
        _popupOverlay.style.display = DisplayStyle.None;
        OnPopupClosed?.Invoke();
    }

    // ==========================================
    //          ÉVÉNEMENTS DE LA LISTE
    // ==========================================

    private void OnListViewSelectionChanged(string scenarioId)
    {
        _selectedScenarioId = scenarioId;
        _loadButton?.SetEnabled(!string.IsNullOrEmpty(scenarioId));
    }

    // ==========================================
    //          CALLBACKS BOUTONS
    // ==========================================

    private void OnConfirmLoadScenario()
    {
        if (string.IsNullOrEmpty(_selectedScenarioId))
        {
            Debug.LogWarning("[ScenarioSelectionController] Aucun scénario sélectionné.");
            return;
        }

        ClosePopup();
        OnScenarioSelected?.Invoke(_selectedScenarioId);
        _dataService?.LoadScenario(_selectedScenarioId);
    }

    private void OnNewScenarioClicked()
    {
        Debug.Log("[ScenarioSelectionController] Nouveau scénario - à implémenter.");
        ClosePopup();
    }
}