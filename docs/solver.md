# Solver

The physics decisions and the reasoning behind them. Kernel and buffer names are inventory the code already
states; this records why each piece has the shape it has.

## XPBD with small substeps

XPBD (Macklin 2019) expresses stiffness as physical compliance rather than as an iteration-count artifact, so
the same cloth looks the same at every quality setting — the property that lets one solver serve interactive
use and offline baking. Quality is dialled by substeps per second, not by iterations: each substep runs one
Gauss-Seidel sweep over every constraint colour. `α̃ = α / h²` uses the **substep** `h`, which is the classic
XPBD bug to get wrong; the inspector's Diagnostics prints the effective `α̃` so it can be checked.

## Constraint colouring

A quad `(x, y)` spans `(x,y), (x+1,y), (x,y+1), (x+1,y+1)` with a fixed diagonal. Every colour class must be
a matching (no vertex twice), because `_Pos` is read-modify-written in place with no ping-pong.

- **Structural — 4 colours.** Horizontal edges `(x,y)–(x+1,y)` split on `x % 2`; vertical on `y % 2`.
  Horizontal edges in different rows never share a vertex, so one parity per axis suffices. H and V cannot
  merge: both touch `(x,y)` when `x` and `y` are both even.
- **Shear — 4 more.** DiagA `(x,y)–(x+1,y+1)` and DiagB `(x+1,y)–(x,y+1)`, each split on `x % 2`. A and B
  cannot merge: `quad(x,y)A` and `quad(x,y+1)B` share `(x+1,y+1)` at equal parity.
- **8 colours is the floor**: an interior vertex of the grid-with-diagonals graph has degree 8. Shear is what
  makes cloth read stiff and paper-like, so it ships off in the Realtime and Balanced presets — four fewer
  dispatches *and* a better drape.
- **Bending — 3 phases per direction.** Colinear triples centred at `i` are disjoint iff centres differ by
  ≥ 3, so `phase = centre % 3`. Different directions share a centre's neighbourhood and cannot batch; each
  direction is its own kernel so it reads its own rest spacing without branching.
- Dispatches are 2D and compact (`x = 2·gid.x + parity`, `y = gid.y`) so no thread is wasted and no vertex
  index needs an integer division.

The only genuine write hazard is self-collision, resolved by a Jacobi accumulate → apply pair over `_DeltaPos`.

## Stiffness is authored, compliance is derived

Raw compliance is unauthorable: `Σw|∇C|²` for bending reaches ~1e7 on a 5 m × 128 sheet, and the useful range
moves as `1 / (ρ · restSpacing⁴)`, so no value survives a resolution change. The profile therefore authors
`structuralStiffness`, `shearStiffness` and `bendingStiffness` on `[0, 1]`, and the solver resolves each to

```
α = 10^lerp(limp, rigid, s) · Σw|∇C|²_ref · h₀²        h₀ = 1/480 s
```

against the constraint's own `Σw|∇C|²` on a fixed **reference sheet**: 5 m at resolution 128, the sample
scene's grid, `VelaClothConstraintScale.ReferenceSpacing = 5/127 m`. `α̃ = α / h²` on the live substep is
unchanged, so substep-invariance holds.

| Family | `Σw\|∇C\|²` on the reference sheet | Where cloth sits |
|---|---|---|
| structural, shear | `2 / (ρ·s²)` | `1` for a drape; below ~0.4 it sags under its own weight |
| bending | `6 / (ρ·s⁴)` | ~0.5; `0` is a plastic bag, `1` is sheet metal |

**The reference spacing is what buys grid-invariance.** `Σw|∇C|²` carries `1/(ρ·dx·dy)`, so taking it on the
*live* grid ties `α` to the cell area: a sheet softens as `1/res²` in stretch and `1/res⁴` in bending, and its
size dependence inverts (`α ∝ 1/size²`) — a lattice's macroscopic stiffness is `k·W/L` however many springs
it holds. `DistanceCompliance` and `BendingCompliance` therefore take no `VelaClothGrid` and no density: the
guarantee is a signature, not a promise. `areaDensity` cancels by design, which is why it only shows under
wind (a real force, so `F/m` stops cancelling).

