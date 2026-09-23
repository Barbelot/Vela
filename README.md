# Vela

GPU cloth for large flowing drapes — curtains, flags, sails — simulated on a generated rectangular grid and
drawn by a stock `MeshRenderer`. Realtime at roughly 5k–40k vertices; one profile asset describes a fabric
and means the same fabric at any resolution, sheet size or quality setting. The plugin is self-contained:
engine modules only, no other package or project code.

## Requirements

- Unity 6 (6000.6) and a GPU with compute shader support.
- HDRP for the shipped lit material, `Rendering/HDRP/VelaClothLit_HDRP.mat`, and for the sample art
  shaders. The simulation itself is pipeline-agnostic and `Runtime/Materials/VelaClothDebug.mat` renders on
  any pipeline.

## Installation

1. Copy `Assets/Plugins/Vela/` into your project. `Samples/` can be deleted; nothing else depends on it.
2. Using it from a scene needs nothing more. To reference the `Vela` namespace from your own scripts, add
   `Vela` to your assembly definition's references — the plugin's asmdef is not
   auto-referenced. `Samples/Vela.Samples.asmdef` is.

## Getting started

1. Create an empty GameObject and add **Vela → Cloth Simulation**. It adds and drives its own `MeshFilter`
   and `MeshRenderer`.
2. Assign a material: `Rendering/HDRP/VelaClothLit_HDRP.mat`, or `VelaClothDebug.mat` to see normals, UVs, velocity or front/back facing.
3. Assign a **Cloth Profile** — one of `Runtime/Resources/ClothQuality_{Realtime,Balanced,Offline}` or a
   fabric from `Samples/Profiles/`. With none assigned the component runs on built-in defaults.
4. Leave `pinMode` on `TopEdge` and press Play: the sheet hangs and settles. Add **GameObject → Vela → Force
   Volume** for wind (see *Cloth Force Volume*). Move or rotate the GameObject and the pins drag the sheet
   through the air.

`Samples/Scenes/ClothSimulation.unity` hangs the five sample fabrics side by side under one set of wind
volumes, with a box and sphere colliders to drape over. `Samples/Scenes/Voiles.unity` throws four sheer voiles
across a white studio (see *Voiles* below).

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
- `material` — assigned to the `MeshRenderer`.
- `castShadows` — `TwoSided` is usually right for a drape.
- `writeMotionVectors` — see *Motion vectors* below.

**Diagnostics** (collapsed by default) reports what the profile and this grid resolve to together: substeps
per second, each constraint family's effective compliance and per-substep response, damping time constants,
how many wind and acceleration volumes reach the cloth, the long-range and self-collision state, and dispatch
counts. Reading it while dragging a slider
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

### Cloth Force Volume

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
| `ClothFabric_Voile` | 0.05 | Barely there: falls slowly in wide soft folds, answers the least draught, self-colliding |

`ClothQuality_Test_Substeps2` / `_Substeps32` are a calibration pair, not fabrics.

### Sample shaders

`Samples/Scenes/ClothShaders.unity` hangs eight sheets side by side, each on a hand-written HDRP shader from
`Samples/Shaders/` that shades from the solver's own state rather than from textures alone. Strain, shear,
curvature, velocity, pinning and displacement from rest are derived per vertex from the position and velocity
buffers; the materials in `Samples/Materials/` expose every dial.

| Material | Driven by | Reads as |
|---|---|---|
| `VelaClothArt_DataDebug` | any one quantity | Diverging or heat ramp over strain, shear, curvature, velocity, speed, pins, displacement, rest position or facing — the reference for writing a new shader |
| `VelaClothArt_Woven` | strain | Procedural warp/weft weave with an anisotropic sheen; threads part and show the backing where stretched, darken where compressed |
| `VelaClothArt_Iridescent` | curvature, velocity | Thin-film colour from the view angle, hue shifted in folds, shimmer that moves with speed |
| `VelaClothArt_SumiE` | curvature, velocity, facing | Paper grain with three-tone hatching; ink pools in creases and smears with motion; lining colour on the back |
| `VelaClothArt_Hologram` | velocity, pins, strain | Emissive speed ramp under a UV grid and scanlines; pinned vertices glow, over-stretched areas show warning bands |
| `VelaClothArt_WeatheredPrint` | strain, world normal, curvature | A printed motif that cracks where stretched, dust on upward-facing cloth, grime in the folds |
| `VelaClothArt_StainedGlass` | facing, velocity | Lead-came panes coloured from a palette, backlit from the far side, each pane's hue shifted by its own speed |
| `VelaClothArt_Heraldic` | displacement, strain, facing | A different print on each face, flaking away where the sheet has moved furthest from rest or is stretched |
| `VelaClothArt_Voile` | facing, curvature | Sheer voile whose opacity follows the view ray's path through the cloth, so layers stack and the silhouette goes solid; optional pleats (used in *Voiles* below, not in this scene) |
| `VelaClothArt_FabricVoile` | facing, curvature | The voile again on `VelaClothArt_Fabric.shadergraph`, an HDRP **Fabric** (Silk) Shader Graph: the same sheer alpha, hem and weave, but lit by every light with shadows, GI, transmission and motion vectors |

### Fabric Shader Graph

