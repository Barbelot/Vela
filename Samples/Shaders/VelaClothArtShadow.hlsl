#ifndef VELA_CLOTH_ART_SHADOW_INCLUDED
#define VELA_CLOTH_ART_SHADOW_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

float4 VelaShadowVert(float3 positionOS : POSITION) : SV_POSITION
{
    return TransformWorldToHClip(TransformObjectToWorld(positionOS));
}

void VelaShadowFrag()
{
}

#endif