Stiffness `1` returns compliance exactly `0`, so rigid stays rigid at every scale. The two families carry
**separate** log ranges (`DistanceRatioRange`, `BendingRatioRange`), because equal ratios do not read as equal
stiffness — a drape sits at a ratio near 0.7 for distance and near 30 for curvature — calibrated so the tuned
sample lands mid-slider on the reference sheet.

Size still shows the way real cloth shows it: strain under a sheet's own weight is `ρ·L·g·α`, so a 10 m
curtain hangs longer than a 5 m one at the same slider. That is the sheet being bigger, not the profile
meaning something different.

The Diagnostics section reports per family `α`, `α̃`, the **live** grid's `Σw|∇C|²` and the per-substep
**response** (fraction of a violation removed). Response climbs with resolution even though the material does
not change: it is per-constraint, and a finer grid puts more constraints in series. 100 % is rigid; single
digits mean the setting does almost nothing in this scene.

## Bending

Discrete-curvature (isometric/quadratic) form on colinear triples: `C = |x_{i−1} − 2x_i + x_{i+1}|`, gradients
`(1, −2, 1)·L̂ / |L|` scaled by the direction's rest spacing. `bendingCompliance` is divided by rest spacing
squared inside the constraint; the diagonal directions run at `2α` (half weight).

Why not the alternatives: **dihedral-angle** bending is right for creased garments with non-zero rest angles,
but costs two normalized crosses plus an `acos`, and its gradient blows up as `sin θ → 0` — large drapes live
in the smooth regime where that buys nothing and pass *through* the flat configuration where it is fragile.
**Bender's full 4-vertex quadratic** is the same energy but its diamond stencil colours far worse.
**Cross-quad distance** (`i−2` to `i+2`) is cheapest but anisotropic and couples bending to in-plane stretch.
`VelaClothBendingMode` keeps `Dihedral` and `CrossQuad` as reserved entries; only `Curvature` is implemented.

The guard `|L| < 1e-7 → return` is not optional: the flat case is the common case, and one NaN propagates
through every subsequent kernel.

`RowColumnAndDiagonals` adds both diagonals at half weight and removes the faint axis-aligned preference
visible in folds at two directions, for six more dispatches per substep.

## Damping

Both fields are rates in 1/s that compound to `exp(−rate · t)`, so they mean the same thing at any substep
count; the inspector reports each as a time constant.

- `globalDamping` scales velocity outright — air drag on everything, including the bulk swing. Enough of it
  to kill spring-back reads as syrup.
- `localDamping` is a Laplacian velocity smoothing folded into `KUpdateVelocity`: each vertex's velocity is
  lerped towards its four grid neighbours' mean. It removes only *relative* motion — ripple and spring-back —
  and leaves uniform motion untouched, which is what makes it the weight dial. Neighbour velocities are
  derived from `_Pos`/`_PosPrev` (`VelaClothSubstepVelocity`), never read from `_Vel`, which the same pass is
  writing. The neighbour mean is a discrete Laplacian carrying `dx²/4`, so the rate is corrected by
  `(ReferenceSpacing / spacing)²` (`VelaClothConstraintScale.SmoothingScale`) to keep one value meaning one
  physical diffusion at any resolution or sheet size. That correction is unbounded and the per-substep lerp is
  not, so a heavy rate on a fine grid saturates at "snap to the neighbour mean"; the inspector prints the rate
  it actually acts as.

## Long-range attachments

A pinned drape stretches because constraint information crawls one row per colour pass — at 8 substeps a
128-row drape takes ~16 frames to learn its top is pinned, and that lag *is* the spring. Long-range attachment
forbids each vertex from travelling further from its anchor than its **rest geodesic**, in one uncoloured
dispatch per substep. Anchors are kinematic, so the whole correction lands on the free vertex and no two
threads write the same element.

- Tables come from one labelled multi-source Dijkstra over the 8-neighbour grid, weighted by rest lengths
  (`restDx`, `restDy`, their hypotenuse — not hops, since diagonals differ), keeping up to K labels with
  *distinct* anchors per vertex rather than K separate runs. Seeds are closed on insertion: a path through a
  pinned vertex is always dominated by one starting there. The 8-neighbour graph overestimates the true
  geodesic by 2–5 %, the safe direction, which `lraStretchAllowance` absorbs; fast marching is not worth it.
