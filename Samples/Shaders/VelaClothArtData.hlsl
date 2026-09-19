#ifndef VELA_CLOTH_ART_DATA_INCLUDED
#define VELA_CLOTH_ART_DATA_INCLUDED

StructuredBuffer<float4> _VelaPositions;
StructuredBuffer<float4> _VelaVelocities;
float4 _VelaGrid;         // W, H, restDx, restDy
float4 _VelaSheet;        // size.x, size.y, pivot.x, pivot.y

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

uint VelaClothId(uint x, uint y)
{
    return y * (uint)_VelaGrid.x + x;
}

ClothVertexData VelaReadClothVertex(uint vid, float3 positionOS, float3 normalOS, float3 tangentOS, float2 uv)
{
    ClothVertexData d = (ClothVertexData)0;
    d.uv = uv;
    d.positionOS = positionOS;
    d.normalOS = normalOS;
    d.tangentOS = tangentOS;

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
    d.curvature = dot(lap, normalOS) / max(4.0 * _VelaGrid.z * _VelaGrid.w, 1e-8);

    d.velocityOS = _VelaVelocities[vid].xyz;
    d.pinned = pc.w == 0.0 ? 1.0 : 0.0;
    d.restPositionOS = float3(x * _VelaGrid.z - _VelaSheet.z * _VelaSheet.x,
                              y * _VelaGrid.w - _VelaSheet.w * _VelaSheet.y, 0.0);
    d.displacementOS = pc.xyz - d.restPositionOS;
    return d;
}

// Shader Graph vertex-stage Custom Function; each output is packed for one float4 custom interpolator.
void VelaClothVertex_float(float vertexId, float3 positionOS, float3 normalOS, float2 uv,
                           out float4 strain, out float4 motion, out float4 sheet)
{
    ClothVertexData d = VelaReadClothVertex((uint)vertexId, positionOS, normalOS, float3(1, 0, 0), uv);
    strain = float4(d.strainU, d.strainV, d.shear, d.curvature);
    motion = float4(d.velocityOS, length(d.displacementOS));
    sheet = float4(uv * _VelaSheet.xy, d.pinned, 0.0);
}

void VelaClothSheet_float(out float2 size)
{
    size = _VelaSheet.xy;
}

#endif