`Samples/Shaders/VelaClothArt_Fabric.shadergraph` is the template for a lit cloth material. Duplicate it
and wire your own look from the *Cloth Data (fragment)* group, which exposes per pixel: strain U/V, shear,
signed curvature (concave toward the viewer), world-space velocity and speed, displacement from rest, pinned,
and the sheet UV in metres. The data arrives through a vertex-stage Custom Function on
`VelaClothArtData.hlsl` (`VelaClothVertex`) packed into three custom interpolators `VelaStrain`, `VelaMotion`
and `VelaSheet`; the same include's `VelaClothSheet` gives the sheet size in the fragment stage.

- It needs the **Cloth Art Binder** like the hand-written shaders; without it the sheet does not draw.
- *Add Precomputed Velocity* is on, so with `writeMotionVectors` the sheet gets correct motion vectors.
- Transmission (the backlight) reads a diffusion profile — the material defaults to HDRP's *Cotton Thin*.
  A profile only works once it is in **Project Settings ▸ Graphics ▸ HDRP ▸ Default Volume ▸ Diffusion
  Profile List**, or with *Auto Register Diffusion Profiles* enabled there.
- Fabric type (Silk / Cotton Wool), transmission and surface type are graph settings, not material ones:
  change them in the Graph Inspector. `Opacity Face On` at 1 makes the template read as opaque cloth.
- The Shader Graph preview and the material thumbnail draw with unbound buffers and show a collapsed sheet;
  only a scene with a binder shows the cloth.

### Voiles

`Samples/Scenes/Voiles.unity` is a white studio with four sheer voiles caught mid-fall: two long
ones hanging from pins above the frame on `VelaClothArt_VoileIvory` and `VelaClothArt_VoileInk`, and two
shorter ones blown in from below. What makes it read as voile rather than as cloth:

- **Opacity follows the path length through the sheet.** A view ray crossing the cloth at an angle passes
  through `1/cos θ` of it, so the face-on middle is sheer and the silhouette goes solid, and every overlap
  darkens. That one term, not a texture, is the whole look; `_Opacity` sets it face on and `_MinFacing`
  caps how solid the edges may get.
- **The material is premultiplied alpha with no depth write**, so a sheet blends with itself in triangle order.
  Layers of one colour hide the error; two very different colours crossing in one sheet would not.
- **Long narrow sheets, not panels.** A wide pinned edge streams flat like a flag; a 2–3 m width over 6 m of
  length twists and rolls into cones on its own.
- **`Draft.Core.*` are three invisible sphere colliders** the falling cloth breaks over — they are what turns a
  straight drop into a curl. Nothing renders them.
- **Pins sit outside the frame** so the fabric enters and leaves the picture with no visible anchor.
- **Two global wind volumes blow upward**, a Directional one with a slow gust and a Turbulence one; the
  voile hangs in them rather than carrying its own wind.
- **`Studio Volume` holds `Voiles`**, the scene's own volume profile: fixed exposure at 0 EV, no
  tonemapping and no bloom, so the camera's white background stays white and the 0–1 material colours land as
  authored. Sky is off; the camera clears to white.

`preRollSteps` is 150 on every sheet, so the composition above is what the scene shows the moment it loads.
Press Play and the cloth keeps falling: the arrangement stays but the exact folds do not.

To use one on your own cloth, assign the material and add **Vela → Samples → Cloth Art Binder** next to the
Cloth Simulation. The binder hands the position and velocity buffers and the grid constants to the material
every frame and pushes the scene's directional light as the shading light (`sun` picks one explicitly);
without it the sheet does not draw at all, since the shaders read a buffer that is otherwise unbound.

The hand-written ones are stylized forward passes, not HDRP Lit: they cast shadows but receive none, take
no GI, and stay out of the depth prepass, so SSAO, SSR, decals and per-object motion vectors do not apply to
them. Colour is authored in 0–1 and ignores exposure. `VelaClothArt_Voile` is transparent, so it neither casts
nor receives a shadow. `VelaClothArt_Fabric` is the lit exception (see above).

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
  when the wind looks right but the cloth is too eager; `airDensity` is the blunt one. A steady Directional
  wind plus a Turbulence volume is the usual pair; turbulence is what makes a large drape flow rather than
  vibrate.
- Read the **Diagnostics** foldout while dragging any slider.
- **Bright bands along fold creases are shadow leaks**: the two layers of a fold sit `2 × half-thickness`
  apart (~6 cm at 128 vertices over 5 m), so a directional shadow map whose texel plus bias exceeds that lights
  the lower layer through the upper one. Raise the light's shadow resolution (the sample uses 4096) or lower
  its normal bias until a texel is well under the layer gap; the material's double-sided mode is not the cause.

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
- Up to 16 wind and 16 acceleration volumes per cloth.
- Self-collision is point-based (no edge or triangle tests) and degrades into approximation when more than
  64 vertices share a hash cell.
- Results are bit-reproducible on the same GPU and driver, not across vendors.
- Ray-traced effects lag the geometry by a frame.

## Further reading

- [docs/architecture.md](docs/architecture.md) — constraints, conventions, who owns a setting, dispatch order.
- [docs/solver.md](docs/solver.md) — the physics and the reasoning behind every dial.
- [docs/rendering.md](docs/rendering.md) — vertex buffer, motion vectors, bounds, pipeline portability.
- [docs/testing.md](docs/testing.md) — the tests and the manual checks that are not obvious.
