using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RobotSNAP.Core.Scenario;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UIElements;

/// <summary>
/// Drives the scenario browser and the three-step scenario creation workflow.
/// </summary>
public class ScenariosTabController : MonoBehaviour
{
    private const string HiddenClass = "scenario-view-hidden";
    private const string StepHiddenClass = "creation-step-hidden";
    private const string ErrorClass = "error";
    private const string TagPlaceholder = "Ajouter un tag…";
    private const string MapPlaceholder = "Sélectionnez une carte…";
    private const string PreviewPlaceholder = "Aucune image";

    [Header("References")]
    [SerializeField] private UIDocument uiDocument;
    [SerializeField, FormerlySerializedAs("_dataService")] private ScenarioDataService dataService;
    [SerializeField, FormerlySerializedAs("_cardTemplate")] private VisualTreeAsset cardTemplate;
    [SerializeField, FormerlySerializedAs("_scenarioLoader")] private ScenarioLoader scenarioLoader;

    [Header("Grid View settings")]
    [SerializeField, FormerlySerializedAs("_columns")] private int columns = 4;
    [SerializeField, FormerlySerializedAs("_cardSpacingPercent")] private float cardSpacingPercent = 0.2f;

    [Header("Debug")]
    [SerializeField, FormerlySerializedAs("_logEvents")] private bool logEvents = true;

    private ScenarioListView _listView;
    private ScenarioDetailsView _detailsView;

    private VisualElement _browserView;
    private VisualElement _editorView;
    private readonly VisualElement[] _stepContents = new VisualElement[3];
    private readonly VisualElement[] _stepHeaders = new VisualElement[3];

    private Button _newButton;
    private Button _backButton;
    private Button _cancelButton;
    private Button _previousButton;
    private Button _nextButton;
    private Button _saveButton;
    private Button _addTagButton;

    private Label _editorTitle;
    private Label _editorSubtitle;
    private Label _editorFeedback;
    private Label _nameCounter;
    private Label _descriptionCounter;
    private Label _mapName;
    private Image _environmentImage;
    private Label _environmentPlaceholder;

    private TextField _nameField;
    private TextField _descriptionField;
    private TextField _tagInputField;
    private DropdownField _mapDropdown;
    private DropdownField _previewDropdown;
    private DropdownField _tagsDropdown;
    private VisualElement _selectedTagsContainer;

    private DropdownField _robotTypeDropdown;
    private DropdownField _robotBehaviorDropdown;
    private FloatField _robotSpeedField;
    private FloatField _durationField;
    private FloatField _startXField;
    private FloatField _startZField;
    private FloatField _startYawField;
    private FloatField _goalXField;
    private FloatField _goalZField;

    private IntegerField _humanCountField;
    private FloatField _humanSpeedField;
    private DropdownField _humanBehaviorDropdown;
    private DropdownField _movementControllerDropdown;
    private FloatField _humanSpawnXField;
    private FloatField _humanSpawnZField;
    private FloatField _humanGoalXField;
    private FloatField _humanGoalZField;

    private Label _summaryName;
    private Label _summaryType;
    private Label _summaryEnvironment;
    private Label _summaryAgents;
    private Label _summaryObjectives;
    private Label _summaryDuration;
    private Label _pedestrianCount;
    private Label _totalAgentCount;
    private Label _summaryRobotConfig;
    private Label _summaryRobotRoute;
    private Label _summaryHumanConfig;
    private Label _summaryHumanRoute;
    private Label _summaryDescription;
    private Label _validationLabel;
    private VisualElement _validationStatusIcon;
    private Label _checkInformation;
    private Label _checkEnvironment;
    private Label _checkRoute;
    private Label _checkHumans;

    private readonly HashSet<string> _selectedTags = new(StringComparer.OrdinalIgnoreCase);
    private bool _initialized;
    private int _currentStep = 1;
    private string _editingScenarioId;
    private ScenarioData _editingScenario;

    private void Awake()
    {
        dataService ??= FindFirstObjectByType<ScenarioDataService>();
        scenarioLoader ??= FindFirstObjectByType<ScenarioLoader>();
        MainViewController.OnViewLoaded += OnViewLoaded;
    }

    private void OnDestroy()
    {
        MainViewController.OnViewLoaded -= OnViewLoaded;
        _listView?.Dispose();

        if (_newButton != null) _newButton.clicked -= OpenNewScenario;
        if (_backButton != null) _backButton.clicked -= ShowBrowser;
        if (_cancelButton != null) _cancelButton.clicked -= ShowBrowser;
        if (_previousButton != null) _previousButton.clicked -= GoToPreviousStep;
        if (_nextButton != null) _nextButton.clicked -= GoToNextStep;
        if (_saveButton != null) _saveButton.clicked -= SaveScenario;
        if (_addTagButton != null) _addTagButton.clicked -= AddCustomTag;
    }

    private void OnEnable()
    {
        if (_initialized && _editorView.ClassListContains(HiddenClass))
            _listView?.Refresh();
    }

    private void OnViewLoaded(string viewName)
    {
        if (viewName == "Scenarios")
            Initialize();
    }

