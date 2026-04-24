using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class SlideMenu : MonoBehaviour
{
    public RectTransform menuPanel;
    public float targetWidth = 200f;      // largeur déployée
    public float animationDuration = 0.3f;
    public Button iconButton;
    public Button settingsButton;
    public Button resetButton;

    private bool isOpen = false;
    private Coroutine currentAnimation;

    void Start()
    {
        // Initial : largeur = 0
        menuPanel.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 0);
        iconButton.onClick.AddListener(ToggleMenu);
        settingsButton.onClick.AddListener(OnSettings);
        resetButton.onClick.AddListener(OnReset);
    }

    void ToggleMenu()
    {
        if (currentAnimation != null) StopCoroutine(currentAnimation);
        currentAnimation = StartCoroutine(AnimateWidth(isOpen ? 0f : targetWidth));
        isOpen = !isOpen;
    }

    IEnumerator AnimateWidth(float targetWidth)
    {
        float startWidth = menuPanel.rect.width;
        float elapsed = 0f;
        while (elapsed < animationDuration)
        {
            float t = elapsed / animationDuration;
            float newWidth = Mathf.Lerp(startWidth, targetWidth, t);
            menuPanel.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, newWidth);
            elapsed += Time.deltaTime;
            yield return null;
        }
        menuPanel.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, targetWidth);
    }

    void OnSettings() { Debug.Log("Settings clicked"); /* ouvrir panneau settings */ }
    void OnReset() { Debug.Log("Reset clicked"); /* appel reset */ }
}