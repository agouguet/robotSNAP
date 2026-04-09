#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using TMPro;

namespace RobotSNAP.UI.Editor
{
    public class TargetSelectionPopupCreator : EditorWindow
    {
        private string _prefabPath = "Assets/Prefabs/UI/TargetSelectionPopup.prefab";
        private string _buttonPrefabPath = "Assets/Prefabs/UI/TargetButton.prefab";
        private bool _createButtonPrefab = true;
        private Color _popupBackgroundColor = new Color(0.1f, 0.1f, 0.15f, 0.95f);
        
        [MenuItem("RobotSNAP/Create Target Selection Popup")]
        public static void ShowWindow()
        {
            GetWindow<TargetSelectionPopupCreator>("Create Popup Prefab");
        }
        
        private void OnGUI()
        {
            GUILayout.Label("Target Selection Popup Creator", EditorStyles.boldLabel);
            GUILayout.Space(10);
            
            _prefabPath = EditorGUILayout.TextField("Popup Prefab Path", _prefabPath);
            _buttonPrefabPath = EditorGUILayout.TextField("Button Prefab Path", _buttonPrefabPath);
            _createButtonPrefab = EditorGUILayout.Toggle("Create Button Prefab", _createButtonPrefab);
            _popupBackgroundColor = EditorGUILayout.ColorField("Popup Background Color", _popupBackgroundColor);
            
            GUILayout.Space(20);
            
            if (GUILayout.Button("Create Prefabs", GUILayout.Height(30)))
            {
                CreatePrefabs();
            }
            
            GUILayout.Space(10);
            
            EditorGUILayout.HelpBox(
                "This will create:\n" +
                "1. TargetButton prefab\n" +
                "2. TargetSelectionPopup prefab\n\n" +
                "Make sure TMP Essentials are imported!",
                MessageType.Info);
        }
        
        private void CreatePrefabs()
        {
            // Créer les dossiers si nécessaire
            EnsureDirectoryExists(_prefabPath);
            EnsureDirectoryExists(_buttonPrefabPath);
            
            GameObject buttonPrefab = null;
            
            // Créer le prefab du bouton d'abord
            if (_createButtonPrefab)
            {
                buttonPrefab = CreateButtonPrefab();
            }
            
            // Créer le prefab de la popup
            GameObject popupPrefab = CreatePopupPrefab(buttonPrefab);
            
            // Sauvegarder les prefabs
            if (_createButtonPrefab && buttonPrefab != null)
            {
                PrefabUtility.SaveAsPrefabAsset(buttonPrefab, _buttonPrefabPath);
                Debug.Log($"Button prefab created at: {_buttonPrefabPath}");
            }
            
            PrefabUtility.SaveAsPrefabAsset(popupPrefab, _prefabPath);
            Debug.Log($"Popup prefab created at: {_prefabPath}");
            
            // Sélectionner le prefab dans le Project window
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(_prefabPath);
            
            EditorUtility.DisplayDialog("Success", "Prefabs created successfully!", "OK");
        }
        
        private void EnsureDirectoryExists(string filePath)
        {
            string directory = System.IO.Path.GetDirectoryName(filePath);
            if (!System.IO.Directory.Exists(directory))
            {
                System.IO.Directory.CreateDirectory(directory);
            }
        }
        
        private GameObject CreateButtonPrefab()
        {
            // Créer le GameObject racine du bouton
            GameObject buttonObj = new GameObject("TargetButton");
            
            // Ajouter RectTransform
            RectTransform rectTransform = buttonObj.AddComponent<RectTransform>();
            rectTransform.sizeDelta = new Vector2(200, 40);
            
            // Ajouter Image (fond du bouton)
            Image image = buttonObj.AddComponent<Image>();
            image.color = new Color(0.2f, 0.2f, 0.25f, 1f);
            
            // Ajouter Button
            Button button = buttonObj.AddComponent<Button>();
            ColorBlock colors = button.colors;
            colors.normalColor = new Color(0.25f, 0.25f, 0.3f, 1f);
            colors.highlightedColor = new Color(0.35f, 0.35f, 0.4f, 1f);
            colors.pressedColor = new Color(0.15f, 0.15f, 0.2f, 1f);
            colors.selectedColor = new Color(0.3f, 0.5f, 0.3f, 1f);
            colors.disabledColor = new Color(0.2f, 0.2f, 0.2f, 0.5f);
            button.colors = colors;
            
            // Ajouter LayoutElement
            LayoutElement layoutElement = buttonObj.AddComponent<LayoutElement>();
            layoutElement.minHeight = 35;
            layoutElement.preferredHeight = 40;
            
            // Créer le texte du bouton
            GameObject textObj = new GameObject("Text");
            textObj.transform.SetParent(buttonObj.transform, false);
            
            RectTransform textRect = textObj.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(10, 5);
            textRect.offsetMax = new Vector2(-10, -5);
            
            TextMeshProUGUI tmpText = textObj.AddComponent<TextMeshProUGUI>();
            tmpText.text = "Button";
            tmpText.fontSize = 16;
            tmpText.fontStyle = FontStyles.Normal;
            tmpText.alignment = TextAlignmentOptions.Left;
            tmpText.color = Color.white;
            
            // Ajouter ContentSizeFitter pour l'auto-sizing
            ContentSizeFitter sizeFitter = buttonObj.AddComponent<ContentSizeFitter>();
            sizeFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            sizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            
            return buttonObj;
        }
        
