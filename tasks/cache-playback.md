# Play a baked cache back through the live mesh path

## Context

Requires `docs/cache-format.md` and `Runtime/Scripts/Cache/VelaClothCacheWriter.cs`, which exist once baking ships; until then this task is blocked. Playback reuses `VelaClothMeshBinding`, `KWriteVertexBuffer` and `VelaClothBounds` unchanged: the only new work is getting quantized frames from disk into `_Pos` (and `_PosPrevFrame` for motion vectors) without solving. `VelaClothSimulation` currently has no mode field — the realtime driver is the only `IVelaClothDriver`.

## Scope

- `Runtime/Scripts/Cache/VelaClothCacheReader.cs` — `Unity.IO.LowLevel.Unsafe.AsyncReadManager.Read` (in `UnityEngine.CoreModule`, so no package dependency; this is why the runtime asmdef carries `allowUnsafeCode: true`). Ring of 8 staging `NativeArray<byte>` slots plus one Raw `GraphicsBuffer` of `8 × payloadBytes`; keep 2 `ReadHandle`s in flight; on completion `SetData` the slot at its ring offset. Seek drops in-flight handles and re-prefetches from `dataOffset + frame · frameStride`. Loop wraps the index and prefetches frame 0 while still playing the tail.
- `KDequantLerp` in `VelaClothCache.compute` — fetch the two bracketing frames, dequantize against each frame's AABB, lerp into `_Pos`.
- `Runtime/Scripts/Drivers/VelaClothCacheDriver.cs` — no solving; `frameCursor += dt · cacheFrameRate`, signed so scrubbing and reverse work; then `KDequantLerp` → `KWriteVertexBuffer`.
- `VelaClothSimulation` gains `mode` (`Simulate | PlayCache`), `cache` (`VelaClothCacheAsset`), `playbackSpeed`, `loop`, `loopBlendFrames` (blends the last K frames when the bake is not loop-matched). Add them to a `VelaClothInspectorGroup` — `VelaClothInspectorCoverageTests` fails otherwise.
- `Editor/VelaClothCacheInspector.cs` — header display and a scrub slider previewing frames in edit mode.
- Record the shipped design in `docs/`, update `README.md` (Limitations) and `docs/components.md`, then delete this file.

## Design

Not yet current state; moves to `docs/` when implemented.

- **Zero `TEXCOORD4` on the frame after any seek or loop wrap**, or TAA smears across the discontinuity. The simplest way is to copy the dequantized frame into `_PosPrevFrame` as well on that frame.
- `preRollSteps` is meaningless in `PlayCache`; hide it and the solver-only fields in that mode.
- The reader must tolerate a frame rate that differs from the render rate (a 30 fps bake played at 60) — that is what the lerp is for.
- Buffer lifetime follows `VelaClothMeshBinding`'s pattern: dispose in `OnDisable`, `OnDestroy` and `beforeAssemblyReload`.

## Verification

- Bake 300 frames, play the cache next to a live sim from the same state, diff a readback: must match to quantization error (~0.1 mm).
- Scrub forwards and backwards in the inspector without hitching; loop point shows no jump and no TAA smear.
- Play a 30 fps bake at 60 fps render: motion is smooth, not stepped.
- Leak check: enter/exit play mode 10× in `PlayCache`, console stays clean.
