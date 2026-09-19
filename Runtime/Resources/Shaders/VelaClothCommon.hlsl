#ifndef LUNE_CLOTH_COMMON_INCLUDED
#define LUNE_CLOTH_COMMON_INCLUDED

#define THREADS_1D 256

RWStructuredBuffer<float4> _Pos;           // xyz position (object space), w = invMass
RWStructuredBuffer<float4> _PosPrev;       // x at substep start
RWStructuredBuffer<float4> _Vel;           // xyz velocity, w = scratch
StructuredBuffer<float4>   _PosRest;       // rest positions, w = invMass
StructuredBuffer<uint>     _LraAnchor;     // N*K pin ids, LRA_NO_ANCHOR where unreachable
StructuredBuffer<float>    _LraDist;       // N*K rest geodesics to those pins

#define LRA_NO_ANCHOR 0xFFFFFFFFu

uint  _W;
uint  _H;
uint  _VertexCount;
float _RestDx;
float _RestDy;

float  _SubstepDt;
float  _InvSubstepDt;
float3 _Gravity;
float  _MaxVelocity;
float  _VelocityDamp;
float  _VelocitySmooth;
float  _AlphaStructuralH;
float  _AlphaStructuralV;
float  _AlphaShear;
float  _AlphaBend;
float  _RestDiag;
uint   _Parity;
uint   _Phase;
uint   _LraAnchorCount;
float  _LraSlack;
float4x4 _TransformDelta;   // previous cloth frame -> current cloth frame
float  _TransformInertia;
float  _PinSweep;           // 1 / (substeps remaining in the step), the pins' share of their way back to rest

// HLSL forbids writing a swizzle of an RWStructuredBuffer element, so every position write is a whole float4.
#define WRITE_POS(id, p, w) _Pos[id] = float4(p, w)

uint ClothId(uint x, uint y) { return y * _W + x; }

// Derived from the position buffers rather than read from _Vel, so it is safe to gather across threads in
// the very pass that writes _Vel.
float3 ClothSubstepVelocity(uint id)
{
    return (_Pos[id].xyz - _PosPrev[id].xyz) * _InvSubstepDt;
}

// Monotonic float->uint so InterlockedMin/Max order negatives correctly.
uint FloatToOrderedUint(float f)
{
    uint u = asuint(f);
    return (u & 0x80000000u) ? ~u : (u | 0x80000000u);
}

float OrderedUintToFloat(uint u)
{
    return asfloat((u & 0x80000000u) ? (u & 0x7FFFFFFFu) : ~u);
}

#endif
