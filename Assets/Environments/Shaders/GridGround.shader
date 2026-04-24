Shader "Unlit/GridGround_Unlit"
{
    Properties
    {
        _BgColor ("Background Color", Color) = (0.04, 0.1, 0.16, 1)
        _LineColor ("Line Color", Color) = (0.29, 0.55, 1, 1)
        _CellSize ("Cell Size (meters)", Float) = 1.0
        _LineThickness ("Line Thickness", Range(0.001, 0.1)) = 0.03
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float3 worldPos : TEXCOORD0;
            };

            float4 _BgColor;
            float4 _LineColor;
            float _CellSize;
            float _LineThickness;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                // Position dans le monde (pour que la grille reste fixe)
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Coordonnées X et Z
                float x = i.worldPos.x / _CellSize;
                float z = i.worldPos.z / _CellSize;

                // Parties fractionnaires (position dans la cellule)
                float fracX = frac(x);
                float fracZ = frac(z);

                // Détection des lignes (bords des cellules)
                float xLine = (fracX < _LineThickness) || (fracX > 1.0 - _LineThickness);
                float zLine = (fracZ < _LineThickness) || (fracZ > 1.0 - _LineThickness);
                float isLine = xLine || zLine;

                // Mélange des couleurs
                fixed4 col = lerp(_BgColor, _LineColor, isLine);
                return col;
            }
            ENDCG
        }
    }
}