    private void Initialize()
    {
        if (_initialized)
        {
            _listView?.Refresh();
            return;
        }

        uiDocument ??= GetComponent<UIDocument>();
        if (uiDocument == null)
        {
            Debug.LogError("[ScenariosTabController] UIDocument is missing.");
            return;
        }

        VisualElement root = uiDocument.rootVisualElement;
        var missing = new List<string>();

        _browserView = Require<VisualElement>(root, "ScenarioBrowserView", missing);
        _editorView = Require<VisualElement>(root, "ScenarioEditorView", missing);
        _stepContents[0] = Require<VisualElement>(root, "Step1Content", missing);
        _stepContents[1] = Require<VisualElement>(root, "Step2Content", missing);
        _stepContents[2] = Require<VisualElement>(root, "Step3Content", missing);
        _stepHeaders[0] = Require<VisualElement>(root, "Step1", missing);
        _stepHeaders[1] = Require<VisualElement>(root, "Step2", missing);
        _stepHeaders[2] = Require<VisualElement>(root, "Step3", missing);

        _newButton = Require<Button>(root, "NewScenarioButton", missing);
        _backButton = Require<Button>(root, "BackToScenariosButton", missing);
        _cancelButton = Require<Button>(root, "CancelEditorButton", missing);
        _previousButton = Require<Button>(root, "PreviousStepButton", missing);
        _nextButton = Require<Button>(root, "NextStepButton", missing);
        _saveButton = Require<Button>(root, "SaveEditorButton", missing);
        _addTagButton = Require<Button>(root, "AddTagButton", missing);

        _editorTitle = Require<Label>(root, "EditorTitle", missing);
        _editorSubtitle = Require<Label>(root, "EditorSubtitle", missing);
        _editorFeedback = Require<Label>(root, "EditorFeedbackLabel", missing);
        _nameCounter = Require<Label>(root, "NameCounter", missing);
        _descriptionCounter = Require<Label>(root, "DescriptionCounter", missing);
        _mapName = Require<Label>(root, "MapName", missing);
        _environmentImage = Require<Image>(root, "EnvironmentImage", missing);
        _environmentPlaceholder = Require<Label>(root, "EnvironmentPlaceholder", missing);

        _nameField = Require<TextField>(root, "NameField", missing);
        _descriptionField = Require<TextField>(root, "DescriptionField", missing);
        _tagInputField = Require<TextField>(root, "TagInputField", missing);
        _mapDropdown = Require<DropdownField>(root, "MapDropdown", missing);
        _previewDropdown = Require<DropdownField>(root, "PreviewDropdown", missing);
        _tagsDropdown = Require<DropdownField>(root, "TagsDropdown", missing);
        _selectedTagsContainer = Require<VisualElement>(root, "SelectedTags", missing);

        _robotTypeDropdown = Require<DropdownField>(root, "RobotTypeDropdown", missing);
        _robotBehaviorDropdown = Require<DropdownField>(root, "RobotBehaviorDropdown", missing);
        _robotSpeedField = Require<FloatField>(root, "RobotSpeedField", missing);
        _durationField = Require<FloatField>(root, "DurationField", missing);
        _startXField = Require<FloatField>(root, "StartXField", missing);
        _startZField = Require<FloatField>(root, "StartZField", missing);
        _startYawField = Require<FloatField>(root, "StartYawField", missing);
        _goalXField = Require<FloatField>(root, "GoalXField", missing);
        _goalZField = Require<FloatField>(root, "GoalZField", missing);

        _humanCountField = Require<IntegerField>(root, "HumanCountField", missing);
        _humanSpeedField = Require<FloatField>(root, "HumanSpeedField", missing);
        _humanBehaviorDropdown = Require<DropdownField>(root, "HumanBehaviorDropdown", missing);
        _movementControllerDropdown = Require<DropdownField>(root, "MovementControllerDropdown", missing);
        _humanSpawnXField = Require<FloatField>(root, "HumanSpawnXField", missing);
        _humanSpawnZField = Require<FloatField>(root, "HumanSpawnZField", missing);
        _humanGoalXField = Require<FloatField>(root, "HumanGoalXField", missing);
        _humanGoalZField = Require<FloatField>(root, "HumanGoalZField", missing);

        _summaryName = Require<Label>(root, "SummaryName", missing);
        _summaryType = Require<Label>(root, "SummaryType", missing);
        _summaryEnvironment = Require<Label>(root, "SummaryEnvironment", missing);
        _summaryAgents = Require<Label>(root, "SummaryAgents", missing);
        _summaryObjectives = Require<Label>(root, "SummaryObjectives", missing);
        _summaryDuration = Require<Label>(root, "SummaryDuration", missing);
        _pedestrianCount = Require<Label>(root, "PedestrianCount", missing);
        _totalAgentCount = Require<Label>(root, "TotalAgentCount", missing);
        _summaryRobotConfig = Require<Label>(root, "SummaryRobotConfig", missing);
        _summaryRobotRoute = Require<Label>(root, "SummaryRobotRoute", missing);
        _summaryHumanConfig = Require<Label>(root, "SummaryHumanConfig", missing);
        _summaryHumanRoute = Require<Label>(root, "SummaryHumanRoute", missing);
        _summaryDescription = Require<Label>(root, "SummaryDescription", missing);
        _validationLabel = Require<Label>(root, "ValidationLabel", missing);
        _validationStatusIcon = Require<VisualElement>(root, "ValidationStatusIcon", missing);
        _checkInformation = Require<Label>(root, "CheckInformation", missing);
        _checkEnvironment = Require<Label>(root, "CheckEnvironment", missing);
        _checkRoute = Require<Label>(root, "CheckRoute", missing);
        _checkHumans = Require<Label>(root, "CheckHumans", missing);

        if (missing.Count > 0)
        {
            Debug.LogError($"[ScenariosTabController] Missing UI elements: {string.Join(", ", missing)}");
            return;
        }

        dataService?.EnsureLoaded();
        BuildBrowser(root);
        ConfigureEditor();
        RegisterEvents();
        ShowBrowser();
        _initialized = true;
        Log("Scenario UI initialized.");
    }

