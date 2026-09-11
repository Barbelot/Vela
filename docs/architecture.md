# Architecture

Design constraints and conventions that the code cannot state for itself. For the physics see
[solver.md](solver.md), for the mesh and pipeline side [rendering.md](rendering.md), for how it is checked
[testing.md](testing.md).

## Environment

Unity 6000.6, HDRP 17.6, .NET Standard 2.1, Mono desktop/Windows. `allowUnsafeCode` is off project-wide; the
runtime asmdef opts in on its own rather than changing the project setting, for `AsyncReadManager`
(`void*` buffers) once a cache reader needs it. Both asmdefs are `autoReferenced: false`.

## Hard constraints

- **`VelaClothSolver.Step(float dt)` never reads `Time`, `Application` or any frame counter.** Time enters only as
  an argument. `VelaClothSolver.SimulationTime` is state accumulated from `dt` and zeroed by `Reset`, not a clock.
  This is what lets one solver serve a realtime driver, an offline bake and cache playback, and what makes a
  bake see the same gust at the same frame as a live run.
- **`Runtime/` never references a render pipeline.** No SRP asmdef reference, no `#if HDRP`. It touches only
  `Mesh`, `MeshFilter`, `MeshRenderer`, `GraphicsBuffer`, `ComputeShader`, `AsyncGPUReadback` and
  `AsyncReadManager`. Pipeline-specific material lives under `Rendering/<Pipeline>/` as assets only.
- **No reference to other project code** — not UnityUtilities, OCF, NaughtyAttributes, FluXY. Engine modules only.
- **Generated grids only.** Every per-vertex buffer is flat with `id = y · W + x`; constraints are partitioned
  by index arithmetic, so there is no colouring preprocess and no atomics in the solve.
- **GPU compute only.** There is no CPU backend.

## Conventions and why

- Compute shaders and `.hlsl` live in `Runtime/Resources/Shaders/` and load by name through `VelaClothResources`,
  so no component needs them wired by hand. **Each `VelaClothSolver` instantiates its own copies** of the
  `ComputeShader` assets, because keywords and uniforms on the asset are shared state between solvers.
- `VelaClothProfile.cs.meta` and `VelaClothDebug.shader.meta` carry hand-authored GUIDs, because the `.asset`
  and `.mat` files that reference them are hand-authored too. Do not let Unity regenerate them.
- `KCollideAnalytic` lives in `VelaClothSolver.compute` rather than its own file: a second `ComputeShader`
  instance would need every buffer bound and every per-substep uniform pushed twice for one kernel.
  Self-collision does get its own `VelaClothSelfCollision.compute`, because its buffers come and go with the
  profile (below) and nothing else in that file is compiled either way.
- `VelaClothColliderRegistry` is **per solver**, not a static singleton. The previous-pose history that makes a
  moving collider drag the cloth is per-cloth state; a shared registry would let the first cloth to pack in a
  frame consume the motion and leave the second with none. Colliders are packed once at the top of
  `VelaClothSolver.Step`, so a driver running two steps in a frame does not replay the same motion twice.
- Self-collision buffers are owned by `VelaClothSelfCollision`, which `VelaClothSolver.Step` constructs on the first
  step a profile wants it and disposes on the first step that does not. It is the one feature whose memory
  comes and goes; everything else is a fixed cost the profile merely re-tunes. That is what makes "toggling it
  off restores the prior cost exactly" true of memory as well as time.
- `VelaClothForceSettings` holds only `gravity` and is kept as a struct on purpose: it is the seam a later force
  lands on without disturbing the component's field list.
- The inspectors declare their foldout groups as data (`VelaClothInspectorSections`, `VelaClothInspectorGroup[]` per
  editor) and `Tests/VelaClothInspectorCoverageTests.cs` reads the same tables to assert every serialized field is
  drawn by exactly one group and has a non-empty tooltip. `Tests` therefore references the Editor asmdef.
  Adding a field without a group or tooltip fails a test instead of silently vanishing from the inspector.

## Who owns a setting

**The profile is what the fabric *is*; the component is *this drape, here, and the world around it*.** The
profile is meant to be shared, so everything intrinsic to the cloth lives on it — mass, stiffness, damping,
aerodynamic coefficients, speed clamp, solver cost — and everything about this particular sheet and its
surroundings lives on the component.

