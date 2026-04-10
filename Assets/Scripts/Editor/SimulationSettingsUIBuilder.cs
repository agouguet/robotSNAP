// #if UNITY_EDITOR
// using UnityEngine;
// using UnityEditor;
// using UnityEngine.UI;
// using TMPro;
// using System.Collections.Generic;

// namespace RobotSNAP.UI.Editor
// {
//     /// <summary>
//     /// Builder pour créer automatiquement le panneau UI complet
//     /// </summary>
//     public class SimulationSettingsUIBuilder : EditorWindow
//     {
//         [MenuItem("RobotSNAP/Create Simulation Settings UI")]
//         public static void CreateUI()
//         {
//             // Créer le canvas
//             GameObject canvasGO = new GameObject("SimulationSettingsCanvas");
//             Canvas canvas = canvasGO.AddComponent<Canvas>();
//             canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            
//             CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
//             scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
//             scaler.referenceResolution = new Vector2(1920, 1080);
            
//             canvasGO.AddComponent<GraphicRaycaster>();
            
//             // Créer le panneau principal
//             GameObject panelGO = CreateSettingsPanel(canvasGO);
            
//             // Ajouter le script APRÈS avoir créé le panel
//             SimulationSettingsUI uiScript = panelGO.AddComponent<SimulationSettingsUI>();
            
//             // Créer tous les éléments UI et assigner les références
//             CreateAllUIElements(panelGO, uiScript);
            
//             // Créer le bouton toggle
//             CreateToggleButton(canvasGO, uiScript);
            
//             // Sauvegarder le prefab
//             string path = "Assets/Prefabs/UI/SimulationSettingsUI.prefab";
//             if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
//                 AssetDatabase.CreateFolder("Assets", "Prefabs");
//             if (!AssetDatabase.IsValidFolder("Assets/Prefabs/UI"))
//                 AssetDatabase.CreateFolder("Assets/Prefabs", "UI");
            
//             PrefabUtility.SaveAsPrefabAsset(canvasGO, path);
//             DestroyImmediate(canvasGO);
            
//             Debug.Log($"UI créée à : {path}");
//             AssetDatabase.Refresh();
            
//             EditorUtility.DisplayDialog("Succès", "L'interface de paramètres a été créée avec succès !\nTrouvez-la dans Assets/Prefabs/UI/", "OK");
//         }
        
//         private static GameObject CreateSettingsPanel(GameObject parent)
//         {
//             GameObject panelGO = new GameObject("SettingsPanel");
//             panelGO.transform.SetParent(parent.transform);
            
//             RectTransform panelRect = panelGO.AddComponent<RectTransform>();
//             panelRect.anchorMin = new Vector2(0.5f, 0.5f);
//             panelRect.anchorMax = new Vector2(0.5f, 0.5f);
//             panelRect.sizeDelta = new Vector2(500, 700);
            
//             Image panelImage = panelGO.AddComponent<Image>();
//             panelImage.color = new Color(0.1f, 0.1f, 0.1f, 0.95f);
            
//             // ScrollRect
//             GameObject scrollGO = new GameObject("ScrollView");
//             scrollGO.transform.SetParent(panelGO.transform);
//             RectTransform scrollRectTransform = scrollGO.AddComponent<RectTransform>();
//             scrollRectTransform.anchorMin = Vector2.zero;
//             scrollRectTransform.anchorMax = Vector2.one;
//             scrollRectTransform.offsetMin = new Vector2(10, 60);
//             scrollRectTransform.offsetMax = new Vector2(-10, -10);
            
//             ScrollRect scroll = scrollGO.AddComponent<ScrollRect>();
//             scroll.horizontal = false;
//             scroll.vertical = true;
//             scroll.movementType = ScrollRect.MovementType.Elastic;
//             scroll.inertia = true;
            
//             // Viewport
//             GameObject viewportGO = new GameObject("Viewport");
//             viewportGO.transform.SetParent(scrollGO.transform);
//             RectTransform viewportRect = viewportGO.AddComponent<RectTransform>();
//             viewportRect.anchorMin = Vector2.zero;
//             viewportRect.anchorMax = Vector2.one;
//             viewportRect.sizeDelta = Vector2.zero;
            
//             // Mask sans image visible
//             Mask mask = viewportGO.AddComponent<Mask>();
//             mask.showMaskGraphic = false;
            
//             // Image transparente nécessaire pour le mask mais invisible
//             Image viewportImage = viewportGO.AddComponent<Image>();
//             viewportImage.color = new Color(0, 0, 0, 0);
//             viewportImage.raycastTarget = false;
            
