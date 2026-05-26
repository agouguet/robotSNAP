using UnityEngine;
using UnityEngine.UIElements;
using RobotSNAP.UI; // si vous avez mis SlideToggle dans ce namespace

public class SidebarController : MonoBehaviour
{
    [Header("UI Document")]
    [SerializeField] private UIDocument uiDocument;

    public System.Action<string> OnViewChanged;
    public System.Action<bool> OnDarkModeToggled;

    private VisualElement _sidebar;
    private Button _collapseButton;
    private VisualElement _collapseContainer;
    private SlideToggle _darkModeToggle;   // utilisez SlideToggle au lieu de Toggle
    private bool _isSidebarCollapsed = false;

    private void OnEnable()
    {
        if (uiDocument == null)
            uiDocument = GetComponent<UIDocument>();

        var root = uiDocument.rootVisualElement;

        _sidebar = root.Q<VisualElement>("Sidebar");
        _collapseButton = root.Q<Button>("CollapseButton");
        _collapseContainer = root.Q<VisualElement>("CollapseContainer");
        _darkModeToggle = root.Q<SlideToggle>("DarkModeToggle");   // Récupération du SlideToggle

        // Collapse
        if (_collapseButton != null)
            _collapseButton.clicked += ToggleSidebar;
        if (_collapseContainer != null)
            _collapseContainer.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.target == _collapseButton) return;
                ToggleSidebar();
            });

        // Dark Mode avec SlideToggle
        if (_darkModeToggle != null)
        {
            // Abonnement au changement de valeur
            _darkModeToggle.RegisterValueChangedCallback(evt => ToggleDarkMode(evt.newValue));
            
            // Optionnel : restaurer l'état sauvegardé (ex: PlayerPrefs)
            bool isDark = PlayerPrefs.GetInt("DarkMode", 0) == 1;
            _darkModeToggle.SetValueWithoutNotify(isDark);
            ToggleDarkMode(isDark);
        }

        // Menu items
        RegisterMenuItems();
    }

    private void ToggleDarkMode(bool isOn)
    {
        var root = uiDocument.rootVisualElement;
        if (isOn)
            root.AddToClassList("dark-mode");
        else
            root.RemoveFromClassList("dark-mode");

        PlayerPrefs.SetInt("DarkMode", isOn ? 1 : 0);
        
        OnDarkModeToggled?.Invoke(isOn);
    }

    private void RegisterMenuItems()
    {
        var root = uiDocument.rootVisualElement;
        // Recherche directe de tous les boutons avec la classe "menu-item" dans l'arbre complet
        var allMenuButtons = root.Query<Button>(className: "menu-item").ToList();
        foreach (var btn in allMenuButtons)
        {
            if (btn.name == "DarkModeToggle") continue;
            // Récupérer le label par sa classe (pas de nom)
            var label = btn.Q<Label>(className: "menu-label");
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

    // Méthode publique pour plier/déplier la sidebar depuis un autre script
    public void SetSidebarCollapsed(bool collapsed)
    {
        if (collapsed == _isSidebarCollapsed) return;
        ToggleSidebar();
    }
}