#ifndef LUNE_CLOTH_WIND_INCLUDED
#define LUNE_CLOTH_WIND_INCLUDED

#include "VelaClothCommon.hlsl"
#include "VelaClothVolumes.hlsl"

float  _DragFactor;         // 0.5 * airDensity * dragCoefficient
float  _LiftFactor;         // 0.5 * airDensity * liftCoefficient

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
