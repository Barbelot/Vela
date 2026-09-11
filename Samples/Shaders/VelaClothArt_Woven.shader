Shader "Vela/Samples/Woven"
{
    Properties
    {
        _WarpColor ("Warp Colour", Color) = (0.55, 0.12, 0.10, 1)
        _WeftColor ("Weft Colour", Color) = (0.72, 0.58, 0.32, 1)
        _BackingColor ("Backing Colour", Color) = (0.07, 0.05, 0.04, 1)
        _FibreTex ("Fibre Noise", 2D) = "gray" {}
        _ThreadsPerMeter ("Threads Per Metre", Float) = 40
        _ThreadWidth ("Thread Width", Range(0.3, 1)) = 0.8
        _OpenStrain ("Threads Part At Strain", Range(0.005, 0.2)) = 0.06
        _CompressDarken ("Compression Darkening", Range(0, 3)) = 1.5
        _SheenColor ("Sheen Colour", Color) = (1, 0.92, 0.75, 1)
        _SheenPower ("Sheen Power", Range(1, 200)) = 40
        _SheenStrength ("Sheen Strength", Range(0, 2)) = 0.6
        _Ambient ("Ambient", Range(0, 1)) = 0.25
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

            float4 _WarpColor;
            float4 _WeftColor;
            float4 _BackingColor;
            sampler2D _FibreTex;
            float _ThreadsPerMeter;
            float _ThreadWidth;
            float _OpenStrain;
            float _CompressDarken;
            float4 _SheenColor;
            float _SheenPower;
            float _SheenStrength;
            float _Ambient;

            Varyings vert(Attributes a, uint vid : SV_VertexID)
            {
                return VelaPackVaryings(VelaReadClothVertex(vid, a));
            }

            float4 frag(Varyings i, bool isFrontFace : SV_IsFrontFace) : SV_Target
            {
                ClothSurface s = VelaUnpackSurface(i, isFrontFace);

                float2 t = s.uv * _VelaSheet.xy * _ThreadsPerMeter;
                float2 cell = floor(t);
                float2 fr = frac(t) - 0.5;
                bool warpOnTop = fmod(cell.x + cell.y, 2.0) < 1.0;
                float fibre = tex2D(_FibreTex, t * 0.08).r;

                float openU = saturate(s.strainU / _OpenStrain);
                float openV = saturate(s.strainV / _OpenStrain);
                float halfU = 0.5 * _ThreadWidth * (1.0 - 0.6 * openU);
                float halfV = 0.5 * _ThreadWidth * (1.0 - 0.6 * openV);
                float coverU = 1.0 - smoothstep(halfU - 0.05, halfU + 0.05, abs(fr.x));
                float coverV = 1.0 - smoothstep(halfV - 0.05, halfV + 0.05, abs(fr.y));
                float bumpU = 1.0 - 0.7 * abs(fr.x) / max(halfU, 1e-3);
                float bumpV = 1.0 - 0.7 * abs(fr.y) / max(halfV, 1e-3);

                float3 topCol = warpOnTop ? _WarpColor.rgb : _WeftColor.rgb;
                float3 underCol = warpOnTop ? _WeftColor.rgb : _WarpColor.rgb;
                float top = warpOnTop ? coverU : coverV;
                float under = warpOnTop ? coverV : coverU;
                float bump = warpOnTop ? bumpU : bumpV;
                float3 albedo = lerp(lerp(_BackingColor.rgb, underCol * 0.8, under), topCol * saturate(bump), top);
                // Threads narrower than a pixel alias into moiré, so the weave fades to its mean colour with distance.
                float3 meanAlbedo = lerp(_BackingColor.rgb, 0.5 * (_WarpColor.rgb + _WeftColor.rgb) * 0.75, _ThreadWidth);
                float detail = saturate(1.5 - (fwidth(t.x) + fwidth(t.y)) * 2.0);
                albedo = lerp(meanAlbedo, albedo, detail);
                albedo *= 0.85 + 0.3 * fibre;
                albedo *= 1.0 - _CompressDarken * saturate(-min(s.strainU, s.strainV));

                float diff = VelaHalfLambert(s.normalWS, s.lightDirWS);
                float3 threadDir = warpOnTop ? s.bitangentWS : s.tangentWS;
                float sheen = VelaKajiyaKay(threadDir, s.lightDirWS, s.viewDirWS, _SheenPower) * fibre;
                float3 col = albedo * (_Ambient + diff * s.lightColor) + _SheenColor.rgb * sheen * _SheenStrength * top;
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
