using System;
using UnityEngine;

namespace Vela
{
    /// <summary>The wind field itself — a property of the air around this drape. What the cloth does with it is the profile's drag and lift.</summary>
    [Serializable]
    public struct VelaClothWindSettings
    {
        [Tooltip("World-space direction the wind blows towards. Normalized before it reaches the solver.")]
        public Vector3 direction;

        [Tooltip("Steady wind speed in m/s. A flag starts to lift around 3 and snaps taut past 12.")]
        [Min(0f)] public float speed;

        [Tooltip("Gust depth as a fraction of speed: 0.5 swings between half and one and a half. Gusts travel downwind, crossing the sheet instead of pulsing all of it at once.")]
        [Range(0f, 1f)] public float gustAmplitude;

        [Tooltip("Gusts per second. Below 0.3 reads as weather, above 2 as a fan.")]
        [Min(0f)] public float gustFrequency;

        [Tooltip("Swirl amplitude in m/s, as curl noise. What makes a large drape flow rather than vibrate, and the heaviest ALU in the solver.")]
        [Min(0f)] public float turbulence;

        [Tooltip("Swirl size, as eddies per metre. 0.2 gives rolls the size of the whole sheet, 3 gives ripples.")]
        [Min(0.001f)] public float turbulenceScale;

        [Tooltip("How fast the swirl pattern drifts downwind, in m/s. 0 freezes the eddies in space and the sheet settles into them.")]
        [Min(0f)] public float turbulenceSpeed;

        [Tooltip("Air density in kg/m³. 1.225 is sea-level air; raising it makes the whole wind bite harder.")]
        [Min(0f)] public float airDensity;

        public static VelaClothWindSettings Default => new VelaClothWindSettings
        {
            direction = new Vector3(0f, 0f, 1f),
            speed = 4f,
            gustAmplitude = 0.35f,
            gustFrequency = 0.15f,
            turbulence = 1.5f,
            turbulenceScale = 0.5f,
            turbulenceSpeed = 2f,
            airDensity = 1.225f
        };

        /// <summary>False when the air itself is still. Whether the cloth responds to it is the profile's business.</summary>
        public bool HasEffect => (speed > 0f || turbulence > 0f) && airDensity > 0f;

        public Vector3 NormalizedDirection =>
            direction.sqrMagnitude > 1e-8f ? direction.normalized : Vector3.forward;
    }
}
