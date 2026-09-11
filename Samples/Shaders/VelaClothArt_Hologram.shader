Shader "Vela/Samples/Hologram"
{
    Properties
    {
        _RampTex ("Speed Ramp", 2D) = "white" {}
        _SpeedRange ("Ramp End Speed (m/s)", Range(0.1, 10)) = 4
        _Emission ("Emission", Range(0, 4)) = 1
        _GridPerMeter ("Grid Lines Per Metre", Float) = 4
        _GridWidth ("Grid Width", Range(0.005, 0.2)) = 0.04
        _GridColor ("Grid Colour", Color) = (0.2, 0.9, 1, 1)
        _AnchorColor ("Anchor Colour", Color) = (1, 0.6, 0.1, 1)
        _AnchorStrength ("Anchor Strength", Range(0, 4)) = 2
        _WarnStrain ("Warning Strain", Range(0.005, 0.2)) = 0.06
        _WarnColor ("Warning Colour", Color) = (1, 0.15, 0.1, 1)
        _WarnBandsPerMeter ("Warning Bands Per Metre", Float) = 6
        _ScanPerMeter ("Scanlines Per Metre", Float) = 30
        _ScanSpeed ("Scanline Speed", Float) = 2
        _Fresnel ("Rim", Range(0, 2)) = 0.6
        _BackDim ("Back Face Dim", Range(0, 1)) = 0.5
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

            sampler2D _RampTex;
            float _SpeedRange;
            float _Emission;
            float _GridPerMeter;
            float _GridWidth;
            float4 _GridColor;
            float4 _AnchorColor;
            float _AnchorStrength;
            float _WarnStrain;
            float4 _WarnColor;
            float _WarnBandsPerMeter;
            float _ScanPerMeter;
            float _ScanSpeed;
            float _Fresnel;
            float _BackDim;

            Varyings vert(Attributes a, uint vid : SV_VertexID)
            {
                return VelaPackVaryings(VelaReadClothVertex(vid, a));
            }

            float4 frag(Varyings i, bool isFrontFace : SV_IsFrontFace) : SV_Target
            {
                ClothSurface s = VelaUnpackSurface(i, isFrontFace);

                float2 sheetUv = s.uv * _VelaSheet.xy;
                float3 e = tex2D(_RampTex, float2(saturate(s.speed / _SpeedRange), 0.5)).rgb * _Emission;
                float grid = VelaGridLine(sheetUv * _GridPerMeter, _GridWidth);
                float scan = 0.5 + 0.5 * sin(s.positionWS.y * _ScanPerMeter * 6.2831853 - _Time.y * _ScanSpeed * 6.2831853);
                float anchor = sqrt(saturate(s.pinned)) * _AnchorStrength;

                float stretch = max(abs(s.strainU), abs(s.strainV));
                float bands = step(0.5, frac((s.uv.x - s.uv.y) * _VelaSheet.x * _WarnBandsPerMeter + _Time.y));
                float warn = saturate((stretch - _WarnStrain) / _WarnStrain) * bands;
                float rim = VelaFresnel(s.normalWS, s.viewDirWS, 3.0) * _Fresnel;

                float3 col = e * (0.6 + 0.4 * scan)
                           + grid * _GridColor.rgb
                           + anchor * _AnchorColor.rgb
                           + warn * _WarnColor.rgb
                           + rim * _GridColor.rgb;
                col *= s.front ? 1.0 : _BackDim;
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
