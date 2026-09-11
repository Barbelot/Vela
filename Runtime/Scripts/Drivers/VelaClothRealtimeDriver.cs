using UnityEngine;

namespace Vela.Drivers
{
    /// <summary>Fixed-rate accumulator, decoupled from the render rate. No leftover interpolation — it would corrupt the motion-vector channel.</summary>
    public sealed class VelaClothRealtimeDriver : IVelaClothDriver
    {
        readonly VelaClothSolver _solver;
        float _accumulator;

        public float SimulationRate { get; set; } = 60f;
        public int MaxStepsPerFrame { get; set; } = 3;

        public VelaClothRealtimeDriver(VelaClothSolver solver) => _solver = solver;

        public float SimulationDt => 1f / Mathf.Max(1f, SimulationRate);

        public void Tick(float deltaTime)
        {
            float dt = SimulationDt;
            _accumulator += Mathf.Max(0f, deltaTime);

            int steps = 0;
            while (_accumulator >= dt && steps < MaxStepsPerFrame)
            {
                _solver.Step(dt);
                _accumulator -= dt;
                steps++;
            }

            // Drop the backlog instead of chasing it; an editor hitch must not turn into a death spiral.
            if (steps == MaxStepsPerFrame)
                _accumulator = 0f;
        }

        public void PreRoll(int steps)
        {
            float dt = SimulationDt;
            for (int i = 0; i < steps; i++)
                _solver.Step(dt);

            _solver.ResetMotionVectorHistory();
        }

        public void Rewind()
        {
            _accumulator = 0f;
            _solver.Reset();
        }
    }
}
