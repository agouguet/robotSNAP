Shader "Unlit/GridGround_HDRP"
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
        Tags { "RenderPipeline"="HDRenderPipeline" "RenderType"="Opaque" "Queue"="Geometry" }

        Pass
        {
            // --- CES 4 LIGNES RÈGLENT LE PROBLÈME DE TRANSPARENCE ---
            Cull Back          // Évite d'afficher l'intérieur du plan si on le retourne
            ZTest LEqual       // Test de profondeur standard (plus proche l'emporte)
            ZWrite On          // Écriture dans le Z-Buffer (indispensable pour un sol opaque)
            Blend Off          // Pas de mélange de couleurs (opaque pur)
            // ---------------------------------------------------------

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Material.hlsl"

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
                o.vertex = TransformObjectToHClip(v.vertex.xyz);
                o.worldPos = TransformObjectToWorld(v.vertex.xyz);
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                float x = i.worldPos.x / _CellSize;
                float z = i.worldPos.z / _CellSize;

                float fracX = frac(x);
                float fracZ = frac(z);

                float xLine = (fracX < _LineThickness) || (fracX > 1.0 - _LineThickness);
                float zLine = (fracZ < _LineThickness) || (fracZ > 1.0 - _LineThickness);
                float isLine = xLine || zLine;

                float4 col = lerp(_BgColor, _LineColor, isLine);
                return col;
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/InternalErrorShader"
}