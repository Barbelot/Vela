# Components

Every field also has a tooltip in the inspector.

## Cloth Simulation

The one component a drape needs. It adds and drives its own `MeshFilter` and `MeshRenderer`; with no profile
assigned it runs on built-in defaults.

**Geometry**
- `sizeMeters` — width and height of the sheet, before the transform's scale.
- `resolution` — vertices along the longer axis; the other axis follows to keep quads square. This is the
  single cost/detail dial; the profile means the same fabric at any value.
- `pivot` — where the transform sits on the sheet, in 0–1 of its size. `(0.5, 1)` hangs it from the middle of
  its top edge.

**Quality**
- `qualityProfile` — the fabric (see *Cloth Profile*). Shared between drapes; swapping it at runtime works.
- `simulationRate` — simulation steps per second, independent of the render rate (default 60).
- `maxStepsPerFrame` — steps allowed in one frame; on a hitch the sim falls behind in slow motion rather than
  spiralling (default 3).
- `preRollSteps` — steps run at rebuild so the drape starts settled instead of snapping down from flat
  (default 60). Force volumes act during them too.

**Pinning**
- `pinMode` — `None`, `TopEdge`, `TopCorners`, `LeftEdge` or `Corners` (all four, for a sheet hung flat like
  a canopy). `Custom` is reserved.

**Environment**
- `forces.gravity` — world-space acceleration in m/s². Every other push comes from *Cloth Force Volumes* in
  the scene.
- `airDensity` — kg/m³ of the air the wind volumes blow through; 1.225 is sea-level air, raising it makes
  every wind bite harder, 0 makes them all inert.
- `volumeMask` — only force volumes on these layers reach this cloth.
- `transformInertia` — how much the free vertices resist the transform's own motion. At 1 they stay where
  they were in the world and the pins drag the sheet along; at 0 the whole sheet moves rigidly. A jump of
  several metres in one frame leaves the sheet that far behind; call `Rebuild()` after a teleport.

**Rendering**
- `material` — assigned to the `MeshRenderer`. `VelaClothDebug.mat` shows normals, UVs, velocity or
  front/back facing.
- `castShadows` — `TwoSided` is usually right for a drape.
- `writeMotionVectors` — see *Motion vectors*.

**Diagnostics** (collapsed by default) reports what the profile and this grid resolve to together: substeps
per second, each constraint family's effective compliance and per-substep response, damping time constants,
how many wind and acceleration volumes reach the cloth, the long-range and self-collision state, and dispatch
counts. Reading it while dragging a slider is the intended way to tune — a response in single digits means
that setting is doing almost nothing in this scene.

Public API: `Grid`, `Mesh`, `PositionBuffer`, `VelocityBuffer` (for VFX Graph or custom rendering),
`Resolution`, `Profile`, `Rebuild()`.

## Cloth Profile

Create one with **Assets → Create → Vela → Cloth Profile**. **The profile is what the fabric *is*; the
component is *this drape and the world around it*.** Everything intrinsic to the cloth lives here so one asset
can be shared by every drape made of that material.

| Profile — the fabric | Component — this drape |
|---|---|
| `areaDensity`, the stiffness sliders, bending mode and directions | `sizeMeters`, `resolution`, `pivot`, `pinMode` |
| `globalDamping`, `localDamping` | `gravity`, `transformInertia` |
| `dragCoefficient`, `liftCoefficient`, `useAerodynamics` | `airDensity`, `volumeMask` (the wind itself is a force volume in the scene) |
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
distance, the cheapest way to stop a pinned drape stretching; `lraAnchorCount` (how many pins hold each
vertex on an edge hang — a sheet pinned at up to four points uses all of them regardless) and
`lraStretchAllowance` (0–10 % past the rest distance, live). Disables itself with nothing pinned.

**Self-collision** — `useSelfCollision` stops the sheet passing through itself and is the most expensive
feature; `selfCollisionThicknessMode` picks between a half-thickness as a fraction of rest spacing
(`selfCollisionRadiusScale`, right at any resolution) or in metres (`selfCollisionThickness`, holds the look
across resolutions but is clamped by the grid); `selfCollisionStride` (solve every Nth substep, the cost dial),
`maxContactsPerVertex`, `selfCollisionFriction` (0 lets folds slide flat, 1 makes them grip).

