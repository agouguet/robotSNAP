Shader "Unlit/HumanOutlineSinglePass"
{
    Properties
    {
        _BodyColor ("Body Color", Color) = (0.2, 0.5, 1, 0.4)
        _OutlineColor ("Outline Color", Color) = (0, 0.8, 1, 1)
        _OutlineWidth ("Outline Width", Range(0, 0.3)) = 0.1
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        LOD 100

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            ZTest LEqual

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _BodyColor;
            float4 _OutlineColor;
            float _OutlineWidth;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float3 normal : NORMAL;
                float3 viewDir : TEXCOORD1;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.normal = mul(unity_ObjectToWorld, v.normal);
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.viewDir = normalize(_WorldSpaceCameraPos - worldPos);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Calcul de contour : fort quand la normale est perpendiculaire à la vue
                float outline = 1.0 - abs(dot(i.normal, i.viewDir));
                outline = smoothstep(0.0, _OutlineWidth, outline);
                // Mélange entre couleur du corps et couleur d'outline
                float4 col = lerp(_OutlineColor, _BodyColor, outline);
                return col;
            }
            ENDCG
        }
    }
}