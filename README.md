# Vela

GPU cloth for large flowing drapes — curtains, flags, sails — simulated on a generated rectangular grid and
drawn by a stock `MeshRenderer`. Realtime at roughly 5k–40k vertices; one profile asset describes a fabric
and means the same fabric at any resolution, sheet size or quality setting. The plugin is self-contained:
engine modules only, no other package or project code.

## Requirements

- Unity 6 (6000.6) and a GPU with compute shader support.
- HDRP for the shipped lit material, `Rendering/HDRP/VelaClothLit_HDRP.mat`. The simulation itself is
  pipeline-agnostic and `Runtime/Materials/VelaClothDebug.mat` renders on any pipeline.

## Installation

1. Copy `Assets/Plugins/Vela/` into your project.
2. Using it from a scene needs nothing more. To reference the `Vela` namespace from your own scripts, add
   `Vela` to your assembly definition's references — the plugin's asmdef is not
   auto-referenced.

## Getting started

1. Create an empty GameObject and add **Vela → Cloth Simulation**. It adds and drives its own `MeshFilter`
   and `MeshRenderer`.
2. Assign a material: `Rendering/HDRP/VelaClothLit_HDRP.mat`, or `VelaClothDebug.mat` to see normals, UVs or velocity.
3. Assign a **Cloth Profile** — one of `Runtime/Resources/ClothQuality_{Realtime,Balanced,Offline}` or a
   fabric from `Samples/Profiles/`. With none assigned the component runs on built-in defaults.
4. Leave `pinMode` on `TopEdge` and press Play: the sheet hangs, settles and answers the wind field under
   **Environment**.

`Samples/Scenes/ClothSimulation.unity` hangs the five sample fabrics side by side under one wind, with a box
and sphere colliders to drape over.

## Components

### Cloth Simulation

The one component a drape needs. Its inspector is grouped into foldouts; every field has a tooltip.

**Geometry**
- `sizeMeters` — width and height of the sheet, before the transform's scale.
- `resolution` — vertices along the longer axis; the other axis follows to keep quads square. This is the
  single cost/detail dial; the profile means the same fabric at any value.
- `pivot` — where the transform sits on the sheet, in 0–1 of its size. `(0.5, 1)` hangs it from the middle of
  its top edge.

**Quality**
- `qualityProfile` — the fabric (see below). Shared between drapes; swapping it at runtime works.
- `simulationRate` — simulation steps per second, independent of the render rate (default 60).
- `maxStepsPerFrame` — steps allowed in one frame; on a hitch the sim falls behind in slow motion rather than
  spiralling (default 3).
- `preRollSteps` — steps run at rebuild so the drape starts settled instead of snapping down from flat
  (default 60). Wind runs during them too.

**Pinning**
- `pinMode` — `None`, `TopEdge`, `TopCorners` or `LeftEdge`. `Custom` is reserved.

**Environment**
- `forces.gravity` — world-space acceleration in m/s².
- `wind` — the air around this drape, in world space: `direction`, `speed` (m/s; a flag lifts around 3 and
  snaps taut past 12), `gustAmplitude` and `gustFrequency` (gusts travel downwind across the sheet),
  `turbulence`, `turbulenceScale` (eddies per metre: 0.2 is sheet-sized rolls, 3 is ripples),
  `turbulenceSpeed`, and `airDensity` (1.225 is sea-level air; raising it makes the whole wind bite harder).

**Rendering**
- `material` — assigned to the `MeshRenderer`.
- `castShadows` — `TwoSided` is usually right for a drape.
- `writeMotionVectors` — see *Motion vectors* below.

**Diagnostics** (collapsed by default) reports what the profile and this grid resolve to together: substeps
per second, each constraint family's effective compliance and per-substep response, damping time constants,
wind pressure, the long-range and self-collision state, and dispatch counts. Reading it while dragging a slider
is the intended way to tune — a response in single digits means that setting is doing almost nothing in this
scene.

