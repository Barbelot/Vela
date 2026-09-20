#ifndef LUNE_CLOTH_NOISE_INCLUDED
#define LUNE_CLOTH_NOISE_INCLUDED

float ClothNoiseHash(int3 c, uint seed)
{
    uint3 u = asuint(c);
    uint h = u.x * 73856093u ^ u.y * 19349663u ^ u.z * 83492791u ^ seed;
    h ^= h >> 13;
    h *= 0x5BD1E995u;
    h ^= h >> 15;
    return h * (2.0 / 4294967295.0) - 1.0;
}

// Value noise carrying its analytic gradient, because the curl below needs gradients and nothing else:
// a finite-difference curl would cost six times the samples for the same field.
float ClothValueNoise(float3 p, uint seed, out float3 grad)
{
    float3 i = floor(p);
    float3 f = p - i;
    float3 u  = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
    float3 du = 30.0 * f * f * (f * (f - 2.0) + 1.0);

    int3 c = (int3)i;
    float v000 = ClothNoiseHash(c + int3(0, 0, 0), seed);
    float v100 = ClothNoiseHash(c + int3(1, 0, 0), seed);
    float v010 = ClothNoiseHash(c + int3(0, 1, 0), seed);
    float v001 = ClothNoiseHash(c + int3(0, 0, 1), seed);
    float v110 = ClothNoiseHash(c + int3(1, 1, 0), seed);
    float v011 = ClothNoiseHash(c + int3(0, 1, 1), seed);
    float v101 = ClothNoiseHash(c + int3(1, 0, 1), seed);
    float v111 = ClothNoiseHash(c + int3(1, 1, 1), seed);

    float k0 = v000;
    float k1 = v100 - v000;
    float k2 = v010 - v000;
    float k3 = v001 - v000;
    float k4 = v110 - v010 - v100 + v000;
    float k5 = v011 - v001 - v010 + v000;
    float k6 = v101 - v001 - v100 + v000;
    float k7 = v111 - v011 - v101 - v110 + v001 + v010 + v100 - v000;

    grad = du * float3(k1 + k4 * u.y + k6 * u.z + k7 * u.y * u.z,
                       k2 + k4 * u.x + k5 * u.z + k7 * u.z * u.x,
                       k3 + k5 * u.y + k6 * u.x + k7 * u.x * u.y);

    return k0 + k1 * u.x + k2 * u.y + k3 * u.z
         + k4 * u.x * u.y + k5 * u.y * u.z + k6 * u.z * u.x + k7 * u.x * u.y * u.z;
}

// The curl of a vector potential is divergence-free by construction, so the field swirls instead of pumping —
// that is what makes a large drape read as flowing rather than vibrating.
float3 ClothCurlNoise(float3 p)
{
    float3 g0, g1, g2;
    ClothValueNoise(p,                    0x9E3779B9u, g0);
    ClothValueNoise(p + float3(31.4, 0.0, 17.1), 0x85EBCA6Bu, g1);
    ClothValueNoise(p + float3(0.0, 57.7, 43.2), 0xC2B2AE35u, g2);

    return float3(g2.y - g1.z, g0.z - g2.x, g1.x - g0.y);
}

#endif
