Shader "Vela/Samples/Backdrop"
{
    Properties
    {
        _TopColor ("Top", Color) = (0.985, 0.98, 0.972, 1)
        _BottomColor ("Bottom", Color) = (0.93, 0.92, 0.905, 1)
        _Curve ("Falloff", Range(0.25, 4)) = 1.6
        _Vignette ("Vignette", Range(0, 0.5)) = 0.08
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Cull Off
        ZWrite On

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "ForwardOnly" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            #include "UnityCG.cginc"

            float4 _TopColor;
            float4 _BottomColor;
            float _Curve;
            float _Vignette;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(float4 positionOS : POSITION, float2 uv : TEXCOORD0)
            {
                Varyings o;
                o.positionCS = UnityObjectToClipPos(positionOS);
                o.uv = uv;
                return o;
            }

            float4 frag(Varyings i) : SV_Target
            {
                float t = pow(saturate(i.uv.y), _Curve);
                float3 col = lerp(_BottomColor.rgb, _TopColor.rgb, t);
                col *= 1.0 - _Vignette * smoothstep(0.15, 0.7, length((i.uv - 0.5) * float2(1.0, 0.75)));
                return float4(col, 1);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