//             // Content
//             GameObject contentGO = new GameObject("Content");
//             contentGO.transform.SetParent(viewportGO.transform);
//             RectTransform contentRect = contentGO.AddComponent<RectTransform>();
//             contentRect.anchorMin = new Vector2(0, 1);
//             contentRect.anchorMax = new Vector2(1, 1);
//             contentRect.pivot = new Vector2(0.5f, 1);
//             contentRect.offsetMin = Vector2.zero;
//             contentRect.offsetMax = Vector2.zero;
            
//             // Layout du content
//             VerticalLayoutGroup layout = contentGO.AddComponent<VerticalLayoutGroup>();
//             layout.padding = new RectOffset(15, 15, 15, 15);
//             layout.spacing = 10;
//             layout.childAlignment = TextAnchor.UpperCenter;
//             layout.childControlWidth = true;
//             layout.childControlHeight = true;
//             layout.childForceExpandWidth = true;
//             layout.childForceExpandHeight = false;
            
//             ContentSizeFitter fitter = contentGO.AddComponent<ContentSizeFitter>();
//             fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
//             fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            
//             scroll.viewport = viewportRect;
//             scroll.content = contentRect;
            
//             // Scrollbar verticale
//             CreateScrollbar(scrollGO, scroll);
            
//             return panelGO;
//         }
        
//         private static void CreateScrollbar(GameObject scrollView, ScrollRect scrollRect)
//         {
//             // Créer le Scrollbar
//             GameObject scrollbarGO = new GameObject("Scrollbar Vertical");
//             scrollbarGO.transform.SetParent(scrollView.transform);
            
//             RectTransform scrollbarRect = scrollbarGO.AddComponent<RectTransform>();
//             scrollbarRect.anchorMin = new Vector2(1, 0);
//             scrollbarRect.anchorMax = new Vector2(1, 1);
//             scrollbarRect.pivot = new Vector2(1, 0.5f);
//             scrollbarRect.sizeDelta = new Vector2(20, 0);
//             scrollbarRect.anchoredPosition = Vector2.zero;
            
//             // Background du scrollbar
//             Image bgImage = scrollbarGO.AddComponent<Image>();
//             bgImage.color = new Color(0.1f, 0.1f, 0.1f);
            
//             // Handle Area
//             GameObject handleAreaGO = new GameObject("Handle Area");
//             handleAreaGO.transform.SetParent(scrollbarGO.transform);
//             RectTransform handleAreaRect = handleAreaGO.AddComponent<RectTransform>();
//             handleAreaRect.anchorMin = Vector2.zero;
//             handleAreaRect.anchorMax = Vector2.one;
//             handleAreaRect.sizeDelta = Vector2.zero;
            
//             // Handle
//             GameObject handleGO = new GameObject("Handle");
//             handleGO.transform.SetParent(handleAreaGO.transform);
//             RectTransform handleRect = handleGO.AddComponent<RectTransform>();
//             handleRect.anchorMin = Vector2.zero;
//             handleRect.anchorMax = Vector2.one;
//             handleRect.sizeDelta = new Vector2(0, 0);
            
//             Image handleImage = handleGO.AddComponent<Image>();
//             handleImage.color = new Color(0.5f, 0.5f, 0.5f);
            
//             // Configurer le Scrollbar component
//             Scrollbar scrollbar = scrollbarGO.AddComponent<Scrollbar>();
//             scrollbar.handleRect = handleRect;
//             scrollbar.targetGraphic = handleImage;
//             scrollbar.direction = Scrollbar.Direction.TopToBottom;  // Sens haut vers bas
            
//             // Lier au ScrollRect
//             scrollRect.verticalScrollbar = scrollbar;
//             scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
//             scrollRect.verticalScrollbarSpacing = 3;
//         }
        
//         private static void CreateAllUIElements(GameObject panelGO, SimulationSettingsUI uiScript)
//         {
//             Transform content = panelGO.transform.Find("ScrollView/Viewport/Content");
//             if (content == null)
//             {
//                 Debug.LogError("Content not found!");
//                 return;
//             }
            
//             Debug.Log("Creating UI elements in Content...");
            
//             // === ENVIRONMENT ===
//             CreateSectionHeader(content, "ENVIRONMENT");
//             uiScript.environmentCountInput = CreateInputField(content, "Number of Environments", "10");
//             uiScript.environmentSpacingInput = CreateInputField(content, "Environment Spacing", "100");
            