- **The tables store the rest geodesic, not the allowed distance.** `_LraSlack` multiplies on the GPU, so
  `lraStretchAllowance` is live while only `lraAnchorCount` forces a rebuild.
- `VelaClothLongRangeAttachment.Build` returns `null` when nothing is pinned, and that null is what auto-disables
  the constraint.
- **The first application after the constraint activates runs outside the substep loop.** Enabling it on a
  settled drape — or swapping to a profile that has it on — is a correction the size of all the stretch
  accumulated so far; inside the loop `KUpdateVelocity` reads it as velocity and flings the sheet up to the
  `maxVelocity` clamp, ringing for seconds. Outside, the position lands and nothing sees it, because
  `KPredict` rewrites `_PosPrev` at the top of the next substep. Uploading new tables re-arms it.
- Over-locking: too tight an allowance on a wide drape stiffens its lower region into a cone.

## Transform motion

The solver runs in cloth object space, so a moving transform would carry every vertex rigidly. Instead of
moving the state into world space, `KApplyTransform` maps every vertex's position, previous position and
velocity from the previous cloth frame into the current one (`worldToLocal_now · localToWorld_prev`),
blended by `transformInertia`. The whole sheet keeps its world placement, pins included; each substep's
`KPredict` then lerps the pins back towards their local rest by `_PinSweep = 1 / (substeps remaining)`, a
linear sweep over the step, and the constraints do the dragging.

- **The pins are swept, not snapped.** Moving only the free vertices leaves the pin row a whole frame's
  displacement from its neighbours; the distance constraints close that in substep 1 and `v = (x − xⁿ)/h`
  reports `substeps ×` the transform's real speed — the same impulse the collider lerp exists to avoid — which
  reads as jitter under the pins whenever the sheet is dragged. The sweep sets the pins' `_PosPrev` before the
  move, so wind and self-collision see their true velocity.

- **Not a world-space solver, because that costs more.** World-space state has to re-transform the pins
  every step and convert the mesh back to object space every frame; the counter-transform is one dispatch,
  only on frames the transform moved, and every other subsystem stays untouched: colliders already pack
  against the current cloth matrix with a previous-pose history, bounds stay local, rest state stays local.
- **Once per frame, before `Step`, not per step.** The colliders pack against the new pose at the top of the
  step, so the vertices must already be in it; and a frame that runs no step (accumulator short) must still
  hold the sheet still in the world. A frame that runs several steps spends the pin sweep in the first one.
- **`_PosPrevFrame` is not transformed.** The object-space displacement this pass produces, plus the
  renderer's own object motion, is exactly the world motion the temporal passes should see.
- **Scale is excluded from the delta.** Both matrices use the current `lossyScale`, so a scale edit stays
  rigid; only position and rotation carry inertia.
- **No teleport detection.** A jump of several metres leaves the sheet that far behind, and long-range
  attachment then hauls it back at `maxVelocity`. `Rebuild` is the teleport.
- Force volumes are sampled in their own space, so a cloth carried through turbulence sees the field move
  past it; gusts and eddies stay put in the world.

## Colliders

- The solver runs in cloth object space, so `GpuCollider` (304 B) carries cloth→collider and collider→cloth
  matrices at the current *and* previous pose — pushing a vertex out of a primitive needs the way back too.
  At ≤ 64 colliders that is 19 KB per cloth.
- Matrices are rigid (position + rotation); **scale is folded into the primitive's parameters** so
  `thickness` and `friction` keep their metric meaning. Spheres and capsules take the largest scale axis,
  boxes scale per axis, and the inspector warns where non-uniform scale loses information.
- The plane is the local `Y = 0` half space facing `+Y`; no normal is stored, the transform carries it.
- **The collider pose is lerped across the substeps** (`_ColliderT0`/`_ColliderT1`). Landing a whole step of
  collider motion in substep 1 makes `v = (x − xⁿ)/h` report `substeps ×` the collider's real speed and
  saturates the position-level friction, which reads as the cloth being flung around whenever the collider
  moves. The lerp is componentwise; a per-step rotation large enough to make the interpolated matrices
  measurably non-rigid is already tunnelling, which is why both matrix directions are stored rather than
  inverted on the GPU.
