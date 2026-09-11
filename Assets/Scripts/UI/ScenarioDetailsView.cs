using UnityEngine;
using UnityEngine.UIElements;
using RobotSNAP.Core.Scenario;

/// <summary>
/// Affiche les détails d'un scénario sélectionné.
/// </summary>
public class ScenarioDetailsView : VisualElement
{
    // === Éléments ===
    private Label _titleLabel;
    private Label _categoryLabel;
    private Label _descriptionLabel;
    private Label _environmentLabel;
    private Label _robotLabel;
    private Label _agentsLabel;
    private Label _durationLabel;
    private VisualElement _tagsContainer;
    private Image _previewImage;

    private ScenarioInfo _currentInfo;

    // === Logs ===
    public bool LogEvents { get; set; } = true;

    public ScenarioInfo CurrentInfo => _currentInfo;

    private ScenarioDataService _dataService;

    public ScenarioDetailsView(ScenarioDataService dataService)
    {
        _dataService = dataService;
        BuildUI();
        ShowEmpty();
        Log("[ScenarioDetailsView] Constructor done.");
    }

    private void BuildUI()
    {
        AddToClassList("scenario-details");

        // Header
        var header = new VisualElement();
        header.AddToClassList("scenario-details-header");
        var titleContainer = new VisualElement();
        titleContainer.AddToClassList("scenario-details-title-container");
        _titleLabel = new Label("No scenario selected");
        _titleLabel.AddToClassList("scenario-details-title");
        _categoryLabel = new Label("");
        _categoryLabel.AddToClassList("scenario-details-category");
        titleContainer.Add(_titleLabel);
        titleContainer.Add(_categoryLabel);
        header.Add(titleContainer);
        Add(header);

        // Preview
        var previewContainer = new VisualElement();
        previewContainer.AddToClassList("scenario-details-preview");
        _previewImage = new Image { scaleMode = ScaleMode.ScaleAndCrop };
        _previewImage.AddToClassList("scenario-details-preview-image");
        previewContainer.Add(_previewImage);
        Add(previewContainer);

        // Description
        var descSection = new VisualElement();
        descSection.AddToClassList("scenario-details-section");
        var descTitle = new Label("Description");
        descTitle.AddToClassList("scenario-section-title");
        _descriptionLabel = new Label("Select a scenario to view its description.");
        _descriptionLabel.AddToClassList("scenario-description");
        descSection.Add(descTitle);
        descSection.Add(_descriptionLabel);
        Add(descSection);

        // Informations
        var infoSection = new VisualElement();
        infoSection.AddToClassList("scenario-details-section");
        var infoTitle = new Label("Information");
        infoTitle.AddToClassList("scenario-section-title");
        var infoGrid = new VisualElement();
        infoGrid.AddToClassList("scenario-info-grid");

        _environmentLabel = AddInfoItem(infoGrid, "Environment", "—");
        _robotLabel = AddInfoItem(infoGrid, "Robot", "—");
        _agentsLabel = AddInfoItem(infoGrid, "Agents", "—");
        _durationLabel = AddInfoItem(infoGrid, "Duration", "—");

        infoSection.Add(infoTitle);
        infoSection.Add(infoGrid);
        Add(infoSection);

        // Tags
        var tagsSection = new VisualElement();
        tagsSection.AddToClassList("scenario-details-section");
        var tagsTitle = new Label("Tags");
        tagsTitle.AddToClassList("scenario-section-title");
        _tagsContainer = new VisualElement();
        _tagsContainer.AddToClassList("scenario-tags");
        tagsSection.Add(tagsTitle);
        tagsSection.Add(_tagsContainer);
        Add(tagsSection);

        Log("[ScenarioDetailsView] UI structure built.");
    }

    private Label AddInfoItem(VisualElement grid, string labelText, string initialValue)
    {
        var item = new VisualElement();
        item.AddToClassList("scenario-info-item");
        var label = new Label(labelText);
        label.AddToClassList("scenario-info-label");
        var value = new Label(initialValue);
        value.AddToClassList("scenario-info-value");
        item.Add(label);
        item.Add(value);
        grid.Add(item);
        return value;
    }