//             // === SPAWN ===
//             CreateSectionHeader(content, "SPAWN");
//             uiScript.minHumansInput = CreateInputField(content, "Min Humans", "0");
//             uiScript.maxHumansInput = CreateInputField(content, "Max Humans", "5");
//             uiScript.minAgentDistanceInput = CreateInputField(content, "Min Agent Distance", "2");
            
//             // === ROBOT ===
//             CreateSectionHeader(content, "ROBOT");
//             uiScript.robotControlModeDropdown = CreateDropdown(content, "Control Mode", new[] { "Keyboard", "ROS", "Hybrid" });
//             uiScript.robotLaserSampleDropdown = CreateDropdown(content, "Laser Samples", new[] { "36", "72", "180", "360", "720" });
            
//             // === HUMAN ===
//             CreateSectionHeader(content, "HUMAN");
//             uiScript.humanDefaultSpeedInput = CreateInputField(content, "Default Speed", "0.8");
//             uiScript.humanMaxSpeedInput = CreateInputField(content, "Max Speed", "1.0");
//             uiScript.humanControllerTypeDropdown = CreateDropdown(content, "Controller Type", new[] { "SFM", "ONNX", "Hybrid" });
//             uiScript.humanInteractionRadiusSlider = CreateSlider(content, "Interaction Radius", 1f, 10f, 5f);
            
//             // Find value label for slider
//             if (uiScript.humanInteractionRadiusSlider != null)
//             {
//                 Transform valueLabel = uiScript.humanInteractionRadiusSlider.transform.parent.Find("Header/ValueLabel");
//                 if (valueLabel != null)
//                     uiScript.humanInteractionRadiusValue = valueLabel.GetComponent<TextMeshProUGUI>();
//             }
            
//             // === SIMULATION ===
//             CreateSectionHeader(content, "SIMULATION");
//             uiScript.timeScaleSlider = CreateSlider(content, "Time Scale", 0.1f, 5f, 2f);
            
//             // Find value label for slider
//             if (uiScript.timeScaleSlider != null)
//             {
//                 Transform valueLabel = uiScript.timeScaleSlider.transform.parent.Find("Header/ValueLabel");
//                 if (valueLabel != null)
//                     uiScript.timeScaleValue = valueLabel.GetComponent<TextMeshProUGUI>();
//             }
            
//             uiScript.pauseToggle = CreateToggle(content, "Pause Simulation");
//             uiScript.inferenceModeToggle = CreateToggle(content, "Inference Mode");
            
//             // === ROS ===
//             CreateSectionHeader(content, "ROS");
//             uiScript.rosPrefixInput = CreateInputField(content, "ROS Prefix", "");
//             uiScript.rosPublishFrequencyInput = CreateInputField(content, "Publish Frequency", "10");
//             uiScript.rosPublishToggle = CreateToggle(content, "Enable ROS Publishing");
            
//             // === STATUS ===
//             CreateSectionHeader(content, "STATUS");
//             uiScript.statusText = CreateStatusText(content);
            
//             // Boutons (passer uiScript qui n'est pas null)
//             CreateActionButtons(panelGO, uiScript);
            
//             Debug.Log("All UI elements created successfully");
//         }
        
//         private static void CreateSectionHeader(Transform parent, string title)
//         {
//             GameObject headerGO = new GameObject($"Header_{title}");
//             headerGO.transform.SetParent(parent);
            
//             RectTransform headerRect = headerGO.AddComponent<RectTransform>();
//             headerRect.sizeDelta = new Vector2(0, 35);
            
//             // Ajouter un fond pour le header
//             Image headerBg = headerGO.AddComponent<Image>();
//             headerBg.color = new Color(0.2f, 0.2f, 0.2f);
            
//             GameObject textObject = new GameObject("Text");
//             textObject.transform.SetParent(headerGO.transform);
//             TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
//             text.text = title;
//             text.fontSize = 18;
//             text.fontStyle = FontStyles.Bold;
//             text.color = new Color(1f, 0.8f, 0.2f);
//             text.alignment = TextAlignmentOptions.Center;
            
//             LayoutElement layout = headerGO.AddComponent<LayoutElement>();
//             layout.minHeight = 35;
//             layout.preferredHeight = 35;
//         }
        
//         private static TMP_InputField CreateInputField(Transform parent, string label, string defaultValue)
//         {
//             GameObject container = new GameObject($"Input_{label.Replace(" ", "_")}");
//             container.transform.SetParent(parent);
            
