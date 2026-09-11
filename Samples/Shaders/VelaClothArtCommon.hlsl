#ifndef VELA_CLOTH_ART_COMMON_INCLUDED
#define VELA_CLOTH_ART_COMMON_INCLUDED

#include "UnityCG.cginc"

StructuredBuffer<float4> _VelaPositions;
StructuredBuffer<float4> _VelaVelocities;
float4 _VelaGrid;         // W, H, restDx, restDy
float4 _VelaSheet;        // size.x, size.y, pivot.x, pivot.y
float4 _VelaSunDirection; // world space, towards the light, w = 1 once a binder has pushed it
float4 _VelaSunColor;

struct Attributes
{
    float3 positionOS : POSITION;
    float3 normalOS   : NORMAL;
    float4 tangentOS  : TANGENT;
    float2 uv         : TEXCOORD0;
};

struct ClothVertexData
{
    uint2  cell;
    float2 uv;
    float3 positionOS;
    float3 normalOS;
    float3 tangentOS;
    float3 restPositionOS;
    float3 displacementOS;
    float3 velocityOS;
    float  strainU;      // edge length over rest length minus one: 0 at rest, positive stretched
    float  strainV;
    float  shear;        // cosine between the u and v edges: 0 at rest
    float  curvature;    // 1/m, positive when the sheet bends toward its normal
    float  pinned;
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float4 uvStrain   : TEXCOORD0;
    float4 normalWS   : TEXCOORD1;
    float4 tangentWS  : TEXCOORD2;
    float4 positionWS : TEXCOORD3;
    float4 velocityWS : TEXCOORD4;
    float4 extra      : TEXCOORD5;
};

struct ClothSurface
{
    float2 uv;
    float  strainU;
    float  strainV;
    float  shear;
    float  curvature;
    float  pinned;
    float  displacement;
    float  speed;
    float3 positionWS;
    float3 normalWS;
    float3 tangentWS;
    float3 bitangentWS;
    float3 viewDirWS;
    float3 lightDirWS;
    float3 lightColor;
    float3 velocityWS;
    bool   front;
};

uint VelaClothId(uint x, uint y)
{
    return y * (uint)_VelaGrid.x + x;
}

ClothVertexData VelaReadClothVertex(uint vid, Attributes a)
{
    ClothVertexData d = (ClothVertexData)0;
    d.uv = a.uv;
    d.positionOS = a.positionOS;
    d.normalOS = a.normalOS;
    d.tangentOS = a.tangentOS.xyz;

    uint w = (uint)_VelaGrid.x;
    uint h = (uint)_VelaGrid.y;
    if (w < 2 || h < 2)
        return d;

    uint x = vid % w;
    uint y = vid / w;
    uint xm = x > 0 ? x - 1 : x;
    uint xp = min(x + 1, w - 1);
    uint ym = y > 0 ? y - 1 : y;
    uint yp = min(y + 1, h - 1);

    float4 pc  = _VelaPositions[vid];
    float3 pxm = _VelaPositions[VelaClothId(xm, y)].xyz;
    float3 pxp = _VelaPositions[VelaClothId(xp, y)].xyz;
    float3 pym = _VelaPositions[VelaClothId(x, ym)].xyz;
    float3 pyp = _VelaPositions[VelaClothId(x, yp)].xyz;

    float3 du = pxp - pxm;
    float3 dv = pyp - pym;
    float lenU = length(du);
    float lenV = length(dv);
    float restU = (xp - xm) * _VelaGrid.z;
    float restV = (yp - ym) * _VelaGrid.w;

    d.cell = uint2(x, y);
    d.strainU = lenU / max(restU, 1e-6) - 1.0;
    d.strainV = lenV / max(restV, 1e-6) - 1.0;
    d.shear = dot(du / max(lenU, 1e-6), dv / max(lenV, 1e-6));

    // Two vertices out: the adjacent-vertex Laplacian alternates row by row with the solver's residual.
    // A one-sided stencil would read the border as a permanent crease, so each axis is dropped there.
    float wx = (x > 1 && x + 2 < w) ? 1.0 : 0.0;
    float wy = (y > 1 && y + 2 < h) ? 1.0 : 0.0;
    uint xm2 = x > 1 ? x - 2 : x, xp2 = min(x + 2, w - 1);
    uint ym2 = y > 1 ? y - 2 : y, yp2 = min(y + 2, h - 1);
    float3 lap = wx * (_VelaPositions[VelaClothId(xp2, y)].xyz + _VelaPositions[VelaClothId(xm2, y)].xyz - 2.0 * pc.xyz)
               + wy * (_VelaPositions[VelaClothId(x, yp2)].xyz + _VelaPositions[VelaClothId(x, ym2)].xyz - 2.0 * pc.xyz);
    d.curvature = dot(lap, a.normalOS) / max(4.0 * _VelaGrid.z * _VelaGrid.w, 1e-8);

    d.velocityOS = _VelaVelocities[vid].xyz;
    d.pinned = pc.w == 0.0 ? 1.0 : 0.0;
    d.restPositionOS = float3(x * _VelaGrid.z - _VelaSheet.z * _VelaSheet.x,
                              y * _VelaGrid.w - _VelaSheet.w * _VelaSheet.y, 0.0);
    d.displacementOS = pc.xyz - d.restPositionOS;
    return d;
}

