using System;
using System.Collections.Generic;
using System.Linq;
using RobotSNAP.Core.Scenario;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>Drives the scenario browser and its guided creator.</summary>
public class ScenariosTabController : MonoBehaviour
{
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private ScenarioDataService dataService;
    [SerializeField] private VisualTreeAsset cardTemplate;
    [SerializeField] private ScenarioLoader scenarioLoader;
    [SerializeField] private int columns = 4;
    [SerializeField] private float cardSpacingPercent = 0.2f;

    private ScenarioListView _listView;
    private ScenarioDetailsView _detailsView;
    private VisualElement _browser, _creator;
    private TextField _name, _description, _tags;
    private DropdownField _map, _preview, _behavior;
    private IntegerField _humans;
    private FloatField _duration, _startX, _startZ, _startYaw, _goalX, _goalZ;
    private Label _validation, _summary;
    private bool _initialized;

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
    }

    private void OnViewLoaded(string name)
    {
        if (name == "Scenarios") Initialize();
    }

    private void Initialize()
    {
        if (_initialized) return;
        uiDocument ??= GetComponent<UIDocument>();
        if (uiDocument == null) return;
        var root = uiDocument.rootVisualElement;
        _browser = root.Q<VisualElement>("ScenarioBrowserView");
        _creator = root.Q<VisualElement>("ScenarioEditorView");
        _name = root.Q<TextField>("NameField"); _description = root.Q<TextField>("DescriptionField"); _tags = root.Q<TextField>("TagsField");
        _map = root.Q<DropdownField>("MapDropdown"); _preview = root.Q<DropdownField>("PreviewDropdown"); _behavior = root.Q<DropdownField>("BehaviorDropdown");
        _humans = root.Q<IntegerField>("HumanCountField"); _duration = root.Q<FloatField>("DurationField");
        _startX = root.Q<FloatField>("StartXField"); _startZ = root.Q<FloatField>("StartZField"); _startYaw = root.Q<FloatField>("StartYawField");
        _goalX = root.Q<FloatField>("GoalXField"); _goalZ = root.Q<FloatField>("GoalZField");
        _validation = root.Q<Label>("ValidationLabel"); _summary = root.Q<Label>("ScenarioSummaryLabel");
        if (!ValidateUiReferences()) return;
        BuildBrowser(root);
        ConfigureCreator();
        var newButton = root.Q<Button>("NewScenarioButton");
        var backButton = root.Q<Button>("BackToScenariosButton");
        var saveButton = root.Q<Button>("SaveEditorButton");
        if (newButton != null) newButton.clicked += ShowCreator;
        if (backButton != null) backButton.clicked += ShowBrowser;
        if (saveButton != null) saveButton.clicked += Save;
        ShowBrowser();
        dataService?.EnsureLoaded();
        _initialized = true;
    }

    private void BuildBrowser(VisualElement root)
    {
        var browser = root.Q<VisualElement>("ScenarioBrowser");
        if (browser != null)
        {
            browser.Clear();
            _listView = new ScenarioListView(cardTemplate, columns, cardSpacingPercent);
            _listView.Initialize(dataService);
            browser.Add(_listView);
        }
        var details = root.Q<VisualElement>("ScenarioDetailsContainer");
        if (details == null) return;
        details.Clear();
        _detailsView = new ScenarioDetailsView(dataService) { LogEvents = false };
        details.Add(_detailsView);
        var actions = new VisualElement(); actions.AddToClassList("scenario-details-actions");
        var edit = new Button(ShowSelectedForEdit) { text = "Modifier" }; edit.AddToClassList("action-button"); edit.AddToClassList("secondary");
        var use = new Button(UseSelected) { text = "Utiliser" }; use.AddToClassList("action-button"); use.AddToClassList("primary");
        actions.Add(edit); actions.Add(use); details.Add(actions);
        _listView.OnScenarioSelected += id => _detailsView.ShowScenario(dataService?.GetScenarioInfo(id));
    }

    private bool ValidateUiReferences()
    {
        bool valid = _browser != null && _creator != null && _name != null && _description != null && _tags != null &&
                     _map != null && _preview != null && _behavior != null && _humans != null && _duration != null &&
                     _startX != null && _startZ != null && _startYaw != null && _goalX != null && _goalZ != null &&
                     _validation != null && _summary != null;
        if (!valid) Debug.LogError("[ScenariosTabController] ScenariosTab.uxml is missing one or more required elements.");
        return valid;
    }

    private void ConfigureCreator()
    {
        _map.choices = new List<string> { string.Empty }.Concat(scenarioLoader?.GetAvailableMaps() ?? new List<string>()).ToList();
        _preview.choices = new List<string> { string.Empty }.Concat(Resources.LoadAll<Texture2D>("ScenarioPreviews").Select(x => x.name)).ToList();
        _behavior.choices = new List<string> { "normal", "cautious", "aggressive" };
        foreach (var field in new BaseField<float>[] { _duration, _startX, _startZ, _startYaw, _goalX, _goalZ }) field?.RegisterValueChangedCallback(_ => UpdateSummary());
        _humans.RegisterValueChangedCallback(_ => UpdateSummary());
    }

    private void ShowCreator()
    {
        _browser.AddToClassList("scenario-view-hidden");
        _creator.RemoveFromClassList("scenario-view-hidden");
        _name.value = string.Empty; _description.value = string.Empty; _tags.value = string.Empty;
        _map.value = _map.choices.FirstOrDefault(); _preview.value = _preview.choices.FirstOrDefault(); _behavior.value = "normal";
        _humans.value = 0; _duration.value = 0; _startX.value = 0; _startZ.value = 0; _startYaw.value = 0; _goalX.value = 2; _goalZ.value = 0;
        _validation.text = string.Empty; UpdateSummary();
    }

    private void ShowSelectedForEdit()
    {
        if (_listView?.SelectedScenarioInfo == null) return;
        ShowCreator();
        var info = _listView.SelectedScenarioInfo;
        _name.value = info.Name; _description.value = info.Description; _tags.value = info.Tags == null ? string.Empty : string.Join(", ", info.Tags);
        _map.value = _map.choices.Contains(info.MapImage) ? info.MapImage : string.Empty;
        _preview.value = _preview.choices.Contains(info.PreviewImage) ? info.PreviewImage : string.Empty;
        _duration.value = info.Duration;
    }

    private void ShowBrowser()
    {
        _creator.AddToClassList("scenario-view-hidden");
        _browser.RemoveFromClassList("scenario-view-hidden");
        _listView?.Refresh();
    }

    private void UpdateSummary() => _summary.text = $"{Mathf.Max(0, _humans.value)} piéton(s) · départ ({_startX.value:0.0}, {_startZ.value:0.0}) · objectif ({_goalX.value:0.0}, {_goalZ.value:0.0})";

    private void Save()
    {
        if (string.IsNullOrWhiteSpace(_name.value)) { _validation.text = "Le nom est obligatoire."; return; }
        if (string.IsNullOrWhiteSpace(_map.value)) { _validation.text = "Choisissez une carte."; return; }
        if (_humans.value < 0) { _validation.text = "Le nombre de piétons ne peut pas être négatif."; return; }
        if (Mathf.Approximately(_startX.value, _goalX.value) && Mathf.Approximately(_startZ.value, _goalZ.value)) { _validation.text = "Le départ et l’objectif doivent être différents."; return; }
        var scenario = BuildScenario();
        if (!scenarioLoader.ExportScenario(scenario, ToFileId(_name.value) + ".yaml")) { _validation.text = "Échec de l’export YAML. Consultez la Console."; return; }
        dataService.ReloadAllScenarioInfos();
        ShowBrowser();
    }

    private ScenarioData BuildScenario()
    {
        var points = new Dictionary<string, RefPoint> {
            ["robot_start"] = new RefPoint { X = _startX.value, Y = 0, Z = _startZ.value, Yaw = _startYaw.value },
            ["robot_goal"] = new RefPoint { X = _goalX.value, Y = 0, Z = _goalZ.value }
        };
        var humans = new List<HumanScenarioConfig>();
        if (_humans.value > 0) humans.Add(new HumanScenarioConfig { Id = "pedestrians", Count = _humans.value, Spawn = new SpawnConfig { Type = "point", Reference = "robot_goal" }, Goal = new GoalConfig { Type = "point", Reference = "robot_start" }, Behavior = _behavior.value, Speed = 1, MovementController = new MovementControllerConfig { Type = "SFM" } });
        return new ScenarioData { Info = new ScenarioInfo { Name = _name.value.Trim(), Description = _description.value.Trim(), Type = "Custom", Version = "1.0", Created = DateTime.Now.ToString("yyyy-MM-dd HH:mm"), Tags = _tags.value.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).Distinct().ToArray(), MapImage = _map.value, PreviewImage = _preview.value, Duration = Mathf.Max(0, _duration.value), RobotType = "TurtleBot4" }, Points = points, Robot = new RobotScenarioConfig { StartRef = "robot_start", GoalRef = "robot_goal", Behavior = _behavior.value, Speed = 1.2f }, Humans = humans };
    }

    private void UseSelected() { if (!string.IsNullOrEmpty(_listView?.SelectedScenarioId)) dataService.LoadScenario(_listView.SelectedScenarioId); }
    private static string ToFileId(string name) => new string(name.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray()).Trim('_');
}