    public void ShowScenario(ScenarioInfo info)
    {
        Log($"[ScenarioDetailsView] ShowScenario called with info: {(info == null ? "null" : info.Name)}");

        _currentInfo = info;

        if (info == null)
        {
            Log("[ScenarioDetailsView] info is null -> ShowEmpty");
            ShowEmpty();
            return;
        }

        _titleLabel.text = info.Name;
        _categoryLabel.text = info.Type ?? "";
        _descriptionLabel.text = info.Description ?? "No description available.";
        _environmentLabel.text = !string.IsNullOrWhiteSpace(info.MapImage)
            ? info.MapImage
            : !string.IsNullOrWhiteSpace(info.Location) ? info.Location : "Unknown environment";
        _robotLabel.text = info.RobotType ?? "Unknown robot";
        _agentsLabel.text = "Configured in YAML";
        _durationLabel.text = info.Duration > 0 ? $"{info.Duration} s" : "Unlimited";

        // Tags
        _tagsContainer.Clear();
        if (info.Tags != null)
        {
            foreach (var tag in info.Tags)
            {
                if (string.IsNullOrWhiteSpace(tag)) continue;
                var tagElement = new VisualElement();
                tagElement.AddToClassList("card-tag");

                // Vous pouvez ici recréer votre logique de tag avec couleur et texte
                // par simplicité, je fais un Label avec couleur
                var label = new Label(tag);
                var color = _dataService.GetColorFromTag(tag);
                label.style.color = Color.Lerp(color, Color.white, 0.4f);
                tagElement.Add(label);

                var colorFilter = new VisualElement();
                colorFilter.AddToClassList("card-tag-color-filter");
                colorFilter.style.backgroundColor = color;
                tagElement.Add(colorFilter);
                
                _tagsContainer.Add(tagElement);
            }
            Log($"[ScenarioDetailsView] Tags added: {info.Tags.Length}");
        }
        else
        {
            Log("[ScenarioDetailsView] No tags for this scenario.");
        }

        // Preview
        LoadPreviewImage(info.PreviewImage);
        Log($"[ScenarioDetailsView] ShowScenario finished for: {info.Name}");
    }

    public void ShowEmpty()
    {
        Log("[ScenarioDetailsView] ShowEmpty called.");
        _currentInfo = null;
        _titleLabel.text = "No scenario selected";
        _categoryLabel.text = "";
        _descriptionLabel.text = "Select a scenario to view its description.";
        _environmentLabel.text = "—";
        _robotLabel.text = "—";
        _agentsLabel.text = "—";
        _durationLabel.text = "—";
        _tagsContainer.Clear();
        _previewImage.image = null;
        _previewImage.style.backgroundColor = new Color(0.2f, 0.2f, 0.25f);
    }

    private void LoadPreviewImage(string previewName)
    {
        Log($"[ScenarioDetailsView] LoadPreviewImage: '{previewName}'");
        if (string.IsNullOrEmpty(previewName))
        {
            Log("[ScenarioDetailsView] previewName empty -> default preview");
            SetDefaultPreview();
            return;
        }
        string fileNameWithoutExt = System.IO.Path.GetFileNameWithoutExtension(previewName);
        string resourcePath = $"ScenarioPreviews/{fileNameWithoutExt}";
        Log($"[ScenarioDetailsView] Loading from Resources: {resourcePath}");
        Texture2D texture = Resources.Load<Texture2D>(resourcePath);
        if (texture != null)
        {
            _previewImage.image = texture;
            _previewImage.style.backgroundImage = StyleKeyword.Null;
            Log("[ScenarioDetailsView] Preview loaded successfully.");
        }
        else
        {
            Log("[ScenarioDetailsView] Preview not found, using default.");
            SetDefaultPreview();
        }
    }

    private void SetDefaultPreview()
    {
        Log("[ScenarioDetailsView] SetDefaultPreview");
        Texture2D defaultTex = Resources.Load<Texture2D>("ScenarioPreviews/default");
        if (defaultTex != null)
        {
            _previewImage.image = defaultTex;
            _previewImage.style.backgroundImage = StyleKeyword.Null;
        }
        else
        {
            _previewImage.image = null;
            _previewImage.style.backgroundColor = new Color(0.2f, 0.2f, 0.25f);
            Log("[ScenarioDetailsView] No default preview found, using color fallback.");
        }
    }

    private void Log(string msg)
    {
        if (LogEvents)
            Debug.Log(msg);
    }
}