        private GameObject CreatePopupPrefab(GameObject buttonPrefab)
        {
            // Créer le Canvas parent (si nécessaire dans la scène)
            GameObject popupRoot = new GameObject("TargetSelectionPopup");
            
            // Ajouter RectTransform
            RectTransform rootRect = popupRoot.AddComponent<RectTransform>();
            
            // Ajouter le script TargetSelectionPopup
            TargetSelectionPopup popupScript = popupRoot.AddComponent<TargetSelectionPopup>();
            
            // === PANEL PRINCIPAL ===
            GameObject panel = CreateUIElement("Panel", popupRoot.transform);
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(400, 500);
            panelRect.anchoredPosition = Vector2.zero;
            
            Image panelImage = panel.AddComponent<Image>();
            panelImage.color = _popupBackgroundColor;
            
            // Ajouter un contour
            Outline outline = panel.AddComponent<Outline>();
            outline.effectColor = new Color(0.3f, 0.3f, 0.4f, 1f);
            outline.effectDistance = new Vector2(2, -2);
            
            // Ajouter VerticalLayoutGroup pour organiser les éléments
            VerticalLayoutGroup panelLayout = panel.AddComponent<VerticalLayoutGroup>();
            panelLayout.padding = new RectOffset(10, 10, 10, 10);
            panelLayout.spacing = 10;
            panelLayout.childAlignment = TextAnchor.UpperCenter;
            panelLayout.childControlWidth = true;
            panelLayout.childControlHeight = false;
            panelLayout.childForceExpandWidth = true;
            panelLayout.childForceExpandHeight = false;
            
            // === TITLE BAR ===
            GameObject titleBar = CreateUIElement("TitleBar", panel.transform);
            titleBar.AddComponent<LayoutElement>().preferredHeight = 40;
            
            HorizontalLayoutGroup titleLayout = titleBar.AddComponent<HorizontalLayoutGroup>();
            titleLayout.childAlignment = TextAnchor.MiddleLeft;
            titleLayout.childControlWidth = true;
            titleLayout.childForceExpandWidth = true;
            
            // Titre
            GameObject titleObj = CreateUIElement("TitleText", titleBar.transform);
            TextMeshProUGUI titleText = titleObj.AddComponent<TextMeshProUGUI>();
            titleText.text = "Sélectionner une cible";
            titleText.fontSize = 20;
            titleText.fontStyle = FontStyles.Bold;
            titleText.color = Color.white;
            titleText.alignment = TextAlignmentOptions.Left;
            popupScript.titleText = titleText;
            
            // Bouton Close
            GameObject closeBtn = CreateUIElement("CloseButton", titleBar.transform);
            closeBtn.AddComponent<LayoutElement>().preferredWidth = 30;
            
            Image closeImage = closeBtn.AddComponent<Image>();
            closeImage.color = new Color(0.8f, 0.2f, 0.2f, 1f);
            
            Button closeButton = closeBtn.AddComponent<Button>();
            popupScript.closeButton = closeButton;
            
            GameObject closeText = CreateUIElement("X", closeBtn.transform);
            TextMeshProUGUI closeTMP = closeText.AddComponent<TextMeshProUGUI>();
            closeTMP.text = "✕";
            closeTMP.fontSize = 20;
            closeTMP.color = Color.white;
            closeTMP.alignment = TextAlignmentOptions.Center;
            
            // === CURRENT TARGET DISPLAY ===
            GameObject currentTarget = CreateUIElement("CurrentTarget", panel.transform);
            currentTarget.AddComponent<LayoutElement>().preferredHeight = 30;
            
            TextMeshProUGUI currentText = currentTarget.AddComponent<TextMeshProUGUI>();
            currentText.text = "Actuel: Aucune cible";
            currentText.fontSize = 14;
            currentText.color = Color.gray;
            currentText.alignment = TextAlignmentOptions.Left;
            currentText.fontStyle = FontStyles.Italic;
            popupScript.currentTargetText = currentText;
            
            // === SEARCH BAR ===
            GameObject searchBar = CreateUIElement("SearchBar", panel.transform);
            searchBar.AddComponent<LayoutElement>().preferredHeight = 40;
            
            HorizontalLayoutGroup searchLayout = searchBar.AddComponent<HorizontalLayoutGroup>();
            searchLayout.spacing = 5;
            
            // Icône de recherche
            GameObject searchIcon = CreateUIElement("SearchIcon", searchBar.transform);
            searchIcon.AddComponent<LayoutElement>().preferredWidth = 30;
            
            TextMeshProUGUI iconText = searchIcon.AddComponent<TextMeshProUGUI>();
            iconText.text = "🔍";
            iconText.fontSize = 20;
            iconText.alignment = TextAlignmentOptions.Center;
            
            // Input field de recherche
            GameObject searchInput = CreateUIElement("SearchInput", searchBar.transform);
            
            Image inputBg = searchInput.AddComponent<Image>();
            inputBg.color = new Color(0.15f, 0.15f, 0.2f, 1f);
            
            TMP_InputField inputField = searchInput.AddComponent<TMP_InputField>();
            
            // Text Area
            GameObject textArea = CreateUIElement("TextArea", searchInput.transform);
            RectTransform textAreaRect = textArea.GetComponent<RectTransform>();
            textAreaRect.anchorMin = Vector2.zero;
            textAreaRect.anchorMax = Vector2.one;
            textAreaRect.offsetMin = new Vector2(10, 5);
            textAreaRect.offsetMax = new Vector2(-10, -5);
            
            TextMeshProUGUI placeholder = textArea.AddComponent<TextMeshProUGUI>();
            placeholder.text = "Rechercher...";
            placeholder.fontSize = 14;
            placeholder.color = new Color(0.5f, 0.5f, 0.5f, 1f);
            placeholder.fontStyle = FontStyles.Italic;
            
            // Actual text
            GameObject actualText = CreateUIElement("Text", textArea.transform);
            TextMeshProUGUI inputText = actualText.AddComponent<TextMeshProUGUI>();
            inputText.text = "";
            inputText.fontSize = 14;
            inputText.color = Color.white;
            
            inputField.textViewport = textAreaRect;
            inputField.textComponent = inputText;
            inputField.placeholder = placeholder;
            
            popupScript.searchInput = inputField;
            
            // === SCROLL VIEW ===
            GameObject scrollView = CreateUIElement("ScrollView", panel.transform);
            scrollView.AddComponent<LayoutElement>().flexibleHeight = 1;
            
            Image scrollBg = scrollView.AddComponent<Image>();
            scrollBg.color = new Color(0.05f, 0.05f, 0.08f, 1f);
            
            ScrollRect scrollRect = scrollView.AddComponent<ScrollRect>();
            
            // Viewport
            GameObject viewport = CreateUIElement("Viewport", scrollView.transform);
            RectTransform viewportRect = viewport.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = Vector2.zero;
            viewportRect.offsetMax = Vector2.zero;
            
            Image viewportImage = viewport.AddComponent<Image>();
            viewportImage.color = new Color(0.08f, 0.08f, 0.12f, 1f);
            
            Mask mask = viewport.AddComponent<Mask>();
            mask.showMaskGraphic = true;
            
            // Content
            GameObject content = CreateUIElement("Content", viewport.transform);
            RectTransform contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0, 1);
            contentRect.anchorMax = new Vector2(1, 1);
            contentRect.pivot = new Vector2(0.5f, 1);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = new Vector2(0, 0);
            