- Friction is Coulomb at the position level: the contact cancels the tangential slide, but never by more than
  `friction × depth`.

## Self-collision

Point-based: each vertex guards a ball of the cloth's half-thickness. There are no vertex-triangle or
edge-edge tests, so the radius has to stand in for them.

- **Counting sort with a prefix sum, not an atomic linked list.** A linked list traverses neighbours in a
  race-dependent order, which destroys reproducibility between a preview and a bake of the same scene, and
  scatters its reads; counting sort leaves each cell's members contiguous in `_SortedIds`.
- **`KHashSort` exists for the same reason.** The scatter's cursor is an atomic, so a cell's members land in a
  race-dependent order, and the accumulate sums floats over that order. One insertion sort per cell over a
  handful of members buys the bit-reproducibility back. Both the sort and the neighbour walk stop at
  **64 members per cell**, so a sheet collapsed into a sub-spacing volume degrades into approximation rather
  than an O(k²) hang; past that limit reproducibility is gone.
- **The scatter cursor is `_CellCount` counting back down**, not a second buffer: after the scan it still holds
  each count, `InterlockedAdd(…, 0xFFFFFFFF)` walks it to zero, and the next `KHashClear` finds it there.
  Saves 8 MB at a million vertices.
- `KScanBlocks` is one group walking the block totals in chunks of 512 with a carry: a million vertices means
  2^21 cells and 4096 blocks, which does not fit one 512-wide pass. `numCells = nextPow2(2N)` with a floor of
  64, so a small grid genuinely leaves the last scan block partly empty — the off-by-one the tests exercise.
- **The hash is rebuilt with every resolve, not once per step.** `KSelfCollideAccum` tests distance against
  live `_Pos` but takes its candidates from `_SortedIds`, so a membership built substeps earlier drops
  contacts whose vertices have crossed a cell — contacts blinking in and out, which is the shiver.
  `selfCollisionStride` is therefore the real cost dial: nine dispatches per solved substep.
- **`cellSize = 2 · radius`, with no motion margin.** Nothing writes positions between building the hash and
  using it, so a margin is dead weight — and harmful, because `maxVelocity · h · stride` does not shrink with
  resolution while the radius does; at resolution 512 on a 5 m sheet a margined cell held ~102 vertices
  against the 64 cap and the *flat* sheet overflowed. Occupancy is now a constant handful at any resolution,
  and hash cost is decoupled from `maxVelocity`, which now governs tunnelling only: a vertex crossing more
  than `2 · radius` in one substep passes a layer undetected.
- **The accumulate skips the whole 3×3 grid neighbourhood** (structural constraints already hold it at rest
  spacing), so the closest pair a flat sheet tests is its 2-ring at `2 · restSpacing`. That is why the radius
  cap is 0.95 spacing rather than 0.49, and why the default is 0.8: below ~0.5 spacings the contact spheres
  stop covering the surface and two layers cross cleanly *between* vertices.
- `VelaClothThicknessMode.Metres` exists because the spacing fraction keeps coverage right at every resolution but
  makes the metric thickness `scale · size / (resolution − 1)`, so doubling resolution halves the cloth's
  thickness and the drape reads thinner — contradicting what the stiffness normalisation buys. The metric value
  is clamped under one rest spacing and `VelaClothSelfCollision.IsThicknessClamped` drives an inspector line,
  because a silent clamp just thins the cloth.
- **The 27-cell walk starts at the home cell.** `maxContactsPerVertex` truncates it, and a contact is at most
  one cell away, so spending the budget in raw scan order resolved only the low-corner neighbours — a net push
  along +xyz on every dense region. The default is 16; 8 binds in exactly the pile the feature exists for.
- Resolve is Jacobi: accumulate `Δ` and a count per vertex, then `p += 0.5 · Δ / max(count, 1)` plus
  Coulomb friction taking the accumulated correction as the contact normal. No atomics, no ordering hazard.
- **The inspector reports CPU submission time, not GPU ms.** A `ProfilerMarker` measures the thread that
  submits the dispatches; Unity exposes no per-marker GPU timer. What it states honestly is cell count,
  megabytes and dispatches per step, and the whole block vanishes when the feature is off.