//             // Ajouter un fond pour le conteneur
//             Image containerBg = container.AddComponent<Image>();
//             containerBg.color = new Color(0.2f, 0.2f, 0.2f);
            
//             RectTransform containerRect = container.GetComponent<RectTransform>();
//             containerRect.sizeDelta = new Vector2(0, 40);
            
//             HorizontalLayoutGroup layout = container.AddComponent<HorizontalLayoutGroup>();
//             layout.padding = new RectOffset(10, 10, 8, 8);
//             layout.spacing = 10;
//             layout.childForceExpandWidth = true;
//             layout.childForceExpandHeight = false;
            
//             // Label
//             GameObject labelGO = new GameObject("Label");
//             labelGO.transform.SetParent(container.transform);
//             RectTransform labelRect = labelGO.AddComponent<RectTransform>();
//             labelRect.sizeDelta = new Vector2(180, 24);
            
//             TextMeshProUGUI labelText = labelGO.AddComponent<TextMeshProUGUI>();
//             labelText.text = label + ":";
//             labelText.fontSize = 14;
//             labelText.color = Color.white;
//             labelText.alignment = TextAlignmentOptions.MidlineLeft;
            
//             // Input
//             GameObject inputGO = new GameObject("Input");
//             inputGO.transform.SetParent(container.transform);
//             RectTransform inputRect = inputGO.AddComponent<RectTransform>();
//             inputRect.sizeDelta = new Vector2(200, 30);
            
//             TMP_InputField input = inputGO.AddComponent<TMP_InputField>();
            
//             // Text
//             GameObject textGO = new GameObject("Text");
//             textGO.transform.SetParent(inputGO.transform);
//             RectTransform textRect = textGO.AddComponent<RectTransform>();
//             textRect.anchorMin = Vector2.zero;
//             textRect.anchorMax = Vector2.one;
//             textRect.offsetMin = Vector2.zero;
//             textRect.offsetMax = Vector2.zero;
            
//             TextMeshProUGUI tmpText = textGO.AddComponent<TextMeshProUGUI>();
//             tmpText.fontSize = 14;
//             tmpText.alignment = TextAlignmentOptions.MidlineLeft;
            
//             // Placeholder
//             GameObject placeholderGO = new GameObject("Placeholder");
//             placeholderGO.transform.SetParent(inputGO.transform);
//             RectTransform placeholderRect = placeholderGO.AddComponent<RectTransform>();
//             placeholderRect.anchorMin = Vector2.zero;
//             placeholderRect.anchorMax = Vector2.one;
//             placeholderRect.offsetMin = Vector2.zero;
//             placeholderRect.offsetMax = Vector2.zero;
            
//             TextMeshProUGUI placeholder = placeholderGO.AddComponent<TextMeshProUGUI>();
//             placeholder.text = defaultValue;
//             placeholder.fontSize = 14;
//             placeholder.color = Color.gray;
//             placeholder.alignment = TextAlignmentOptions.MidlineLeft;
            
//             input.text = defaultValue;
//             input.textComponent = tmpText;
//             input.placeholder = placeholder;
            
//             LayoutElement layoutElement = container.AddComponent<LayoutElement>();
//             layoutElement.minHeight = 40;
//             layoutElement.preferredHeight = 40;
            
//             return input;
//         }
        
//         private static TMP_Dropdown CreateDropdown(Transform parent, string label, string[] options)
//         {
//             GameObject container = new GameObject($"Dropdown_{label.Replace(" ", "_")}");
//             container.transform.SetParent(parent);
            
//             // Ajouter un fond
//             Image containerBg = container.AddComponent<Image>();
//             containerBg.color = new Color(0.2f, 0.2f, 0.2f);
            
//             RectTransform containerRect = container.GetComponent<RectTransform>();
//             containerRect.sizeDelta = new Vector2(0, 40);
            
//             HorizontalLayoutGroup layout = container.AddComponent<HorizontalLayoutGroup>();
//             layout.padding = new RectOffset(10, 10, 8, 8);
//             layout.spacing = 10;
//             layout.childForceExpandWidth = true;
//             layout.childForceExpandHeight = false;
            
//             // Label
//             GameObject labelGO = new GameObject("Label");
//             labelGO.transform.SetParent(container.transform);
//             RectTransform labelRect = labelGO.AddComponent<RectTransform>();
//             labelRect.sizeDelta = new Vector2(180, 24);
            
