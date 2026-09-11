# Bug: bright bands along fold creases

## Context

A moving drape shows bright streaks hugging its fold creases, reading as light leaking through the sheet. Reproduces in `Samples/Scenes/ClothSimulation.unity` with a single directional light and `Rendering/HDRP/VelaClothLit_HDRP.mat`.

## Ruled out — do not re-test

- **Transmission.** `_TransmissionEnable: 0` on the material changes nothing. (The material still carries `_MaterialID: 1` and no diffusion profile — inert, but Unity keeps re-serializing the ID.)
- **Specular.** Moving `_Smoothness` across its range does not affect the bands, so the term is diffuse.
- **Normal reconstruction.** `Runtime/Materials/VelaClothDebug.mat` in `_MODE_NORMAL` shows smooth continuous gradients across every crease. The normals are correct (gathered from the six incident faces, matching the grid's clockwise winding, front face on −Z).
- **Double-sided normal mode.** `Flip` and `Mirror` band identically — expected, both negate the vertex normal on backfaces. `None` removes the bands but lights both faces, which is wrong and unexplained: with no flip the away-facing side should be ambient-only. **That contradiction is the most promising lead** — something downstream of the material is re-deriving or absolute-valuing the normal.

## Next to check

1. Directional-light shadow bias / normal bias against a `TwoSided` thin caster: leaked shadow along a crease looks identical to this. Try `castShadows = On` (single-sided) and a larger normal bias to see if the bands move.
2. HDRP deferred GBuffer normal encoding on backfaces — compare forward vs deferred lit mode.

## Verification

Same scene, drape moving under wind, creases show no bright edge in either lit mode; then update `README.md` (Limitations → remove the known issue) and delete this file.
