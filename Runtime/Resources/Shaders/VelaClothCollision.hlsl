#ifndef LUNE_CLOTH_COLLISION_INCLUDED
#define LUNE_CLOTH_COLLISION_INCLUDED

#include "VelaClothCommon.hlsl"

#define CLOTH_COLLIDER_SPHERE  0
#define CLOTH_COLLIDER_CAPSULE 1
#define CLOTH_COLLIDER_BOX     2
#define CLOTH_COLLIDER_PLANE   3

struct ClothColliderData
{
    float4x4 clothToCollider;
    float4x4 colliderToCloth;
    float4x4 prevClothToCollider;
    float4x4 prevColliderToCloth;
    float4   paramsA;
    float4   paramsB;
    uint4    flags;
};

StructuredBuffer<ClothColliderData> _Colliders;
uint  _ColliderCount;
float _ColliderT0;   // where along the step's collider motion this substep starts
float _ColliderT1;   // and where it ends

bool ProjectSphere(inout float3 p, float radius)
{
    float d = length(p);
    if (d >= radius)
        return false;

    p = d > 1e-6 ? p * (radius / d) : float3(0, radius, 0);
    return true;
}

bool ProjectCapsule(inout float3 p, float halfHeight, float radius)
{
    float3 c = float3(0, clamp(p.y, -halfHeight, halfHeight), 0);
    float3 d = p - c;
    float len = length(d);
    if (len >= radius)
        return false;

    p = c + (len > 1e-6 ? d * (radius / len) : float3(radius, 0, 0));
    return true;
}

bool ProjectPlane(inout float3 p, float thickness)
{
    if (p.y >= thickness)
        return false;

    p.y = thickness;
    return true;
}

bool ProjectBox(inout float3 p, float3 halfExtents, float thickness)
{
    float3 q = clamp(p, -halfExtents, halfExtents);
    float3 d = p - q;
    float len = length(d);

    if (len > 1e-6)
    {
        if (len >= thickness)
            return false;

        p = q + d * (thickness / len);
        return true;
    }

    // Deep inside, so leave along the axis of least penetration.
    float3 pen = halfExtents - abs(p);
    uint axis = 0;
    float least = pen.x;
    if (pen.y < least) { least = pen.y; axis = 1; }
    if (pen.z < least) { axis = 2; }

    float3 mask = float3(axis == 0, axis == 1, axis == 2);
    float3 sgn = float3(p.x >= 0 ? 1 : -1, p.y >= 0 ? 1 : -1, p.z >= 0 ? 1 : -1);

    p = p * (1.0 - mask) + mask * sgn * (halfExtents + thickness);
    return true;
}

// Moves p, in collider local space, out to the surface. Returns false when it was already clear.
bool ClothProject(inout float3 p, ClothColliderData c)
{
    float thickness = c.paramsB.y;
    uint type = c.flags.x;

    if (type == CLOTH_COLLIDER_SPHERE)
        return ProjectSphere(p, c.paramsA.w + thickness);
    if (type == CLOTH_COLLIDER_CAPSULE)
        return ProjectCapsule(p, c.paramsA.y, c.paramsA.w + thickness);
    if (type == CLOTH_COLLIDER_BOX)
        return ProjectBox(p, c.paramsA.xyz, thickness);

    return ProjectPlane(p, thickness);
}

// Position-level Coulomb friction: cancel the tangential slide, but never more than the friction
// coefficient times the normal correction, which is what lets a sweeping collider drag the cloth.
float3 ClothCollideAll(float3 x, float3 xPrev)
{
    for (uint i = 0; i < _ColliderCount; i++)
    {
        ClothColliderData c = _Colliders[i];

        // The collider sweeps across the step's substeps rather than jumping to its end pose in the first
        // one, or (x - xPrev) / h would report substeps-times its real speed and kick the cloth. The lerp
        // is componentwise, so a per-step rotation large enough to make these matrices non-rigid would
        // already be tunnelling.
        float4x4 toCollider = lerp(c.prevClothToCollider, c.clothToCollider, _ColliderT1);
        float4x4 toCloth = lerp(c.prevColliderToCloth, c.colliderToCloth, _ColliderT1);

        float3 local = mul(toCollider, float4(x, 1.0)).xyz;
        if (!ClothProject(local, c))
            continue;

        float3 target = mul(toCloth, float4(local, 1.0)).xyz;
        float3 correction = target - x;
        float depth = length(correction);
        if (depth < 1e-9)
            continue;

        float3 n = correction / depth;

        // Where the collider material point that was under x at the substep's start has moved to by its end.
        float4x4 fromSubstepStart = lerp(c.prevClothToCollider, c.clothToCollider, _ColliderT0);
        float3 carried = mul(toCloth, mul(fromSubstepStart, float4(x, 1.0))).xyz;
        float3 relative = (x - xPrev) - (carried - x);
        float3 tangent = relative - dot(relative, n) * n;
        float tangentLen = length(tangent);

        x = target;
        if (tangentLen > 1e-9)
            x -= tangent * min(1.0, c.paramsB.x * depth / tangentLen);
    }

    return x;
}

#endif
