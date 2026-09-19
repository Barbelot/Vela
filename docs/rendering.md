# Rendering

## The solver writes the Mesh's own vertex buffer

`VelaClothMeshBinding` sets `mesh.vertexBufferTarget = GraphicsBuffer.Target.Raw` and hands `GetVertexBuffer(0)`
to `KWriteVertexBuffer`; a stock `MeshRenderer` draws it. The decisive argument is not speed but that this
inherits the whole pipeline — shadows, decals, SSR/SSGI, probes, culling, Shader Graph authoring — including
the hard part, **correct motion vectors on deforming geometry**. `Graphics.RenderPrimitives` would mean
reimplementing every HDRP pass by hand, is fragile across versions, and would still need the same velocity
trick.

Traps:

- Set `vertexBufferTarget` **before** `SetVertexBufferParams`, or the writes silently go nowhere.
- `GetVertexBuffer(0)` returns a **new** object per call. It is cached in `VelaClothMeshBinding` and disposed in
  `OnDisable`, `OnDestroy` and `AssemblyReloadEvents.beforeAssemblyReload`; leaks in edit mode are
  near-certain without all three.
- HDRP does not rebuild the BLAS for a compute-written vertex buffer on a static renderer, so ray-traced
  effects lag the geometry by a frame. Documented, not fixed; irrelevant for a rasterized installation.

## Vertex layout

Single stream, `IndexFormat.UInt32`, 60 B with motion vectors and 48 B without:

| Offset | Channel | Format |
|---|---|---|
| 0 | POSITION | f32×3 |
| 12 | NORMAL | f32×3 |
| 24 | TANGENT | f32×4 |
| 40 | TEXCOORD0 | f32×2, static, uploaded once |
| 48 | TEXCOORD4 | f32×3, precomputed velocity |

One stream rather than a static/dynamic split: `SetVertexBufferParams` requires descriptors ordered by
stream, and splitting would put TEXCOORD4 before TEXCOORD0. The kernel skips bytes `[40, 48)`.

Normals need no adjacency buffer: the up-to-six incident triangles are index arithmetic on the grid, and
`n = normalize(Σ cross(p1 − p0, p2 − p0))` over them is area-weighted for free and matches the grid's
clockwise winding — **front face on −Z**. Not a central difference: `p[x+1] − p[x−1]` collapses to zero
across a fold crease, which streaks the shading along every crease. The tangent is `p[x+1,y] − p[x−1,y]`
Gram-Schmidt'd against `n`, since it degenerates on the same creases.

## Motion vectors

HDRP computes motion vectors from the object transform only, so a per-frame-rewritten vertex buffer produces
wrong ones. HDRP already solves this for Alembic through the Lit / Shader Graph **"Add Precomputed Velocity"**
option, which reads an object-space displacement from `TEXCOORD4`. Three things must all be right:

1. `writeMotionVectors` on the component (writes the channel and keeps `_PosPrevFrame`).
2. `MeshRenderer.motionVectorGenerationMode = Object`, which the component sets.
3. "Add Precomputed Velocity" on the material — `Rendering/HDRP/VelaClothLit_HDRP.mat` has it.

**The channel is `TEXCOORD4 = positionOS − previousPositionOS`** (current minus previous): HDRP's motion-vector
pass reconstructs `previousPositionOS = positionOS − precomputedVelocity`. `KWriteVertexBuffer` writes it and
then copies `_Pos` into `_PosPrevFrame`.

Motion vectors are not a TAA-only input. HDRP also feeds them to motion blur, DLSS/FSR2/TAAU, SSR
accumulation, SSGI, ray-tracing denoisers, volumetric reprojection and Recorder's motion-vector output, so a
wrong channel surfaces as TAA smearing *and* as SSR/SSGI ghosting trails. An installation using none of those
can turn `writeMotionVectors` off: it drops `_PosPrevFrame` and the channel and shortens the write kernel.

## Bounds

`mesh.bounds` comes from a GPU AABB reduce — `KBoundsClear` then a single `KBoundsReduce` (groupshared reduce,
then one ordered-int atomic per axis per group) — read back with `AsyncGPUReadback`. The two frames of
latency are tolerated by padding the box with `2 · maxVelocity · dt`, and a new reduce is issued only when
none is in flight, so the frame never stalls. `VelaClothBounds` owns the buffer and the readback.

## Pipeline portability

The whole pipeline question reduces to one fact: the solver emits a per-vertex object-space displacement in
`TEXCOORD4`, and that is precisely the input both HDRP's and URP's object motion-vector paths need. HDRP
ships a consumer for it; URP does not, so a URP material needs a hand-written `MotionVectors` pass that
substitutes `positionOS − TEXCOORD4` for the previous position. Rendering assets are therefore split per
pipeline under `Rendering/<Pipeline>/`, and nothing in `Runtime/` knows which one is in use.

The HDRP pass is not hand-written to share one implementation with URP: the `LightMode = "MotionVectors"`
tag is the same string, but the includes, varyings, output encoding and render-graph plumbing differ
completely, and reproducing what HDRP's Shader Graph generates is fragile across HDRP versions — the same
objection that rules out `RenderPrimitives`.

