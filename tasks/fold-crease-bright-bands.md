# Verify: bright bands along fold creases

## Diagnosis

The bands are directional-shadow leaks between the two layers of a fold. In `Samples/Scenes/ClothSimulation.unity`
the layers sit ~6 cm apart (half-thickness 0.8 × 3.9 cm spacing) while the light's shadow map was 512 px over a
7.5 m cascade (~1.5 cm/texel) with 0.75 texel normal bias, 0.5 slope bias and PCF filtering — enough tolerance
to light the lower layer through the upper one. The double-sided mode only steers the normal bias: `Flip`/`Mirror`
push back-face samples toward the light (leak visible), `None` pushes them away (leak hidden, but back faces then
shade with the unflipped normal).

## Applied

- Sample light shadow resolution 512 → 4096.
- `VelaClothDebug.shader` gained a `Facing` mode (green front, red back).
- README Tuning entry and `docs/rendering.md` section on self-shadowing across a fold.

## Verification

Open the sample, drape moving under wind, `VelaClothLit_HDRP.mat` in `Flip` mode: no bright edge on any crease
in forward or deferred lit mode. If a faint band remains, lower the light's normal bias (0.75 → 0.3) before
looking elsewhere. If bands persist at 4096 with low bias, the shadow explanation is wrong: switch to
`VelaClothDebug.mat` in `Facing` mode and check whether the band pixels are red — the remaining suspect is then
the lower layer protruding past the crease crest. Delete this file once verified.
