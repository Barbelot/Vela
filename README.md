# Vela

GPU cloth simulation for Unity — curtains, flags, sails and other large flowing drapes.

- Realtime at roughly 5k–40k vertices, simulated in compute shaders and drawn by a stock `MeshRenderer`.
- One **Cloth Profile** asset describes a fabric and looks the same at any resolution, sheet size or quality.
- Wind, turbulence and attractors from scene-placed **Force Volumes**; sphere, capsule, box and plane colliders.
- Self-collision, long-range attachments, aerodynamic drag and lift, correct HDRP motion vectors.
- Self-contained: engine modules only, no other package required.

## Requirements

- Unity 6 (6000.6) and a GPU with compute shader support.
- HDRP for the shipped lit material and the sample shaders. The simulation itself is pipeline-agnostic, and
  `Runtime/Materials/VelaClothDebug.mat` renders on any pipeline.

## Installation

Add this repository as a git submodule anywhere under `Assets/`, or copy it there:

```
git submodule add https://github.com/Barbelot/Vela Assets/Plugins/Vela
```

To use the `Vela` namespace from your own scripts, reference the `Vela` assembly definition from yours — it is
not auto-referenced. `Samples/` can be deleted; nothing depends on it.

## Quick start

1. Create an empty GameObject and add **Vela → Cloth Simulation**.
2. Assign `Rendering/HDRP/VelaClothLit_HDRP.mat` as its material.
3. Assign a **Cloth Profile**: a preset from `Runtime/Resources/` or a fabric from `Samples/Profiles/`.
4. Press Play: the sheet hangs from its top edge and settles.
5. Add **GameObject → Vela → Force Volume** for wind, and **Vela → Cloth Collider** to drape it over something.

Open `Samples/Scenes/ClothSimulation.unity` to see every sample fabric side by side in the wind.

## Limitations

- Generated rectangular grids only; no arbitrary meshes.
- No baking or cache playback, no painted pin masks, no URP material yet.
- Analytic colliders only (up to 64 per cloth); up to 16 wind and 16 acceleration volumes per cloth.

## Documentation

- [Components](docs/components.md) — every component and setting, presets and fabrics, motion vectors.
- [Tuning](docs/tuning.md) — how to get the fabric you want.
- [Samples](docs/samples.md) — sample scenes, art shaders and the Fabric Shader Graph template.
- [Solver](docs/solver.md), [Rendering](docs/rendering.md), [Architecture](docs/architecture.md),
  [Testing](docs/testing.md) — how it works inside.
