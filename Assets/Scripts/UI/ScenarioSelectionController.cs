using UnityEngine;
using UnityEngine.UIElements;
using RobotSNAP.Core;
using RobotSNAP.Core.Scenario;
using System;
using System.Linq;

public class ScenarioSelectionController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private ScenarioDataService _dataService;
    [SerializeField] private VisualTreeAsset _cardTemplate;

    [Header("Card Grid Settings")]
    [SerializeField] private int _columns = 2;
    [SerializeField] private float _cardSpacingPercent = 2f;

    [Header("Search / Filter / Sort Settings")]
    [SerializeField] private string _searchPlaceholder = "Rechercher un scénario...";

    // Éléments UI du popup
    private VisualElement _root;
    private VisualElement _popupOverlay;
    private ScrollView _scenarioListView;
    private VisualElement _cardContainer;
    private TextField _searchField;
    private DropdownField _filterDropdown;
    private DropdownField _sortDropdown;
    private Button _cancelButton;
    private Button _loadButton;
    private Button _newScenarioButton;

    // État
    private string _selectedScenarioId = null;

    public event Action<string> OnScenarioSelected;
    public event Action OnPopupClosed;

    private void Start()
    {
        if (_dataService == null)
            _dataService = FindObjectOfType<ScenarioDataService>();
        if (_dataService == null)
            Debug.LogError("[ScenarioSelectionController] ScenarioDataService not found!");

        InitializeUI();
        SubscribeToDataService();
    }

    private void SubscribeToDataService()
    {
        if (_dataService == null) return;
        _dataService.OnScenarioListChanged += RefreshScenarioList;
        // On pourrait aussi écouter OnScenarioLoaded pour mettre à jour l'affichage
    }

    private void OnDestroy()
    {
        if (_dataService != null)
            _dataService.OnScenarioListChanged -= RefreshScenarioList;
        UnsubscribeUI();
    }

    private void InitializeUI()
    {
        if (uiDocument == null)
            uiDocument = GetComponent<UIDocument>();
        if (uiDocument == null) return;

        _root = uiDocument.rootVisualElement;
        _popupOverlay = _root.Q<VisualElement>("PopupOverlay");
        if (_popupOverlay == null)
            _popupOverlay = uiDocument.rootVisualElement.Q<VisualElement>("PopupOverlay");

        if (_popupOverlay != null)
        {
            _scenarioListView = _popupOverlay.Q<ScrollView>("ScenarioListView");
            _searchField = _popupOverlay.Q<TextField>("ScenarioSearchField");
            _filterDropdown = _popupOverlay.Q<DropdownField>("FilterDropdown");
            _sortDropdown = _popupOverlay.Q<DropdownField>("SortDropdown");
            _cancelButton = _popupOverlay.Q<Button>("CancelPopupButton");
            _loadButton = _popupOverlay.Q<Button>("LoadScenarioConfirmButton");
            _newScenarioButton = _popupOverlay.Q<Button>("NewScenarioButton");
        }

        // Config du conteneur de cartes
        if (_scenarioListView != null)
        {
            _scenarioListView.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            _scenarioListView.verticalScrollerVisibility = ScrollerVisibility.Auto;

            _cardContainer = new VisualElement();
            _cardContainer.style.flexWrap = Wrap.Wrap;
            _cardContainer.style.flexDirection = FlexDirection.Row;
            _cardContainer.style.justifyContent = Justify.FlexStart;
            _cardContainer.style.marginLeft = 0;
            _cardContainer.style.marginRight = 0;
            _cardContainer.style.paddingLeft = 4;
            _cardContainer.style.paddingRight = 4;
            _cardContainer.style.width = Length.Percent(100);
            _cardContainer.style.overflow = Overflow.Hidden;

            _scenarioListView.Clear();
            _scenarioListView.Add(_cardContainer);
        }

        // Abonnements UI
        SubscribeUI();

        // Fermeture en cliquant sur l'overlay
        if (_popupOverlay != null)
        {
            VisualElement panel = _popupOverlay.Q<VisualElement>(className: "popup-panel");
            _popupOverlay.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (panel != null && panel.worldBound.Contains(evt.position))
                    return;
                ClosePopup();
            });
        }

        ClosePopup();
    }

    private void SubscribeUI()
    {
        if (_cancelButton != null) _cancelButton.clicked += ClosePopup;
        if (_loadButton != null) _loadButton.clicked += OnConfirmLoadScenario;
        if (_newScenarioButton != null) _newScenarioButton.clicked += OnNewScenarioClicked;
        if (_searchField != null)
        {
            _searchField.RegisterValueChangedCallback(OnSearchValueChanged);
            _searchField.RegisterCallback<FocusInEvent>(_ => _searchField.textEdition.placeholder = "");
            _searchField.RegisterCallback<FocusOutEvent>(_ =>
            {
                if (string.IsNullOrEmpty(_searchField.value))
                    _searchField.textEdition.placeholder = _searchPlaceholder;
            });
        }
        if (_filterDropdown != null)
            _filterDropdown.RegisterValueChangedCallback(OnFilterChanged);
        if (_sortDropdown != null)
            _sortDropdown.RegisterValueChangedCallback(OnSortChanged);
    }

    private void UnsubscribeUI()
    {
        if (_cancelButton != null) _cancelButton.clicked -= ClosePopup;
        if (_loadButton != null) _loadButton.clicked -= OnConfirmLoadScenario;
        if (_newScenarioButton != null) _newScenarioButton.clicked -= OnNewScenarioClicked;
        if (_searchField != null) _searchField.UnregisterValueChangedCallback(OnSearchValueChanged);
        if (_filterDropdown != null) _filterDropdown.UnregisterValueChangedCallback(OnFilterChanged);
        if (_sortDropdown != null) _sortDropdown.UnregisterValueChangedCallback(OnSortChanged);
    }

    // ==========================================
    //          API PUBLIQUE
    // ==========================================

    public void OpenPopup()
    {
        if (_popupOverlay == null) return;

        _selectedScenarioId = null;
        if (_searchField != null)
        {
            _searchField.value = "";
            _searchField.textEdition.placeholder = _searchPlaceholder;
        }

        _dataService.EnsureLoaded();

        // Rafraîchir les filtres (tags)
        UpdateFilterOptions();

        if (_filterDropdown != null) _filterDropdown.value = "Tous";
        if (_sortDropdown != null) _sortDropdown.value = "Nom (A-Z)";

        RefreshScenarioList();
        _popupOverlay.style.display = DisplayStyle.Flex;
    }

    public void ClosePopup()
    {
        if (_popupOverlay == null) return;
        _popupOverlay.style.display = DisplayStyle.None;
        OnPopupClosed?.Invoke();
    }

    // ==========================================
    //          MÉTHODES UI
    // ==========================================

    private void UpdateFilterOptions()
    {
        if (_filterDropdown == null || _dataService == null) return;
        var tags = _dataService.GetAllTags();
        _filterDropdown.choices = tags;
        if (!_filterDropdown.choices.Contains(_filterDropdown.value))
            _filterDropdown.value = "Tous";
    }

    private void RefreshScenarioList()
    {
        if (_cardContainer == null || _dataService == null) return;
        _cardContainer.Clear();

        string filter = _filterDropdown?.value ?? "Tous";
        string search = _searchField?.value ?? "";
        string sort = _sortDropdown?.value ?? "Nom (A-Z)";

        var filtered = _dataService.GetFilteredAndSortedScenarios(filter, search, sort);

        if (filtered.Count == 0)
        {
            var emptyLabel = new Label("Aucun scénario correspondant.");
            emptyLabel.style.color = new StyleColor(Color.gray);
            emptyLabel.style.paddingTop = 20;
            emptyLabel.style.paddingBottom = 20;
            emptyLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            emptyLabel.style.width = Length.Percent(100);
            _cardContainer.Add(emptyLabel);
            return;
        }

        foreach (var item in filtered)
            AddCard(item.Id, item.Info);

        // Appliquer la classe "loaded" au scénario chargé
        string loadedId = _dataService.LoadedScenarioId;
        if (!string.IsNullOrEmpty(loadedId))
        {
            foreach (var child in _cardContainer.Children())
            {
                if (child.userData as string == loadedId)
                {
                    child.AddToClassList("loaded");
                    break;
                }
            }
        }
    }

    private void AddCard(string fileId, ScenarioInfo info)
    {
        // (La méthode AddCard reste quasi inchangée, mais elle utilise info fourni par le service)
        // Je la réécris brièvement pour montrer l'utilisation.
        if (_cardTemplate == null) return;

        var templateContainer = _cardTemplate.Instantiate();
        var card = templateContainer.Q<VisualElement>("CardRoot") ?? templateContainer.Children().FirstOrDefault() as VisualElement;
        if (card == null) return;

        card.userData = fileId;

        // Dimensions
        float spacing = _cardSpacingPercent / 100f;
        float cardWidthPercent = (1f - (_columns - 1) * spacing) / _columns;
        float marginPercent = spacing / 2f;

        card.style.width = new Length(cardWidthPercent * 100, LengthUnit.Percent);
        card.style.marginLeft = new Length(marginPercent * 100, LengthUnit.Percent);
        card.style.marginRight = new Length(marginPercent * 100, LengthUnit.Percent);
        card.style.marginBottom = 8;
        card.style.flexShrink = 0;

        // Titre
        var titleLabel = card.Q<Label>("CardTitle");
        if (titleLabel != null) titleLabel.text = info.Name;

        // Tags
        var tagsContainer = card.Q<VisualElement>("CardTagsContainer");
        if (tagsContainer != null && info.Tags != null)
        {
            tagsContainer.Clear();
            foreach (var tag in info.Tags)
            {
                if (string.IsNullOrWhiteSpace(tag)) continue;
                var tagElement = CreateTagElement(tag);
                tagsContainer.Add(tagElement);
            }
        }

        // Location
        var locationLabel = card.Q<Label>("CardLocation");
        if (locationLabel != null) locationLabel.text = info.Location ?? "";

        // Date
        var dateLabel = card.Q<Label>("CardDate");
        if (dateLabel != null) dateLabel.text = info.Created ?? "";

        // Preview
        var previewImage = card.Q<Image>("CardPreviewImage");
        if (previewImage != null) LoadPreviewImage(previewImage, info);

        // Sélection
        card.RegisterCallback<ClickEvent>(_ =>
        {
            foreach (var child in _cardContainer.Children())
                child.RemoveFromClassList("selected");
            card.AddToClassList("selected");
            _selectedScenarioId = fileId;
        });

        _cardContainer.Add(card);
    }

    private VisualElement CreateTagElement(string tag)
    {
        var tagElement = new VisualElement();
        tagElement.AddToClassList("card-tag");

        // Vous pouvez ici recréer votre logique de tag avec couleur et texte
        // par simplicité, je fais un Label avec couleur
        var label = new Label(tag);
        var color = GetColorFromTag(tag);
        label.style.color = Color.Lerp(color, Color.white, 0.4f);
        tagElement.Add(label);

        // Le fond coloré derrière
        var colorFilter = new VisualElement();
        colorFilter.AddToClassList("card-tag-color-filter");
        colorFilter.style.backgroundColor = color;
        tagElement.Add(colorFilter);

        tagElement.RegisterCallback<ClickEvent>(_ =>
        {
            if (_filterDropdown != null)
            {
                _filterDropdown.value = tag;
                RefreshScenarioList();
            }
        });

        return tagElement;
    }

    private Color GetColorFromTag(string tag)
    {
        int hash = Mathf.Abs(tag.GetHashCode());
        float hue = (hash % 360) / 360f;
        return Color.HSVToRGB(hue, 0.85f, 0.9f);
    }

    private void LoadPreviewImage(Image imageElement, ScenarioInfo info)
    {
        // (inchangé)
        if (imageElement == null || info == null) return;
        Texture2D previewTexture = _dataService.GetScenarioPreviewImage(info);
        imageElement.image = previewTexture;
        imageElement.style.backgroundImage = StyleKeyword.Null;


        // string previewName = info.PreviewImage;
        // if (string.IsNullOrEmpty(previewName))
        // {
        //     SetDefaultImage(imageElement);
        //     return;
        // }
        // string fileNameWithoutExt = System.IO.Path.GetFileNameWithoutExtension(previewName);
        // string resourcePath = $"ScenarioPreviews/{fileNameWithoutExt}";
        // Texture2D previewTexture = Resources.Load<Texture2D>(resourcePath);
        // if (previewTexture != null)
        // {
        //     imageElement.image = previewTexture;
        //     imageElement.style.backgroundImage = StyleKeyword.Null;
        // }
        // else
        // {
        //     SetDefaultImage(imageElement);
        // }
    }

    

    // ==========================================
    //          CALLBACKS UI
    // ==========================================

    private void OnSearchValueChanged(ChangeEvent<string> evt) => RefreshScenarioList();
    private void OnFilterChanged(ChangeEvent<string> evt) => RefreshScenarioList();
    private void OnSortChanged(ChangeEvent<string> evt) => RefreshScenarioList();

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