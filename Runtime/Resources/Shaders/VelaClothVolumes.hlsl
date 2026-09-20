#ifndef LUNE_CLOTH_VOLUMES_INCLUDED
#define LUNE_CLOTH_VOLUMES_INCLUDED

#include "VelaClothNoise.hlsl"

#define CLOTH_VOLUME_GLOBAL 0
#define CLOTH_VOLUME_BOX    1
#define CLOTH_VOLUME_SPHERE 2

#define CLOTH_FIELD_DIRECTIONAL 0
#define CLOTH_FIELD_RADIAL      1
#define CLOTH_FIELD_VORTEX      2
#define CLOTH_FIELD_TURBULENCE  3

struct ClothVolumeData
{
    float4x4 clothToVolume;
    float4x4 volumeToCloth;
    float4   shapeParams;   // box: xyz half extents; sphere: x radius; w blend distance
    float4   paramsA;       // x strength, y gust amplitude, z gust frequency, w weight
    float4   paramsB;       // vortex: x inward pull, y axial lift; turbulence: x scroll speed, z noise scale, w scrolled
    uint4    flags;         // x shape, y field
};

StructuredBuffer<ClothVolumeData> _WindVolumes;
StructuredBuffer<ClothVolumeData> _ForceVolumes;
uint  _WindVolumeCount;
uint  _ForceVolumeCount;
float _WindTime;
float _SubstepOffset;   // seconds from the step's start to where this substep samples the field

// 1 inside, fading to 0 across the blend distance measured inward from the surface, times the volume's weight.
float ClothVolumeWeight(ClothVolumeData v, float3 pv)
{
    float weight = v.paramsA.w;
    uint shape = v.flags.x;

    if (shape == CLOTH_VOLUME_GLOBAL)
        return weight;

    float inside;
    if (shape == CLOTH_VOLUME_BOX)
    {
        float3 d = abs(pv) - v.shapeParams.xyz;
        inside = -max(d.x, max(d.y, d.z));
    }
    else
    {
        inside = v.shapeParams.x - length(pv);
    }

    return weight * smoothstep(0.0, 1.0, inside / max(v.shapeParams.w, 1e-4));
}

float3 ClothSafeNormalize(float3 d)
{
    float len = length(d);
    return len > 1e-5 ? d / len : 0.0;
}

// The field in volume space, magnitude included. Vortex extras carry their own magnitude so a pure pull or
// lift works at strength 0.
float3 ClothVolumeField(ClothVolumeData v, float3 pv)
{
    float strength = v.paramsA.x;
    uint field = v.flags.y;
    float3 f;

    if (field == CLOTH_FIELD_DIRECTIONAL)
    {
        f = float3(0.0, 0.0, strength);
    }
    else if (field == CLOTH_FIELD_RADIAL)
    {
        f = ClothSafeNormalize(pv) * strength;
    }
    else if (field == CLOTH_FIELD_VORTEX)
    {
        float3 radial = ClothSafeNormalize(float3(pv.x, 0.0, pv.z));
        float3 tangent = float3(radial.z, 0.0, -radial.x);
        f = tangent * strength - radial * v.paramsB.x + float3(0.0, v.paramsB.y, 0.0);
    }
    else
    {
        float scroll = v.paramsB.w + v.paramsB.x * _SubstepOffset;
        f = strength * ClothCurlNoise((pv - float3(0.0, 0.0, scroll)) * v.paramsB.z);
    }

    // The gust rides along +Z at the field's own speed, so it crosses the sheet instead of pulsing all of it
    // in unison — which is the difference between weather and a tremble.
    float phase = 6.2831853 * v.paramsA.z * (_WindTime - pv.z / max(abs(strength), 0.01));
    return f * (1.0 + v.paramsA.y * sin(phase));
}

float3 ClothSampleVolumes(StructuredBuffer<ClothVolumeData> volumes, uint count, float3 p)
{
    float3 sum = 0.0;

    for (uint i = 0; i < count; i++)
    {
        ClothVolumeData v = volumes[i];
        float3 pv = mul(v.clothToVolume, float4(p, 1.0)).xyz;
        float w = ClothVolumeWeight(v, pv);
        if (w <= 0.0)
            continue;

        float3 f = ClothVolumeField(v, pv);
        sum += mul((float3x3)v.volumeToCloth, f) * w;
    }

    return sum;
}

// Air velocity in cloth space, m/s.
float3 ClothWindAt(float3 p)
{
    return ClothSampleVolumes(_WindVolumes, _WindVolumeCount, p);
}

// Acceleration in cloth space, m/s², added beside gravity.
float3 ClothForceAt(float3 p)
{
    return ClothSampleVolumes(_ForceVolumes, _ForceVolumeCount, p);
}

#endif
