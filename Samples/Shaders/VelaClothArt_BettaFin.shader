Shader "Vela/Samples/BettaFin"
{
    Properties
    {
        [Header(Membrane)]
        _BaseColor ("Base Colour", Color) = (0.62, 0.42, 0.36, 1)
        _TipColor ("Tip Colour", Color) = (0.93, 0.87, 0.80, 1)
        _RayColor ("Ray Colour", Color) = (0.22, 0.13, 0.11, 1)
        _TipDarkColor ("Tip Shadow Colour", Color) = (0.12, 0.15, 0.18, 1)
        _TipDarken ("Tip Darkening", Range(0, 1)) = 0.7
        _TipDarkWidth ("Tip Darkening Width", Range(0.01, 1)) = 0.3

        [Header(Fan)]
        _BaseWidth ("Base Width", Range(0.05, 1)) = 0.3
        _FanCurve ("Fan Curve", Range(0.2, 4)) = 0.7
        _EdgeFray ("Edge Fray", Range(0, 0.5)) = 0.15
        _FrayScale ("Fray Scale", Float) = 6

        [Header(Rays)]
        _RayCount ("Rays Per Radian", Float) = 14
        _RayWobble ("Ray Wobble", Range(0, 2)) = 0.4
        _RaySplit ("Ray Forking", Range(0, 1)) = 0.6
        _RayStrength ("Ray Line Strength", Range(0, 1)) = 0.6
        _RayWidth ("Ray Line Width", Range(0.01, 0.5)) = 0.12
        _PleatDepth ("Pleat Depth", Range(0, 2)) = 0.8
        _FoldDarken ("Fold Darkening", Range(0, 1)) = 0.3

        [Header(Light)]
        _Transmission ("Transmission Tint", Color) = (1.0, 0.85, 0.7, 1)
        _Diffuse ("Diffuse", Range(0, 2)) = 0.5
        _Backlight ("Backlight", Range(0, 4)) = 0.6
        _SheenColor ("Sheen Colour", Color) = (1.0, 0.95, 0.9, 1)
        _SheenPower ("Sheen Power", Range(1, 256)) = 48
        _SheenStrength ("Sheen Strength", Range(0, 2)) = 0.6
        _RimStrength ("Rim Strength", Range(0, 2)) = 0.5
        _RimPower ("Rim Power", Range(0.5, 8)) = 3
        _BackDim ("Back Face Dim", Range(0, 1)) = 0.85
        _Ambient ("Ambient", Range(0, 1)) = 0.06
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "AlphaTest" }
        Cull Off
        ZWrite On

        HLSLINCLUDE
        float _BaseWidth;
        float _FanCurve;
        float _EdgeFray;
        float _FrayScale;

        float VelaFinHash21(float2 p)
        {
            p = frac(p * float2(123.34, 456.21));
            p += dot(p, p + 45.32);
            return frac(p.x * p.y);
        }

        float VelaFinNoise(float2 p)
        {
            float2 i = floor(p);
            float2 f = frac(p);
            f = f * f * (3.0 - 2.0 * f);
            return lerp(lerp(VelaFinHash21(i), VelaFinHash21(i + float2(1, 0)), f.x),
                        lerp(VelaFinHash21(i + float2(0, 1)), VelaFinHash21(i + 1.0), f.x), f.y);
        }

        float VelaFinFbm(float2 p)
        {
            return 0.5 * VelaFinNoise(p) + 0.25 * VelaFinNoise(p * 2.0) + 0.125 * VelaFinNoise(p * 4.0);
        }

        // Positive inside the fan: narrow at the pinned edge, an arc around the pivot at the tip, every edge frayed.
        float VelaFinMask(float2 uv, float2 sheet)
        {
            float t = 1.0 - uv.y;
            float halfWidth = lerp(_BaseWidth, 1.0, pow(saturate(t), _FanCurve)) * 0.5;
            float side = (VelaFinFbm(float2(uv.y * _FrayScale, uv.x > 0.5 ? 3.7 : 9.1)) - 0.5) * _EdgeFray;
            float dSide = halfWidth + side - abs(uv.x - 0.5);
            float2 p = float2((uv.x - 0.5) * sheet.x, t * sheet.y);
            float tip = (VelaFinFbm(float2(atan2(p.x, p.y) * _FrayScale * 0.5, 17.3)) - 0.5) * _EdgeFray * 2.0;
            float dTip = 1.0 - length(p) / sheet.y - (_EdgeFray + tip) * 0.5;
            return min(dSide, dTip);
        }
        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "ForwardOnly" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            #include "VelaClothArtCommon.hlsl"

            float4 _BaseColor;
            float4 _TipColor;
            float4 _RayColor;
            float4 _TipDarkColor;
            float _TipDarken;
            float _TipDarkWidth;
            float _RayCount;
            float _RayWobble;
            float _RaySplit;
            float _RayStrength;
            float _RayWidth;
            float _PleatDepth;
            float _FoldDarken;
            float4 _Transmission;
            float _Diffuse;
            float _Backlight;
            float4 _SheenColor;
            float _SheenPower;
            float _SheenStrength;
            float _RimStrength;
            float _RimPower;
            float _BackDim;
            float _Ambient;

            Varyings vert(Attributes a, uint vid : SV_VertexID)
            {
                ClothVertexData d = VelaReadClothVertex(vid, a);
                return VelaPackVaryings(d);
            }

            float4 frag(Varyings i, bool isFrontFace : SV_IsFrontFace) : SV_Target
            {
                ClothSurface s = VelaUnpackSurface(i, isFrontFace);
                clip(VelaFinMask(s.uv, _VelaSheet.xy));

                float2 p = s.uv * _VelaSheet.xy - _VelaSheet.zw * _VelaSheet.xy;
                float t = saturate(length(p) / _VelaSheet.y);
                float ang = atan2(p.x, -p.y);
                float wobble = (VelaFbm(p * 2.0, 3) - 0.5) * _RayWobble;
                float r = ang * _RayCount + wobble;
                float fork = smoothstep(0.3, 0.8, t) * _RaySplit;
                float ridge = lerp(abs(frac(r) - 0.5) * 2.0, abs(frac(r * 2.0) - 0.5) * 2.0, fork);
                float rayLine = smoothstep(1.0 - _RayWidth * 2.0, 1.0, ridge);

                float3 pleatDir = s.tangentWS * cos(ang) + s.bitangentWS * sin(ang);
                float3 n = normalize(s.normalWS + pleatDir * sign(frac(r) - 0.5) * _PleatDepth * (1.0 - rayLine));

                float3 membrane = lerp(_BaseColor.rgb, _TipColor.rgb, t);
                float tipMask = smoothstep(1.0 - _TipDarkWidth, 1.0, t + (VelaFbm(p * 3.0, 3) - 0.5) * 0.5);
                membrane = lerp(membrane, _RayColor.rgb, rayLine * _RayStrength);
                membrane *= 1.0 - saturate(abs(s.curvature) * 0.15) * _FoldDarken;

                float diff = VelaHalfLambert(n, s.lightDirWS);
                float through = saturate(dot(-n, s.lightDirWS));
                float sheen = VelaKajiyaKay(normalize(pleatDir), s.lightDirWS, s.viewDirWS, _SheenPower) * _SheenStrength;
                float rim = VelaFresnel(n, s.viewDirWS, _RimPower) * _RimStrength;

                float3 lit = _Ambient + _Diffuse * diff * s.lightColor + through * _Backlight * _Transmission.rgb;
                float3 col = membrane * lit;
                col += _SheenColor.rgb * s.lightColor * sheen * (1.0 - rayLine * 0.7);
                col += _TipColor.rgb * rim;
                col = lerp(col, _TipDarkColor.rgb * lit, tipMask * _TipDarken);
                col *= s.front ? 1.0 : 1.0 - _BackDim * 0.5;
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
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            float4 _VelaSheet;

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            ShadowVaryings vert(float3 positionOS : POSITION, float2 uv : TEXCOORD0)
            {
                ShadowVaryings o;
                o.positionCS = TransformWorldToHClip(TransformObjectToWorld(positionOS));
                o.uv = uv;
                return o;
            }

            void frag(ShadowVaryings i)
            {
                clip(VelaFinMask(i.uv, _VelaSheet.xy));
            }
            ENDHLSL
        }
    }

    Fallback Off
}
