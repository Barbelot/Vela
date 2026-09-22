# Watercolor sample scenes

## Context

The lit path is a template nobody has dressed: `Samples/Shaders/VelaClothArt_Fabric.shadergraph` has one
material, `VelaClothArt_FabricVoile.mat`, and all three of its texture slots are empty. Every other sample
material is procedural, so no scene shows the graph doing what only it can do — transmission, GI, shadows.

`Assets/Lune/Textures/Watercolor/` holds 91 deep-blue/black/gold paintings, ~78 of them unreferenced. Two
scenes dress the graph with them, each showing one capability the hand-written shaders cannot reach:
**Voûte**, a backlit canopy read from below, and **Encrier**, ribbons blooming in a force-volume pair where
self-collision stacks the alpha.

Two constraints found while scoping. There is no four-corner pin — `VelaClothPinMode` stops at
`TopCorners`, and `Custom` is reserved for the mask feature in `pin-painting-and-attachments.md`; Voûte
needs one, so this task adds `Corners`. And the graph has no opacity texture slot (`_BaseMap`, `_NormalMap`,
`_MaskMap` only) while the paintings are pigment on black with no alpha, so alpha has to come from base-map
luminance.

These two materials reference `Assets/Lune/` directly, which is a deliberate exception: Vela's samples are
otherwise self-contained, and these will be blank if the plugin is copied elsewhere.

## Scope

- `Runtime/Scripts/VelaClothPinning.cs` — a `Corners` pin mode (all four), with explicit enum values.
- `Tests/VelaClothGridTopologyTests.cs` — a `Corners` case beside the existing `TopCorners` LRA cases.
- `Samples/Shaders/VelaClothArt_Fabric.shadergraph` — `_PigmentAlpha` and `_PigmentGamma`, defaulting to a
  no-op.
- `Samples/Materials/VelaClothArt_FabricNocturne.mat`, `…_FabricEncre.mat` — new, on that graph.
- `Samples/Scenes/Voute.unity`, `Encrier.unity`, and a shared dark `Voute.asset` volume profile.
- `README.md` — the new pin mode at *Components → Pinning*, the two dials at *Fabric Shader Graph*, the two
  materials in the *Sample shaders* table, and a *Watercolor* section after *Voiles*.
- Repo `CLAUDE.md` — one bullet: these samples depend on `Assets/Lune/Textures/Watercolor/`, so those files
  must not be moved or renamed.

Scenes and materials are authored **through the Unity CLI against the open Editor**, never by hand-editing
YAML. The `.shadergraph` is the exception — it is JSON, edited on disk with the graph window closed, then
force-reimported.

## Design

Not yet current state; the parts worth keeping move to `docs/rendering.md` and `README.md` when implemented.

### Corners pin mode

```csharp
public enum VelaClothPinMode
{
    None = 0, TopEdge = 1, TopCorners = 2, LeftEdge = 3, Corners = 4, Custom = 5,
}
```

```csharp
case VelaClothPinMode.Corners:
    return (y == top || y == 0) && (x == 0 || x == grid.width - 1);
```

`pinMode` serializes as an int, so inserting `Corners` at 4 pushes `Custom` to 5 and would silently
reinterpret any asset storing 4. Nothing consumes `Custom`, and all 19 serialized `pinMode` values in the
project are `1`, so no existing data moves. The explicit values freeze the numbering against the next
insertion.

Nothing else changes: `VelaClothSimulation.Rebuild()` already routes `pinMode` through
`VelaClothPinning.FillRestState`, and `VelaClothLongRangeAttachment.Build` seeds from whatever came back
pinned — four anchors is four Dijkstra sources. `VelaClothInspectorCoverageTests` asserts over serialized
fields, and this adds none.

### Pigment-driven alpha

Two exposed properties, appended to the existing *Sheer* category:

| Reference | Display | Type | Default |
|---|---|---|---|
| `_PigmentAlpha` | Pigment Opacity | Range 0–1 | **0** |
| `_PigmentGamma` | Pigment Contrast | Range 0.2–4 | 1 |

Spliced between the *Sheer* group's output (`m_ObjectId 4993d3e7aa70491cbbd74e0fb8c1c879`) and the
`SurfaceDescription.Alpha` block (`m_ObjectId 624a41592f244711b84be5b8bc62b859`):

```
Base Map sample .RGB → Dot(0.2126, 0.7152, 0.0722) → Power(_PigmentGamma)
                     → Lerp(1, that, _PigmentAlpha) → Multiply(Sheer alpha) → Alpha
```

At the default the chain collapses to `× 1`, so `VelaClothArt_FabricVoile.mat` is unaffected and *Voiles*
and *Helix* do not move.

`m_CategoryData[0]` must stay the unnamed default category, and the two new property ids must join the
existing *Sheer* category rather than creating a new first one — otherwise the blackboard duplicates every
property on the next open (see *Gotchas* in the repo `CLAUDE.md`).

### Materials

Both start as duplicates of `VelaClothArt_FabricVoile.mat`, keeping its keyword set and `_DiffusionProfile`
(HDRP *Cotton Thin*, hash `3.4884546`).

