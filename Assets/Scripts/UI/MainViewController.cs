using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;

public class MainViewController : MonoBehaviour
{
    // Événement statique pour notifier qu'une vue a été chargée
    public static System.Action<string> OnViewLoaded;

    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private VisualTreeAsset simulatorTemplate;
    [SerializeField] private VisualTreeAsset analysisTemplate;
    [SerializeField] private VisualTreeAsset scenariosTemplate;
    [SerializeField] private VisualTreeAsset agentsTemplate;
    [SerializeField] private VisualTreeAsset recordingsTemplate;
    [SerializeField] private VisualTreeAsset settingsTemplate;
    [SerializeField] private VisualTreeAsset profileTemplate;
    [SerializeField] private VisualTreeAsset documentationTemplate;

    private VisualElement _contentContainer;
    private SidebarController _sidebarController;
    private Dictionary<string, VisualElement> _viewCache = new Dictionary<string, VisualElement>();
    private string _currentView = "";

    private void OnEnable()
    {
        if (uiDocument == null) uiDocument = GetComponent<UIDocument>();
        _contentContainer = uiDocument.rootVisualElement.Q<VisualElement>("MainContent");
        if (_contentContainer == null)
        {
            Debug.LogError("MainContent introuvable dans MainWindow.uxml");
            return;
        }

        _sidebarController = GetComponent<SidebarController>();
        if (_sidebarController != null)
            _sidebarController.OnViewChanged += OnViewChanged;

        ShowView("Simulator");
    }

    private void OnViewChanged(string viewName)
    {
        ShowView(viewName);
    }

    private void ShowView(string viewName)
    {
        if (_currentView == viewName) return;

        if (!_viewCache.TryGetValue(viewName, out VisualElement viewInstance))
        {
            VisualTreeAsset template = GetTemplateForView(viewName);
            if (template == null)
            {
                Debug.LogWarning($"Template non trouvé pour {viewName}");
                return;
            }
            viewInstance = template.Instantiate();
            viewInstance.style.flexGrow = 1;
            viewInstance.style.height = new StyleLength(Length.Percent(100));
            viewInstance.style.minHeight = 0;
            _viewCache[viewName] = viewInstance;
        }

        _contentContainer.Clear();
        _contentContainer.Add(viewInstance);
        _currentView = viewName;

        OnViewLoaded?.Invoke(viewName);
    }

    private VisualTreeAsset GetTemplateForView(string viewName)
    {
        switch (viewName)
        {
            case "Simulator": return simulatorTemplate;
            case "Analysis": return analysisTemplate;
            case "Scenarios": return scenariosTemplate;
            case "Agents": return agentsTemplate;
            case "Recordings": return recordingsTemplate;
            case "Settings": return settingsTemplate;
            case "Profile": return profileTemplate;
            case "Documentation": return documentationTemplate;
            default: return null;
        }
    }
}