Three splits look arbitrary and are not:

- **Drag and lift are on the profile, `airDensity` is on the component.** The coefficients describe how a
  surface catches air, so silk and leather differ in them; the density describes the air, and two drapes in
  one scene breathe the same air.
- **Damping is on the profile, gravity is on the component.** Damping is how a fabric dissipates its own
  motion; gravity is the scene pulling on it, and it is an acceleration, so it does not care what the sheet is
  made of.
- **`maxVelocity` is on the profile** because the two values that decide whether it is safe — `substeps` and
  the self-collision contact radius — are both profile-side.

`VelaClothWindSettings.HasEffect` describes the air alone; `VelaClothProfile.HasAerodynamicResponse` the
fabric alone; `VelaClothSolver.AerodynamicsActive` ANDs them.

## Dirty flags

- Any component field edit (`OnValidate`) sets one dirty flag; the next `Update` rebuilds mesh, solver,
  inverse mass and long-range tables and re-runs `preRollSteps`, so the sheet re-settles instead of snapping.
- **A density edit on the profile sets the dirty flag rather than rebuilding inline.** Density rides in every
  vertex's inverse mass, so it needs the full rebuild — but `Rebuild` disposes the solver, and calling it from
  the middle of `Update` leaves the rest of that frame's `PushSettings`/`Tick` holding a null. Deferring by one
  frame costs nothing.
- `lraAnchorCount` is watched in `VelaClothSimulation.Update`, not `OnValidate`, because editing the profile
  *asset* never reaches the component's `OnValidate`. It takes the cheap path: a Dijkstra rebuild and table
  upload, no mesh rebuild. Every other profile field is re-read every step, which is what makes a live profile
  swap work.
- The Dijkstra build is never run per inspector repaint — a million vertices takes about a second.

## Dispatch order per substep

`h = dt / substeps`. Colliders are packed and step constants pushed once per step.

| # | Kernel(s) | Dispatches | Condition |
|---|---|---|---|
| 0 | `KLra` | 1 | once, outside the loop, on the step the constraint activates |
| 1 | `KPredict` | 1 | |
| 2 | `KDistanceH`, `KDistanceV` × 2 parities | 4 | |
| 3 | `KShearA`, `KShearB` × 2 parities | 4 | `useShear` |
| 4 | `KBendH`, `KBendV` (+ `KBendDiagA`, `KBendDiagB`) × 3 phases | 6 or 12 | |
| 5 | `KLra` | 1 | long-range attachment active |
| 6 | hash build (7) + `KSelfCollideAccum`, `KSelfCollideApply` | 9 | `useSelfCollision`, every `selfCollisionStride`-th substep |
| 7 | `KCollideAnalytic` | 1 | at least one collider |
| 8 | `KUpdateVelocity` | 1 | |

Default profile (shear off, two bending directions, self-collision off) is 13 dispatches per substep. Two
orderings are load-bearing:

- **Self-collision resolves before `KCollideAnalytic`.** A Jacobi pile pushes in whatever direction it needs
  and half of those point into the ground; the analytic primitives must have the last positional word or a
  substep ends with vertices inside them and `KUpdateVelocity` reads the penetration as velocity.
- **The aerodynamic gather runs at the end of `KUpdateVelocity`, not in `KPredict`.** `KPredict` writes
  `_Pos` and `_Vel`, so a gather over neighbouring vertices there races sibling threads and costs
  bit-reproducibility. `KUpdateVelocity` only reads `_Pos`/`_PosPrev` and derives every velocity from them,
  so the gather is exact there. The impulse lands half a substep later, which is not observable; `_WindTime`
  is therefore pushed per substep as `SimulationTime + (s + 1) · h`.

Per rendered frame: `KWriteVertexBuffer`, then `KBoundsClear` + `KBoundsReduce` only when no readback is in
flight.

## Reproducibility

Fixed `dt`, fixed substeps, a hash sorted by vertex id (`KHashSort`) and no float atomics make the solver
bit-reproducible on the same GPU and driver. It is **not** reproducible across vendors, nor inside a hash cell
that has overflowed its 64-member cap.
