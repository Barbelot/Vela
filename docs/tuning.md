# Tuning

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

The reasoning behind every dial is in [solver.md](solver.md).
