using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using RobotSNAP.Agents;

namespace RobotSNAP.UI
{
    public class TargetSelectionPopup : MonoBehaviour
    {
        [Header("Popup References")]
        public GameObject popupPanel;
        public Transform contentContainer;
        public GameObject buttonPrefab;
        public TMP_InputField searchInput;
        public Button closeButton;
        public TextMeshProUGUI titleText;
        public TextMeshProUGUI currentTargetText;
        
        [Header("Settings")]
        public Color robotColor = Color.cyan;
        public Color humanColor = Color.green;
        public Color otherColor = Color.gray;
        public int maxButtonsBeforeScroll = 10;
        
        private CameraController _cameraController;
        private List<Transform> _currentTargets = new List<Transform>();
        private List<GameObject> _spawnedButtons = new List<GameObject>();
        private Transform _selectedTarget;
        
        #region Unity Lifecycle
        
        private void Start()
        {
            FindReferences();
            SetupCallbacks();
            
            if (popupPanel != null)
                popupPanel.SetActive(false);
        }
        
        private void OnDestroy()
        {
            if (_cameraController != null)
            {
                _cameraController.OnTargetsUpdated -= OnTargetsUpdated;
                _cameraController.OnFollowTargetChanged -= OnFollowTargetChanged;
            }
            
            if (closeButton != null)
                closeButton.onClick.RemoveListener(ClosePopup);
            
            if (searchInput != null)
                searchInput.onValueChanged.RemoveListener(OnSearchChanged);
        }
        
        #endregion
        
        #region Initialization
        
        private void FindReferences()
        {
            _cameraController = FindObjectOfType<CameraController>();
            
            if (_cameraController != null)
            {
                _cameraController.OnTargetsUpdated += OnTargetsUpdated;
                _cameraController.OnFollowTargetChanged += OnFollowTargetChanged;
            }
        }
        
        private void SetupCallbacks()
        {
            if (closeButton != null)
                closeButton.onClick.AddListener(ClosePopup);
            
            if (searchInput != null)
                searchInput.onValueChanged.AddListener(OnSearchChanged);
        }
        
        #endregion
        
        #region Popup Control
        
        public void ShowPopup()
        {
            if (popupPanel == null) return;
            
            // Rafraîchir la liste des cibles
            if (_cameraController != null)
            {
                _cameraController.RefreshFollowableTargets();
                _currentTargets = _cameraController.GetFollowableTargets();
            }
            
            RefreshButtonList();
            
            // Mettre à jour le titre avec le nombre de cibles
            if (titleText != null)
                titleText.text = $"Sélectionner une cible ({_currentTargets.Count})";
            
            // Afficher la cible actuelle
            UpdateCurrentTargetDisplay();
            
            popupPanel.SetActive(true);
            
            // Focus sur la recherche
            if (searchInput != null)
            {
                searchInput.text = "";
                searchInput.Select();
                searchInput.ActivateInputField();
            }
        }
        
        public void ClosePopup()
        {
            if (popupPanel != null)
                popupPanel.SetActive(false);
        }
        
        public void TogglePopup()
        {
            if (popupPanel != null && popupPanel.activeSelf)
                ClosePopup();
            else
                ShowPopup();
        }
        
        #endregion
        
        #region Button Management
        
        private void RefreshButtonList(string searchFilter = "")
        {
            // Nettoyer les anciens boutons
            foreach (var btn in _spawnedButtons)
            {
                if (btn != null) Destroy(btn);
            }
            _spawnedButtons.Clear();
            
            if (contentContainer == null || buttonPrefab == null) return;
            
            // Filtrer les cibles
            List<Transform> filteredTargets = FilterTargets(searchFilter);

            Debug.Log(filteredTargets.Count);
            
            // Créer les boutons
            foreach (var target in filteredTargets)
            {
                CreateTargetButton(target);
            }
            
            // Ajuster la hauteur du conteneur si nécessaire
            AdjustContainerHeight(filteredTargets.Count);
        }
        
        private List<Transform> FilterTargets(string filter)
        {
            if (string.IsNullOrEmpty(filter))
                return _currentTargets;
            
            List<Transform> filtered = new List<Transform>();
            string lowerFilter = filter.ToLower();
            
            foreach (var target in _currentTargets)
            {
                if (target == null) continue;
                
                // Recherche par nom
                if (target.name.ToLower().Contains(lowerFilter))
                {
                    filtered.Add(target);
                    continue;
                }
                
                // Recherche par tag
                if (target.tag.ToLower().Contains(lowerFilter))
                {
                    filtered.Add(target);
                    continue;
                }
                
                // Recherche par composants
                if (target.GetComponent<HumanAgent>() != null && "human".Contains(lowerFilter))
                {
                    filtered.Add(target);
                }
                else if (target.CompareTag("Robot") && "robot".Contains(lowerFilter))
                {
                    filtered.Add(target);
                }
            }
            
            return filtered;
        }
        
