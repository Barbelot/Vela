# Samples

Everything here lives under `Samples/` and can be deleted without affecting the plugin.

## Scenes

- `Samples/Scenes/ClothSimulation.unity` — the sample fabrics side by side under one set of wind volumes, with
  box and sphere colliders to drape over.
- `Samples/Scenes/ClothShaders.unity` — eight sheets side by side, one per art shader.
- `Samples/Scenes/Voiles.unity` — four sheer voiles thrown across a white studio.

## Cloth Art Binder

The art shaders read the solver's buffers directly, so they need **Vela → Samples → Cloth Art Binder** next to
the Cloth Simulation; without it the sheet does not draw at all. The binder hands the position and velocity
buffers and the grid constants to the material every frame and pushes the scene's directional light as the
shading light (`sun` picks one explicitly).

## Art shaders

Each is a hand-written HDRP shader from `Samples/Shaders/` that shades from the solver's own state rather than
from textures alone. Strain, shear, curvature, velocity, pinning and displacement from rest are derived per
vertex from the position and velocity buffers; the materials in `Samples/Materials/` expose every dial.

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
| `VelaClothArt_Voile` | facing, curvature | Sheer voile whose opacity follows the view ray's path through the cloth, so layers stack and the silhouette goes solid; optional pleats (used in *Voiles*, not in `ClothShaders`) |
| `VelaClothArt_FabricVoile` | facing, curvature | The voile again on the Fabric Shader Graph: the same sheer alpha, hem and weave, but lit by every light with shadows, GI, transmission and motion vectors |

The hand-written ones are stylized forward passes, not HDRP Lit: they cast shadows but receive none, take no
GI, and stay out of the depth prepass, so SSAO, SSR, decals and per-object motion vectors do not apply to them.
Colour is authored in 0–1 and ignores exposure. `VelaClothArt_Voile` is transparent, so it neither casts nor
receives a shadow. The Fabric Shader Graph is the lit exception.

## Fabric Shader Graph

`Samples/Shaders/VelaClothArt_Fabric.shadergraph` is the template for a lit cloth material, an HDRP **Fabric**
(Silk) Shader Graph. Duplicate it and wire your own look from the *Cloth Data (fragment)* group, which exposes
per pixel: strain U/V, shear, signed curvature (concave toward the viewer), world-space velocity and speed,
displacement from rest, pinned, and the sheet UV in metres. The data arrives through a vertex-stage Custom
Function on `VelaClothArtData.hlsl` (`VelaClothVertex`) packed into three custom interpolators `VelaStrain`,
`VelaMotion` and `VelaSheet`; the same include's `VelaClothSheet` gives the sheet size in the fragment stage.

- It needs the **Cloth Art Binder** like the hand-written shaders; without it the sheet does not draw.
- *Add Precomputed Velocity* is on, so with `writeMotionVectors` the sheet gets correct motion vectors.
- Transmission (the backlight) reads a diffusion profile — the material defaults to HDRP's *Cotton Thin*.
  A profile only works once it is in **Project Settings ▸ Graphics ▸ HDRP ▸ Default Volume ▸ Diffusion
  Profile List**, or with *Auto Register Diffusion Profiles* enabled there.
- Fabric type (Silk / Cotton Wool), transmission and surface type are graph settings, not material ones:
  change them in the Graph Inspector. `Opacity Face On` at 1 makes the template read as opaque cloth.
- The Shader Graph preview and the material thumbnail draw with unbound buffers and show a collapsed sheet;
  only a scene with a binder shows the cloth.
- **`_PigmentAlpha` and `_PigmentGamma` take alpha from the base map's luminance**, for textures such as
  pigment-on-black paintings that have no alpha channel of their own. `_PigmentAlpha` blends that luminance
  into the sheer alpha — at its default **0** the term is exactly 1 and the material is unaffected — and
  `_PigmentGamma` bends the ramp, above 1 pushing the thin washes towards transparent.

## Voiles

`Samples/Scenes/Voiles.unity` is a white studio with four sheer voiles caught mid-fall: two long ones hanging
from pins above the frame on `VelaClothArt_VoileIvory` and `VelaClothArt_VoileInk`, and two shorter ones blown
in from below. What makes it read as voile rather than as cloth:

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