**Aerodynamics** — `useAerodynamics`, `dragCoefficient` (~1 for cloth; drag alone looks limp and wet) and
`liftCoefficient` (what makes a flag billow; silk ~0.35, leather ~0.05).

### Presets and fabrics

`Runtime/Resources/` ships three quality presets that differ in cost, not material:

| Preset | Substeps | Shear | Self-collision |
|---|---|---|---|
| `ClothQuality_Realtime` | 6 | off | off |
| `ClothQuality_Balanced` | 10 | off | off |
| `ClothQuality_Offline` | 20 | on, diagonal bending | on |

`Samples/Profiles/` ships fabrics that differ in density, bending, shear, damping, lift and self-collision
friction at once:

| Fabric | kg/m² | Reads as |
|---|---|---|
| `ClothFabric_Silk` | 0.06 | Fine, fast ripple; lifts and billows on the least wind, falls in many shallow folds |
| `ClothFabric_Cotton` | 0.20 | The neutral middle — a shirt or a bedsheet |
| `ClothFabric_Denim` | 0.45 | Heavier, fewer and broader folds, resists twisting |
| `ClothFabric_Canvas` | 0.60 | Sailcloth: holds its shape, swings slowly, barely ripples |
| `ClothFabric_Leather` | 1.00 | Nearly rigid, deep sculpted creases, almost inert under wind |
| `ClothFabric_Voile` | 0.05 | Barely there: falls slowly in wide soft folds, answers the least draught, self-colliding |

`ClothQuality_Test_Substeps2` / `_Substeps32` are a calibration pair, not fabrics.

## Cloth Collider

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

## Cloth Force Volume

Add **GameObject → Vela → Force Volume** (or **Vela → Cloth Force Volume** on any GameObject). A volume is a
region of space carrying a field; every cloth samples the volumes it overlaps, up to 16 wind and 16
acceleration volumes each. Nothing on the cloth references them: place them like HDRP volumes.

- **`mode`** — how the field reaches the cloth. **Wind** is a velocity in m/s the profile's drag and lift
  answer: orientation-dependent, it saturates once the sheet moves with it, needs `useAerodynamics` and a
  non-zero `airDensity`, and pushes a heavy fabric less. **Acceleration** is m/s² added beside gravity:
  mass-independent, works with aerodynamics off, never saturates.
  Rule of thumb: Wind for anything that should look like air; Acceleration for attractors, lift, and
  stylised pushes.
- **`field`** — the spatial shape, sampled in the volume's own space so gusts and eddies stay put in the world
  while the cloth moves through them. **Directional** blows along the volume's local **+Z**. **Radial**
  pushes out from the centre (negative `strength` pulls in). **Vortex** swirls around local **+Y**, with
  `inwardPull` and `axialLift` in the same unit as `strength`. **Turbulence** is curl noise of amplitude
  `strength`, `noiseScale` eddies per metre (0.2 is sheet-sized rolls, 3 is ripples), drifting along +Z at
  `scrollSpeed`.
- `intensity` scales the whole volume; `strength` is the magnitude in the mode's unit (as wind, a flag lifts
  around 3 m/s and snaps taut past 12). `gustAmplitude` and `gustFrequency` modulate any field, and the gust
  travels along +Z.
- **Shape**: `global` reaches every cloth at full weight; otherwise a `Box` (`size`) or `Sphere` (`radius`)
  centred on the transform, with scale folded in. `blendDistance` fades the field in over that many metres
  inward from the surface; 0 is a hard edge. Overlapping volumes **add**, each scaled by its falloff and
  `weight` — there is no priority.
- A cloth's `volumeMask` filters volumes by layer; a disabled volume, or one with `intensity` 0, is ignored.
- The inspector prints the unit of `strength` for the current mode and, as wind, the peak dynamic pressure in
  sea-level air.

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
- Up to 16 wind and 16 acceleration volumes per cloth.
- Self-collision is point-based (no edge or triangle tests) and degrades into approximation when more than
  64 vertices share a hash cell.
- Results are bit-reproducible on the same GPU and driver, not across vendors.
- Ray-traced effects lag the geometry by a frame.
