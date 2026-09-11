# Painted pin masks and Transform attachments

## Context

Pinning is declarative only: `VelaClothPinMode` on the component, decoded by `VelaClothPinning.IsPinned` into the per-vertex inverse mass in `_Pos.w`. The enum already carries a `Custom` entry that nothing consumes. Long-range attachment tables (`VelaClothLongRangeAttachment.Build`) are seeded from whatever is pinned, so any new pin source must re-arm the same rebuild path a `pinMode` change takes.

## Scope

- `Runtime/Scripts/VelaClothPinMaskAsset.cs` — `ScriptableObject` with a `uint[]` bitfield (`W·H` bits) plus the `W, H` it was painted for. The component warns, rather than silently corrupting, when its grid dims differ.
- `VelaClothPinMode.Custom` reads the mask; `VelaClothSimulation` gains a `pinMask` field in the Pinning inspector group.
- `Editor/VelaClothPinPaintTool.cs` — an `EditorTool` scene-view brush that raycasts the cloth mesh, toggles bits in the asset, and draws painted vertices. Undo-aware.
- `Transform[] attachments` on the component — each binds its nearest vertex (by rest position at bind time) and drives it kinematically: `_AttachIndex` / `_AttachTarget` buffers, and `KPredict` snaps those vertices to the target (in cloth object space) with zero inverse mass. Targets are pushed every step from the transforms, so a rigged or animated pin follows.
- Record the design in `docs/`, update `README.md` (Components → Pinning), then delete this file.

## Design

Not yet current state; moves to `docs/` when implemented.

- Attached vertices count as pinned for `VelaClothLongRangeAttachment.Build` and for `VelaClothSolver.LongRangeActive`.
- A change to the mask, the attachment list or `pinMode` sets the same dirty flag a resolution change does; the Dijkstra rebuild must never run per inspector repaint (~1 s at a million vertices).
- Attachment targets are motion the cloth reads in `v = (x − xⁿ)/h`, so a target that jumps flings its neighbourhood. Lerp the target across substeps the way collider poses are (`_ColliderT0` / `_ColliderT1`).

## Verification

- Paint an arbitrary pattern on a 64×64 sheet; the painted vertices hold, the rest hang; long-range attachment reports the painted anchor count.
- Change `resolution` with a mask assigned: inspector warning, no exception, sheet falls back to unpinned.
- Attach two transforms, animate one in play mode: the sheet follows without ringing; a Transform moved 1 m in one frame does not throw the sheet past `maxVelocity`.
- `VelaClothInspectorCoverageTests` passes with the new fields.
