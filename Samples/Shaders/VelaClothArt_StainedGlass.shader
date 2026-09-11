Shader "Vela/Samples/StainedGlass"
{
    Properties
    {
        _CellsU ("Panes Across", Float) = 8
        _CellsV ("Panes Down", Float) = 8
        _LeadWidth ("Lead Width", Range(0.01, 0.2)) = 0.06
        _LeadColor ("Lead Colour", Color) = (0.08, 0.08, 0.09, 1)
        _PaletteTex ("Glass Palette", 2D) = "white" {}
        _GlassTex ("Glass Texture (B)", 2D) = "gray" {}
        _GlassPerMeter ("Glass Tiles Per Metre", Float) = 1
        _Backlight ("Backlight", Range(0, 3)) = 1.2
        _VelocityHue ("Palette Shift Per m/s", Range(0, 2)) = 0.3
        _Transmission ("Transmission Tint", Color) = (0.9, 0.95, 1, 1)
        _Ambient ("Ambient", Range(0, 1)) = 0.15
        _EdgeWobble ("Edge Wobble", Range(0, 0.3)) = 0.08
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

            float _CellsU;
            float _CellsV;
            float _LeadWidth;
            float4 _LeadColor;
            sampler2D _PaletteTex;
            sampler2D _GlassTex;
            float _GlassPerMeter;
            float _Backlight;
            float _VelocityHue;
            float4 _Transmission;
            float _Ambient;
            float _EdgeWobble;

            Varyings vert(Attributes a, uint vid : SV_VertexID)
            {
                ClothVertexData d = VelaReadClothVertex(vid, a);
                Varyings o = VelaPackVaryings(d);
                float2 cells = float2(_CellsU, _CellsV);
                float2 cell = floor(a.uv * cells);
                o.extra.yz = cell;
                uint w = (uint)_VelaGrid.x;
                uint h = (uint)_VelaGrid.y;
                if (w >= 2 && h >= 2)
                {
                    float2 centre = (cell + 0.5) / cells;
                    uint cx = (uint)round(centre.x * (w - 1));
                    uint cy = (uint)round(centre.y * (h - 1));
                    o.extra.x = length(_VelaVelocities[VelaClothId(cx, cy)].xyz);
                }
                return o;
            }

            float4 frag(Varyings i, bool isFrontFace : SV_IsFrontFace) : SV_Target
            {
                ClothSurface s = VelaUnpackSurface(i, isFrontFace);

                float2 g = s.uv * float2(_CellsU, _CellsV);
                float2 f = frac(g) + (VelaValueNoise(g * 3.0) - 0.5) * _EdgeWobble;
                float edge = min(min(f.x, 1.0 - f.x), min(f.y, 1.0 - f.y));
                float lead = 1.0 - smoothstep(_LeadWidth, _LeadWidth + 0.02, edge);

                float hue = VelaHash21(floor(i.extra.yz + 0.5)) + i.extra.x * _VelocityHue;
                float3 glass = tex2D(_PaletteTex, float2(frac(hue), 0.5)).rgb;
                glass *= 0.7 + 0.5 * tex2D(_GlassTex, s.uv * _VelaSheet.xy * _GlassPerMeter).b;

                float diff = VelaHalfLambert(s.normalWS, s.lightDirWS);
                float through = saturate(dot(-s.normalWS, s.lightDirWS));
                float3 col = glass * (_Ambient + 0.4 * diff * s.lightColor + through * _Backlight * _Transmission.rgb);
                col = lerp(col, _LeadColor.rgb * (0.3 + 0.7 * diff), lead);
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