//             TextMeshProUGUI labelText = labelGO.AddComponent<TextMeshProUGUI>();
//             labelText.text = label + ":";
//             labelText.fontSize = 14;
//             labelText.color = Color.white;
//             labelText.alignment = TextAlignmentOptions.MidlineLeft;
            
//             // Dropdown
//             GameObject dropdownGO = new GameObject("Dropdown");
//             dropdownGO.transform.SetParent(container.transform);
//             RectTransform dropdownRect = dropdownGO.AddComponent<RectTransform>();
//             dropdownRect.sizeDelta = new Vector2(200, 30);
            
//             TMP_Dropdown dropdown = dropdownGO.AddComponent<TMP_Dropdown>();
            
//             // Caption
//             GameObject captionGO = new GameObject("Caption");
//             captionGO.transform.SetParent(dropdownGO.transform);
//             RectTransform captionRect = captionGO.AddComponent<RectTransform>();
//             captionRect.anchorMin = Vector2.zero;
//             captionRect.anchorMax = Vector2.one;
//             captionRect.offsetMin = Vector2.zero;
//             captionRect.offsetMax = Vector2.zero;
            
//             TextMeshProUGUI caption = captionGO.AddComponent<TextMeshProUGUI>();
//             caption.fontSize = 14;
//             caption.alignment = TextAlignmentOptions.MidlineLeft;
//             dropdown.captionText = caption;
            
//             // Template
//             GameObject templateGO = new GameObject("Template");
//             templateGO.transform.SetParent(dropdownGO.transform);
//             RectTransform templateRect = templateGO.AddComponent<RectTransform>();
//             templateRect.sizeDelta = new Vector2(200, 150);
//             templateGO.SetActive(false);
//             dropdown.template = templateRect;
            
//             dropdown.ClearOptions();
//             dropdown.AddOptions(new List<string>(options));
            
//             LayoutElement layoutElement = container.AddComponent<LayoutElement>();
//             layoutElement.minHeight = 40;
//             layoutElement.preferredHeight = 40;
            
//             return dropdown;
//         }
        
//         private static Slider CreateSlider(Transform parent, string label, float minValue, float maxValue, float defaultValue)
//         {
//             GameObject container = new GameObject($"Slider_{label.Replace(" ", "_")}");
//             container.transform.SetParent(parent);
            
//             // Ajouter un fond
//             Image containerBg = container.AddComponent<Image>();
//             containerBg.color = new Color(0.2f, 0.2f, 0.2f);
            
//             RectTransform containerRect = container.GetComponent<RectTransform>();
//             containerRect.sizeDelta = new Vector2(0, 65);
            
//             VerticalLayoutGroup layout = container.AddComponent<VerticalLayoutGroup>();
//             layout.padding = new RectOffset(10, 10, 8, 8);
//             layout.spacing = 5;
//             layout.childForceExpandWidth = true;
//             layout.childForceExpandHeight = false;
            
//             // Header with label and value
//             GameObject headerGO = new GameObject("Header");
//             headerGO.transform.SetParent(container.transform);
//             RectTransform headerRect = headerGO.AddComponent<RectTransform>();
//             headerRect.sizeDelta = new Vector2(0, 25);
            
//             HorizontalLayoutGroup headerLayout = headerGO.AddComponent<HorizontalLayoutGroup>();
//             headerLayout.childForceExpandWidth = true;
            
//             GameObject labelGO = new GameObject("Label");
//             labelGO.transform.SetParent(headerGO.transform);
//             RectTransform labelRect = labelGO.AddComponent<RectTransform>();
//             labelRect.sizeDelta = new Vector2(200, 25);
            
//             TextMeshProUGUI labelText = labelGO.AddComponent<TextMeshProUGUI>();
//             labelText.text = label + ":";
//             labelText.fontSize = 14;
//             labelText.color = Color.white;
            
//             GameObject valueGO = new GameObject("ValueLabel");
//             valueGO.transform.SetParent(headerGO.transform);
//             RectTransform valueRect = valueGO.AddComponent<RectTransform>();
//             valueRect.sizeDelta = new Vector2(80, 25);
            
//             TextMeshProUGUI valueText = valueGO.AddComponent<TextMeshProUGUI>();
//             valueText.text = defaultValue.ToString("F1");
//             valueText.fontSize = 14;
//             valueText.color = Color.white;
//             valueText.alignment = TextAlignmentOptions.MidlineRight;
            
