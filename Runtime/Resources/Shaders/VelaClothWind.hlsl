#ifndef LUNE_CLOTH_WIND_INCLUDED
#define LUNE_CLOTH_WIND_INCLUDED

#include "VelaClothCommon.hlsl"

float3 _WindDir;            // object space, normalized
float  _WindSpeed;
float  _GustAmplitude;
float  _GustFrequency;
float  _Turbulence;
float  _TurbulenceScale;
float3 _WindAdvection;      // object space, how far the turbulence field has drifted downwind so far
float  _WindTime;
float  _DragFactor;         // 0.5 * airDensity * dragCoefficient
float  _LiftFactor;         // 0.5 * airDensity * liftCoefficient

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

float3 ClothWindAt(float3 p)
{
    // The gust rides along the wind at the wind's own speed, so it crosses the sheet instead of pulsing all
    // of it in unison — which is the difference between weather and a tremble.
    float phase = 6.2831853 * _GustFrequency * (_WindTime - dot(p, _WindDir) / max(_WindSpeed, 0.01));
    float3 w = _WindDir * (_WindSpeed * (1.0 + _GustAmplitude * sin(phase)));

    if (_Turbulence > 0.0)
        w += _Turbulence * ClothCurlNoise((p - _WindAdvection) * _TurbulenceScale);

    return w;
}

float3 ClothTriangleForce(uint i0, uint i1, uint i2, float3 wind)
{
    float3 p0 = _Pos[i0].xyz;
    float3 p1 = _Pos[i1].xyz;
    float3 p2 = _Pos[i2].xyz;

    float3 cr = cross(p1 - p0, p2 - p0);
    float twiceArea = length(cr);
    if (twiceArea < 1e-12)
        return 0.0;

    float3 n = cr / twiceArea;
    float3 vRel = (ClothSubstepVelocity(i0) + ClothSubstepVelocity(i1) + ClothSubstepVelocity(i2)) / 3.0 - wind;
    float speed = length(vRel);
    if (speed < 1e-6)
        return 0.0;

    float3 vHat = vRel / speed;

    // Effective area is the triangle seen edge-on from the flow, so a sheet aligned with the wind catches none.
    float q = 0.5 * twiceArea * abs(dot(n, vHat)) * speed * speed;
    float3 f = -(_DragFactor * q) * vHat;

    float3 lift = cross(cross(n, vHat), vHat);
    float liftLen = length(lift);
    if (liftLen > 1e-6)
        f -= (_LiftFactor * q) * (lift / liftLen);

    return f;
}

// A vertex's up to six incident triangles are pure index arithmetic on a regular grid, so the force is
// gathered here rather than scattered with atomics; each triangle is evaluated up to three times, which is
// ALU-cheap and still strictly better than an atomic scatter plus a second pass.
float3 ClothAeroForce(uint x, uint y, float3 wind)
{
    bool left = x > 0;
    bool below = y > 0;
    bool right = x + 1 < _W;
    bool above = y + 1 < _H;

    float3 f = 0.0;

    if (right && above)
    {
        uint a = ClothId(x, y), b = ClothId(x + 1, y), c = ClothId(x, y + 1), d = ClothId(x + 1, y + 1);
        f += ClothTriangleForce(a, c, d, wind);
        f += ClothTriangleForce(a, d, b, wind);
    }

    if (left && above)
        f += ClothTriangleForce(ClothId(x - 1, y), ClothId(x, y + 1), ClothId(x, y), wind);

    if (right && below)
        f += ClothTriangleForce(ClothId(x, y - 1), ClothId(x, y), ClothId(x + 1, y), wind);

    if (left && below)
    {
        uint a = ClothId(x - 1, y - 1), b = ClothId(x, y - 1), c = ClothId(x - 1, y), d = ClothId(x, y);
        f += ClothTriangleForce(a, c, d, wind);
        f += ClothTriangleForce(a, d, b, wind);
    }

    return f / 3.0;
}

#endif
