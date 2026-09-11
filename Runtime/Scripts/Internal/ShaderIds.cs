using UnityEngine;

namespace Vela.Internal
{
    internal static class ShaderIds
    {
        public static readonly int Pos = Shader.PropertyToID("_Pos");
        public static readonly int PosPrev = Shader.PropertyToID("_PosPrev");
        public static readonly int PosPrevFrame = Shader.PropertyToID("_PosPrevFrame");
        public static readonly int PosRest = Shader.PropertyToID("_PosRest");
        public static readonly int Vel = Shader.PropertyToID("_Vel");
        public static readonly int LraAnchor = Shader.PropertyToID("_LraAnchor");
        public static readonly int LraDist = Shader.PropertyToID("_LraDist");
        public static readonly int LraAnchorCount = Shader.PropertyToID("_LraAnchorCount");
        public static readonly int LraSlack = Shader.PropertyToID("_LraSlack");
        public static readonly int VertexBuffer = Shader.PropertyToID("_VertexBuffer");
        public static readonly int BoundsAtomic = Shader.PropertyToID("_BoundsAtomic");
        public static readonly int Colliders = Shader.PropertyToID("_Colliders");
        public static readonly int ColliderCount = Shader.PropertyToID("_ColliderCount");
        public static readonly int ColliderT0 = Shader.PropertyToID("_ColliderT0");
        public static readonly int ColliderT1 = Shader.PropertyToID("_ColliderT1");

        public static readonly int CellCount = Shader.PropertyToID("_CellCount");
        public static readonly int CellStart = Shader.PropertyToID("_CellStart");
        public static readonly int SortedIds = Shader.PropertyToID("_SortedIds");
        public static readonly int ScanBlocks = Shader.PropertyToID("_ScanBlocks");
        public static readonly int DeltaPos = Shader.PropertyToID("_DeltaPos");
        public static readonly int CellSize = Shader.PropertyToID("_CellSize");
        public static readonly int NumCells = Shader.PropertyToID("_NumCells");
        public static readonly int ScanBlockCount = Shader.PropertyToID("_ScanBlockCount");
        public static readonly int SelfRadius = Shader.PropertyToID("_SelfRadius");
        public static readonly int MaxContacts = Shader.PropertyToID("_MaxContacts");
        public static readonly int SelfRelax = Shader.PropertyToID("_SelfRelax");
        public static readonly int SelfFriction = Shader.PropertyToID("_SelfFriction");

        public static readonly int W = Shader.PropertyToID("_W");
        public static readonly int H = Shader.PropertyToID("_H");
        public static readonly int VertexCount = Shader.PropertyToID("_VertexCount");
        public static readonly int RestDx = Shader.PropertyToID("_RestDx");
        public static readonly int RestDy = Shader.PropertyToID("_RestDy");
        public static readonly int RestDiag = Shader.PropertyToID("_RestDiag");
        public static readonly int VertexStride = Shader.PropertyToID("_VertexStride");

        public static readonly int SubstepDt = Shader.PropertyToID("_SubstepDt");
        public static readonly int InvSubstepDt = Shader.PropertyToID("_InvSubstepDt");
        public static readonly int Gravity = Shader.PropertyToID("_Gravity");
        public static readonly int MaxVelocity = Shader.PropertyToID("_MaxVelocity");
        public static readonly int VelocityDamp = Shader.PropertyToID("_VelocityDamp");
        public static readonly int VelocitySmooth = Shader.PropertyToID("_VelocitySmooth");
        public static readonly int AlphaStructuralH = Shader.PropertyToID("_AlphaStructuralH");
        public static readonly int AlphaStructuralV = Shader.PropertyToID("_AlphaStructuralV");
        public static readonly int AlphaShear = Shader.PropertyToID("_AlphaShear");
        public static readonly int AlphaBend = Shader.PropertyToID("_AlphaBend");
        public static readonly int Parity = Shader.PropertyToID("_Parity");
        public static readonly int Phase = Shader.PropertyToID("_Phase");

        public static readonly int WindDir = Shader.PropertyToID("_WindDir");
        public static readonly int WindSpeed = Shader.PropertyToID("_WindSpeed");
        public static readonly int GustAmplitude = Shader.PropertyToID("_GustAmplitude");
        public static readonly int GustFrequency = Shader.PropertyToID("_GustFrequency");
        public static readonly int Turbulence = Shader.PropertyToID("_Turbulence");
        public static readonly int TurbulenceScale = Shader.PropertyToID("_TurbulenceScale");
        public static readonly int TurbulenceSpeed = Shader.PropertyToID("_TurbulenceSpeed");
        public static readonly int WindTime = Shader.PropertyToID("_WindTime");
        public static readonly int DragFactor = Shader.PropertyToID("_DragFactor");
        public static readonly int LiftFactor = Shader.PropertyToID("_LiftFactor");
    }
}