Public API: `Grid`, `Mesh`, `PositionBuffer`, `VelocityBuffer` (for VFX Graph or custom rendering),
`Resolution`, `Profile`, `Rebuild()`.

### Cloth Profile

Create one with **Assets → Create → Vela → Cloth Profile**. **The profile is what the fabric *is*; the
component is *this drape and the world around it*.** Everything intrinsic to the cloth lives here so one asset
can be shared by every drape made of that material.

| Profile — the fabric | Component — this drape |
|---|---|
| `areaDensity`, the stiffness sliders, bending mode and directions | `sizeMeters`, `resolution`, `pivot`, `pinMode` |
| `globalDamping`, `localDamping` | `gravity` |
| `dragCoefficient`, `liftCoefficient`, `useAerodynamics` | the wind field and `airDensity` |
| `substeps`, `maxVelocity`, long-range attachment, self-collision | `simulationRate`, `maxStepsPerFrame`, `preRollSteps`, rendering |

**Fabric** — `areaDensity`, kg/m² (silk 0.06, cotton 0.2, denim 0.45, leather 1). Decides how hard wind
pushes the sheet; under gravity alone it changes nothing.

**Stepping** — `substeps` (constraint iterations per step, the main cost dial; raising it converges harder
without changing the look) and `maxVelocity` (a safety clamp in m/s, not a damper).

**Stiffness** — `structuralStiffness`, `useShear` + `shearStiffness`, `bendingStiffness`, all `[0, 1]`:
0 is limp, 1 is rigid, and the value means the same fabric at any resolution, sheet size, density or substep
count. Shear makes cloth read stiff and paper-like, so drapes usually leave it off.

**Bending** — `bendingMode` (only `Curvature` is implemented) and `bendingDirections`; adding the diagonals
removes a faint axis-aligned preference in folds for six more dispatches per substep.

**Damping** — `globalDamping` (air drag on everything, in 1/s) and `localDamping` (removes velocity
differences between neighbours, in 1/s — the weight dial).

**Long-range attachment** — `useLongRangeAttachment` caps each vertex's distance from its pins at the rest
distance, the cheapest way to stop a pinned drape stretching; `lraAnchorCount` (1 for a top-edge hang, 2 for
two corners) and `lraStretchAllowance` (0–10 % past the rest distance, live). Disables itself with nothing
pinned.

**Self-collision** — `useSelfCollision` stops the sheet passing through itself and is the most expensive
feature; `selfCollisionThicknessMode` picks between a half-thickness as a fraction of rest spacing
(`selfCollisionRadiusScale`, right at any resolution) or in metres (`selfCollisionThickness`, holds the look
across resolutions but is clamped by the grid); `selfCollisionStride` (solve every Nth substep, the cost dial),
`maxContactsPerVertex`, `selfCollisionFriction` (0 lets folds slide flat, 1 makes them grip).

**Aerodynamics** — `useAerodynamics`, `dragCoefficient` (~1 for cloth; drag alone looks limp and wet) and
`liftCoefficient` (what makes a flag billow; silk ~0.35, leather ~0.05).

### Cloth Collider

Add **Vela → Cloth Collider** to any GameObject. Sphere, capsule, box or plane; every cloth in the scene finds
it, up to 64 per cloth. These are independent of Unity Physics, so you can author collision volumes that exist
only for the cloth.

- The transform supplies position and rotation. Scale is folded into `radius` / `height` / `size` so
  `thickness` and `friction` stay in metres; spheres and capsules take the largest axis and the inspector warns
  when non-uniform scale is lost.
- The plane is the local `Y = 0` half space facing `+Y`, infinite whatever its gizmo shows.
- `thickness` is the gap kept between surface and cloth (keep it above about half the rest spacing);
  `friction` is 0 slips, 1 sticks.
- A moving collider drags the cloth with it rather than shaving through it.
- `alwaysDrawGizmo` and `gizmoColor`, since the volume has no renderer.

### Presets and fabrics

`Runtime/Resources/` ships three quality presets that differ in cost, not material:

