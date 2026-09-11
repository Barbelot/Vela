Shader "Vela/Samples/SumiE"
{
    Properties
    {
        _PaperColor ("Paper Colour", Color) = (0.93, 0.89, 0.80, 1)
        _InkColor ("Ink Colour", Color) = (0.06, 0.05, 0.07, 1)
        _LiningColor ("Lining Colour (back)", Color) = (0.78, 0.70, 0.58, 1)
        _PaperTex ("Paper Grain", 2D) = "gray" {}
        _HatchTex ("Hatching (R light, G mid, B dark)", 2D) = "black" {}
        _PaperScale ("Paper Tiles Per Metre", Float) = 2
        _HatchScale ("Hatch Tiles Per Metre", Float) = 4
        _PoolCurvature ("Full Pool At Curvature (1/m)", Range(0.1, 10)) = 3
        _PoolStrength ("Pool Strength", Range(0, 1)) = 0.8
        _Smear ("Smear (uv per m/s)", Range(0, 0.5)) = 0.08
        _RimInk ("Rim Ink", Range(0, 1)) = 0.6
        _ToneSoftness ("Tone Softness", Range(0.01, 0.5)) = 0.15
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

            #include "VelaClothArtCommon.hlsl"

            float4 _PaperColor;
            float4 _InkColor;
            float4 _LiningColor;
            sampler2D _PaperTex;
            sampler2D _HatchTex;
            float _PaperScale;
            float _HatchScale;
            float _PoolCurvature;
            float _PoolStrength;
            float _Smear;
            float _RimInk;
            float _ToneSoftness;

            Varyings vert(Attributes a, uint vid : SV_VertexID)
            {
                return VelaPackVaryings(VelaReadClothVertex(vid, a));
            }

            float4 frag(Varyings i, bool isFrontFace : SV_IsFrontFace) : SV_Target
            {
                ClothSurface s = VelaUnpackSurface(i, isFrontFace);

                float2 sheetUv = s.uv * _VelaSheet.xy;
                float2 vt = VelaTangentVelocity(s) * _Smear;
                float paper = tex2D(_PaperTex, sheetUv * _PaperScale).r;
                float3 hatch = tex2D(_HatchTex, sheetUv * _HatchScale + vt * (paper - 0.5) * 4.0).rgb;

                float tone = VelaHalfLambert(s.normalWS, s.lightDirWS);
                float ink = lerp(hatch.b, hatch.g, smoothstep(0.33 - _ToneSoftness, 0.33 + _ToneSoftness, tone));
                ink = lerp(ink, hatch.r, smoothstep(0.66 - _ToneSoftness, 0.66 + _ToneSoftness, tone));
                ink = lerp(ink, 0.0, smoothstep(0.9, 1.0, tone));

                float pool = saturate(s.curvature / _PoolCurvature) * _PoolStrength * (0.6 + 0.4 * paper);
                float rim = VelaFresnel(s.normalWS, s.viewDirWS, 5.0) * _RimInk;
                float inkAmount = saturate(ink + pool + rim + length(vt) * (1.0 - paper) * 0.5);

                float3 base = (s.front ? _PaperColor.rgb : _LiningColor.rgb) * (0.85 + 0.15 * paper);
                float3 col = lerp(base, _InkColor.rgb, inkAmount);
                return float4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            Cull Off
            ZClip [_ZClip]
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex VelaShadowVert
            #pragma fragment VelaShadowFrag
            #pragma target 4.5
            #include "VelaClothArtShadow.hlsl"
            ENDHLSL
        }
    }

    Fallback Off
}