`VelaClothArt_FabricNocturne.mat` — the canopy. `_BaseMap` →
`Assets/Lune/Textures/Watercolor/Originals/Benoit_deep_blue_and_black_watercolor_on_a_black_background_tex_18f133ff-ca77-42ab-a2ff-34e706dea524.png`
(816×1456, blue-black cloud field). `_Opacity 0.8` · `_PigmentAlpha 0.35` · `_Thickness 0.55` ·
`_TransmissionTint` cool blue · `_WeaveScale 140` · `_WeaveOpenness 0.25` · `_Anisotropy 0.6` ·
`_ShadowDensity 0.6`. Mostly opaque: transmission, not sheerness, carries the image.

`VelaClothArt_FabricEncre.mat` — the ribbons. `_BaseMap` →
`Assets/Lune/Textures/Watercolor/Originals/Benoit_elegant_poetic_serene_deep_blue_gold_and_black_watercolo_d31d1398-b402-4ff0-9f3e-face94506d26.png`
(1456×604, gold drips), `_BaseTiling (1, 0.35)` so the gold band runs the ribbon's length. `_Opacity 0.25` ·
`_PigmentAlpha 0.9` · `_PigmentGamma 1.6` · `_Anisotropy 0.85` · `_ShadowDensity 0.35`. The high pigment
alpha is what makes bare canvas vanish and each self-overlap double the wash.

### Voute.unity

A dark field — camera clears to solid `(0.03, 0.03, 0.05)`, `Voute.asset` fixes exposure at 0 EV with no
tonemapping and no bloom, sky off. The paintings are pigment on black, so their own blacks dissolve into the
backdrop and the blue and gold appear to float.

| Object | Setup |
|---|---|
| `Main Camera` | `(0, 0.6, 0)`, pitched up ~70°, FOV 45 |
| `Canopy` | `sizeMeters (7,7)`, `resolution 200`, `pivot (0.5, 0.5)`, `pinMode Corners`, `ClothFabric_Silk`, `preRollSteps 200`, `gravity (0,-9.81,0)`, `castShadows 2`, `writeMotionVectors 1`, material `FabricNocturne`, plus `VelaClothArtBinder` (`sun` = Key Light). Rotated −90° X at y ≈ 4.5 so the sheet lies flat overhead and sags into a catenary between its four corners |
| `Breath` | `VelaClothForceVolume`, `global 1`, `field Turbulence`, `strength 0.5`, `noiseScale 0.4`, `scrollSpeed 0.3` — the sag breathes rather than sitting dead |
| `Key Light` | Directional, above and slightly behind the canopy, intensity ≈ 4, 6500 K, so transmission carries the painting down to camera |

### Encrier.unity

Reuses `Voute.asset`.

| Object | Setup |
|---|---|
| `Main Camera` | `(0, 0, -6)`, FOV 38 |
| `Ribbon` ×3 | `sizeMeters (0.5, 7)`, `resolution 280`, `pinMode TopCorners` (on a 0.5 m strip, effectively one hidden anchor that keeps the ribbon in frame), `ClothFabric_Voile` (self-collision on, stride 2), `gravity (0,0,0)`, `preRollSteps 120`, material `FabricEncre`, `VelaClothArtBinder`. Anchors clustered near the vortex centre at staggered depth |
| `Bloom.Radial` | `global 0`, Sphere `radius 3`, `blendDistance 1`, `mode Wind`, `field Radial`, `strength 1.8` — pushes outward |
| `Bloom.Vortex` | Sphere `radius 3.5`, `field Vortex`, `strength 2.2`, `inwardPull 0.9`, `axialLift 0.4`, `scrollSpeed 2` |
| `Klak.Motion.BrownianMotion` | On each ribbon, as in `Voiles.unity`, so the three never sync |
| Lights | Key raking from the side; one warm rim behind camera-left to catch the gold |

### Owner-authored asset

One normal map for `…on_a_black_background_tex_18f133ff-….png` (816×1456), used by the canopy — raised wet
pigment and gold leaf under a directly-overhead key light is where a normal map shows. Import as
`textureType: 1`, `sRGBTexture: 0`. Nothing is blocked waiting for it; the canopy ships flat until it lands.

## Verification

- `unity command recompile`, poll `recompile_status` until it leaves `triggered`/`compiling`; check
  `errors[]` and `get_console_logs --severity error`.
- `VelaClothGridTopologyTests` (the new `Corners` case and the existing LRA cases) and
  `VelaClothInspectorCoverageTests` pass.
- `Voiles.unity`, `Helix.unity` and `ClothSimulation.unity`: every sheet still pinned along its top edge,
  proving the enum renumber moved no data, and visually unchanged, proving `_PigmentAlpha 0` left
  `VelaClothArt_FabricVoile.mat` alone.
- `VelaClothArt_Fabric.shadergraph` opens with one default category and no duplicated properties.
- `Voute.unity`: the cloth draws at all (an unbound `StructuredBuffer` drops the draw silently, so this is
  the binder check) and hangs from four corners, not two. The painting reads *through* the sheet from below.
  No backlight means the diffusion profile is missing from the `DiffusionProfileList` override in
  `Assets/Settings/HDRPDefaultResources/DefaultSettingsVolumeProfile.asset` —
  `autoRegisterDiffusionProfiles` is `0`, so it will not appear by itself.
- `Encrier.unity`: in Play, each ribbon folds over itself and every overlap visibly darkens. If it does not,
  `_PigmentAlpha` is too low or `useSelfCollision` is off in `ClothFabric_Voile`.
- Both scenes compose correctly on load, before Play — that is what `preRollSteps` is for; raise it if the
  opening frame is still falling.
