using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;

public class MainUIController : MonoBehaviour
{
    [Header("UI Document")]
    [SerializeField] private UIDocument uiDocument;

    [Header("Caméra")]
    [SerializeField] private Camera simulationCamera; // Optionnel : si vous voulez assigner une caméra existante

    // Sidebar
    private VisualElement _sidebar;
    private VisualElement _sidebarContent;
    private VisualElement _collapseContainer;
    private Label _collapseLabel;
    private Button _collapseButton;
    private Toggle _darkModeToggle;
    private bool _isSidebarCollapsed = false;

    // Caméra UI
    private VisualElement _cameraView;
    private Camera _activeCamera;
    private RenderTexture _currentRenderTexture;

    // Métriques & logs
    private Label _humansCountLabel;
    private Label _robotsCountLabel;
    private Label _durationLabel;
    private Label _progressLabel;
    private ListView _logsListView;
    private List<string> _logs = new List<string>();

    private void OnEnable()
    {
        if (uiDocument == null)
            uiDocument = GetComponent<UIDocument>();

        var root = uiDocument.rootVisualElement;

        // --- Sidebar rétractable ---
        _sidebar = root.Q<VisualElement>("Sidebar");
        _sidebarContent = root.Q<VisualElement>("SidebarContent");
        _collapseButton = root.Q<Button>("CollapseButton");
        _collapseContainer = root.Q<VisualElement>("CollapseContainer");
        
        if (_collapseButton != null)
            _collapseButton.clicked += ToggleSidebar;
        
        Debug.Log($"Collapse container found: {_collapseContainer != null}");
        if (_collapseContainer != null)
            _collapseContainer.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.target == _collapseButton)
                    return;
                Debug.Log("Collapse container clicked");
                ToggleSidebar();
            });

        // --- Mode sombre ---
        _darkModeToggle = root.Q<Toggle>("DarkModeToggle");
        if (_darkModeToggle != null)
            _darkModeToggle.RegisterValueChangedCallback(evt => ToggleDarkMode(evt.newValue));

        // --- Caméra View ---
        _cameraView = root.Q<VisualElement>("CameraContainer");
        if (_cameraView != null)
        {
            // Si aucune caméra n'est assignée dans l'inspecteur, on en crée une dynamique
            if (simulationCamera == null)
            {
                GameObject camGO = new GameObject("SimulationCamera");
                _activeCamera = camGO.AddComponent<Camera>();
                _activeCamera.clearFlags = CameraClearFlags.Skybox;
            }
            else
            {
                _activeCamera = simulationCamera;
            }

            // Abonnement au redimensionnement
            _cameraView.RegisterCallback<GeometryChangedEvent>(evt => OnCameraViewResized());
            OnCameraViewResized(); // création initiale
        }
        else
        {
            Debug.LogWarning("Aucun VisualElement nommé 'CameraView' trouvé dans l'UI.");
        }

        // --- Métriques (exemples) ---
        _humansCountLabel = root.Q<Label>("HumansCount");
        _robotsCountLabel = root.Q<Label>("RobotsCount");
        _durationLabel = root.Q<Label>("Duration");
        _progressLabel = root.Q<Label>("Progress");

        // --- Logs ListView ---
        _logsListView = root.Q<ListView>("LogsListView");
        if (_logsListView != null)
        {
            _logsListView.makeItem = () => new Label();
            _logsListView.bindItem = (item, index) => (item as Label).text = _logs[index];
            _logsListView.itemsSource = _logs;
        }

        InitializeDemoData();
    }

    private void OnDisable()
    {
        // Nettoyage : on remet la target texture de la caméra à null et on libère la RenderTexture
        if (_activeCamera != null && _currentRenderTexture != null)
        {
            _activeCamera.targetTexture = null;
            _currentRenderTexture.Release();
        }
    }

    // ==================== SIDEBAR ====================
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

    private void OnCollapseContainerClicked(ClickEvent evt)
    {
        // Évite de réagir si le clic vient d'un élément interne qui aurait déjà traité l'événement
        // (mais comme on n'a pas d'autre callback, ce n'est pas obligatoire)
        ToggleSidebar();
    }

    // ==================== MODE SOMBRE ====================
    private void ToggleDarkMode(bool isOn)
    {
        var root = uiDocument.rootVisualElement;
        if (isOn)
            root.AddToClassList("dark-mode");
        else
            root.RemoveFromClassList("dark-mode");
    }

    // ==================== CAMÉRA ====================
    private void OnCameraViewResized()
    {
        if (_cameraView == null || _activeCamera == null) return;

        // Récupère les dimensions résolues (en pixels)
        float width = _cameraView.resolvedStyle.width;
        float height = _cameraView.resolvedStyle.height;

        if (width <= 0 || height <= 0) return;

        int w = Mathf.Max(1, (int)width);
        int h = Mathf.Max(1, (int)height);

        // Libère l'ancienne texture
        if (_currentRenderTexture != null)
        {
            _currentRenderTexture.Release();
        }

        _currentRenderTexture = new RenderTexture(w, h, 24);
        _activeCamera.targetTexture = _currentRenderTexture;

        // Attribue la texture à l'arrière-plan du VisualElement
        _cameraView.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(_currentRenderTexture));
    }

    // Option : vous pouvez exposer cette méthode pour changer la caméra active en cours de route
    public void SetCamera(Camera newCamera)
    {
        if (_activeCamera != null && _currentRenderTexture != null)
            _activeCamera.targetTexture = null;

        _activeCamera = newCamera;
        if (_activeCamera != null && _cameraView != null)
            OnCameraViewResized(); // recrée la texture pour la nouvelle caméra
    }

    // ==================== MÉTRIQUES ET LOGS (exemples) ====================
    private void InitializeDemoData()
    {
        UpdateMetrics(humans: 18, robots: 1, duration: "10:00", progress: 27);
        AddLog("Début de la simulation");
        AddLog("Humain détecté à gauche");
        AddLog("Ré-planification de trajectoire");
        AddLog("Interaction sociale détectée");
        AddLog("Trajectoire dégagée");
    }

    public void UpdateMetrics(int humans, int robots, string duration, int progress)
    {
        if (_humansCountLabel != null) _humansCountLabel.text = humans.ToString();
        if (_robotsCountLabel != null) _robotsCountLabel.text = robots.ToString();
        if (_durationLabel != null) _durationLabel.text = duration;
        if (_progressLabel != null) _progressLabel.text = $"{progress}%";
    }

    public void AddLog(string message)
    {
        _logs.Insert(0, $"{System.DateTime.Now:HH:mm:ss} {message}");
        if (_logs.Count > 100) _logs.RemoveAt(_logs.Count - 1);
        _logsListView?.RefreshItems();
        // Faire défiler en haut
        var scrollView = _logsListView?.parent as ScrollView;
        if (scrollView != null) scrollView.scrollOffset = Vector2.zero;
    }
}