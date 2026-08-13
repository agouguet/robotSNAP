using UnityEngine;
using UnityEngine.UIElements;

public class MinimapRenderer : ImmediateModeElement
{
    public RenderTexture minimapRT;

    protected override void ImmediateRepaint()
    {
        if (minimapRT == null) return;
        GUI.DrawTexture(new Rect(0, 0, layout.width, layout.height), minimapRT);
    }
}