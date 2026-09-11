using UnityEngine;
using UnityEngine.UIElements;
using RobotSNAP.Core.Scenario;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Control the list of scenarios in the popup, with search, filter, and sort functionalities.
/// </summary>
public class ScenarioListView : VisualElement
{
    // === Propriétés publiques ===
    public string SelectedScenarioId { get; private set; }
    public ScenarioInfo SelectedScenarioInfo { get; private set; }

    // === Événements ===
    public event Action<string> OnScenarioSelected;
    public event Action<string> OnScenarioDoubleClicked;

    private int _columns = 2;
    private float _cardSpacingPercent = 2f;

    // === Éléments internes ===
    private TextField _searchField;
    private DropdownField _filterDropdown;
    private DropdownField _sortDropdown;
    private VisualElement _gridContainer;
    private Label _countLabel;
    private VisualTreeAsset _cardTemplate;

    private ScenarioDataService _dataService;
    private List<string> _availableTags = new List<string>();

    private string _selectedScenarioId = null;

    // === Constructeur ===
    public ScenarioListView(VisualTreeAsset cardTemplate = null, int columns = 2, float cardSpacingPercent = 2f)
    {
        _columns = columns;
        _cardSpacingPercent = cardSpacingPercent;
        _cardTemplate = cardTemplate;
        BuildUI();
    }

    private void BuildUI()
    {
        // Créer la structure UI
        AddToClassList("scenario-browser");

        // Toolbar
        var toolbar = new VisualElement();
        toolbar.AddToClassList("scenario-toolbar");
        Add(toolbar);

        // Search
        var searchContainer = new VisualElement();
        searchContainer.AddToClassList("scenario-search-container");
        var searchIcon = new VisualElement();
        searchIcon.AddToClassList("scenario-search-icon");
        searchIcon.style.backgroundImage = new StyleBackground(Resources.Load<Texture2D>("Icons/search"));
        _searchField = new TextField();
        _searchField.AddToClassList("scenario-search-field");
        _searchField.textEdition.placeholder = "Search scenarios...";
        searchContainer.Add(searchIcon);
        searchContainer.Add(_searchField);
        toolbar.Add(searchContainer);

        // Filters
        var filters = new VisualElement();
        filters.AddToClassList("scenario-filters");
        _filterDropdown = new DropdownField { choices = new List<string> { "All" }, value = "All" };
        _filterDropdown.AddToClassList("scenario-filter-dropdown");
        _sortDropdown = new DropdownField { choices = new List<string> { "Name (A-Z)", "Name (Z-A)", "Newest", "Oldest" }, value = "Name (A-Z)" };
        _sortDropdown.AddToClassList("scenario-sort-dropdown");
        filters.Add(_filterDropdown);
        filters.Add(_sortDropdown);
        toolbar.Add(filters);

        // Count label
        _countLabel = new Label("0 scenarios");
        _countLabel.AddToClassList("scenario-count-label");
        Add(_countLabel);

        // Grid
        var scrollView = new ScrollView(ScrollViewMode.Vertical);
        scrollView.AddToClassList("scenario-list-view");
        _gridContainer = new VisualElement();
        _gridContainer.AddToClassList("scenario-grid");
        scrollView.Add(_gridContainer);
        Add(scrollView);

        // Abonnements
        _searchField.RegisterValueChangedCallback(evt => Refresh());
        _filterDropdown.RegisterValueChangedCallback(evt => Refresh());
        _sortDropdown.RegisterValueChangedCallback(evt => Refresh());
    }

    // === Public Methods ===
    public void Initialize(ScenarioDataService dataService)
    {
        _dataService = dataService;
        if (_dataService != null)
        {
            _dataService.OnScenarioListChanged += Refresh;
            Refresh();
        }
    }

    public void Dispose()
    {
        if (_dataService != null)
            _dataService.OnScenarioListChanged -= Refresh;
    }

    public void ClearSelection()
    {
        _selectedScenarioId = null;
        SelectedScenarioId = null;
        SelectedScenarioInfo = null;

        foreach (VisualElement child in _gridContainer.Children())
            child.RemoveFromClassList("selected");
    }

