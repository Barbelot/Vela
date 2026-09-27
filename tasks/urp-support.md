# URP rendering support

## Context

`Runtime/` touches no render pipeline; the only pipeline-specific piece is `Rendering/HDRP/VelaClothLit_HDRP.mat` on HDRP's stock Lit shader, whose "Add Precomputed Velocity" option consumes the object-space displacement the solver writes to `TEXCOORD4`. URP ships no consumer for that channel. See `docs/rendering.md` (Pipeline portability).

## Scope

- `Rendering/URP/VelaClothLit_URP.shader` + `.mat`: a hand-written URP Lit variant whose `MotionVectors` pass is URP's own object motion-vector pass with the previous position replaced by `positionOS − TEXCOORD4`. Everything else about URP support is a material swap.
- Only if C# becomes necessary: a `Vela.URP` asmdef with `versionDefines` on `com.unity.render-pipelines.universal` → `URP_ENABLED` plus a matching `defineConstraints`, mirroring `Assets/Plugins/UnityBarbelotUtilities/PostProcess/HybridKuwahara/Runtime/HybridKuwahara.asmdef`. The core asmdef never gains a pipeline reference.
- Update `README.md` (Requirements, Limitations), `docs/components.md` and `docs/rendering.md`, then delete this file.

## Design

Not yet current state; moves to `docs/rendering.md` when implemented.

Do not hand-write the HDRP motion-vector pass to share one implementation with URP. The `LightMode = "MotionVectors"` tag is the same string, but the includes, varyings, output encoding and render-graph plumbing differ completely, and reproducing what HDRP's Shader Graph generates is fragile across HDRP versions. One free built-in plus one hand-written pass is the maintainable shape.

## Verification

- Sample scene renders in a URP project and the Rendering Debugger's motion-vector view shows per-vertex motion on the drape; TAA does not smear.
- **Zero changes to `Runtime/`** — that is the acceptance criterion.