        private void CreateTargetButton(Transform target)
        {
            GameObject buttonObj = Instantiate(buttonPrefab, contentContainer);
            _spawnedButtons.Add(buttonObj);
            
            // Configurer le texte
            TextMeshProUGUI buttonText = buttonObj.GetComponentInChildren<TextMeshProUGUI>();
            if (buttonText != null)
            {
                string typeIcon = GetTypeIcon(target);
                buttonText.text = $"{typeIcon} {target.name}";
                buttonText.color = GetTargetColor(target);
            }
            
            // Configurer le bouton
            Button button = buttonObj.GetComponent<Button>();
            if (button != null)
            {
                button.onClick.AddListener(() => OnTargetSelected(target));
                
                // Surligner si c'est la cible actuelle
                if (_cameraController != null && 
                    _cameraController.GetCurrentFollowTarget() == target)
                {
                    ColorBlock colors = button.colors;
                    colors.normalColor = new Color(0.3f, 0.5f, 0.3f);
                    button.colors = colors;
                }
            }
            
            // Ajouter info supplémentaire au survol (optionnel)
            AddTooltipInfo(buttonObj, target);
        }
        
        private string GetTypeIcon(Transform target)
        {
            if (target.CompareTag("Robot"))
                return "🤖";
            else if (target.CompareTag("Human") || target.GetComponent<HumanAgent>() != null)
                return "🚶";
            else if (target.CompareTag("Agent"))
                return "🔷";
            else
                return "📦";
        }
        
        private Color GetTargetColor(Transform target)
        {
            if (target.CompareTag("Robot"))
                return robotColor;
            else if (target.CompareTag("Human") || target.GetComponent<HumanAgent>() != null)
                return humanColor;
            else
                return otherColor;
        }
        
        private void AddTooltipInfo(GameObject buttonObj, Transform target)
        {
            // Ajouter des infos comme la distance, l'ID, etc.
            string tooltip = $"Tag: {target.tag}\n";
            
            var human = target.GetComponent<HumanAgent>();
            if (human != null)
            {
                tooltip += $"ID: {human.agentId}\n";
            }
            
            if (_cameraController != null)
            {
                float distance = Vector3.Distance(
                    _cameraController.transform.position, 
                    target.position
                );
                tooltip += $"Distance: {distance:F1}m";
            }
            
            // Ajouter un composant Tooltip si vous en avez un
            // ou utiliser un TextMeshPro qui s'affiche au survol
        }
        
        private void AdjustContainerHeight(int buttonCount)
        {
            if (contentContainer == null) return;
            
            RectTransform rect = contentContainer.GetComponent<RectTransform>();
            if (rect != null && buttonPrefab != null)
            {
                RectTransform prefabRect = buttonPrefab.GetComponent<RectTransform>();
                if (prefabRect != null)
                {
                    float buttonHeight = prefabRect.rect.height;
                    float spacing = contentContainer.GetComponent<VerticalLayoutGroup>()?.spacing ?? 5f;
                    float totalHeight = buttonCount * (buttonHeight + spacing);
                    
                    // Limiter la hauteur maximale
                    totalHeight = Mathf.Min(totalHeight, 400f);
                    rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, totalHeight);
                }
            }
        }
        
        #endregion
        
        #region Callbacks
        
        private void OnTargetSelected(Transform target)
        {
            if (_cameraController != null)
            {
                _cameraController.SetFollowTarget(target);
                _selectedTarget = target;
                
                UpdateCurrentTargetDisplay();
            }
            
            // Fermer la popup après sélection (optionnel)
            // ClosePopup();
            
            // Ou juste mettre à jour les boutons pour montrer la sélection
            RefreshButtonList(searchInput?.text ?? "");
        }
        
        private void OnTargetsUpdated(List<Transform> targets)
        {
            _currentTargets = targets;
            
            if (popupPanel != null && popupPanel.activeSelf)
            {
                RefreshButtonList(searchInput?.text ?? "");
                
                if (titleText != null)
                    titleText.text = $"Sélectionner une cible ({targets.Count})";
            }
        }
        
        private void OnFollowTargetChanged(Transform target)
        {
            _selectedTarget = target;
            UpdateCurrentTargetDisplay();
        }
        
        private void OnSearchChanged(string searchText)
        {
            RefreshButtonList(searchText);
        }
        
        private void UpdateCurrentTargetDisplay()
        {
            if (currentTargetText != null && _cameraController != null)
            {
                Transform current = _cameraController.GetCurrentFollowTarget();
                if (current != null)
                {
                    string icon = GetTypeIcon(current);
                    currentTargetText.text = $"Actuel: {icon} {current.name}";
                    currentTargetText.color = GetTargetColor(current);
                }
                else
                {
                    currentTargetText.text = "Aucune cible";
                    currentTargetText.color = Color.white;
                }
            }
        }
        
        #endregion
        
        #region Public Methods
        
        public void CycleNext()
        {
            if (_cameraController != null)
                _cameraController.CycleFollowTarget(1);
        }
        
        public void CyclePrevious()
        {
            if (_cameraController != null)
                _cameraController.CycleFollowTarget(-1);
        }
        
        public void RefreshTargets()
        {
            if (_cameraController != null)
                _cameraController.RefreshFollowableTargets();
        }
        
        #endregion
    }
}