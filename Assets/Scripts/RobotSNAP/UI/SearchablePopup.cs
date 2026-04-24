using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System;

public class SearchablePopup : MonoBehaviour
{
    [Header("UI References")]
    public GameObject popupPanel;
    public Transform contentContainer;
    public GameObject buttonPrefab;
    public TMP_InputField searchInput;
    public Button closeButton;
    public TextMeshProUGUI titleText;

    [Header("Settings")]
    public int maxButtonsBeforeScroll = 10;

    private List<SelectorItem> _allItems = new List<SelectorItem>();
    private List<SelectorItem> _filteredItems = new List<SelectorItem>();
    private List<GameObject> _spawnedButtons = new List<GameObject>();
    private Action<SelectorItem> _onItemSelected;

    public void Initialize(List<SelectorItem> items, string title, Action<SelectorItem> onSelected)
    {
        _allItems = new List<SelectorItem>(items);
        _onItemSelected = onSelected;
        if (titleText != null) titleText.text = title;
        RefreshList();
    }

    public void Show()
    {
        if (popupPanel != null) popupPanel.SetActive(true);
        if (searchInput != null)
        {
            searchInput.text = "";
            searchInput.Select();
            searchInput.ActivateInputField();
        }
    }

    public void Close() => popupPanel?.SetActive(false);

    private void Start()
    {
        if (closeButton != null) closeButton.onClick.AddListener(Close);
        if (searchInput != null) searchInput.onValueChanged.AddListener(OnSearchChanged);
    }

    private void OnSearchChanged(string filter) => RefreshList(filter);

    private void RefreshList(string filter = "")
    {
        // Filtrer
        _filteredItems.Clear();
        string lowerFilter = filter?.ToLower() ?? "";
        foreach (var item in _allItems)
        {
            if (string.IsNullOrEmpty(lowerFilter) || item.displayName.ToLower().Contains(lowerFilter))
                _filteredItems.Add(item);
        }

        // Nettoyer les anciens boutons
        foreach (var btn in _spawnedButtons) Destroy(btn);
        _spawnedButtons.Clear();

        // Créer les nouveaux
        foreach (var item in _filteredItems)
        {
            GameObject btnObj = Instantiate(buttonPrefab, contentContainer);
            _spawnedButtons.Add(btnObj);

            var btnText = btnObj.GetComponentInChildren<TextMeshProUGUI>();
            if (btnText != null) btnText.text = item.displayName;

            var btn = btnObj.GetComponent<Button>();
            if (btn != null)
            {
                // capture locale
                var capturedItem = item;
                btn.onClick.AddListener(() => {
                    _onItemSelected?.Invoke(capturedItem);
                    Close();
                });
            }
        }

        // Ajuster la hauteur du conteneur
        AdjustContainerHeight(_filteredItems.Count);
    }

    private void AdjustContainerHeight(int count)
    {
        if (contentContainer == null) return;
        RectTransform rect = contentContainer.GetComponent<RectTransform>();
        if (rect != null && buttonPrefab != null)
        {
            RectTransform prefabRect = buttonPrefab.GetComponent<RectTransform>();
            float buttonHeight = prefabRect.rect.height;
            float spacing = contentContainer.GetComponent<VerticalLayoutGroup>()?.spacing ?? 5f;
            float totalHeight = count * (buttonHeight + spacing);
            totalHeight = Mathf.Min(totalHeight, 400f);
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, totalHeight);
        }
    }
}