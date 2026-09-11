# LDS-tiled constraint solver

## Context

Every constraint colour is its own dispatch: 4 structural, 4 shear, 3 phases × 2–4 bending directions, plus LRA, collision and velocity — 18 dispatches per substep on the default profile, ~145 per frame at 8 substeps. Submission overhead is ~0.6–1 ms per frame, which dominates at 5k vertices where the GPU work itself is trivial. See `docs/architecture.md` for the dispatch table and `docs/solver.md` for the colouring.

## Scope

- A second solve path, keyword- or profile-gated, that keeps the existing colour-pass kernels intact as the correctness reference.
- `KSolveTile`: load a 16×16 tile plus a 1-vertex apron into `groupshared`, run all structural / shear / bending colour passes for the tile interior with `GroupMemoryBarrierWithGroupSync()` between colours, write back. A second dispatch offset by 8 in both axes fixes the seams. Collapses ~14 dispatches per substep to 2 and turns stride-2 global reads into LDS traffic.
- A profile toggle (or automatic selection below a vertex threshold) and a Diagnostics line reporting which path is active and the dispatch count.
- Record the design in `docs/solver.md`, update `README.md` if a field is exposed, then delete this file.

## Design

Not yet current state; moves to `docs/solver.md` when implemented.

- The tile path must produce the same colour order as the naive path within a tile, so that the two paths agree to float rounding; the determinism check (step twice, compare bitwise) must still hold within one path.
- The apron is read-only; a constraint touching an apron vertex writes only its interior end, which is why the offset second pass is needed.
- Bending phases need a 2-vertex apron in their direction; size the apron for the widest stencil the profile enables.

## Verification

- Same scene, both paths: visually identical drape; sag distance within float noise.
- Dispatch count in Diagnostics drops from ~18 to ~6 per substep on the default profile.
- Frame time at 5k vertices drops measurably; at 1M the path is at least not slower.
- `Tests/` topology tests still pass (they cover the naive path's colouring, which is unchanged).

## Notes

- A NaN hunt is likeliest during this work: an optional `KSanitize` debug kernel counting non-finite positions, reported in Diagnostics, pays for itself here. The flat-triple guard `|L| < 1e-7` in bending is the known NaN source.
