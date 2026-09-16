using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Keeps the keyboard where the user put it.
///
/// A UI Toolkit text field keeps the focus long after the click that gave it, so once the agent
/// filter had been touched every later keystroke landed in that field even though the user was
/// looking somewhere else entirely. Clicking outside a focused field, or pressing Escape, hands the
/// keyboard back to the application.
/// </summary>
public static class HudFocusGuard
{
    /// <summary>Installs the guard on the root of the interface. Calling it twice does nothing.</summary>
    public static void Install(VisualElement root)
    {
        if (root == null || root.ClassListContains(InstalledClass)) return;

        root.AddToClassList(InstalledClass);

        // Runs before the click reaches its target, so the field the user is clicking on still gets
        // the focus after the previous one has been released.
        root.RegisterCallback<PointerDownEvent>(
            evt => BlurUnlessInside(root, evt.target as VisualElement),
            TrickleDown.TrickleDown);

        root.RegisterCallback<KeyDownEvent>(
            evt =>
            {
                if (evt.keyCode == KeyCode.Escape)
                    Blur(root);
            },
            TrickleDown.TrickleDown);
    }

    /// <summary>Releases the focus when the click did not land on the field that holds it.</summary>
    private static void BlurUnlessInside(VisualElement root, VisualElement target)
    {
        VisualElement field = FocusedField(root);
        if (field == null) return;

        if (target != null && (target == field || field.Contains(target))) return;

        field.Blur();
    }

    private static void Blur(VisualElement root) => FocusedField(root)?.Blur();

    /// <summary>The text field holding the keyboard, or null when the keyboard is free.</summary>
    private static VisualElement FocusedField(VisualElement root)
    {
        VisualElement focused = root?.panel?.focusController?.focusedElement as VisualElement;

        // A focused field can report one of its inner elements, so the chain is walked upwards.
        for (VisualElement element = focused; element != null; element = element.parent)
        {
            if (element is TextField) return element;
        }

        return null;
    }

    private const string InstalledClass = "hud-focus-guard";
}