## Force volumes, wind and aerodynamics

Every push but gravity comes from `VelaClothForceVolume`s. Per vertex, once per substep, each volume the
cloth's `volumeMask` admits is sampled in **volume space** (`pv = clothToVolume · p`) and summed back in cloth
space:

```
field(pv) · (1 + gustAmplitude · sin(2π · gustFrequency · (t − pv.z / |strength|))) · falloff(pv) · weight
```

with `field` one of Directional `(0, 0, strength)`, Radial `normalize(pv) · strength`, Vortex
`tangent(+Y) · strength − radial · inwardPull + (0, axialLift, 0)`, or Turbulence
`strength · curlNoise((pv − (0, 0, scroll)) · noiseScale)`, and
`falloff = smoothstep(0, 1, insideDistance / blendDistance)` (1 for a global volume; blend 0 is a hard edge).

- **Volume space, not cloth space.** The old per-cloth wind was sampled at object-space positions, so a sheet
  carried through turbulence took its eddies along. A volume's field is fixed in the world and the cloth moves
  through it, which is what a spatial field is for. Volumes add with no priority: two overlapping winds sum,
  exactly as two fans would.
- **`scroll` is integrated per step by the registry (`+= scrollSpeed · intensity · dt`), never computed as
  `scrollSpeed · t`.** Animating the speed would otherwise re-sample the whole field somewhere else at every
  change; and it is per-solver state fed by the solver's `dt`, so a bake sees the same eddies as a live run.
  Within a step the shader adds `scrollSpeed · _SubstepOffset` so each substep samples the field where it is.
- **Wind mode vs Acceleration mode.** A wind volume is an air velocity fed to the per-triangle drag and lift
  below; an acceleration volume is added to gravity in `KPredict`. The difference is what saturation means: a
  vortex *wind* stops pushing once the sheet swirls at the air's speed, so it settles into the flow; a vortex
  *acceleration* never stops, so the sheet keeps gaining speed until damping and `maxVelocity` hold it. Wind
  needs the profile's aerodynamics and `airDensity > 0`; acceleration works on any profile and ignores mass.
- The gust is phased along the volume's +Z and travels at the field's own speed. A global sinusoid pulses the
  whole sheet in unison, which reads as a tremble; a travelling gust reads as weather with no extra parameter.
- `curlNoise` is the analytic curl of three offset value-noise fields, so it is divergence-free — it swirls
  rather than pumping volume, which is what makes a large drape read as *flowing*. The value noise carries its
  analytic gradient, so a curl is three samples rather than the eighteen a finite difference needs.
  `strength` scales the noise-space curl directly, so `noiseScale` resizes the eddies without changing the
  swirl's strength. A steady Directional volume plus a Turbulence volume is the old single wind.
- Wind is sampled once per **vertex** and shared by its six incident triangles. Sampling per triangle would
  triple the volume walk — the solver's heaviest ALU with turbulence in it — for a difference below one rest
  spacing.
- The wind walk is gated by the `CLOTH_AERODYNAMICS` keyword, so the noise leaves `KUpdateVelocity`'s
  register footprint entirely when aerodynamics is off; the acceleration walk in `KPredict` is a loop over
  `_ForceVolumeCount`, which is 0 when none reach the cloth.

Per-triangle aerodynamics, gathered per vertex with `v_tri` the mean of the three vertex velocities:

```
v_rel  = v_tri − w
A_eff  = A_tri · |dot(n, v̂_rel)|
F_drag = −½ ρ Cd A_eff |v_rel|² v̂_rel
F_lift = −½ ρ Cl A_eff |v_rel|² normalize(cross(cross(n, v̂_rel), v̂_rel))
```

Each vertex takes ⅓ of each incident triangle's force. Lift produces the billowing; drag alone gives a limp,
wet look. On a regular grid the ≤ 6 incident triangles are index arithmetic, so the pass gathers with no
scatter, no atomics and no extra dispatch; each triangle is evaluated up to three times, which is ALU-cheap
and strictly better than an atomic scatter plus a second pass.

At a million vertices the curl noise per vertex per substep is the heaviest ALU in the solver; the options are
to evaluate the volumes once per step into a low-resolution buffer, or drop the Turbulence volume and rely on
gusts.
