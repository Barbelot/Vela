Shader "Vela/Samples/Voile"
{
    Properties
    {
        [Header(Cloth)]
        _ClothColor ("Cloth Colour", Color) = (0.05, 0.05, 0.06, 1)
        _Opacity ("Opacity Face On", Range(0.005, 1)) = 0.35
        _MinFacing ("Grazing Clamp", Range(0.02, 1)) = 0.12
        _FoldGather ("Fold Gathering", Range(0, 2)) = 0.4
        _HemWidth ("Rolled Hem Width (m)", Range(0, 0.1)) = 0.012
        _HemLayers ("Rolled Hem Layers", Range(0, 4)) = 2

        [Header(Weave)]
        _WeaveScale ("Threads Per Metre", Float) = 160
        _WeaveOpenness ("Weave Openness", Range(0, 1)) = 0.35

        [Header(Pleats)]
        _PleatFrequency ("Pleats Per Metre (0 is flat)", Float) = 0
        _PleatDepth ("Pleat Depth", Range(0, 2)) = 0.6
        _PleatShade ("Pleat Shading", Range(0, 1)) = 0.25

        [Header(Light)]
        _Diffuse ("Diffuse", Range(0, 2)) = 0.55
        _Transmission ("Transmission Tint", Color) = (1, 1, 1, 1)
        _Backlight ("Backlight", Range(0, 4)) = 0.8
        _SheenColor ("Sheen Colour", Color) = (1, 1, 1, 1)
        _SheenPower ("Sheen Power", Range(1, 256)) = 64
        _SheenStrength ("Sheen Strength", Range(0, 2)) = 0.4
        _RimStrength ("Rim Strength", Range(0, 2)) = 0.3
        _RimPower ("Rim Power", Range(0.5, 8)) = 4
        _BackDim ("Back Face Dim", Range(0, 1)) = 0.1
        _Ambient ("Ambient", Range(0, 2)) = 0.55
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" }
        Cull Off
        ZWrite Off
        Blend One OneMinusSrcAlpha

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "ForwardOnly" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            #include "VelaClothArtCommon.hlsl"

            float4 _ClothColor;
            float _Opacity;
            float _MinFacing;
            float _FoldGather;
            float _HemWidth;
            float _HemLayers;
            float _WeaveScale;
            float _WeaveOpenness;
            float _PleatFrequency;
            float _PleatDepth;
            float _PleatShade;
            float _Diffuse;
            float4 _Transmission;
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
                return VelaPackVaryings(VelaReadClothVertex(vid, a));
            }

            float4 frag(Varyings i, bool isFrontFace : SV_IsFrontFace) : SV_Target
            {
                ClothSurface s = VelaUnpackSurface(i, isFrontFace);
                float2 sheetUv = s.uv * _VelaSheet.xy;

                // -0.5 at one crease, +0.5 at the next: the facet slope, and its own sign for the shading.
                float tri = abs(frac(sheetUv.x * _PleatFrequency) - 0.5) * 2.0 - 0.5;
                float pleat = _PleatFrequency > 0.0 ? _PleatDepth : 0.0;
                float3 n = normalize(s.normalWS + s.tangentWS * tri * pleat);

                // Beer-Lambert through a sheet of constant density: the view ray crosses 1/cos(theta) of it,
                // so the silhouette goes solid while the face-on middle stays sheer. This is the whole look.
                float alpha = 1.0 - pow(1.0 - _Opacity, 1.0 / max(abs(dot(n, s.viewDirWS)), _MinFacing));

                // The selvedge is rolled and stitched, so the last centimetre is several layers thick.
                float2 edge = min(sheetUv, _VelaSheet.xy - sheetUv);
                float hem = 1.0 - smoothstep(0.0, _HemWidth, min(edge.x, edge.y));
                alpha = 1.0 - pow(1.0 - alpha, 1.0 + hem * _HemLayers);
                alpha *= 1.0 + saturate(abs(s.curvature) * 0.1) * _FoldGather;

                float2 w = sheetUv * _WeaveScale;
                float2 fw = fwidth(w);
                float thread = max(abs(frac(w.x) - 0.5), abs(frac(w.y) - 0.5)) * 2.0;
                alpha *= 1.0 - _WeaveOpenness * (1.0 - hem) * saturate(1.0 - max(fw.x, fw.y)) * (1.0 - thread);
                alpha = saturate(alpha);

                float diff = VelaHalfLambert(n, s.lightDirWS);
                float through = saturate(dot(-n, s.lightDirWS));
                float sheen = VelaKajiyaKay(s.tangentWS, s.lightDirWS, s.viewDirWS, _SheenPower) * _SheenStrength;
                float rim = VelaFresnel(n, s.viewDirWS, _RimPower) * _RimStrength;

                float3 lit = _Ambient + _Diffuse * diff * s.lightColor + through * _Backlight * _Transmission.rgb;
                float3 col = _ClothColor.rgb * lit;
                col += _SheenColor.rgb * s.lightColor * sheen;
                col += _SheenColor.rgb * rim;
                col *= 1.0 - _PleatShade * (1.0 - abs(tri) * 2.0);
                col *= s.front ? 1.0 : 1.0 - _BackDim;

                return float4(col * alpha, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
