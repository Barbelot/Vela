Shader "Vela/Samples/WeatheredPrint"
{
    Properties
    {
        _ClothColor ("Cloth Colour", Color) = (0.80, 0.74, 0.62, 1)
        _PrintTex ("Print (RGB ink, A mask)", 2D) = "white" {}
        _InkTint ("Ink Tint", Color) = (1, 1, 1, 1)
        _PrintPerMeter ("Print Tiles Per Metre", Float) = 0.5
        _GrainTex ("Grain", 2D) = "gray" {}
        _GrainPerMeter ("Grain Tiles Per Metre", Float) = 3
        _CrackStrain ("Full Crack At Strain", Range(0.005, 0.3)) = 0.08
        _CrackScale ("Crack Scale", Float) = 12
        _DustColor ("Dust Colour", Color) = (0.85, 0.82, 0.75, 1)
        _DustAmount ("Dust Amount", Range(0, 1)) = 0.6
        _GrimeColor ("Grime Colour", Color) = (0.25, 0.20, 0.15, 1)
        _GrimeCurvature ("Full Grime At Curvature (1/m)", Range(0.1, 10)) = 2
        _GrimeAmount ("Grime Amount", Range(0, 1)) = 0.7
        _SpecPower ("Specular Power", Range(1, 100)) = 12
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

            float4 _ClothColor;
            sampler2D _PrintTex;
            float4 _InkTint;
            float _PrintPerMeter;
            sampler2D _GrainTex;
            float _GrainPerMeter;
            float _CrackStrain;
            float _CrackScale;
            float4 _DustColor;
            float _DustAmount;
            float4 _GrimeColor;
            float _GrimeCurvature;
            float _GrimeAmount;
            float _SpecPower;
            float _Ambient;

            Varyings vert(Attributes a, uint vid : SV_VertexID)
            {
                return VelaPackVaryings(VelaReadClothVertex(vid, a));
            }

            float4 frag(Varyings i, bool isFrontFace : SV_IsFrontFace) : SV_Target
            {
                ClothSurface s = VelaUnpackSurface(i, isFrontFace);

                float2 sheetUv = s.uv * _VelaSheet.xy;
                float4 print = tex2D(_PrintTex, sheetUv * _PrintPerMeter);
                float grain = tex2D(_GrainTex, sheetUv * _GrainPerMeter).r;

                float stretch = max(max(s.strainU, s.strainV), 0.0);
                float c = saturate(stretch / _CrackStrain);
                float crack = VelaFbm(sheetUv * _CrackScale, 3);
                float inkMask = print.a * smoothstep(c - 0.08, c + 0.08, crack);
                float3 albedo = lerp(_ClothColor.rgb, print.rgb * _InkTint.rgb, inkMask);

                float dust = _DustAmount * saturate(s.normalWS.y) * (0.5 + 0.5 * grain);
                albedo = lerp(albedo, _DustColor.rgb, dust);
                float grime = _GrimeAmount * saturate(s.curvature / _GrimeCurvature) * (0.4 + 0.6 * grain);
                albedo = lerp(albedo, _GrimeColor.rgb, grime);

                float diff = VelaHalfLambert(s.normalWS, s.lightDirWS);
                float spec = VelaBlinnSpec(s.normalWS, s.lightDirWS, s.viewDirWS, _SpecPower);
                float3 col = albedo * (_Ambient + diff * s.lightColor) + 0.08 * spec * (1.0 - grime);
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
