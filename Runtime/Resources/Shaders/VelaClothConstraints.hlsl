#ifndef LUNE_CLOTH_CONSTRAINTS_INCLUDED
#define LUNE_CLOTH_CONSTRAINTS_INCLUDED

#include "VelaClothCommon.hlsl"

// Small-substep XPBD: one iteration per substep, so the Lagrange multiplier restarts at zero and needs no storage.
void SolveDistance(uint ia, uint ib, float restLength, float alphaTilde)
{
    float4 a = _Pos[ia];
    float4 b = _Pos[ib];

    float wSum = a.w + b.w;
    if (wSum <= 0.0)
        return;

    float3 d = a.xyz - b.xyz;
    float len = length(d);
    if (len < 1e-9)
        return;

    float3 n = d / len;
    float lambda = -(len - restLength) / (wSum + alphaTilde);

    if (a.w > 0.0)
        WRITE_POS(ia, a.xyz + n * (lambda * a.w), a.w);
    if (b.w > 0.0)
        WRITE_POS(ib, b.xyz - n * (lambda * b.w), b.w);
}

// Discrete curvature on a colinear triple: C = |x0 - 2*x1 + x2| / restSpacing, gradients (1,-2,1) * Lhat / restSpacing.
// Dividing by the rest spacing keeps bendingCompliance meaningful when resolution changes.
void SolveBending(uint i0, uint i1, uint i2, float restSpacing, float alphaTilde)
{
    float4 a = _Pos[i0];
    float4 b = _Pos[i1];
    float4 c = _Pos[i2];

    float3 L = a.xyz - 2.0 * b.xyz + c.xyz;
    float len = length(L);

    // The flat drape is the common case and the single most likely NaN source in the solver.
    if (len < 1e-7)
        return;

    float invRest = 1.0 / restSpacing;
    float wSum = (a.w + 4.0 * b.w + c.w) * invRest * invRest;
    if (wSum <= 0.0)
        return;

    float3 n = (L / len) * invRest;
    float lambda = -(len * invRest) / (wSum + alphaTilde);

    if (a.w > 0.0)
        WRITE_POS(i0, a.xyz + n * (lambda * a.w), a.w);
    if (b.w > 0.0)
        WRITE_POS(i1, b.xyz - n * (2.0 * lambda * b.w), b.w);
    if (c.w > 0.0)
        WRITE_POS(i2, c.xyz + n * (lambda * c.w), c.w);
}

// Inequality against the rest geodesic to a pin. The anchor is kinematic, so the whole correction lands on
// this thread's vertex and no colouring is needed — this is what stops a pinned drape stretching under load.
float3 SolveLongRange(uint id, float3 p)
{
    for (uint k = 0; k < _LraAnchorCount; k++)
    {
        uint anchor = _LraAnchor[id * _LraAnchorCount + k];
        if (anchor == LRA_NO_ANCHOR)
            continue;

        float maxDist = _LraDist[id * _LraAnchorCount + k] * _LraSlack;
        float3 d = p - _Pos[anchor].xyz;
        float len = length(d);
        if (len <= maxDist || len < 1e-9)
            continue;

        p -= d * ((len - maxDist) / len);
    }

    return p;
}

#endif
