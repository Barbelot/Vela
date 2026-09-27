# Bake a simulation to a streamed binary cache

## Context

The solver runs realtime only: `VelaClothSimulation` owns a `VelaClothRealtimeDriver` that calls `VelaClothSolver.Step(dt)` from `Update`. `Step` reads no clock, so the same solver can be driven as fast as the GPU allows to record a cache that is replayed later — the path a ~1M-vertex drape needs. `VelaClothBounds` already reduces a per-frame AABB on the GPU, which is what quantization needs. Nothing under `Runtime/Scripts/Cache/` or `Editor/VelaClothBake*` exists yet.

## Scope

- `Runtime/Scripts/Cache/VelaClothCacheFormat.cs` — header/frame layout constants and a mirrored header struct.
- `Runtime/Scripts/Cache/VelaClothCacheWriter.cs` — sequential writer over `FileStream` (4 MB buffer, `FileOptions.SequentialScan`), writing the readback `NativeArray<byte>` span directly.
- `Runtime/Scripts/Cache/VelaClothCacheAsset.cs` — `ScriptableObject` holding a project-relative path to the binary plus a mirrored header for the inspector. The binary itself is deliberately not a Unity asset: a multi-GB file must not go through the importer. Store under `Assets/StreamingAssets/VelaClothCache/` and add `*.clothcache` to `.gitattributes` LFS next to the existing `*.bytes` rule.
- `KQuantize` in a `VelaClothCache.compute` — quantize on the GPU after `KBoundsReduce`, so 6 B/vertex cross PCIe instead of 16. That 2.6× reduction is the real bottleneck of a 1M-vertex bake.
- `Editor/VelaClothBakeSession.cs` — pumps `EditorApplication.update` with a 30 ms time budget per tick calling `Step(1 / bakeFrameRate)`, keeping the editor and a cancelable progress bar responsive. Readbacks pipeline through a 4-deep ring (complete frame `f−4` before issuing `f`). Handles `AssemblyReloadEvents.beforeAssemblyReload` (cancel, flush, dispose) and refuses to start when entering play mode.
- `Editor/VelaClothBakeWindow.cs` — duration, fps, pre-roll, output path; shows predicted MB/s and total size **before** the bake starts; progress, ETA, cancel.
- Record the shipped format in `docs/cache-format.md`, update `README.md` (Limitations, a Baking section), then delete this file.

## Design

Not yet current state; moves to `docs/cache-format.md` when implemented.

**Header, 256 B:** `magic u64` (`"VELACLTH"`) · `version u32` · `flags u32` (hasNormals / hasVelocity / quantized) · `vertexCount u32` · `frameCount u32` · `frameRate f32` · `gridW, gridH u32` · `globalBounds f32×6` · `frameStride u64` (padded to 256 B) · `dataOffset u64` · pad.

**Per frame:** 32 B header (`frameIndex u32`, `boundsMin f32×3`, `boundsMax f32×3`, pad) then payload, padded to `frameStride`.

**Precision: 3 × `unorm16` against the per-frame AABB = 6 B/vertex.** Half-float is rejected: its ulp near 5 m is ~4 mm, visible shimmer on a drape; per-frame `unorm16` over a 5 m bound gives 0.076 mm and adapts to scale. `_QuantOut` is a `uint` buffer of `⌈N·6/4⌉`.

**Positions only.** Normals and tangents are recomputed at playback by the same `KWriteVertexBuffer` the live solver uses, saving 40 % of file size and disk bandwidth. Keep `hasNormals` reserved for a future imported-mesh path.

**Quantization breathing.** A per-frame AABB that jumps shifts the quantization grid and shimmers. Use an expand-only AABB over a 16-frame window, snapped to a coarse quantum.

**Bandwidth.** 6 B/vert at 60 fps is 1.8 MB/s per 5k vertices, but 1M vertices is 360 MB/s and 21.6 GB/minute — NVMe at the top end. Expose the two mitigations in the window: bake at 30 fps and interpolate at playback, and a `spatialStride` LOD.

**Determinism.** Fixed dt, fixed substeps, no `Time` in `Step` — a bake sees the same gust at the same frame as a live run (`VelaClothSolver.SimulationTime` is zeroed by `Reset`, and pre-roll runs under wind too).

## Verification

- A `.clothcache` lands on disk at exactly the byte size the window predicted.
- Cancelling mid-bake leaves no `GraphicsBuffer was not disposed` in the console and no partial file that the reader would accept.
- Domain reload mid-bake cancels cleanly.
- **Stress:** a 1M-vertex offline bake completes; watch VRAM (~200 MB sim state + vertex + index + hash), sustained disk MB/s and wall time against the window's estimate.

## Notes

- `VelaClothSolver.Step` must keep reading no clock; the session passes `1 / bakeFrameRate` explicitly.
- Bake the readback from `_QuantOut`, never from `_Pos`, or the PCIe budget is blown 2.6×.
- `Rebuild` disposes the solver; the session must own its solver lifetime rather than borrowing the component's mid-`Update`.