    public void Refresh()
    {
        if (_dataService == null) return;
        _gridContainer.Clear();

        string filter = _filterDropdown?.value ?? "All";
        string search = _searchField?.value ?? "";
        string sort = _sortDropdown?.value ?? "Name (A-Z)";

        var allTags = _dataService.GetAllTags();
        if (!_filterDropdown.choices.SequenceEqual(allTags))
        {
            _filterDropdown.choices = allTags;
            if (!_filterDropdown.choices.Contains(_filterDropdown.value))
                _filterDropdown.value = "All";
        }

        var scenarios = _dataService.GetFilteredAndSortedScenarios(filter, search, sort);
        _countLabel.text = $"{scenarios.Count} scenario{(scenarios.Count == 1 ? "" : "s")}";

        if (scenarios.Count == 0)
        {
            var empty = new Label("No matching scenarios.");
            empty.AddToClassList("scenario-empty-message");
            _gridContainer.Add(empty);
            return;
        }

        foreach (var (id, info) in scenarios)
            AddCard(id, info);

        if (!string.IsNullOrEmpty(SelectedScenarioId))
        {
            foreach (var child in _gridContainer.Children())
            {
                if (child.userData as string == SelectedScenarioId)
                {
                    child.AddToClassList("selected");
                    break;
                }
            }
        }
    }

    private void AddCard(string fileId, ScenarioInfo info)
    {
        if (_cardTemplate == null) return;

        var templateContainer = _cardTemplate.Instantiate();
        var card = templateContainer.Q<VisualElement>("CardRoot") ?? templateContainer.Children().FirstOrDefault() as VisualElement;
        if (card == null) return;

        card.userData = fileId;

        var horizontal_space_percent = 99f;


        float spacing = _cardSpacingPercent / 100f;
        float cardWidthPercent = (1f - (_columns - 1) * spacing) / _columns;
        float marginPercent = spacing / 2f;

        card.style.width = new Length(cardWidthPercent * horizontal_space_percent, LengthUnit.Percent);
        card.style.marginLeft = new Length(marginPercent * horizontal_space_percent, LengthUnit.Percent);
        card.style.marginRight = new Length(marginPercent * horizontal_space_percent, LengthUnit.Percent);
        card.style.marginBottom = 8;
        card.style.flexShrink = 0;

        var titleLabel = card.Q<Label>("CardTitle");
        if (titleLabel != null) titleLabel.text = info.Name;

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

        var locationLabel = card.Q<Label>("CardLocation");
        if (locationLabel != null) locationLabel.text = info.Location ?? "";

        var dateLabel = card.Q<Label>("CardDate");
        if (dateLabel != null) dateLabel.text = info.Created ?? "";

        var previewImage = card.Q<Image>("CardPreviewImage");
        if (previewImage != null)
        {
            previewImage.scaleMode = ScaleMode.ScaleAndCrop;
            LoadPreviewImage(previewImage, info);
        }

        card.RegisterCallback<ClickEvent>(_ =>
        {
            foreach (var child in _gridContainer.Children())
                child.RemoveFromClassList("selected");
            card.AddToClassList("selected");
            _selectedScenarioId = fileId;
            SelectedScenarioId = fileId;
            SelectedScenarioInfo = info;
            OnScenarioSelected?.Invoke(fileId);
        });

        _gridContainer.Add(card);
    }

    private VisualElement CreateTagElement(string tag)
    {
        var tagElement = new VisualElement();
        tagElement.AddToClassList("card-tag");

        var label = new Label(tag);
        var color = _dataService.GetColorFromTag(tag);
        label.style.color = Color.Lerp(color, Color.white, 0.4f);
        tagElement.Add(label);

        var colorFilter = new VisualElement();
        colorFilter.AddToClassList("card-tag-color-filter");
        colorFilter.style.backgroundColor = color;
        tagElement.Add(colorFilter);

        tagElement.RegisterCallback<ClickEvent>(_ =>
        {
            if (_filterDropdown != null)
            {
                _filterDropdown.value = tag;
                Refresh();
            }
        });

        return tagElement;
    }

    private void LoadPreviewImage(Image imageElement, ScenarioInfo info)
    {
        if (imageElement == null || info == null) return;
        string previewName = info.PreviewImage;
        if (string.IsNullOrEmpty(previewName))
        {
            SetDefaultImage(imageElement);
            return;
        }
        string fileNameWithoutExt = System.IO.Path.GetFileNameWithoutExtension(previewName);
        Texture2D texture = Resources.Load<Texture2D>($"ScenarioPreviews/{fileNameWithoutExt}");
        if (texture != null)
        {
            imageElement.image = texture;
            imageElement.style.backgroundImage = StyleKeyword.Null;
        }
        else
        {
            SetDefaultImage(imageElement);
        }
    }

    private void SetDefaultImage(Image imageElement)
    {
        Texture2D defaultTex = Resources.Load<Texture2D>("ScenarioPreviews/default");
        if (defaultTex != null)
            imageElement.image = defaultTex;
        else
            imageElement.style.backgroundColor = new Color(0.2f, 0.2f, 0.25f);
    }
}
