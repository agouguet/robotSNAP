using UnityEngine;
using UnityEngine.UIElements;

public class MinimapRenderer : ImmediateModeElement
{
    public RenderTexture minimapRT;

    protected override void ImmediateRepaint()
    {
        if (minimapRT == null) return;

        // contentRect donne la taille exacte de CET élément (et non du parent)
        Rect rect = contentRect;

        if (rect.width <= 0 || rect.height <= 0) return;

        // Option 1 : Si vous voulez juste remplir le cadre (étire l'image)
        // GUI.DrawTexture(new Rect(0, 0, rect.width, rect.height), minimapRT, ScaleMode.StretchToFill);

        // Option 2 : Garder le ratio 1:1 et centrer (Recommandé)
        float rtAspect = (float)minimapRT.width / minimapRT.height;
        float containerAspect = rect.width / rect.height;

        float w = rect.width;
        float h = rect.height;

        // Ajustement pour garder le ratio
        if (containerAspect > rtAspect)
            w = rect.height * rtAspect;
        else
            h = rect.width / rtAspect;

        // Calcul du décalage pour un centrage parfait
        float x = (rect.width - w) * 0.5f;
        float y = (rect.height - h) * 0.5f;

        // Dessin final
        GUI.DrawTexture(new Rect(x, y, w, h), minimapRT);
    }
}