//             // Slider
//             GameObject sliderGO = new GameObject("Slider");
//             sliderGO.transform.SetParent(container.transform);
//             RectTransform sliderRect = sliderGO.AddComponent<RectTransform>();
//             sliderRect.sizeDelta = new Vector2(0, 20);
            
//             Slider slider = sliderGO.AddComponent<Slider>();
            
//             // Background
//             GameObject bgGO = new GameObject("Background");
//             bgGO.transform.SetParent(sliderGO.transform);
//             RectTransform bgRect = bgGO.AddComponent<RectTransform>();
//             bgRect.anchorMin = Vector2.zero;
//             bgRect.anchorMax = Vector2.one;
//             bgRect.offsetMin = Vector2.zero;
//             bgRect.offsetMax = Vector2.zero;
            
//             Image bgImage = bgGO.AddComponent<Image>();
//             bgImage.color = new Color(0.2f, 0.2f, 0.2f);
            
//             // Fill Area
//             GameObject fillAreaGO = new GameObject("Fill Area");
//             fillAreaGO.transform.SetParent(sliderGO.transform);
//             RectTransform fillAreaRect = fillAreaGO.AddComponent<RectTransform>();
//             fillAreaRect.anchorMin = new Vector2(0, 0.25f);
//             fillAreaRect.anchorMax = new Vector2(1, 0.75f);
//             fillAreaRect.offsetMin = Vector2.zero;
//             fillAreaRect.offsetMax = Vector2.zero;
            
//             GameObject fillGO = new GameObject("Fill");
//             fillGO.transform.SetParent(fillAreaGO.transform);
//             RectTransform fillRect = fillGO.AddComponent<RectTransform>();
//             fillRect.anchorMin = Vector2.zero;
//             fillRect.anchorMax = Vector2.zero;
//             fillRect.sizeDelta = Vector2.zero;
            
//             Image fillImage = fillGO.AddComponent<Image>();
//             fillImage.color = new Color(0.2f, 0.6f, 0.2f);
            
//             // Handle Slide Area
//             GameObject handleAreaGO = new GameObject("Handle Slide Area");
//             handleAreaGO.transform.SetParent(sliderGO.transform);
//             RectTransform handleAreaRect = handleAreaGO.AddComponent<RectTransform>();
//             handleAreaRect.anchorMin = Vector2.zero;
//             handleAreaRect.anchorMax = Vector2.one;
//             handleAreaRect.offsetMin = Vector2.zero;
//             handleAreaRect.offsetMax = Vector2.zero;
            
//             // Handle
//             GameObject handleGO = new GameObject("Handle");
//             handleGO.transform.SetParent(handleAreaGO.transform);
//             RectTransform handleRect = handleGO.AddComponent<RectTransform>();
//             handleRect.sizeDelta = new Vector2(20, 20);
            
//             Image handleImage = handleGO.AddComponent<Image>();
//             handleImage.color = Color.white;
            
//             slider.fillRect = fillRect;
//             slider.handleRect = handleRect;
//             slider.targetGraphic = handleImage;
//             slider.minValue = minValue;
//             slider.maxValue = maxValue;
//             slider.value = defaultValue;
            
//             slider.onValueChanged.AddListener((v) => { if (valueText != null) valueText.text = v.ToString("F1"); });
            
//             LayoutElement layoutElement = container.AddComponent<LayoutElement>();
//             layoutElement.minHeight = 65;
//             layoutElement.preferredHeight = 65;
            
//             return slider;
//         }
        
//         private static Toggle CreateToggle(Transform parent, string label)
//         {
//             GameObject container = new GameObject($"Toggle_{label.Replace(" ", "_")}");
//             container.transform.SetParent(parent);
            
//             // Ajouter un fond
//             Image containerBg = container.AddComponent<Image>();
//             containerBg.color = new Color(0.2f, 0.2f, 0.2f);
            
//             RectTransform containerRect = container.GetComponent<RectTransform>();
//             containerRect.sizeDelta = new Vector2(0, 40);
            
//             HorizontalLayoutGroup layout = container.AddComponent<HorizontalLayoutGroup>();
//             layout.padding = new RectOffset(10, 10, 8, 8);
//             layout.spacing = 10;
//             layout.childForceExpandWidth = false;
//             layout.childForceExpandHeight = false;
            
//             // Toggle
//             GameObject toggleGO = new GameObject("Toggle");
//             toggleGO.transform.SetParent(container.transform);
//             RectTransform toggleRect = toggleGO.AddComponent<RectTransform>();
//             toggleRect.sizeDelta = new Vector2(30, 30);
            
