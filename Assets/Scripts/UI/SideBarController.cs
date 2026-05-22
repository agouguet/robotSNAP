using UnityEngine;
using UnityEngine.UIElements;

public class SidebarController : MonoBehaviour
{
    [Header("UI Document")]
    [SerializeField] private UIDocument uiDocument;

    // Événements déclenchés quand la vue change ou que le mode sombre alterne
    public System.Action<string> OnViewChanged;   // "Simulator", "Analysis", ...
    public System.Action<bool> OnDarkModeToggled;

    // Éléments UI
    private VisualElement _sidebar;
    private Button _collapseButton;
    private VisualElement _collapseContainer;
    private Button _darkModeButton;   // c'est un Button, pas un Toggle
    private bool _isDarkMode = false;

    // Gestion du collapse
    private bool _isSidebarCollapsed = false;

    private void OnEnable()
    {
        if (uiDocument == null)
            uiDocument = GetComponent<UIDocument>();

        var root = uiDocument.rootVisualElement;

        // Récupération des éléments
        _sidebar = root.Q<VisualElement>("Sidebar");
        _collapseButton = root.Q<Button>("CollapseButton");
        _collapseContainer = root.Q<VisualElement>("CollapseContainer");
        _darkModeButton = root.Q<Button>("DarkModeToggle");

        // Abonnement au collapse (bouton + conteneur entier)
        if (_collapseButton != null)
            _collapseButton.clicked += ToggleSidebar;

        if (_collapseContainer != null)
            _collapseContainer.RegisterCallback<ClickEvent>(evt =>
            {
                // Ne pas réagir si le clic vient directement du bouton (déjà traité)
                if (evt.target == _collapseButton) return;
                ToggleSidebar();
            });

        // Abonnement au mode sombre
        if (_darkModeButton != null)
            _darkModeButton.clicked += ToggleDarkMode;

        // Abonnement aux clics sur les éléments du menu
        RegisterMenuItems(root);
    }

    private void RegisterMenuItems(VisualElement root)
    {
        // Récupère tous les boutons ayant la classe "menu-item"
        var menuItems = root.Query<Button>(className: "menu-item").ToList();
        foreach (var btn in menuItems)
        {
            // Exclure le bouton DarkModeToggle (il a aussi la classe menu-item)
            if (btn.name == "DarkModeToggle") continue;

            // Trouver le label à l'intérieur pour connaître le texte de la vue
            var label = btn.Q<Label>("menu-label"); // attention: le nom n'est pas "menu-label" mais la classe? Dans l'UXML, il a class="menu-label" mais pas de name.
            // On peut utiliser la classe :
            label = btn.Q<Label>(className: "menu-label");
            string viewName = label?.text ?? btn.name;

            btn.clicked += () => SetActiveView(btn, viewName);
        }
    }

    private void SetActiveView(Button activeButton, string viewName)
    {
        // Désactiver la classe "active" sur TOUS les menu-item de la sidebar
        var allMenuItems = activeButton.panel.visualTree.Query<Button>(className: "menu-item").ToList();
        if (allMenuItems != null)
        {
            foreach (var item in allMenuItems)
                item.RemoveFromClassList("active");
        }
        // Activer le bouton cliqué
        activeButton.AddToClassList("active");

        // Déclencher l'événement
        OnViewChanged?.Invoke(viewName);
        Debug.Log($"Changement de vue : {viewName}");
    }

    private void ToggleSidebar()
    {
        _isSidebarCollapsed = !_isSidebarCollapsed;
        if (_isSidebarCollapsed)
        {
            _sidebar.AddToClassList("collapsed");
            _collapseButton.text = "▶";   // flèche pour étendre
        }
        else
        {
            _sidebar.RemoveFromClassList("collapsed");
            _collapseButton.text = "◀";   // flèche pour replier
        }
    }

    private void ToggleDarkMode()
    {
        _isDarkMode = !_isDarkMode;
        var root = uiDocument.rootVisualElement;
        if (_isDarkMode)
            root.AddToClassList("dark-mode");
        else
            root.RemoveFromClassList("dark-mode");

        // Optionnel : changer le texte/icône du bouton
        var label = _darkModeButton.Q<Label>(className: "menu-label");
        if (label != null)
            label.text = _isDarkMode ? "Light Mode" : "Dark Mode";

        OnDarkModeToggled?.Invoke(_isDarkMode);
    }

    // Méthode publique pour plier/déplier la sidebar depuis un autre script
    public void SetSidebarCollapsed(bool collapsed)
    {
        if (collapsed == _isSidebarCollapsed) return;
        ToggleSidebar();
    }
}