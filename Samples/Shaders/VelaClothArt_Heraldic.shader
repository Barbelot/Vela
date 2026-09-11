Shader "Vela/Samples/Heraldic"
{
    Properties
    {
        _FrontTex ("Front Print (RGBA)", 2D) = "white" {}
        _BackTex ("Back Print (RGBA)", 2D) = "white" {}
        _FrontField ("Front Field", Color) = (0.75, 0.10, 0.12, 1)
        _BackField ("Back Field", Color) = (0.10, 0.16, 0.45, 1)
        _ClothColor ("Bare Cloth", Color) = (0.82, 0.78, 0.70, 1)
        _FlakeDisplacement ("Full Wear At Displacement (m)", Range(0.1, 20)) = 8
        _FlakeStrain ("Full Wear At Strain", Range(0.005, 0.3)) = 0.08
        _FlakeScale ("Flake Scale", Float) = 10
        _FlakeSharpness ("Flake Sharpness", Range(0.01, 0.3)) = 0.08
        _SpecPower ("Specular Power", Range(1, 100)) = 20
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

            sampler2D _FrontTex;
            sampler2D _BackTex;
            float4 _FrontField;
            float4 _BackField;
            float4 _ClothColor;
            float _FlakeDisplacement;
            float _FlakeStrain;
            float _FlakeScale;
            float _FlakeSharpness;
            float _SpecPower;
            float _Ambient;

            Varyings vert(Attributes a, uint vid : SV_VertexID)
            {
                return VelaPackVaryings(VelaReadClothVertex(vid, a));
            }

            float4 frag(Varyings i, bool isFrontFace : SV_IsFrontFace) : SV_Target
            {
                ClothSurface s = VelaUnpackSurface(i, isFrontFace);

                float2 fuv = s.front ? s.uv : float2(1.0 - s.uv.x, s.uv.y);
                float4 p = s.front ? tex2D(_FrontTex, fuv) : tex2D(_BackTex, fuv);
                float3 field = s.front ? _FrontField.rgb : _BackField.rgb;

                float wear = max(saturate(s.displacement / _FlakeDisplacement),
                                 saturate(max(s.strainU, s.strainV) / _FlakeStrain));
                float flake = smoothstep(wear - _FlakeSharpness, wear + _FlakeSharpness, VelaFbm(s.uv * _VelaSheet.xy * _FlakeScale, 3));
                float3 printed = lerp(field, p.rgb, p.a);
                float3 albedo = lerp(_ClothColor.rgb, printed, flake);

                float diff = VelaHalfLambert(s.normalWS, s.lightDirWS);
                float spec = VelaBlinnSpec(s.normalWS, s.lightDirWS, s.viewDirWS, _SpecPower);
                float3 col = albedo * (_Ambient + diff * s.lightColor) + 0.05 * spec;
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
