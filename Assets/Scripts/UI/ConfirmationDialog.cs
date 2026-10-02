using System;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// A modal the user has to answer before the surface behind it is usable again.
///
/// It exists for the actions that cannot be undone - clearing a session, deleting an episode - where one
/// click must not be the whole decision. The dialog states what is about to happen, puts the safer
/// alternative next to the destructive button, and closes without acting when the user changes their mind:
/// the cancel button, Escape and a click on the backdrop all mean the same thing.
///
/// The component owns no policy. It reports which button was pressed and leaves the work to the caller,
/// which is what lets the same dialog ask about a whole session and about a single episode without knowing
/// either one. A caller that needs an input - a folder path - adds it to <see cref="Body"/>.
///
/// Confirming closes it, whatever the caller then does with the confirmation; the alternative does not,
/// because a caller may have something to report back before the reader can go on - see
/// <see cref="Alternate"/> - and closing it is the handler's decision.
/// </summary>
public sealed class ConfirmationDialog : VisualElement
{
    /// <summary>Raised when the destructive action was asked for.</summary>
    public event Action Confirmed;

    /// <summary>
    /// Raised for the safer alternative - "export first". The dialog stays open on purpose, so the user can
    /// read what happened and still decide; closing it belongs to the handler when it has finished.
    /// </summary>
    public event Action Alternate;

    /// <summary>Raised when the dialog was closed without acting.</summary>
    public event Action Cancelled;

    private readonly Label _status;
    private readonly VisualElement _body;
    private readonly Button _confirm;
    private readonly Button _alternate;
    private readonly Button _cancel;

    public ConfirmationDialog(
        string title,
        string message,
        string confirmText,
        string alternateText = null,
        bool destructive = true)
    {
        AddToClassList("confirmation-overlay");
        focusable = true;

        var panel = new VisualElement();
        panel.AddToClassList("confirmation-panel");
        Add(panel);

        var titleLabel = new Label(title);
        titleLabel.AddToClassList("confirmation-title");
        panel.Add(titleLabel);

        var messageLabel = new Label(message);
        messageLabel.AddToClassList("confirmation-message");
        panel.Add(messageLabel);

        _status = new Label();
        _status.AddToClassList("confirmation-status");
        _status.style.display = DisplayStyle.None;
        panel.Add(_status);

        _body = new VisualElement();
        _body.AddToClassList("confirmation-body");
        panel.Add(_body);

        var footer = new VisualElement();
        footer.AddToClassList("confirmation-footer");

        _cancel = new Button(Dismiss) { text = "Cancel" };
        _cancel.AddToClassList("confirmation-button");
        _cancel.AddToClassList("confirmation-button-cancel");
        footer.Add(_cancel);

        _alternate = new Button(InvokeAlternate) { text = alternateText ?? "Export first" };
        _alternate.AddToClassList("confirmation-button");
        _alternate.AddToClassList("confirmation-button-alternate");
        _alternate.style.display = string.IsNullOrEmpty(alternateText) ? DisplayStyle.None : DisplayStyle.Flex;
        footer.Add(_alternate);

        _confirm = new Button(Accept) { text = confirmText };
        _confirm.AddToClassList("confirmation-button");
        _confirm.AddToClassList("confirmation-button-confirm");
        if (destructive)
            _confirm.AddToClassList("destructive");
        footer.Add(_confirm);

        panel.Add(footer);

        // A press that lands on the backdrop rather than on the panel is the user dismissing the dialog. Key
        // presses are caught in trickle-down so Escape still reaches the dialog when a field inside it holds
        // the focus and would otherwise swallow it.
        RegisterCallback<PointerDownEvent>(OnBackdropPressed);
        RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
    }

    /// <summary>Where a caller adds what the dialog has to carry: a field, a warning, a list.</summary>
    public VisualElement Body => _body;

    /// <summary>The button that asks for the destructive action, so a caller can enable or label it.</summary>
    public Button ConfirmButton => _confirm;

    /// <summary>The button that asks for the alternative, hidden when the dialog was built without one.</summary>
    public Button AlternateButton => _alternate;

    /// <summary>The button that closes the dialog without acting.</summary>
    public Button CancelButton => _cancel;

    /// <summary>Shows the dialog over <paramref name="host"/> and hands it the keyboard.</summary>
    public void Show(VisualElement host)
    {
        if (host == null || parent != null)
            return;

        host.Add(this);
        Focus();
    }

    /// <summary>
    /// The destructive path; this is what the confirm button does. It closes the dialog after the work has
    /// been reported, because a question the reader has answered is not one to keep in front of them: a
    /// modal that stayed up would have them answer it again by reflex.
    /// </summary>
    public void Accept()
    {
        Confirmed?.Invoke();
        Close();
    }

    /// <summary>Closes without acting; this is what the cancel button, Escape and the backdrop do.</summary>
    public void Dismiss()
    {
        Cancelled?.Invoke();
        Close();
    }

    /// <summary>
    /// Leaves the tree without saying which decision closed the dialog. Confirming and dismissing both end
    /// here; only <see cref="Dismiss"/> also reports that nothing was done, so a caller that confirms never
    /// hears its own dialog read as a cancellation.
    /// </summary>
    private void Close() => RemoveFromHierarchy();

    /// <summary>
    /// Writes one line of feedback inside the dialog, for an alternative that has to run while the dialog
    /// stays open. An error is tinted so a failed export is not mistaken for a successful one.
    /// </summary>
    public void SetStatus(string text, bool error = false)
    {
        _status.text = text ?? string.Empty;
        _status.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
        _status.EnableInClassList("confirmation-status-error", error);
    }

    private void InvokeAlternate() => Alternate?.Invoke();

    private void OnBackdropPressed(PointerDownEvent evt)
    {
        if (evt.target == this)
            Dismiss();
    }

    private void OnKeyDown(KeyDownEvent evt)
    {
        if (evt.keyCode != KeyCode.Escape)
            return;

        evt.StopPropagation();
        Dismiss();
    }
}
