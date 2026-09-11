Shader "Vela/Samples/DataDebug"
{
    Properties
    {
        [KeywordEnum(Strain, StrainU, StrainV, Shear, Curvature, Velocity, Speed, Pinned, Displacement, RestPosition, Facing)] _Mode ("Mode", Float) = 0
        _StrainRange ("Strain Range", Range(0.005, 0.5)) = 0.1
        _CurvatureRange ("Curvature Range (1/m)", Range(0.1, 20)) = 4
        _SpeedRange ("Speed Range (m/s)", Range(0.1, 20)) = 3
        _DisplacementRange ("Displacement Range (m)", Range(0.05, 10)) = 2
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
            #pragma multi_compile _MODE_STRAIN _MODE_STRAINU _MODE_STRAINV _MODE_SHEAR _MODE_CURVATURE _MODE_VELOCITY _MODE_SPEED _MODE_PINNED _MODE_DISPLACEMENT _MODE_RESTPOSITION _MODE_FACING
            #pragma target 4.5

            #include "VelaClothArtCommon.hlsl"

            float _StrainRange;
            float _CurvatureRange;
            float _SpeedRange;
            float _DisplacementRange;

            Varyings vert(Attributes a, uint vid : SV_VertexID)
            {
                ClothVertexData d = VelaReadClothVertex(vid, a);
                Varyings o = VelaPackVaryings(d);
                o.extra.xy = d.restPositionOS.xy;
                return o;
            }

            float4 frag(Varyings i, bool isFrontFace : SV_IsFrontFace) : SV_Target
            {
                ClothSurface s = VelaUnpackSurface(i, isFrontFace);
                float3 col;
                #if defined(_MODE_STRAINU)
                    col = VelaDiverging(s.strainU / _StrainRange);
                #elif defined(_MODE_STRAINV)
                    col = VelaDiverging(s.strainV / _StrainRange);
                #elif defined(_MODE_SHEAR)
                    col = VelaDiverging(s.shear);
                #elif defined(_MODE_CURVATURE)
                    col = VelaDiverging(s.curvature / _CurvatureRange);
                #elif defined(_MODE_VELOCITY)
                    col = s.velocityWS / _SpeedRange * 0.5 + 0.5;
                #elif defined(_MODE_SPEED)
                    col = VelaHeat(s.speed / _SpeedRange);
                #elif defined(_MODE_PINNED)
                    col = VelaHeat(s.pinned);
                #elif defined(_MODE_DISPLACEMENT)
                    col = VelaHeat(s.displacement / _DisplacementRange);
                #elif defined(_MODE_RESTPOSITION)
                    col = float3(frac(i.extra.xy), 0.0);
                #elif defined(_MODE_FACING)
                    col = s.front ? float3(0, 1, 0) : float3(1, 0, 0);
                #else
                    float strain = abs(s.strainU) > abs(s.strainV) ? s.strainU : s.strainV;
                    col = VelaDiverging(strain / _StrainRange);
                #endif
                return float4(col, 1);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