            VerticalLayoutGroup contentLayout = content.AddComponent<VerticalLayoutGroup>();
            contentLayout.padding = new RectOffset(5, 5, 5, 5);
            contentLayout.spacing = 5;
            contentLayout.childAlignment = TextAnchor.UpperCenter;
            contentLayout.childControlWidth = true;
            contentLayout.childControlHeight = false;
            contentLayout.childForceExpandWidth = true;
            contentLayout.childForceExpandHeight = false;
            
            ContentSizeFitter contentFitter = content.AddComponent<ContentSizeFitter>();
            contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            
            popupScript.contentContainer = contentRect;
            
            // Scrollbar
            GameObject scrollbar = CreateUIElement("Scrollbar", scrollView.transform);
            RectTransform scrollbarRect = scrollbar.GetComponent<RectTransform>();
            scrollbarRect.anchorMin = new Vector2(1, 0);
            scrollbarRect.anchorMax = new Vector2(1, 1);
            scrollbarRect.pivot = new Vector2(1, 0.5f);
            scrollbarRect.sizeDelta = new Vector2(10, 0);
            
            Image scrollbarBg = scrollbar.AddComponent<Image>();
            scrollbarBg.color = new Color(0.1f, 0.1f, 0.1f, 1f);
            
            Scrollbar scrollbarComp = scrollbar.AddComponent<Scrollbar>();
            scrollbarComp.direction = Scrollbar.Direction.BottomToTop;
            
