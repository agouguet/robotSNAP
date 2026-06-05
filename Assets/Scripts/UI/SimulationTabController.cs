using UnityEngine.UIElements;
using UnityEngine;
using UnityEngine.EventSystems;
using System.Collections;
using RobotSNAP;
using RobotSNAP.Core.Scenario;

public class SimulationTabController : MonoBehaviour
{
    [SerializeField] private UIDocument uiDocument;

    private Supervisor _supervisor;
    private bool _isInitialized = false;

    private VisualElement _root;
    private Button _viewButton;
    private VisualElement _viewMenu;
    private Button _fullscreenButton;
    private Button _loadScenarioButton;
    private Button _startButton;
    private Button _dropdownArrow;
    private VisualElement _cameraOverlay;

    private void OnEnable()
    {
        // S'abonner à l'événement statique
        MainViewController.OnViewLoaded += OnViewLoaded;
        // Cas où la vue Simulator serait déjà chargée (si le script est activé après)
        if (uiDocument != null && uiDocument.rootVisualElement.Q<Button>("ViewButton") != null)
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

        _root = uiDocument.rootVisualElement;
        _viewButton = _root.Q<Button>("ViewButton");
        _viewMenu = _root.Q<VisualElement>("ViewMenu");
        _fullscreenButton = _root.Q<Button>("FullScreenButton");
        _loadScenarioButton = _root.Q<Button>("LoadScenarioButton");
        _startButton = _root.Q<Button>("StartSimulationButton");
        _dropdownArrow = _root.Q<Button>("DropdownArrowButton");
        _cameraOverlay = _root.Q<VisualElement>("CameraOverlay");

        if (_viewButton != null && _viewMenu != null && _fullscreenButton != null)
        {
            _isInitialized = true;
            Initialize();
            // Une fois initialisé, on peut se désabonner pour ne plus être appelé
            MainViewController.OnViewLoaded -= OnViewLoaded;
        }
    }

    private void Initialize()
    {
        _viewButton.clicked += () =>
        {
            _viewMenu.style.display = (_viewMenu.style.display == DisplayStyle.Flex) 
                ? DisplayStyle.None 
                : DisplayStyle.Flex;
        };

        foreach (var option in _viewMenu.Children())
        {
            if (option is Button btn)
            {
                btn.clicked += () =>
                {
                    _viewButton.text = btn.text;
                    _viewMenu.style.display = DisplayStyle.None;
                    OnViewChanged(btn.text);
                };
            }
        }

        _root.RegisterCallback<ClickEvent>(evt =>
        {
            if (_viewMenu.style.display != DisplayStyle.Flex) return;
            VisualElement target = evt.target as VisualElement;
            if (target != _viewButton && !_viewMenu.Contains(target))
            {
                _viewMenu.style.display = DisplayStyle.None;
            }
        });

        if (_fullscreenButton != null)
            _fullscreenButton.clicked += ToggleFullscreen;
        if (_loadScenarioButton != null)
            _loadScenarioButton.clicked += OnLoadScenarioClicked;
        if (_startButton != null)
            _startButton.clicked += OnStartSimulationClicked;
        if (_dropdownArrow != null)
            _dropdownArrow.clicked += ShowSimulationMenu;
        if (_cameraOverlay != null && Supervisor.Instance != null)
        {
            UpdateOverlayVisibility(Supervisor.Instance.IsPaused);
            // Idéalement, écoutez un événement. Sinon, une coroutine simple :
            StartCoroutine(CheckPauseState());
        }
    }

    private void OnViewChanged(string viewName)
    {
        Camera cam = GetComponent<Camera>(); // ou trouvez la caméra de simulation autrement
        if (cam != null)
        {
            switch (viewName)
            {
                case "3D":
                    cam.orthographic = false;
                    break;
                case "2D":
                    cam.orthographic = true;
                    break;
                // autres vues...
            }
        }
    }

    private void OnLoadScenarioClicked()
    {
        
    }

    private void OnStartSimulationClicked()
    {
        var supervisor = Supervisor.Instance;
        if (supervisor == null) return;

        // Si la simulation est en pause, on la reprend ; sinon, on la met en pause.
        if (supervisor.IsPaused) // à définir dans Supervisor
            supervisor.Resume();
        else
            supervisor.Pause();

        UpdateStartButtonState(supervisor.IsPaused);
    }

    private void UpdateStartButtonState(bool isPaused)
    {
        if (_startButton == null) return;
        // Changer l'icône et le texte selon l'état
        var icon = _startButton.Q<VisualElement>("button-icon");
        var label = _startButton.Q<Label>("button-label");
        if (isPaused) // simulation en pause → bouton "Play"
        {
            if (icon != null) icon.style.backgroundImage = new StyleBackground(Resources.Load<Texture2D>("Icons/play"));
            if (label != null) label.text = "Start simulation";
        }
        else // simulation en cours → bouton "Pause"
        {
            if (icon != null) icon.style.backgroundImage = new StyleBackground(Resources.Load<Texture2D>("Icons/pause"));
            if (label != null) label.text = "Pause";
        }
    }

    private void ShowSimulationMenu()
    {
        // Créer un menu temporaire (ou réutiliser un élément caché)
        var menu = new VisualElement();
        menu.AddToClassList("simulation-menu");
        menu.style.position = Position.Absolute;
        menu.style.backgroundColor = new StyleColor(new Color(0.2f, 0.2f, 0.2f));
        // menu.style.borderRadius = 8;
        // menu.style.padding = 8;

        // Obtenir la position du bouton flèche
        var arrowPos = _dropdownArrow.worldBound;
        menu.style.top = arrowPos.yMax;
        menu.style.left = arrowPos.xMin;

        // Ajouter les options
        var pauseItem = new Button(() => { Supervisor.Instance.Pause(); }) { text = "Pause" };
        var resetItem = new Button(() => { FindObjectOfType<ScenarioManager>()?.ResetAndReapply(); }) { text = "Reset" };
        var stepItem = new Button(() => { Supervisor.Instance.Pause(); }) { text = "Step" };

        menu.Add(pauseItem);
        menu.Add(resetItem);
        menu.Add(stepItem);

        _root.Add(menu);

        // Fermer le menu en cliquant ailleurs
        _root.RegisterCallback<ClickEvent>(evt =>
        {
            if (menu.parent != null && evt.target != _dropdownArrow && !menu.Contains(evt.target as VisualElement))
                menu.RemoveFromHierarchy();
        });
    }

    private void ToggleFullscreen()
    {

    }

    private IEnumerator CheckPauseState()
    {
        while (true)
        {
            yield return new WaitForSeconds(0.2f);
            if (Supervisor.Instance != null)
                UpdateOverlayVisibility(Supervisor.Instance.IsPaused);
        }
    }

    private void UpdateOverlayVisibility(bool isPaused)
    {
        _cameraOverlay.style.display = isPaused ? DisplayStyle.Flex : DisplayStyle.None;
    }
}