using System.Collections.Generic;
using UnityEngine;

namespace Vela.Internal
{
    /// <summary>Loads the plugin's own compute shaders, so no component needs them wired by hand.</summary>
    internal static class VelaClothResources
    {
        static ComputeShader _solver;
        static ComputeShader _meshWrite;
        static ComputeShader _selfCollision;

        public static ComputeShader Solver => _solver != null ? _solver : _solver = Load("VelaClothSolver");
        public static ComputeShader MeshWrite => _meshWrite != null ? _meshWrite : _meshWrite = Load("VelaClothMeshWrite");
        public static ComputeShader SelfCollision =>
            _selfCollision != null ? _selfCollision : _selfCollision = Load("VelaClothSelfCollision");

        static readonly HashSet<string> Reported = new HashSet<string>();

        // Self-collision asks for its shader every step, so a missing one must report once rather than per frame.
        static ComputeShader Load(string name)
        {
            var cs = Resources.Load<ComputeShader>($"Shaders/{name}");
            if (cs == null && Reported.Add(name))
                Debug.LogError($"Vela: could not load Resources/Shaders/{name}.compute.");

            return cs;
        }
    }
}