            GameObject slidingArea = CreateUIElement("SlidingArea", scrollbar.transform);
            RectTransform slidingRect = slidingArea.GetComponent<RectTransform>();
            slidingRect.anchorMin = Vector2.zero;
            slidingRect.anchorMax = Vector2.one;
            slidingRect.offsetMin = new Vector2(2, 2);
            slidingRect.offsetMax = new Vector2(-2, -2);
            
            GameObject handle = CreateUIElement("Handle", slidingArea.transform);
            RectTransform handleRect = handle.GetComponent<RectTransform>();
            handleRect.anchorMin = Vector2.zero;
            handleRect.anchorMax = Vector2.one;
            handleRect.offsetMin = Vector2.zero;
            handleRect.offsetMax = Vector2.zero;
            
            Image handleImage = handle.AddComponent<Image>();
            handleImage.color = new Color(0.4f, 0.4f, 0.5f, 1f);
            
            scrollbarComp.handleRect = handleRect;
            scrollbarComp.targetGraphic = handleImage;
            
            scrollRect.viewport = viewportRect;
            scrollRect.content = contentRect;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.verticalScrollbar = scrollbarComp;
            
            // === BOTTOM BAR ===
            GameObject bottomBar = CreateUIElement("BottomBar", panel.transform);
            bottomBar.AddComponent<LayoutElement>().preferredHeight = 40;
            
            HorizontalLayoutGroup bottomLayout = bottomBar.AddComponent<HorizontalLayoutGroup>();
            bottomLayout.spacing = 10;
            bottomLayout.childAlignment = TextAnchor.MiddleCenter;
            bottomLayout.childControlWidth = true;
            bottomLayout.childForceExpandWidth = true;
            
            // Bouton Précédent
            GameObject prevBtn = CreateUIElement("PreviousButton", bottomBar.transform);
            Image prevImage = prevBtn.AddComponent<Image>();
            prevImage.color = new Color(0.3f, 0.3f, 0.4f, 1f);
            
            Button prevButton = prevBtn.AddComponent<Button>();
            
            GameObject prevText = CreateUIElement("Text", prevBtn.transform);
            TextMeshProUGUI prevTMP = prevText.AddComponent<TextMeshProUGUI>();
            prevTMP.text = "◀ Précédent";
            prevTMP.fontSize = 14;
            prevTMP.color = Color.white;
            prevTMP.alignment = TextAlignmentOptions.Center;
            
            // Bouton Suivant
            GameObject nextBtn = CreateUIElement("NextButton", bottomBar.transform);
            Image nextImage = nextBtn.AddComponent<Image>();
            nextImage.color = new Color(0.3f, 0.3f, 0.4f, 1f);
            
            Button nextButton = nextBtn.AddComponent<Button>();
            
            GameObject nextText = CreateUIElement("Text", nextBtn.transform);
            TextMeshProUGUI nextTMP = nextText.AddComponent<TextMeshProUGUI>();
            nextTMP.text = "Suivant ▶";
            nextTMP.fontSize = 14;
            nextTMP.color = Color.white;
            nextTMP.alignment = TextAlignmentOptions.Center;
            
            // === ASSIGNER LES RÉFÉRENCES ===
            popupScript.popupPanel = panel;
            
            // Assigner le prefab du bouton
            if (buttonPrefab != null)
            {
                popupScript.buttonPrefab = buttonPrefab;
            }
            
            return popupRoot;
        }
        
        private GameObject CreateUIElement(string name, Transform parent)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            obj.AddComponent<RectTransform>();
            return obj;
        }
    }
}
#endif