//             Toggle toggle = toggleGO.AddComponent<Toggle>();
            
//             // Background
//             GameObject bgGO = new GameObject("Background");
//             bgGO.transform.SetParent(toggleGO.transform);
//             RectTransform bgRect = bgGO.AddComponent<RectTransform>();
//             bgRect.anchorMin = Vector2.zero;
//             bgRect.anchorMax = Vector2.one;
//             bgRect.offsetMin = Vector2.zero;
//             bgRect.offsetMax = Vector2.zero;
            
//             Image bgImage = bgGO.AddComponent<Image>();
//             bgImage.color = Color.gray;
            
//             // Checkmark
//             GameObject checkGO = new GameObject("Checkmark");
//             checkGO.transform.SetParent(bgGO.transform);
//             RectTransform checkRect = checkGO.AddComponent<RectTransform>();
//             checkRect.anchorMin = new Vector2(0.2f, 0.2f);
//             checkRect.anchorMax = new Vector2(0.8f, 0.8f);
//             checkRect.sizeDelta = Vector2.zero;
            
//             Image checkImage = checkGO.AddComponent<Image>();
//             checkImage.color = Color.green;
            
//             toggle.targetGraphic = bgImage;
//             toggle.graphic = checkImage;
            
//             // Label
//             GameObject labelGO = new GameObject("Label");
//             labelGO.transform.SetParent(container.transform);
//             RectTransform labelRect = labelGO.AddComponent<RectTransform>();
//             labelRect.sizeDelta = new Vector2(250, 30);
            
//             TextMeshProUGUI labelText = labelGO.AddComponent<TextMeshProUGUI>();
//             labelText.text = label;
//             labelText.fontSize = 14;
//             labelText.color = Color.white;
//             labelText.alignment = TextAlignmentOptions.MidlineLeft;
            
//             LayoutElement layoutElement = container.AddComponent<LayoutElement>();
//             layoutElement.minHeight = 40;
//             layoutElement.preferredHeight = 40;
            
//             return toggle;
//         }
        
//         private static TextMeshProUGUI CreateStatusText(Transform parent)
//         {
//             GameObject statusGO = new GameObject("StatusText");
//             statusGO.transform.SetParent(parent);
            
//             // Ajouter un fond
//             Image statusBg = statusGO.AddComponent<Image>();
//             statusBg.color = new Color(0.2f, 0.2f, 0.2f);
            
//             RectTransform statusRect = statusGO.GetComponent<RectTransform>();
//             statusRect.sizeDelta = new Vector2(0, 50);
            
//             GameObject textObject = new GameObject("Text");
//             textObject.transform.SetParent(statusGO.transform);
//             TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
//             text.text = "✓ Ready";
//             text.fontSize = 14;
//             text.color = Color.green;
//             text.alignment = TextAlignmentOptions.Center;
            
//             LayoutElement layoutElement = statusGO.AddComponent<LayoutElement>();
//             layoutElement.minHeight = 50;
//             layoutElement.preferredHeight = 50;
            
//             return text;
//         }
        
//         private static void CreateActionButtons(GameObject panelGO, SimulationSettingsUI uiScript)
//         {
//             if (panelGO == null)
//             {
//                 Debug.LogError("panelGO is null in CreateActionButtons");
//                 return;
//             }
            
//             if (uiScript == null)
//             {
//                 Debug.LogError("uiScript is null in CreateActionButtons");
//                 return;
//             }
            
//             // Apply Button
//             GameObject applyBtnGO = new GameObject("ApplyButton");
//             applyBtnGO.transform.SetParent(panelGO.transform);
//             RectTransform applyRect = applyBtnGO.AddComponent<RectTransform>();
//             applyRect.anchorMin = new Vector2(0, 0);
//             applyRect.anchorMax = new Vector2(0.5f, 0);
//             applyRect.pivot = new Vector2(0.5f, 0);
//             applyRect.offsetMin = new Vector2(15, 15);
//             applyRect.offsetMax = new Vector2(-5, 55);
            
//             Button applyBtn = applyBtnGO.AddComponent<Button>();
//             Image applyImage = applyBtnGO.AddComponent<Image>();
//             applyImage.color = new Color(0.2f, 0.6f, 0.2f);
            
