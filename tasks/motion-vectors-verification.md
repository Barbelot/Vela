# Verify motion vectors end to end

## Context

`KWriteVertexBuffer` writes `TEXCOORD4 = positionOS − previousPositionOS` and the renderer is set to `MotionVectorGenerationMode.Object`; `Rendering/HDRP/VelaClothLit_HDRP.mat` has "Add Precomputed Velocity" on. The chain has never been checked against HDRP's debug view, and it is the most likely visual bug: any one link missing looks like a TAA problem but also degrades SSR, SSGI and temporal upscalers. See `docs/rendering.md` for the convention.

## Scope

- Verify in `Samples/Scenes/ClothSimulation.unity`; fix whichever link is wrong.
- If nothing is wrong, update `docs/components.md` (Motion vectors) to say it is verified, then delete this file.

## Verification

1. Rendering Debugger → Rendering → Motion Vectors, drape moving under wind: the sheet shows coherent per-vertex vectors, not the flat colour of transform-only motion.
2. TAA on: no smear trailing the drape's edges. Compare with `writeMotionVectors` off to see what wrong looks like.
3. SSR on with a glossy floor under the drape: no ghosting trail in the reflection.

## Notes

- Checklist when it fails: `writeMotionVectors` true; `MeshRenderer.motionVectorGenerationMode == Object`; the material's "Add Precomputed Velocity" toggle; `TEXCOORD4` is current minus previous (HDRP reconstructs `previousPositionOS = positionOS − velocity`).
- `VelaClothDebug.mat` in its velocity mode shows the raw channel, which separates a solver-side bug from a material-side one.