`VelaClothSimulation` exposes `PositionBuffer` and `VelocityBuffer` so a VFX Graph or a material can read the
simulation without a second copy. `Runtime/Materials/VelaClothDebug.shader` is unlit and pipeline-agnostic, with
`Normal`, `UV` and `Velocity` modes for inspecting the written channels and a `Facing` mode (green front, red
back) showing which side of the sheet each pixel rasterizes as.

## Reading solver buffers from a material

The sample shaders in `Samples/Shaders/` read the solver directly instead of the vertex stream alone, and the
pattern generalises to any custom material:

- The index buffer references vertices directly with no base vertex, so `SV_VertexID` is the grid id
  `y · W + x`, and a vertex shader can address its neighbours in `PositionBuffer` (`float4`: object-space
  position, `w` = inverse mass, `0` for a pinned vertex) and `VelocityBuffer` (`float4`: object-space m/s).
- `VelaClothArtData.hlsl` declares the buffers and grid constants and derives, per vertex: strain along u and
  v (central-difference edge length over rest spacing, one-sided at the border), shear (cosine between the two
  edges), curvature (Laplacian dotted with the normal over rest area, in 1/m) and displacement from the rest
  position rebuilt from uv, sheet size and pivot. The Laplacian uses the vertices two steps away: the
  adjacent-vertex stencil alternates row by row with the solver's residual, and a one-sided stencil would read
  the border as a permanent crease, so each axis is masked out there instead. It has no pipeline include, so
  both the hand-written shaders and a Shader Graph can pull it in. `VelaClothArtCommon.hlsl` layers the
  hand-written shaders' `Attributes`/`Varyings`, the sun globals and the shading helpers on top of it and
  includes `UnityCG.cginc`, which is why a graph must never include Common.
- **Shader Graph path** (`VelaClothArt_Fabric.shadergraph`): a `Vertex ID` node feeds a file-mode Custom
  Function on `VelaClothArtData.hlsl` (`VelaClothVertex_float`) whose three float4 outputs go to custom
  interpolator blocks `VelaStrain` (strainU, strainV, shear, curvature), `VelaMotion` (velocityOS, displacement)
  and `VelaSheet` (sheetUv in metres, pinned). The Custom Function's file reference is the include's `.meta`
  GUID, so the include must keep its meta. `_VelaGrid`/`_VelaSheet` are declared in the include rather than as
  graph properties so the binder's `MaterialPropertyBlock` reaches them the same way it reaches the buffers.
  The graph file itself is generated JSON; edit it in the Shader Graph window, never by hand.
- `VelaClothArtBinder` binds the buffers and grid constants through a `MaterialPropertyBlock` in `OnEnable`
  and every `LateUpdate`, because `Rebuild()` recreates the buffers and same-object `Update` order is not
  guaranteed. A draw whose `StructuredBuffer` is unbound is dropped with a D3D12 warning, so the first binder
  alive also sets a one-element global fallback for both buffers: draws without a block (the material
  preview in the Inspector) read zeros and collapse silently instead of warning.
- Each shader is a `ForwardOnly` pass (the slot HDRP rasterizes for any SRP-agnostic shader) plus a
  `ShadowCaster` pass. The shadow pass uses HDRP's own includes rather than `UnityCG`: HDRP pushes the shadow
  view-projection into its `_ViewProjMatrix` global and `_ZClip` as a global float, so `TransformWorldToHClip`
  and `ZClip [_ZClip]` follow it while the legacy matrices would not. The forward pass keeps `UnityCG` because
  HDRP calls `SetupCameraProperties` for the camera. There is no `DepthForwardOnly` or `MotionVectors` pass, so
  the sheet is absent from the depth prepass and from per-object motion vectors.

## Sheer cloth

`VelaClothArt_Voile` is the one transparent sample shader: premultiplied alpha (`Blend One OneMinusSrcAlpha`)
in the transparent queue with `ZWrite Off` and no `ShadowCaster` pass. Its alpha is
`1 - (1 - opacity)^(1/|N·V|)`, Beer-Lambert through a sheet of constant density, which is what makes the
silhouette solid while the face-on middle stays sheer. Sorting between sheets is HDRP's per-renderer distance
sort; within one sheet the triangles blend in grid order, which is wrong but invisible while the sheet is one
colour. Since the pass is transparent it is also absent from the depth buffer, so nothing depth-based — fog,
SSR, TAA's depth rejection — sees the cloth.

## Self-shadowing across a fold

A fold is two layers `2 × half-thickness` apart, nothing thicker. Directional shadows resolve that gap only when
a shadow texel plus normal bias is well under it; otherwise the lower layer is lit through the upper one and
the leak shows as a bright band hugging the crease. The double-sided normal mode only changes which way the
normal bias pushes the sample — `Flip`/`Mirror` toward the light on back-face pixels, `None` away from it — so it
hides or reveals the leak without being its cause. The sample scene's light uses a 4096 shadow map for that
reason.