| Preset | Substeps | Shear | Self-collision |
|---|---|---|---|
| `ClothQuality_Realtime` | 6 | off | off |
| `ClothQuality_Balanced` | 10 | off | off |
| `ClothQuality_Offline` | 20 | on, diagonal bending | on |

`Samples/Profiles/` ships five fabrics that differ in density, bending, shear, damping, lift and self-collision
friction at once:

| Fabric | kg/m² | Reads as |
|---|---|---|
| `ClothFabric_Silk` | 0.06 | Fine, fast ripple; lifts and billows on the least wind, falls in many shallow folds |
| `ClothFabric_Cotton` | 0.20 | The neutral middle — a shirt or a bedsheet |
| `ClothFabric_Denim` | 0.45 | Heavier, fewer and broader folds, resists twisting |
| `ClothFabric_Canvas` | 0.60 | Sailcloth: holds its shape, swings slowly, barely ripples |
| `ClothFabric_Leather` | 1.00 | Nearly rigid, deep sculpted creases, almost inert under wind |

`ClothQuality_Test_Substeps2` / `_Substeps32` are a calibration pair, not fabrics.

## Tuning

- **Stiffness sliders are the material, not the sheet.** 1 is rigid; cloth sits at 1 for stretch and around
  0.5 for bending. **Lower** `bendingStiffness` for a heavier hang — stiff bending holds the sheet out like
  card.
- **`localDamping` is the weight dial.** It kills ripple and spring-back without slowing the drape's swing.
  5 is light, 30 reads as silk, 100 as denim, 160 as leather. `globalDamping` slows everything and reads as
  syrup past a few 1/s.
- **Raise `substeps` before lowering stiffness.** A sheet that springs even at stiffness 1 has not converged;
  more substeps, fewer rows or long-range attachment fix it, and only the last is cheap.
- **Long-range attachment** is on by default; leave it. If a wide drape stiffens into a cone at the bottom,
  raise `lraStretchAllowance`.
- **Self-collision**: keep the half-thickness at `0.8` of rest spacing unless the grid is fine enough for a
  metric value; use `selfCollisionStride = 2` to halve the cost; raise `maxContactsPerVertex` if a pile
  interpenetrates; `maxVelocity` below `2 × half-thickness × substeps × simulationRate` prevents tunnelling.
- **Wind**: `liftCoefficient` makes it billow, `dragCoefficient` makes it stream; `areaDensity` is the dial
  when the wind looks right but the cloth is too eager; `airDensity` is the blunt one.
- Read the **Diagnostics** foldout while dragging any slider.

The reasoning behind every dial is in [docs/solver.md](docs/solver.md).

## Motion vectors

Correct motion vectors on a deforming mesh need three things at once: `writeMotionVectors` on, the material's
**Add Precomputed Velocity** option on (`VelaClothLit_HDRP.mat` has it), and the component's own `MeshRenderer`.
Any one missing looks like TAA smearing and also degrades motion blur, SSR/SSGI and temporal upscalers. If the
project uses none of those, turn `writeMotionVectors` off and save 12 B/vertex.

## Limitations

- Not implemented: baking to a cache and cache playback, painted pin masks (`Custom` pin mode), Transform
  attachments, a URP material.
- Generated rectangular grids only; no arbitrary meshes.
- Up to 64 colliders per cloth. Colliders are analytic primitives; there is no mesh or SDF collision.
- Self-collision is point-based (no edge or triangle tests) and degrades into approximation when more than
  64 vertices share a hash cell.
- Results are bit-reproducible on the same GPU and driver, not across vendors.
- Ray-traced effects lag the geometry by a frame.
- Known issue: a moving drape can show bright bands along its fold creases.

## Further reading

- [docs/architecture.md](docs/architecture.md) — constraints, conventions, who owns a setting, dispatch order.
- [docs/solver.md](docs/solver.md) — the physics and the reasoning behind every dial.
- [docs/rendering.md](docs/rendering.md) — vertex buffer, motion vectors, bounds, pipeline portability.
- [docs/testing.md](docs/testing.md) — the tests and the manual checks that are not obvious.