    private static T Require<T>(VisualElement root, string name, ICollection<string> missing) where T : VisualElement
    {
        T element = root.Q<T>(name);
        if (element == null)
            missing.Add(name);
        return element;
    }

    private void BuildBrowser(VisualElement root)
    {
        VisualElement browserContainer = root.Q<VisualElement>("ScenarioBrowser");
        if (browserContainer == null)
        {
            Debug.LogError("[ScenariosTabController] ScenarioBrowser container is missing.");
            return;
        }

        browserContainer.Clear();
        _listView = new ScenarioListView(cardTemplate, columns, cardSpacingPercent);
        _listView.Initialize(dataService);
        _listView.OnScenarioSelected += OnScenarioSelected;
        browserContainer.Add(_listView);

        VisualElement detailsContainer = root.Q<VisualElement>("ScenarioDetailsContainer");
        if (detailsContainer == null)
        {
            Debug.LogError("[ScenariosTabController] ScenarioDetailsContainer is missing.");
            return;
        }

        detailsContainer.Clear();
        _detailsView = new ScenarioDetailsView(dataService) { LogEvents = false };
        detailsContainer.Add(_detailsView);

        var actions = new VisualElement();
        actions.AddToClassList("scenario-details-actions");

        var editButton = new Button(OpenSelectedScenario) { text = "Modifier" };
        editButton.AddToClassList("action-button");
        editButton.AddToClassList("secondary");

        var useButton = new Button(UseSelectedScenario) { text = "Utiliser le scénario" };
        useButton.AddToClassList("action-button");
        useButton.AddToClassList("primary");

        actions.Add(editButton);
        actions.Add(useButton);
        detailsContainer.Add(actions);
    }

    private void ConfigureEditor()
    {
        RefreshChoiceOptions();

        _nameField.RegisterValueChangedCallback(evt =>
        {
            _nameCounter.text = $"{evt.newValue?.Length ?? 0}/60";
            ClearFeedback();
        });
        _descriptionField.RegisterValueChangedCallback(evt =>
            _descriptionCounter.text = $"{evt.newValue?.Length ?? 0}/300");

        _tagsDropdown.RegisterValueChangedCallback(evt =>
        {
            if (!string.IsNullOrWhiteSpace(evt.newValue) && evt.newValue != TagPlaceholder)
                AddTag(evt.newValue);
            _tagsDropdown.SetValueWithoutNotify(TagPlaceholder);
        });
        _tagInputField.RegisterCallback<KeyDownEvent>(evt =>
        {
            if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter)
                return;
            AddCustomTag();
            evt.StopPropagation();
        });