//             GameObject applyTextObject = new GameObject("Text");
//             applyTextObject.transform.SetParent(applyBtnGO.transform);
//             TextMeshProUGUI applyText = applyTextObject.AddComponent<TextMeshProUGUI>();
//             applyText.text = "APPLY";
//             applyText.fontSize = 16;
//             applyText.color = Color.white;
//             applyText.alignment = TextAlignmentOptions.Center;
//             applyText.rectTransform.anchorMin = Vector2.zero;
//             applyText.rectTransform.anchorMax = Vector2.one;
//             applyText.rectTransform.offsetMin = Vector2.zero;
//             applyText.rectTransform.offsetMax = Vector2.zero;
            
//             // Reset Button
//             GameObject resetBtnGO = new GameObject("ResetButton");
//             resetBtnGO.transform.SetParent(panelGO.transform);
//             RectTransform resetRect = resetBtnGO.AddComponent<RectTransform>();
//             resetRect.anchorMin = new Vector2(0.5f, 0);
//             resetRect.anchorMax = new Vector2(1, 0);
//             resetRect.pivot = new Vector2(0.5f, 0);
//             resetRect.offsetMin = new Vector2(5, 15);
//             resetRect.offsetMax = new Vector2(-15, 55);
            
//             Button resetBtn = resetBtnGO.AddComponent<Button>();
//             Image resetImage = resetBtnGO.AddComponent<Image>();
//             resetImage.color = new Color(0.6f, 0.2f, 0.2f);
            
//             GameObject resetTextObject = new GameObject("Text");
//             resetTextObject.transform.SetParent(resetBtnGO.transform);
//             TextMeshProUGUI resetText = resetTextObject.AddComponent<TextMeshProUGUI>();
//             resetText.text = "RESET";
//             resetText.fontSize = 16;
//             resetText.color = Color.white;
//             resetText.alignment = TextAlignmentOptions.Center;
//             resetText.rectTransform.anchorMin = Vector2.zero;
//             resetText.rectTransform.anchorMax = Vector2.one;
//             resetText.rectTransform.offsetMin = Vector2.zero;
//             resetText.rectTransform.offsetMax = Vector2.zero;
            
//             uiScript.applyButton = applyBtn;
//             uiScript.resetButton = resetBtn;
            
//             Debug.Log("Action buttons created successfully");
//         }
        
//         private static void CreateToggleButton(GameObject canvasGO, SimulationSettingsUI uiScript)
//         {
//             if (canvasGO == null)
//             {
//                 Debug.LogError("canvasGO is null in CreateToggleButton");
//                 return;
//             }
            
//             if (uiScript == null)
//             {
//                 Debug.LogError("uiScript is null in CreateToggleButton");
//                 return;
//             }
            
//             GameObject toggleBtnGO = new GameObject("ToggleButton");
//             toggleBtnGO.transform.SetParent(canvasGO.transform);
//             RectTransform toggleRect = toggleBtnGO.AddComponent<RectTransform>();
//             toggleRect.anchorMin = new Vector2(0, 0);
//             toggleRect.anchorMax = new Vector2(0, 0);
//             toggleRect.pivot = new Vector2(0, 0);
//             toggleRect.anchoredPosition = new Vector2(15, 15);
//             toggleRect.sizeDelta = new Vector2(50, 50);
            
//             Image toggleImage = toggleBtnGO.AddComponent<Image>();
//             toggleImage.color = new Color(0.2f, 0.2f, 0.2f, 0.8f);
            
//             Button toggleBtn = toggleBtnGO.AddComponent<Button>();
            
//             GameObject toggleTextObject = new GameObject("Text");
//             toggleTextObject.transform.SetParent(toggleBtnGO.transform);
//             TextMeshProUGUI toggleText = toggleTextObject.AddComponent<TextMeshProUGUI>();
//             toggleText.text = "⚙️";
//             toggleText.fontSize = 28;
//             toggleText.alignment = TextAlignmentOptions.Center;
//             toggleText.rectTransform.anchorMin = Vector2.zero;
//             toggleText.rectTransform.anchorMax = Vector2.one;
//             toggleText.rectTransform.offsetMin = Vector2.zero;
//             toggleText.rectTransform.offsetMax = Vector2.zero;
            
//             uiScript.toggleButton = toggleBtn;
            
//             // Find settings panel
//             Transform settingsPanel = canvasGO.transform.Find("SettingsPanel");
//             if (settingsPanel != null)
//             {
//                 uiScript.settingsPanel = settingsPanel.gameObject;
//                 Debug.Log("SettingsPanel assigned to uiScript");
//             }
//             else
//             {
//                 Debug.LogWarning("SettingsPanel not found in canvas");
//             }
//         }
//     }
// }
// #endif