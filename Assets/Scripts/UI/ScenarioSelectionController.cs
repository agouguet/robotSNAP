using UnityEngine;
using UnityEngine.UIElements;
using RobotSNAP.Core;
using RobotSNAP.Core.Scenario;
using System;
using System.Collections.Generic;
using System.Linq;

public class ScenarioSelectionController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private ScenarioManager scenarioManager;
    [SerializeField] private VisualTreeAsset _cardTemplate;

    [Header("Card Grid Settings")]
    [SerializeField] private int _columns = 2;
    [SerializeField] private float _cardSpacingPercent = 2f;

    [Header("Search / Filter / Sort Settings")]
    [SerializeField] private string _searchPlaceholder = "Rechercher un scénario...";
    [SerializeField] private string[] _filterOptions = { "Tous", "Mes scénarios", "Scénarios partagés" };
    [SerializeField] private string[] _sortOptions = { "Nom (A-Z)", "Nom (Z-A)", "Date récent", "Date ancien" };

    [Header("Debug")]
    [SerializeField] private bool _useDebugData = true;

    // Éléments UI du popup
    private VisualElement _root;
    private VisualElement _popupOverlay;
    private ScrollView _scenarioListView;
    private VisualElement _cardContainer; // conteneur des cartes (en grille)
    private TextField _searchField;
    private DropdownField _filterDropdown;
    private DropdownField _sortDropdown;
    private Button _cancelButton;
    private Button _loadButton;
    private Button _newScenarioButton;

    // État
    private string _selectedScenarioId = null;
    private string _loadedScenarioId = null;
    private Dictionary<string, ScenarioInfo> _scenarioDictionary = new Dictionary<string, ScenarioInfo>();

    public event System.Action<string> OnScenarioSelected;
    public event System.Action OnPopupClosed;

    private void Start()
    {
        if (scenarioManager == null)
            scenarioManager = FindObjectOfType<ScenarioManager>();
        InitializeUI();
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

        // === Configuration du conteneur des cartes (grid) ===
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

        // Abonnements
        if (_cancelButton != null)
            _cancelButton.clicked += ClosePopup;
        if (_loadButton != null)
            _loadButton.clicked += OnConfirmLoadScenario;
        if (_newScenarioButton != null)
            _newScenarioButton.clicked += OnNewScenarioClicked;
        if (_searchField != null){
            _searchField.RegisterValueChangedCallback(OnSearchValueChanged);
            _searchField.RegisterCallback<FocusInEvent>(_ =>
            {
                _searchField.textEdition.placeholder = "";
            });
            _searchField.RegisterCallback<FocusOutEvent>(_ =>
            {
                if (string.IsNullOrEmpty(_searchField.value))
                    _searchField.textEdition.placeholder = _searchPlaceholder;
            });
        }
        if (_filterDropdown != null)
        {
            _filterDropdown.RegisterValueChangedCallback(OnFilterChanged);
            FixDropdownDarkMode(_filterDropdown);
        }
        if (_sortDropdown != null)
        {
            FixDropdownDarkMode(_sortDropdown);
            _sortDropdown.RegisterValueChangedCallback(OnSortChanged);
        }

        // Fermeture en cliquant sur l'overlay
        if (_popupOverlay != null)
        {
            VisualElement panel = _popupOverlay.Q<VisualElement>(className: "popup-panel");
            
            _popupOverlay.RegisterCallback<PointerDownEvent>(evt =>
            {
                // Si le panneau existe et que le clic est à l'intérieur de ses limites (coordonnées écran), on ne ferme pas
                if (panel != null && panel.worldBound.Contains(evt.position))
                    return;

                // Sinon, on ferme
                ClosePopup();
            });
        }

        ClosePopup();
    }

    private void OnDestroy()
    {
        if (_cancelButton != null)
            _cancelButton.clicked -= ClosePopup;
        if (_loadButton != null)
            _loadButton.clicked -= OnConfirmLoadScenario;
        if (_newScenarioButton != null)
            _newScenarioButton.clicked -= OnNewScenarioClicked;
        if (_searchField != null)
            _searchField.UnregisterValueChangedCallback(OnSearchValueChanged);
        if (_filterDropdown != null)
            _filterDropdown.UnregisterValueChangedCallback(OnFilterChanged);
        if (_sortDropdown != null)
            _sortDropdown.UnregisterValueChangedCallback(OnSortChanged);
    }

    // ==========================================
    //          API PUBLIQUE
    // ==========================================

    public void OpenPopup()
    {
        if (_popupOverlay == null)
        {
            Debug.LogWarning("[ScenarioSelectionController] PopupOverlay non trouvé.");
            return;
        }

        _selectedScenarioId = null;
        if (_searchField != null){
            _searchField.value = "";
            _searchField.textEdition.placeholder = _searchPlaceholder;
        }
        // Récupérer le scénario actuellement chargé
        _loadedScenarioId = scenarioManager?.CurrentScenarioId;

        LoadAllScenarioInfos();
        UpdateFilterOptions();

        if (_filterDropdown != null)
            _filterDropdown.value = "Tous";
        if (_sortDropdown != null)
            _sortDropdown.value = "Nom (A-Z)";

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
    //          MÉTHODES PRIVÉES
    // ==========================================

    private void LoadAllScenarioInfos()
    {
        _scenarioDictionary.Clear();

        if (_useDebugData)
        {
            var debugList = GenerateDebugScenarioInfos();
            foreach (var info in debugList)
                _scenarioDictionary[info.Name] = info; // On utilise le nom comme ID pour debug
            Debug.Log($"[ScenarioSelectionController] Utilisation de {_scenarioDictionary.Count} scénarios fictifs (mode debug)");
            return;
        }

        if (scenarioManager == null) return;

        var fileIds = scenarioManager.GetAvailableScenarios(); // liste des noms de fichiers
        foreach (var fileId in fileIds)
        {
            var info = scenarioManager.GetScenarioInfo(fileId);
            if (info != null)
            {
                _scenarioDictionary[fileId] = info; // clé = nom du fichier, valeur = infos
            }
        }
        Debug.Log($"[ScenarioSelectionController] Chargé {_scenarioDictionary.Count} scénarios réels.");
    }

    private List<ScenarioInfo> GenerateDebugScenarioInfos()
    {
        return new List<ScenarioInfo>
        {
            new ScenarioInfo
            {
                Name = "Corridor Crowd",
                Type = "Crowd",
                Location = "Office Building 01",
                Created = "2025-07-27 14:30",
                Tags = new[] { "crowd", "corridor" },
                Description = "Dense crowd in narrow corridor"
            },
            new ScenarioInfo
            {
                Name = "Intersection Busy",
                Type = "Crossing",
                Location = "Urban Plaza",
                Created = "2025-07-25 10:12",
                Tags = new[] { "intersection", "crossing" },
                Description = "Busy intersection with pedestrians"
            },
            new ScenarioInfo
            {
                Name = "Narrow Passage",
                Type = "Navigation",
                Location = "Office Building 02",
                Created = "2025-07-24 16:45",
                Tags = new[] { "navigation", "narrow" },
                Description = "Navigation through tight passage"
            },
            new ScenarioInfo
            {
                Name = "Circular Flow",
                Type = "Crowd",
                Location = "Town Square",
                Created = "2025-07-23 09:18",
                Tags = new[] { "circular", "flow" },
                Description = "Circular pedestrian flow"
            },
            new ScenarioInfo
            {
                Name = "Frontal Approach",
                Type = "Interaction",
                Location = "Office Building 01",
                Created = "2025-07-22 11:05",
                Tags = new[] { "frontal", "approach" },
                Description = "Head-on interaction"
            },
            new ScenarioInfo
            {
                Name = "Corner Turn",
                Type = "Navigation",
                Location = "Office Building 02",
                Created = "2025-07-21 15:22",
                Tags = new[] { "corner", "turn" },
                Description = "Turning around corners"
            },
            new ScenarioInfo
            {
                Name = "Perpendicular Traffic",
                Type = "Crossing",
                Location = "Urban Street",
                Created = "2025-07-20 13:47",
                Tags = new[] { "perpendicular", "traffic" },
                Description = "Perpendicular crossing"
            },
            new ScenarioInfo
            {
                Name = "Dense Crowd",
                Type = "Crowd",
                Location = "Main Hall",
                Created = "2025-07-19 08:33",
                Tags = new[] { "dense", "crowd" },
                Description = "Extremely dense crowd"
            }
        };
    }

    private void UpdateFilterOptions()
    {
        if (_filterDropdown == null) return;

        // Récupérer tous les tags uniques de tous les scénarios
        var allTags = new HashSet<string>();
        allTags.Add("Tous");
        
        foreach (var entry in _scenarioDictionary)
        {
            var info = entry.Value;
            if (info.Tags != null)
            {
                foreach (var tag in info.Tags)
                    allTags.Add(tag);
            }
        }

        var sortedTags = allTags.ToList();
        sortedTags.Sort();

        _filterDropdown.choices = sortedTags;
        if (!_filterDropdown.choices.Contains(_filterDropdown.value))
            _filterDropdown.value = "Tous";
    }

    private void RefreshScenarioList()
    {
        if (_cardContainer == null) return;
        _cardContainer.Clear();

        string filter = _filterDropdown?.value ?? "Tous";
        string search = _searchField?.value ?? "";
        string sort = _sortDropdown?.value ?? "Nom (A-Z)";

        // Filtrer et trier les entrées du dictionnaire
        var filtered = _scenarioDictionary
            .Where(entry => FilterMatches(entry, filter, search))
            .Select(entry => (Id: entry.Key, Info: entry.Value))
            .ToList();

        filtered = SortScenarios(filtered, sort);

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
        {
            AddCard(item.Id, item.Info);
        }

        // Appliquer la classe "loaded" au scénario actuellement chargé
        if (!string.IsNullOrEmpty(_loadedScenarioId))
        {
            foreach (var child in _cardContainer.Children())
            {
                string cardId = child.userData as string;
                if (string.Equals(cardId, _loadedScenarioId, System.StringComparison.OrdinalIgnoreCase))
                {
                    child.AddToClassList("loaded");
                    break;
                }
            }
        }
    }

    private bool FilterMatches(KeyValuePair<string, ScenarioInfo> entry, string filter, string search)
    {
        var info = entry.Value;
        
        // Filtre par tag
        if (filter != "Tous")
        {
            bool tagMatch = info.Tags != null && info.Tags.Contains(filter);
            if (!tagMatch) return false;
        }

        // Recherche textuelle
        if (!string.IsNullOrEmpty(search))
        {
            string lowerSearch = search.ToLower();
            if (!info.Name.ToLower().Contains(lowerSearch))
                return false;
        }
        return true;
    }

    private List<(string Id, ScenarioInfo Info)> SortScenarios(List<(string Id, ScenarioInfo Info)> list, string sortOption)
    {
        switch (sortOption)
        {
            case "Nom (A-Z)":
                return list.OrderBy(x => x.Info.Name).ToList();
            case "Nom (Z-A)":
                return list.OrderByDescending(x => x.Info.Name).ToList();
            case "Date récent":
                return list.OrderByDescending(x => x.Info.Created).ToList();
            case "Date ancien":
                return list.OrderBy(x => x.Info.Created).ToList();
            default:
                return list;
        }
    }

    private void AddCard(string fileId, ScenarioInfo info)
    {
        if (_cardTemplate == null)
        {
            Debug.LogWarning("[ScenarioSelectionController] Card template not assigned.");
            return;
        }

        var templateContainer = _cardTemplate.Instantiate();
        var card = templateContainer.Q<VisualElement>("CardRoot");
        if (card == null)
        {
            card = templateContainer.Children().FirstOrDefault() as VisualElement;
            if (card == null)
                return;
        }

        // Stocker l'ID du fichier comme userData
        card.userData = fileId;

        // Calcul des dimensions
        float spacing = _cardSpacingPercent / 100f;
        float cardWidthPercent = (1f - (_columns - 1) * spacing) / _columns;
        float marginPercent = spacing / 2f;

        card.style.width = new Length(cardWidthPercent * 100, LengthUnit.Percent);
        card.style.marginLeft = new Length(marginPercent * 100, LengthUnit.Percent);
        card.style.marginRight = new Length(marginPercent * 100, LengthUnit.Percent);
        card.style.marginBottom = 8;
        card.style.flexShrink = 0;

        // Remplir les labels avec info
        var titleLabel = card.Q<Label>("CardTitle");
        if (titleLabel != null)
            titleLabel.text = info.Name;

        var tagsContainer = card.Q<VisualElement>("CardTagsContainer");
        if (tagsContainer != null && info.Tags != null)
        {
            tagsContainer.Clear();
            
            foreach (var tag in info.Tags)
            {
                if (string.IsNullOrWhiteSpace(tag)) continue;

                var tagElement = new VisualElement();
                tagElement.AddToClassList("card-tag");

                var colorTag = GetColorFromTag(tag);

                // var colorDot = new VisualElement();
                // colorDot.AddToClassList("card-tag-color-dot");
                // colorDot.style.backgroundColor = GetColorFromTag(tag);
                // tagElement.Add(colorDot);

                var label = new Label(tag);
                var colorText = Color.Lerp(colorTag, Color.white, 0.3f);
                label.style.color = colorText;
                tagElement.Add(label);


                var tagColorFilter = new VisualElement();
                tagColorFilter.AddToClassList("card-tag-color-filter");
                tagColorFilter.style.backgroundColor = colorTag;
                tagElement.Add(tagColorFilter);

                // Clic → filtrer par ce tag
                tagElement.RegisterCallback<ClickEvent>(_ =>
                {
                    if (_filterDropdown != null)
                    {
                        _filterDropdown.value = tag;
                        RefreshScenarioList();
                    }
                });

                tagsContainer.Add(tagElement);
            }
        }

        // === LOCATION ===
        var locationLabel = card.Q<Label>("CardLocation");
        if (locationLabel != null)
            locationLabel.text = info.Location ?? "";

        var dateLabel = card.Q<Label>("CardDate");
        if (dateLabel != null)
            dateLabel.text = info.Created ?? "";

        // Charger l'image de prévisualisation
        var previewImage = card.Q<Image>("CardPreviewImage");
        if (previewImage != null)
        {
            // Charger asynchrone (ou synchrone selon le contexte)
            LoadPreviewImage(previewImage, info);
        }

        // Clic pour sélectionner
        card.RegisterCallback<ClickEvent>(evt =>
        {
            foreach (var child in _cardContainer.Children())
                child.RemoveFromClassList("selected");

            card.AddToClassList("selected");
            _selectedScenarioId = fileId; // on stocke l'ID du fichier
        });

        _cardContainer.Add(card);
    }

    private void LoadPreviewImage(Image imageElement, ScenarioInfo info)
    {
        if (imageElement == null || info == null) return;

        string previewName = info.PreviewImage;
        Debug.Log($"[ScenarioSelectionController] Preview name from YAML: '{previewName}'");

        if (string.IsNullOrEmpty(previewName))
        {
            SetDefaultImage(imageElement);
            return;
        }

        // Enlever l'extension si présente (Resources.Load n'en a pas besoin)
        string fileNameWithoutExt = System.IO.Path.GetFileNameWithoutExtension(previewName);
        string resourcePath = $"ScenarioPreviews/{fileNameWithoutExt}";
        
        Debug.Log($"[ScenarioSelectionController] Trying to load: {resourcePath}");
        
        Texture2D previewTexture = Resources.Load<Texture2D>(resourcePath);
        if (previewTexture != null)
        {
            imageElement.image = previewTexture;
            imageElement.style.backgroundImage = StyleKeyword.Null;
            Debug.Log($"[ScenarioSelectionController] Preview loaded successfully: {previewName}");
        }
        else
        {
            Debug.LogWarning($"[ScenarioSelectionController] Preview not found at: {resourcePath}");
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

    private Color GetColorFromTag(string tag)
    {
        int hash = Mathf.Abs(tag.GetHashCode());
        float hue = (hash % 360) / 360f;
        float saturation = 0.85f;
        float value = 0.9f; // Couleurs vives, car elles sont utilisées uniquement pour la pastille
        return Color.HSVToRGB(hue, saturation, value);
    }

    private void FixDropdownDarkMode(DropdownField dropdown)
    {
        if (dropdown == null) return;

        // Dès que le dropdown est focus (ou qu'il s'ouvre), on intercepte le popup
        dropdown.RegisterCallback<FocusEvent>(evt =>
        {
            var panel = dropdown.panel;
            Debug.LogWarning($"[ScenarioSelectionController] Dropdown opened. Panel: {panel}");
            if (panel != null)
            {
                // Chercher un élément de type "unity-base-dropdown" dans le panel (non garanti)
                // Ceci est un hack et peut ne pas fonctionner
                var popup = panel.visualTree.Q<VisualElement>("unity-base-dropdown");
                if (popup != null && _root.ClassListContains("dark-mode"))
                {
                    popup.AddToClassList("dark-mode");
                }
            }
        });
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

        // Charger via ScenarioManager en utilisant l'ID du fichier
        if (_useDebugData)
        {
            Debug.Log($"[ScenarioSelectionController] (Debug) Scénario fictif sélectionné : {_selectedScenarioId}");
        }
        else if (scenarioManager != null)
        {
            scenarioManager.LoadScenario(_selectedScenarioId, startClock: false, autoApply: false);
            // Mettre à jour l'ID chargé pour la prochaine ouverture
            _loadedScenarioId = _selectedScenarioId;
        }
    }

    private void OnNewScenarioClicked()
    {
        Debug.Log("[ScenarioSelectionController] Nouveau scénario - à implémenter.");
        ClosePopup();
    }
}