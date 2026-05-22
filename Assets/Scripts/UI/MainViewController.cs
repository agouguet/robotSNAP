using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;

public class MainViewController : MonoBehaviour
{
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private VisualTreeAsset simulatorTemplate;
    [SerializeField] private VisualTreeAsset analysisTemplate;
    [SerializeField] private VisualTreeAsset scenariosTemplate;
    [SerializeField] private VisualTreeAsset agentsTemplate;
    [SerializeField] private VisualTreeAsset settingsTemplate;
    [SerializeField] private VisualTreeAsset profileTemplate;
    [SerializeField] private VisualTreeAsset documentationTemplate;
    // ajoutez les autres templates

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

        // Récupérer l'instance mise en cache ou en créer une nouvelle
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

        // Informer le script caméra que la vue Simulator est réaffichée
        if (viewName == "Simulator")
        {
            var camView = FindObjectOfType<SimulationCameraView>();
            if (camView != null) camView.ForceRefresh();
        }
    }

    private VisualTreeAsset GetTemplateForView(string viewName)
    {
        switch (viewName)
        {
            case "Simulator": return simulatorTemplate;
            case "Analysis": return analysisTemplate;
            case "Scenarios": return scenariosTemplate;
            case "Agents": return agentsTemplate;
            case "Settings": return settingsTemplate;
            case "Profile": return profileTemplate;
            case "Documentation": return documentationTemplate;
            // ... autres cas
            default: return null;
        }
    }
}