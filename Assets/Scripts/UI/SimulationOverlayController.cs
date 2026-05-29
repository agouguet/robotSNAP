using UnityEngine.UIElements;
using UnityEngine;

public class SimulationOverlayController : MonoBehaviour
{
    [SerializeField] private UIDocument uiDocument;

    private VisualElement _root;
    private Button _viewButton;
    private VisualElement _viewMenu;
    private Button _fullscreenButton;

    private void OnEnable()
    {
        if (uiDocument == null)
            uiDocument = GetComponent<UIDocument>();

        if (uiDocument == null)
        {
            Debug.LogError("SimulationOverlayController: Aucun UIDocument trouvé !");
            return;
        }

        _root = uiDocument.rootVisualElement;

        _root.schedule.Execute(() =>
        {
            if (_viewButton == null)
            {
                _viewButton = _root.Q<Button>("ViewButton");
                _viewMenu = _root.Q<VisualElement>("ViewMenu");
                _fullscreenButton = _root.Q<Button>("FullScreenButton");
                if (_viewButton != null && _viewMenu != null && _fullscreenButton != null)
                {
                    Initialize();
                }
            }
        }).Every(50); // vérifie toutes les 50ms
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

    private void ToggleFullscreen()
    {

    }
}