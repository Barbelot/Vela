Shader "Vela/ClothDebug"
{
    Properties
    {
        [KeywordEnum(Normal, UV, Velocity)] _Mode ("Debug Mode", Float) = 0
        _VelocityScale ("Velocity Scale", Float) = 20
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Cull Off
        ZWrite On

        Pass
        {
            // HDRP only rasterizes passes it knows; ForwardOnly is the unlit slot every SRP-agnostic shader can claim.
            Tags { "LightMode" = "ForwardOnly" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _MODE_NORMAL _MODE_UV _MODE_VELOCITY
            #pragma target 4.5

            #include "UnityCG.cginc"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float3 velocityOS : TEXCOORD4;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float2 uv         : TEXCOORD1;
                float3 velocityOS : TEXCOORD2;
            };

            float _VelocityScale;

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.positionCS = UnityObjectToClipPos(input.positionOS);
                o.normalWS = UnityObjectToWorldNormal(input.normalOS);
                o.uv = input.uv;
                o.velocityOS = input.velocityOS;
                return o;
            }

            float4 frag(Varyings input) : SV_Target
            {
                #if defined(_MODE_UV)
                    return float4(input.uv, 0, 1);
                #elif defined(_MODE_VELOCITY)
                    return float4(abs(input.velocityOS) * _VelocityScale, 1);
                #else
                    return float4(normalize(input.normalWS) * 0.5 + 0.5, 1);
                #endif
            }
            ENDHLSL
        }
    }

    Fallback Off
}
