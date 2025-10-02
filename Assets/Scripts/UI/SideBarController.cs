using UnityEngine;
using System.Collections;

public class SidebarController : MonoBehaviour
{
    public RectTransform sidebar;
    public float hiddenX = -300f;
    public float visibleX = 0f;
    public float animationDuration = 0.3f;

    public bool isOpen = false;
    private Coroutine currentAnim;

    // void Start(){
    //     isOpen = !isOpen;
    //     ToggleSidebar();
    // }

    public void ToggleSidebar()
    {
        isOpen = !isOpen;

        // Si une animation est déjà en cours, on l'arrête
        if (currentAnim != null) StopCoroutine(currentAnim);

        float targetX = isOpen ? visibleX : hiddenX;
        currentAnim = StartCoroutine(AnimateSidebar(targetX));
    }

    private IEnumerator AnimateSidebar(float targetX)
    {
        Vector2 startPos = sidebar.anchoredPosition;
        Vector2 endPos = new Vector2(targetX, startPos.y);

        float elapsed = 0f;
        while (elapsed < animationDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / animationDuration;
            t = Mathf.SmoothStep(0f, 1f, t); // easing smooth
            sidebar.anchoredPosition = Vector2.Lerp(startPos, endPos, t);
            yield return null;
        }

        sidebar.anchoredPosition = endPos;
        currentAnim = null;
    }
}
