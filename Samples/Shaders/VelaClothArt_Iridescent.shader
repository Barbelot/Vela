Shader "Vela/Samples/Iridescent"
{
    Properties
    {
        _BaseColor ("Base Colour", Color) = (0.04, 0.03, 0.10, 1)
        _FilmStrength ("Film Strength", Range(0, 2)) = 0.8
        _FilmThickness ("Film Thickness", Range(0.2, 4)) = 1
        _CurvatureHue ("Hue Shift Per Curvature", Range(0, 2)) = 0.15
        _ShimmerScale ("Shimmer Scale", Float) = 6
        _ShimmerSpeed ("Shimmer Speed", Float) = 3
        _ShimmerVelocity ("Full Shimmer At Speed (m/s)", Range(0.1, 5)) = 1
        _SpecPower ("Specular Power", Range(1, 200)) = 60
        _FresnelPower ("Fresnel Power", Range(0.5, 8)) = 3
        _Ambient ("Ambient", Range(0, 1)) = 0.2
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

            float4 _BaseColor;
            float _FilmStrength;
            float _FilmThickness;
            float _CurvatureHue;
            float _ShimmerScale;
            float _ShimmerSpeed;
            float _ShimmerVelocity;
            float _SpecPower;
            float _FresnelPower;
            float _Ambient;

            Varyings vert(Attributes a, uint vid : SV_VertexID)
            {
                return VelaPackVaryings(VelaReadClothVertex(vid, a));
            }

            float4 frag(Varyings i, bool isFrontFace : SV_IsFrontFace) : SV_Target
            {
                ClothSurface s = VelaUnpackSurface(i, isFrontFace);

                float cosT = saturate(dot(s.normalWS, s.viewDirWS));
                float shimmer = VelaValueNoise(s.uv * _ShimmerScale + _Time.y * _ShimmerSpeed) * saturate(s.speed / _ShimmerVelocity);
                float phase = _FilmThickness * cosT + _CurvatureHue * clamp(s.curvature, -2.0, 2.0) + 0.5 * shimmer;
                float3 film = VelaThinFilm(phase);

                float diff = VelaHalfLambert(s.normalWS, s.lightDirWS);
                float fres = VelaFresnel(s.normalWS, s.viewDirWS, _FresnelPower);
                float spec = VelaKajiyaKay(s.tangentWS, s.lightDirWS, s.viewDirWS, _SpecPower);

                float3 col = _BaseColor.rgb * (_Ambient + diff * s.lightColor)
                           + film * (0.3 + 0.7 * fres) * _FilmStrength * diff * s.lightColor
                           + spec * film;
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