Varyings VelaPackVaryings(ClothVertexData d)
{
    Varyings o;
    o.positionCS = UnityObjectToClipPos(float4(d.positionOS, 1.0));
    o.uvStrain = float4(d.uv, d.strainU, d.strainV);
    o.normalWS = float4(UnityObjectToWorldNormal(d.normalOS), d.curvature);
    o.tangentWS = float4(UnityObjectToWorldDir(d.tangentOS), d.shear);
    o.positionWS = float4(mul(unity_ObjectToWorld, float4(d.positionOS, 1.0)).xyz, d.pinned);
    o.velocityWS = float4(mul((float3x3)unity_ObjectToWorld, d.velocityOS), length(d.displacementOS));
    o.extra = 0.0;
    return o;
}

// Normal and curvature follow the rasterised side, so a crease is concave toward whoever looks at it.
ClothSurface VelaUnpackSurface(Varyings i, bool isFrontFace)
{
    ClothSurface s;
    s.uv = i.uvStrain.xy;
    s.strainU = i.uvStrain.z;
    s.strainV = i.uvStrain.w;
    s.shear = i.tangentWS.w;
    s.pinned = i.positionWS.w;
    s.displacement = i.velocityWS.w;
    s.front = isFrontFace;
    s.positionWS = i.positionWS.xyz;
    float3 n = normalize(i.normalWS.xyz);
    s.normalWS = isFrontFace ? n : -n;
    s.curvature = isFrontFace ? i.normalWS.w : -i.normalWS.w;
    s.tangentWS = normalize(i.tangentWS.xyz - s.normalWS * dot(s.normalWS, i.tangentWS.xyz));
    s.bitangentWS = cross(s.normalWS, s.tangentWS);
    s.viewDirWS = normalize(_WorldSpaceCameraPos - i.positionWS.xyz);
    bool hasSun = _VelaSunDirection.w > 0.5;
    s.lightDirWS = hasSun ? normalize(_VelaSunDirection.xyz) : normalize(float3(0.3, 0.8, -0.5));
    s.lightColor = hasSun ? _VelaSunColor.rgb : 1.0;
    s.velocityWS = i.velocityWS.xyz;
    s.speed = length(i.velocityWS.xyz);
    return s;
}

float VelaHalfLambert(float3 n, float3 l)
{
    float t = dot(n, l) * 0.5 + 0.5;
    return t * t;
}

float VelaBlinnSpec(float3 n, float3 l, float3 v, float power)
{
    return pow(saturate(dot(n, normalize(l + v))), power);
}

float VelaKajiyaKay(float3 t, float3 l, float3 v, float power)
{
    float th = dot(t, normalize(l + v));
    return pow(sqrt(saturate(1.0 - th * th)), power);
}

float VelaFresnel(float3 n, float3 v, float power)
{
    return pow(1.0 - saturate(dot(n, v)), power);
}

float3 VelaThinFilm(float phase)
{
    return 0.5 + 0.5 * cos(6.2831853 * (phase + float3(0.0, 0.33, 0.67)));
}

float VelaHash21(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

float VelaValueNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(VelaHash21(i), VelaHash21(i + float2(1, 0)), f.x),
                lerp(VelaHash21(i + float2(0, 1)), VelaHash21(i + 1.0), f.x), f.y);
}

float VelaFbm(float2 p, int octaves)
{
    float v = 0.0;
    float a = 0.5;
    for (int k = 0; k < octaves; k++)
    {
        v += a * VelaValueNoise(p);
        p *= 2.0;
        a *= 0.5;
    }
    return v;
}

float VelaGridLine(float2 p, float width)
{
    float2 d = abs(frac(p) - 0.5);
    return 1.0 - smoothstep(0.5 - width, 0.5 - width * 0.5, max(d.x, d.y));
}

float2 VelaTangentVelocity(ClothSurface s)
{
    return float2(dot(s.velocityWS, s.tangentWS), dot(s.velocityWS, s.bitangentWS));
}

float3 VelaDiverging(float x)
{
    x = clamp(x, -1.0, 1.0);
    float3 neg = float3(0.15, 0.4, 1.0);
    float3 mid = float3(0.12, 0.12, 0.12);
    float3 pos = float3(1.0, 0.25, 0.1);
    return x < 0.0 ? lerp(mid, neg, -x) : lerp(mid, pos, x);
}

float3 VelaHeat(float x)
{
    x = saturate(x);
    return float3(smoothstep(0.0, 0.35, x), smoothstep(0.3, 0.7, x), smoothstep(0.65, 1.0, x));
}

#endif
