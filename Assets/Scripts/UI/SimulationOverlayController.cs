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
        _root = uiDocument.rootVisualElement;

        _viewButton = _root.Q<Button>("ViewButton");
        _viewMenu = _root.Q<VisualElement>("ViewMenu");
        _fullscreenButton = _root.Q<Button>("FullScreenButton");

        // Ouvre/ferme le menu au clic sur le bouton
        _viewButton.clicked += () =>
        {
            _viewMenu.style.display = (_viewMenu.style.display == DisplayStyle.Flex) 
                ? DisplayStyle.None 
                : DisplayStyle.Flex;
        };

        // Clic sur une option du menu
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

        // Bonus : ferme le menu si on clique ailleurs (sauf sur le bouton)
        _root.RegisterCallback<ClickEvent>(evt =>
        {
            // Si le menu est fermé, rien à faire
            if (_viewMenu.style.display != DisplayStyle.Flex) return;

            // Récupère la cible du clic
            VisualElement target = evt.target as VisualElement;
            // Si le clic n'est ni sur le bouton, ni à l'intérieur du menu, on ferme
            if (target != _viewButton && !_viewMenu.Contains(target))
            {
                _viewMenu.style.display = DisplayStyle.None;
            }
        });

        // Plein écran (à implémenter)
        if (_fullscreenButton != null)
            _fullscreenButton.clicked += ToggleFullscreen;
    }

    private void OnViewChanged(string viewName)
    {
        Debug.Log($"Changement de vue : {viewName}");
        // Ici, appliquer la modification à la caméra (orthographique/perspective, position, rotation...)
        // Exemple simple :
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
        // Votre logique plein écran (cacher la sidebar, agrandir la caméra, etc.)
        Debug.Log("Toggle fullscreen");
    }
}