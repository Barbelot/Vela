# Testing

EditMode tests run from Window → General → Test Runner; the `Vela.Tests` asmdef needs only
the Test Framework package. `VelaClothSelfCollisionHashTests` drives the compute shader and skips itself without a
compute-capable device.

## What each file guards

- `VelaClothGridTopologyTests` — every structural and shear colour class is a true matching and covers every edge
  exactly once, for several odd/even `W×H`; bending phases touch each centre once; indices stay in range and
  share the fixed diagonal; pinned vertices get zero inverse mass; Dijkstra distances on small grids match
  hand-computed values, satisfy the triangle inequality, and the second anchor is a distinct pin. A colour
  that is not a matching corrupts `_Pos` silently, which is why these are asserted rather than eyeballed.
- `VelaClothScaleInvarianceTests` — `ReferenceSpacing` is the reference sheet's rest spacing; compliance cancels
  `areaDensity`; stiffness `1` is exactly compliance `0`; the local-damping `SmoothingScale` tracks spacing
  squared and depends on spacing alone.
- `VelaClothSelfCollisionHashTests` — the GPU prefix sum matches a CPU exclusive scan for cell counts that are
  **not** a multiple of the 512-wide scan block (that off-by-one corrupts the hash silently, as sporadic missed
  contacts rather than a crash), and the counting sort places every vertex exactly once in its own cell.
- `VelaClothInspectorCoverageTests` — every serialized field on `VelaClothSimulation`, `VelaClothProfile` and
  `VelaClothCollider` is drawn by exactly one inspector group and carries a non-empty tooltip. This is the check
  most likely to rot the next time a field is added.

## Manual checks that are not obvious

- **Substep invariance** uses `Samples/Profiles/ClothQuality_Test_Substeps2` and `_Substeps32`, read on
  **sag distance** of the bottom edge: same stiffness, 16× the substeps, the edge must hang to the same length.
  The pair carries a deliberately soft `structuralStiffness = 0.33`, because at `1` the compliance is `0`, the
  `α̃ = α / h²` division is `0 / h²` either way and the test is blind. Both turn `useLongRangeAttachment`
  **and** `useAerodynamics` off: long-range attachment caps sag at the geodesic, and wind moves the sheet the
  reading is taken from — with either on, every reading collapses or wanders. Oscillation speed is not the
  reading: at higher substep counts a stiff sheet legitimately converges closer to inextensible and bounces
  faster, which is convergence, not a compliance bug.
- **Bending and shear are invisible on an edge-pinned sheet under uniform gravity.** The equilibrium is the
  flat plane through the pin line and the gravity vector; tilting gravity only rotates that plane. The test
  that discriminates is a **cantilever**: rotate the GameObject `X = 90` so gravity runs through the sheet's
  face, pin the top edge, and drop `resolution` to ~32 so the constraints converge over the chain. Leather
  then holds out like a diving board while Silk falls to vertical. At 128 rows and 8 substeps constraint
  information travels ~8 rows per frame and everything reads mushy regardless of profile. A collider draped
  over is the other way to force curvature, and needs no rotation.
- **Fabrics** — the sample scene's `FabricRow` hangs the five `ClothFabric_*` profiles side by side under one
  wind; an artist should be able to name each from its motion alone.
- **Determinism** — step the solver twice from identical state, read back, compare bitwise. This also guards
  against `Time.*` leaking into `VelaClothSolver`.
- **Leak check** — enter/exit play mode and force a domain reload 10× with the component enabled; the console
  must stay free of `GraphicsBuffer was not disposed`.
- **Bounds** — no shadow pop at the frustum edge with the drape swinging; if there is, the readback padding is
  short of `2 · maxVelocity · dt`.
