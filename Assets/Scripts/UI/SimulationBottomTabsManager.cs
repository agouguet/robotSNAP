using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Gère l'affichage des trois onglets du bas : Vue Capteur, Console et Logs.
/// </summary>
public class SimulationBottomTabsManager : MonoBehaviour
{
    [SerializeField] private UIDocument uiDocument;

    private Button _sensorTabButton;
    private Button _consoleTabButton;
    private Button _logsTabButton;
    private VisualElement _sensorTabContent;
    private VisualElement _consoleTabContent;
    private VisualElement _logsTabContent;

    private bool _isInitialized = false;

    private void OnEnable()
    {
        // S'abonner à l'événement de chargement des vues
        MainViewController.OnViewLoaded += OnViewLoaded;
        // Si la vue est déjà chargée, essayer immédiatement
        if (uiDocument != null && uiDocument.rootVisualElement.Q<Button>("TabSensor") != null)
            TryInitialize();
    }

    private void OnDisable()
    {
        MainViewController.OnViewLoaded -= OnViewLoaded;
    }

    private void OnViewLoaded(string viewName)
    {
        if (viewName == "Simulator")
            TryInitialize();
    }

    private void TryInitialize()
    {
        if (_isInitialized) return;

        if (uiDocument == null)
            uiDocument = GetComponent<UIDocument>();

        if (uiDocument == null) return;

        var root = uiDocument.rootVisualElement;

        _sensorTabButton = root.Q<Button>("TabSensor");
        _consoleTabButton = root.Q<Button>("TabConsole");
        _logsTabButton = root.Q<Button>("TabLogs");
        _sensorTabContent = root.Q<VisualElement>("SensorContent");
        _consoleTabContent = root.Q<VisualElement>("ConsoleContent");
        _logsTabContent = root.Q<VisualElement>("LogsContent");

        // Vérifier que les éléments essentiels sont trouvés
        if (_sensorTabButton == null || _consoleTabButton == null || _logsTabButton == null)
            return;

        _isInitialized = true;

        // Enregistrer les événements
        _sensorTabButton.clicked += () => SwitchToTab(TabType.Sensor);
        _consoleTabButton.clicked += () => SwitchToTab(TabType.Console);
        _logsTabButton.clicked += () => SwitchToTab(TabType.Logs);

        // Une fois initialisé, on peut se désabonner
        MainViewController.OnViewLoaded -= OnViewLoaded;
    }

    private enum TabType { Sensor, Console, Logs }

    private void SwitchToTab(TabType tab)
    {
        if (!_isInitialized) return;

        // Désactiver tous les onglets
        _sensorTabButton?.RemoveFromClassList("active");
        _consoleTabButton?.RemoveFromClassList("active");
        _logsTabButton?.RemoveFromClassList("active");

        if (_sensorTabContent != null) _sensorTabContent.style.display = DisplayStyle.None;
        if (_consoleTabContent != null) _consoleTabContent.style.display = DisplayStyle.None;
        if (_logsTabContent != null) _logsTabContent.style.display = DisplayStyle.None;

        // Activer l'onglet choisi
        switch (tab)
        {
            case TabType.Sensor:
                _sensorTabButton?.AddToClassList("active");
                if (_sensorTabContent != null) _sensorTabContent.style.display = DisplayStyle.Flex;
                break;
            case TabType.Console:
                _consoleTabButton?.AddToClassList("active");
                if (_consoleTabContent != null) _consoleTabContent.style.display = DisplayStyle.Flex;
                break;
            case TabType.Logs:
                _logsTabButton?.AddToClassList("active");
                if (_logsTabContent != null) _logsTabContent.style.display = DisplayStyle.Flex;
                break;
        }
    }
}