        _mapDropdown.RegisterValueChangedCallback(_ =>
        {
            _mapName.text = IsPlaceholder(_mapDropdown.value, MapPlaceholder) ? "—" : _mapDropdown.value;
            ClearFeedback();
            UpdateEnvironmentPreview();
        });
        _previewDropdown.RegisterValueChangedCallback(_ => UpdateEnvironmentPreview());
    }

    private void RegisterEvents()
    {
        _newButton.clicked += OpenNewScenario;
        _backButton.clicked += ShowBrowser;
        _cancelButton.clicked += ShowBrowser;
        _previousButton.clicked += GoToPreviousStep;
        _nextButton.clicked += GoToNextStep;
        _saveButton.clicked += SaveScenario;
        _addTagButton.clicked += AddCustomTag;
    }

    private void RefreshChoiceOptions()
    {
        var maps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (scenarioLoader != null)
        {
            foreach (string map in scenarioLoader.GetAvailableMaps())
                if (!string.IsNullOrWhiteSpace(map)) maps.Add(map);
        }
        if (dataService != null)
        {
            foreach (ScenarioInfo info in dataService.AllScenarios.Values)
                if (!string.IsNullOrWhiteSpace(info?.MapImage)) maps.Add(info.MapImage);
        }
        _mapDropdown.choices = new[] { MapPlaceholder }.Concat(maps.OrderBy(x => x)).ToList();

        var previews = Resources.LoadAll<Texture2D>("ScenarioPreviews")
            .Select(texture => texture.name + ".png")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name)
            .ToList();
        _previewDropdown.choices = new[] { PreviewPlaceholder }.Concat(previews).ToList();

        var knownTags = dataService?.GetAllTags()
            .Where(tag => !string.Equals(tag, "All", StringComparison.OrdinalIgnoreCase))
            .OrderBy(tag => tag)
            .ToList() ?? new List<string>();
        _tagsDropdown.choices = new[] { TagPlaceholder }.Concat(knownTags).ToList();

        var robotTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "TurtleBot4" };
        if (dataService != null)
        {
            foreach (ScenarioInfo info in dataService.AllScenarios.Values)
                if (!string.IsNullOrWhiteSpace(info?.RobotType)) robotTypes.Add(info.RobotType);
        }
        _robotTypeDropdown.choices = robotTypes.OrderBy(type => type).ToList();
        _robotBehaviorDropdown.choices = new List<string> { "normal", "cautious", "aggressive", "attentive" };
        _humanBehaviorDropdown.choices = new List<string> { "normal", "cautious", "aggressive", "attentive" };
        _movementControllerDropdown.choices = new List<string> { "SFM", "ONNXPrediction", "Hybrid" };
    }

    private void OpenNewScenario()
    {
        _editingScenarioId = null;
        _editingScenario = null;
        ResetForm();
        ShowEditor();
    }

    private void OpenSelectedScenario()
    {
        string scenarioId = _listView?.SelectedScenarioId;
        if (string.IsNullOrWhiteSpace(scenarioId))
        {
            Debug.LogWarning("[ScenariosTabController] Select a scenario before editing it.");
            return;
        }

        _editingScenarioId = scenarioId;
        _editingScenario = scenarioLoader?.LoadScenario(scenarioId);
        ResetForm();
        FillForm(_editingScenario, _listView.SelectedScenarioInfo);
        ShowEditor();
    }

    private void ShowEditor()
    {
        _browserView.AddToClassList(HiddenClass);
        _editorView.RemoveFromClassList(HiddenClass);
        _editorTitle.text = string.IsNullOrEmpty(_editingScenarioId) ? "Nouveau scénario" : "Modifier le scénario";
        _saveButton.text = string.IsNullOrEmpty(_editingScenarioId)
            ? "Enregistrer le scénario"
            : "Enregistrer les modifications";
        SetStep(1);
    }

    private void ShowBrowser()
    {
        if (_editorView != null) _editorView.AddToClassList(HiddenClass);
        if (_browserView != null) _browserView.RemoveFromClassList(HiddenClass);
        ClearFeedback();
        _listView?.Refresh();
    }

    private void ResetForm()
    {
        RefreshChoiceOptions();
        _selectedTags.Clear();
        RebuildTagChips();

        _nameField.SetValueWithoutNotify(string.Empty);
        _descriptionField.SetValueWithoutNotify(string.Empty);
        _tagInputField.SetValueWithoutNotify(string.Empty);
        _mapDropdown.SetValueWithoutNotify(MapPlaceholder);
        _previewDropdown.SetValueWithoutNotify(PreviewPlaceholder);
        _tagsDropdown.SetValueWithoutNotify(TagPlaceholder);
        _robotTypeDropdown.SetValueWithoutNotify(_robotTypeDropdown.choices.FirstOrDefault() ?? "TurtleBot4");
        _robotBehaviorDropdown.SetValueWithoutNotify("normal");
        _humanBehaviorDropdown.SetValueWithoutNotify("normal");
        _movementControllerDropdown.SetValueWithoutNotify("SFM");

        _robotSpeedField.SetValueWithoutNotify(1.2f);
        _durationField.SetValueWithoutNotify(0f);
        _startXField.SetValueWithoutNotify(0f);
        _startZField.SetValueWithoutNotify(0f);
        _startYawField.SetValueWithoutNotify(0f);
        _goalXField.SetValueWithoutNotify(2f);
        _goalZField.SetValueWithoutNotify(0f);
        _humanCountField.SetValueWithoutNotify(0);
        _humanSpeedField.SetValueWithoutNotify(1f);
        _humanSpawnXField.SetValueWithoutNotify(2f);
        _humanSpawnZField.SetValueWithoutNotify(0f);
        _humanGoalXField.SetValueWithoutNotify(0f);
        _humanGoalZField.SetValueWithoutNotify(0f);

        _nameCounter.text = "0/60";
        _descriptionCounter.text = "0/300";
        _mapName.text = "—";
        ClearFeedback();
        UpdateEnvironmentPreview();
    }

    private void FillForm(ScenarioData scenario, ScenarioInfo fallbackInfo)
    {
        ScenarioInfo info = scenario?.Info ?? fallbackInfo;
        if (info != null)
        {
            _nameField.SetValueWithoutNotify(info.Name ?? string.Empty);
            _descriptionField.SetValueWithoutNotify(info.Description ?? string.Empty);
            _nameCounter.text = $"{_nameField.value.Length}/60";
            _descriptionCounter.text = $"{_descriptionField.value.Length}/300";

            EnsureChoice(_mapDropdown, info.MapImage, MapPlaceholder);
            EnsureChoice(_previewDropdown, info.PreviewImage, PreviewPlaceholder);
            EnsureChoice(_robotTypeDropdown, info.RobotType, "TurtleBot4");
            _durationField.SetValueWithoutNotify(Mathf.Max(0f, info.Duration));

            _selectedTags.Clear();
            if (info.Tags != null)
                foreach (string tag in info.Tags)
                    if (!string.IsNullOrWhiteSpace(tag)) _selectedTags.Add(tag.Trim());
            RebuildTagChips();
        }

        if (scenario?.Robot != null)
        {
            EnsureChoice(_robotBehaviorDropdown, scenario.Robot.Behavior, "normal");
            _robotSpeedField.SetValueWithoutNotify(Mathf.Max(0.01f, scenario.Robot.Speed));

            if (TryGetReferencePosition(scenario, scenario.Robot.StartRef, out Vector3 start))
            {
                _startXField.SetValueWithoutNotify(start.x);
                _startZField.SetValueWithoutNotify(start.z);
                if (scenario.Points.TryGetValue(scenario.Robot.StartRef, out RefPoint startPoint))
                    _startYawField.SetValueWithoutNotify(startPoint.Yaw ?? 0f);
            }
            if (TryGetReferencePosition(scenario, scenario.Robot.GoalRef, out Vector3 goal))
            {
                _goalXField.SetValueWithoutNotify(goal.x);
                _goalZField.SetValueWithoutNotify(goal.z);
            }
        }

        HumanScenarioConfig human = scenario?.Humans?.FirstOrDefault();
        if (human != null)
        {
            _humanCountField.SetValueWithoutNotify(Mathf.Max(0, human.Count));
            _humanSpeedField.SetValueWithoutNotify(Mathf.Max(0.01f, human.Speed));
            EnsureChoice(_humanBehaviorDropdown, human.Behavior, "normal");
            EnsureChoice(_movementControllerDropdown, human.MovementController?.Type, "SFM");

            Vector3 spawn = ResolveSpawnPosition(scenario, human.Spawn);
            Vector3 goal = ResolveGoalPosition(scenario, human.Goal);
            _humanSpawnXField.SetValueWithoutNotify(spawn.x);
            _humanSpawnZField.SetValueWithoutNotify(spawn.z);
            _humanGoalXField.SetValueWithoutNotify(goal.x);
            _humanGoalZField.SetValueWithoutNotify(goal.z);
        }

        _mapName.text = IsPlaceholder(_mapDropdown.value, MapPlaceholder) ? "—" : _mapDropdown.value;
        UpdateEnvironmentPreview();
    }

    private static void EnsureChoice(DropdownField dropdown, string value, string fallback)
    {
        string selected = string.IsNullOrWhiteSpace(value) ? fallback : value;
        if (!dropdown.choices.Contains(selected))
        {
            var choices = dropdown.choices.ToList();
            choices.Add(selected);
            dropdown.choices = choices;
        }
        dropdown.SetValueWithoutNotify(selected);
    }

    private void GoToNextStep()
    {
        if (_currentStep == 1 && !ValidateStepOne(true))
            return;
        if (_currentStep == 2 && !ValidateStepTwo(true))
            return;
        if (_currentStep < 3)
            SetStep(_currentStep + 1);
    }

    private void GoToPreviousStep()
    {
        if (_currentStep > 1)
            SetStep(_currentStep - 1);
    }

    private void SetStep(int step)
    {
        _currentStep = Mathf.Clamp(step, 1, 3);
        ClearFeedback();

        for (int index = 0; index < 3; index++)
        {
            bool active = index == _currentStep - 1;
            SetVisible(_stepContents[index], active, StepHiddenClass);
            _stepHeaders[index].EnableInClassList("active", active);
            _stepHeaders[index].EnableInClassList("completed", index < _currentStep - 1);
        }

        SetVisible(_previousButton, _currentStep > 1, StepHiddenClass);
        SetVisible(_nextButton, _currentStep < 3, StepHiddenClass);
        SetVisible(_saveButton, _currentStep == 3, StepHiddenClass);

        _editorSubtitle.text = _currentStep switch
        {
            1 => "Définissez les informations et l'environnement du scénario.",
            2 => "Configurez le robot, sa trajectoire et les agents humains.",
            _ => "Vérifiez les paramètres avant l'enregistrement."
        };

        if (_currentStep == 3)
            UpdateValidationSummary();
    }

    private static void SetVisible(VisualElement element, bool visible, string hiddenClass)
    {
        element.EnableInClassList(hiddenClass, !visible);
    }

    private bool ValidateStepOne(bool showFeedback)
    {
        string message = null;
        if (string.IsNullOrWhiteSpace(_nameField.value))
            message = "Le nom du scénario est obligatoire.";
        else if (IsPlaceholder(_mapDropdown.value, MapPlaceholder))
            message = "Choisissez un environnement avant de continuer.";

        if (showFeedback)
            SetFeedback(message);
        return message == null;
    }

    private bool ValidateStepTwo(bool showFeedback)
    {
        string message = null;
        if (_robotSpeedField.value <= 0f)
            message = "La vitesse du robot doit être supérieure à zéro.";
        else if (_durationField.value < 0f)
            message = "La durée ne peut pas être négative.";
        else if (SamePoint(_startXField.value, _startZField.value, _goalXField.value, _goalZField.value))
            message = "Le départ et l'objectif du robot doivent être différents.";
        else if (_humanCountField.value < 0)
            message = "Le nombre de piétons ne peut pas être négatif.";
        else if (_humanCountField.value > 0 && _humanSpeedField.value <= 0f)
            message = "La vitesse des piétons doit être supérieure à zéro.";
        else if (_humanCountField.value > 0 &&
                 SamePoint(_humanSpawnXField.value, _humanSpawnZField.value, _humanGoalXField.value, _humanGoalZField.value))
            message = "Le départ et l'objectif des piétons doivent être différents.";

        if (showFeedback)
            SetFeedback(message);
        return message == null;
    }

    private static bool SamePoint(float ax, float az, float bx, float bz)
    {
        return Mathf.Approximately(ax, bx) && Mathf.Approximately(az, bz);
    }

    private void UpdateValidationSummary()
    {
        bool infoValid = ValidateStepOne(false);
        bool routeValid = _robotSpeedField.value > 0f &&
                          _durationField.value >= 0f &&
                          !SamePoint(_startXField.value, _startZField.value, _goalXField.value, _goalZField.value);
        bool humansValid = _humanCountField.value >= 0 &&
                           (_humanCountField.value == 0 ||
                            (_humanSpeedField.value > 0f &&
                             !SamePoint(_humanSpawnXField.value, _humanSpawnZField.value, _humanGoalXField.value, _humanGoalZField.value)));
        bool environmentValid = !IsPlaceholder(_mapDropdown.value, MapPlaceholder);
        bool allValid = infoValid && routeValid && humansValid && environmentValid;

        int preservedHumans = _editingScenario?.Humans?
            .Skip(1)
            .Where(human => human != null)
            .Sum(human => Mathf.Max(0, human.Count)) ?? 0;
        int humanTotal = Mathf.Max(0, _humanCountField.value) + preservedHumans;
        int totalAgents = humanTotal + 1;

        _summaryName.text = string.IsNullOrWhiteSpace(_nameField.value) ? "Non défini" : _nameField.value.Trim();
        _summaryType.text = string.IsNullOrWhiteSpace(_editingScenario?.Info?.Type) ? "Personnalisé" : _editingScenario.Info.Type;
        _summaryEnvironment.text = environmentValid ? _mapDropdown.value : "Non défini";
        _summaryAgents.text = totalAgents.ToString();
        _summaryObjectives.text = (humanTotal > 0 ? 2 : 1).ToString();
        _summaryDuration.text = _durationField.value > 0f ? $"{_durationField.value:0.#} s" : "Illimitée";
        _pedestrianCount.text = humanTotal.ToString();
        _totalAgentCount.text = totalAgents.ToString();

        _summaryRobotConfig.text = $"{_robotTypeDropdown.value} · {_robotBehaviorDropdown.value} · {_robotSpeedField.value:0.##} m/s";
        _summaryRobotRoute.text =
            $"({_startXField.value:0.##}, {_startZField.value:0.##}) → ({_goalXField.value:0.##}, {_goalZField.value:0.##}), orientation {_startYawField.value:0.#}°";
        _summaryHumanConfig.text = humanTotal == 0
            ? "Aucun piéton"
            : $"{humanTotal} piéton(s) · {_humanBehaviorDropdown.value} · {_movementControllerDropdown.value}";
        _summaryHumanRoute.text = humanTotal == 0
            ? "Aucune trajectoire humaine"
            : $"({_humanSpawnXField.value:0.##}, {_humanSpawnZField.value:0.##}) → ({_humanGoalXField.value:0.##}, {_humanGoalZField.value:0.##})";
        _summaryDescription.text = string.IsNullOrWhiteSpace(_descriptionField.value)
            ? "Aucune description."
            : _descriptionField.value.Trim();

        SetCheck(_checkInformation, infoValid, "Informations générales complètes", "Nom obligatoire");
        SetCheck(_checkEnvironment, environmentValid, "Environnement sélectionné", "Environnement manquant");
        SetCheck(_checkRoute, routeValid, "Trajectoire du robot valide", "Trajectoire du robot invalide");
        SetCheck(_checkHumans, humansValid, "Configuration des piétons valide", "Configuration des piétons invalide");

        _validationStatusIcon.EnableInClassList(ErrorClass, !allValid);
        _validationLabel.EnableInClassList(ErrorClass, !allValid);
        _validationLabel.text = allValid
            ? "Le scénario est valide et prêt à être enregistré."
            : "Corrigez les éléments signalés avant l'enregistrement.";
        _saveButton.SetEnabled(allValid);
    }

    private static void SetCheck(Label label, bool valid, string validText, string invalidText)
    {
        label.EnableInClassList(ErrorClass, !valid);
        label.text = valid ? $"✓ {validText}" : $"× {invalidText}";
    }

    private void SaveScenario()
    {
        if (!ValidateStepOne(true) || !ValidateStepTwo(true))
        {
            UpdateValidationSummary();
            return;
        }
        if (scenarioLoader == null)
        {
            SetFeedback("Le chargeur de scénarios n'est pas disponible.");
            return;
        }

        ScenarioData scenario = BuildScenario();
        if (!scenario.IsValid(out string validationError))
        {
            SetFeedback($"Le scénario n'est pas valide : {validationError}");
            return;
        }

        string fileId = string.IsNullOrWhiteSpace(_editingScenarioId)
            ? GetUniqueFileId(ToFileId(_nameField.value))
            : _editingScenarioId;
        if (!scenarioLoader.ExportScenario(scenario, fileId + ".yaml"))
        {
            SetFeedback("Échec de l'enregistrement YAML. Consultez la Console.");
            return;
        }

        scenarioLoader.ClearScenarioCache();
        dataService?.ReloadAllScenarioInfos();
        Log($"Scenario '{scenario.Info.Name}' saved as '{fileId}.yaml'.");
        _editingScenarioId = null;
        _editingScenario = null;
        ShowBrowser();
    }

    private ScenarioData BuildScenario()
    {
        ScenarioData scenario = _editingScenario ?? new ScenarioData();
        scenario.Info ??= new ScenarioInfo();
        scenario.Points ??= new Dictionary<string, RefPoint>();
        scenario.Robot ??= new RobotScenarioConfig();
        scenario.Humans ??= new List<HumanScenarioConfig>();

        scenario.Info.Name = _nameField.value.Trim();
        scenario.Info.Type = string.IsNullOrWhiteSpace(scenario.Info.Type) ? "Custom" : scenario.Info.Type;
        scenario.Info.Description = _descriptionField.value?.Trim() ?? string.Empty;
        scenario.Info.Version = string.IsNullOrWhiteSpace(scenario.Info.Version) ? "1.0" : scenario.Info.Version;
        scenario.Info.Author = string.IsNullOrWhiteSpace(scenario.Info.Author) ? "RobotSNAP" : scenario.Info.Author;
        scenario.Info.Created = string.IsNullOrWhiteSpace(scenario.Info.Created)
            ? DateTime.Now.ToString("yyyy-MM-dd HH:mm")
            : scenario.Info.Created;
        scenario.Info.Tags = _selectedTags.OrderBy(tag => tag).ToArray();
        scenario.Info.MapImage = _mapDropdown.value;
        scenario.Info.PreviewImage = IsPlaceholder(_previewDropdown.value, PreviewPlaceholder)
            ? string.Empty
            : _previewDropdown.value;
        scenario.Info.RobotType = _robotTypeDropdown.value;
        scenario.Info.Duration = Mathf.Max(0f, _durationField.value);

        scenario.Robot.StartRef = string.IsNullOrWhiteSpace(scenario.Robot.StartRef)
            ? "robot_start"
            : scenario.Robot.StartRef;
        scenario.Robot.GoalRef = string.IsNullOrWhiteSpace(scenario.Robot.GoalRef)
            ? "robot_goal"
            : scenario.Robot.GoalRef;
        scenario.Robot.Behavior = _robotBehaviorDropdown.value;
        scenario.Robot.Speed = _robotSpeedField.value;
        scenario.Points[scenario.Robot.StartRef] = new RefPoint
        {
            X = _startXField.value,
            Y = 0f,
            Z = _startZField.value,
            Yaw = _startYawField.value
        };
        scenario.Points[scenario.Robot.GoalRef] = new RefPoint
        {
            X = _goalXField.value,
            Y = 0f,
            Z = _goalZField.value
        };

        HumanScenarioConfig firstHuman = scenario.Humans.FirstOrDefault();
        if (_humanCountField.value <= 0)
        {
            if (firstHuman != null)
                scenario.Humans.RemoveAt(0);
        }
        else
        {
            if (firstHuman == null)
            {
                firstHuman = new HumanScenarioConfig
                {
                    Id = "pedestrians",
                    Spawn = new SpawnConfig { Type = "point", Reference = "human_spawn" },
                    Goal = new GoalConfig { Type = "point", Reference = "human_goal" },
                    MovementController = new MovementControllerConfig()
                };
                scenario.Humans.Insert(0, firstHuman);
            }

            firstHuman.Id = string.IsNullOrWhiteSpace(firstHuman.Id) ? "pedestrians" : firstHuman.Id;
            firstHuman.Count = _humanCountField.value;
            firstHuman.Behavior = _humanBehaviorDropdown.value;
            firstHuman.Speed = _humanSpeedField.value;
            firstHuman.MovementController ??= new MovementControllerConfig();
            firstHuman.MovementController.Type = _movementControllerDropdown.value;
            firstHuman.Spawn ??= new SpawnConfig { Type = "point" };
            firstHuman.Goal ??= new GoalConfig { Type = "point" };
            UpdateSpawnPosition(scenario, firstHuman.Spawn, _humanSpawnXField.value, _humanSpawnZField.value);
            UpdateGoalPosition(scenario, firstHuman.Goal, _humanGoalXField.value, _humanGoalZField.value);
        }

        return scenario;
    }

    private static void UpdateSpawnPosition(ScenarioData scenario, SpawnConfig spawn, float x, float z)
    {
        if (!string.IsNullOrWhiteSpace(spawn.Reference))
        {
            UpdateReferencedPoint(scenario, spawn.Reference, x, z);
            return;
        }
        if (spawn.Position != null)
        {
            spawn.Position.X = x;
            spawn.Position.Y = 0f;
            spawn.Position.Z = z;
            return;
        }
        if (spawn.Zone != null)
        {
            UpdateRefPoint(spawn.Zone, x, z);
            return;
        }

        spawn.Type = "point";
        spawn.Reference = "human_spawn";
        UpdateReferencedPoint(scenario, spawn.Reference, x, z);
    }

    private static void UpdateGoalPosition(ScenarioData scenario, GoalConfig goal, float x, float z)
    {
        if (!string.IsNullOrWhiteSpace(goal.Reference))
        {
            UpdateReferencedPoint(scenario, goal.Reference, x, z);
            return;
        }
        if (goal.Position != null)
        {
            goal.Position.X = x;
            goal.Position.Y = 0f;
            goal.Position.Z = z;
            return;
        }
        if (goal.Zone != null)
        {
            UpdateRefPoint(goal.Zone, x, z);
            return;
        }

        goal.Type = "point";
        goal.Reference = "human_goal";
        UpdateReferencedPoint(scenario, goal.Reference, x, z);
    }

    private static void UpdateReferencedPoint(ScenarioData scenario, string reference, float x, float z)
    {
        if (!scenario.Points.TryGetValue(reference, out RefPoint point) || point == null)
        {
            scenario.Points[reference] = new RefPoint { X = x, Y = 0f, Z = z };
            return;
        }
        UpdateRefPoint(point, x, z);
    }

    private static void UpdateRefPoint(RefPoint point, float x, float z)
    {
        if (point.IsBounds)
        {
            point.Center.X = x;
            point.Center.Y = 0f;
            point.Center.Z = z;
            return;
        }
        point.X = x;
        point.Y = 0f;
        point.Z = z;
    }

    private static bool TryGetReferencePosition(ScenarioData scenario, string reference, out Vector3 position)
    {
        position = Vector3.zero;
        if (scenario?.Points == null || string.IsNullOrWhiteSpace(reference) ||
            !scenario.Points.TryGetValue(reference, out RefPoint point) || point == null)
            return false;
        position = point.ToVector3();
        return true;
    }

    private static Vector3 ResolveSpawnPosition(ScenarioData scenario, SpawnConfig spawn)
    {
        if (spawn == null) return Vector3.zero;
        if (TryGetReferencePosition(scenario, spawn.Reference, out Vector3 referenced)) return referenced;
        if (spawn.Position != null) return spawn.Position.ToVector3();
        if (spawn.Zone != null) return spawn.Zone.ToVector3();
        return Vector3.zero;
    }

    private static Vector3 ResolveGoalPosition(ScenarioData scenario, GoalConfig goal)
    {
        if (goal == null) return Vector3.zero;
        if (TryGetReferencePosition(scenario, goal.Reference, out Vector3 referenced)) return referenced;
        if (goal.Position != null) return goal.Position.ToVector3();
        if (goal.Zone != null) return goal.Zone.ToVector3();
        return Vector3.zero;
    }

    private void AddCustomTag()
    {
        string tag = _tagInputField.value?.Trim();
        if (string.IsNullOrWhiteSpace(tag))
            return;
        AddTag(tag);
        _tagInputField.SetValueWithoutNotify(string.Empty);
    }

    private void AddTag(string tag)
    {
        if (_selectedTags.Add(tag.Trim()))
            RebuildTagChips();
    }

    private void RebuildTagChips()
    {
        _selectedTagsContainer.Clear();
        foreach (string tag in _selectedTags.OrderBy(value => value))
        {
            string capturedTag = tag;
            var chip = new Button(() =>
            {
                _selectedTags.Remove(capturedTag);
                RebuildTagChips();
            })
            {
                text = capturedTag + "  ×",
                tooltip = "Retirer ce tag"
            };
            chip.AddToClassList("selected-tag");
            _selectedTagsContainer.Add(chip);
        }
    }

    private void UpdateEnvironmentPreview()
    {
        string previewName = IsPlaceholder(_previewDropdown.value, PreviewPlaceholder)
            ? null
            : Path.GetFileNameWithoutExtension(_previewDropdown.value);
        Texture2D texture = string.IsNullOrWhiteSpace(previewName)
            ? Resources.Load<Texture2D>("ScenarioPreviews/default")
            : Resources.Load<Texture2D>($"ScenarioPreviews/{previewName}");

        _environmentImage.image = texture;
        _environmentPlaceholder.EnableInClassList(StepHiddenClass, texture != null);
    }

    private void OnScenarioSelected(string scenarioId)
    {
        _detailsView?.ShowScenario(dataService?.GetScenarioInfo(scenarioId));
        Log($"Scenario selected: {scenarioId}");
    }

    private void UseSelectedScenario()
    {
        if (!string.IsNullOrWhiteSpace(_listView?.SelectedScenarioId))
            dataService?.LoadScenario(_listView.SelectedScenarioId);
    }

    private void SetFeedback(string message)
    {
        _editorFeedback.text = message ?? string.Empty;
    }

    private void ClearFeedback()
    {
        if (_editorFeedback != null)
            _editorFeedback.text = string.Empty;
    }

    private string GetUniqueFileId(string requestedId)
    {
        string baseId = string.IsNullOrWhiteSpace(requestedId) ? "scenario" : requestedId;
        string candidate = baseId;
        int suffix = 2;
        while (scenarioLoader.ScenarioExists(candidate))
            candidate = $"{baseId}_{suffix++}";
        return candidate;
    }

    private static string ToFileId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "scenario";
        char[] chars = value.Trim().ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '_')
            .ToArray();
        return new string(chars).Trim('_');
    }

    private static bool IsPlaceholder(string value, string placeholder)
    {
        return string.IsNullOrWhiteSpace(value) || string.Equals(value, placeholder, StringComparison.Ordinal);
    }

    private void Log(string message)
    {
        if (logEvents)
            Debug.Log($"[ScenariosTabController] {message}");
    }
}
