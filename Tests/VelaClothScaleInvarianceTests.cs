using NUnit.Framework;
using UnityEngine;

namespace Vela.Tests
{
    /// <summary>Compliance no longer takes a grid at all, so the sliders' resolution and size invariance is a
    /// signature rather than a test. What is left testable is the reference constant, the density that is still
    /// meant to cancel, and the velocity smoothing — the one place a live grid still reaches a setting.</summary>
    public sealed class VelaClothScaleInvarianceTests
    {
        static readonly int[] Resolutions = { 16, 32, 64, 128, 256, 512 };

        static readonly Vector2[] Sizes =
        {
            new Vector2(0.3f, 0.3f),
            new Vector2(0.5f, 2f),
            new Vector2(2f, 3f),
            new Vector2(5f, 5f),
            new Vector2(40f, 12f)
        };

        const float Density = 0.2f;
        static readonly Vector2 Pivot = new Vector2(0.5f, 1f);

        static VelaClothGrid Make(Vector2 size, int resolution) =>
            VelaClothGrid.FromResolution(size, resolution, Pivot);

        static VelaClothGrid ReferenceSheet() => Make(
            Vector2.one * VelaClothConstraintScale.ReferenceSizeMeters,
            VelaClothConstraintScale.ReferenceResolution);

        static VelaClothProfile Profile(float structural, float bending)
        {
            var profile = ScriptableObject.CreateInstance<VelaClothProfile>();
            profile.structuralStiffness = structural;
            profile.bendingStiffness = bending;
            return profile;
        }

        [Test]
        public void ReferenceSpacingIsTheReferenceSheetsRestSpacing()
        {
            Assert.AreEqual(VelaClothConstraintScale.ReferenceSpacing,
                VelaClothConstraintScale.Spacing(ReferenceSheet()), 1e-6f);
        }

        [Test]
        public void ComplianceCancelsAreaDensity()
        {
            VelaClothProfile profile = Profile(0.6f, 0.5f);
            try
            {
                // Compliance follows 1/ρ, so the response — and with it the drape — does not move with density.
                profile.areaDensity = 2f;
                float heavyDistance = profile.DistanceCompliance(0.6f);
                float heavyBending = profile.BendingCompliance();

                profile.areaDensity = 0.2f;
                Assert.Greater(heavyDistance, 0f);
                Assert.AreEqual(profile.DistanceCompliance(0.6f) * 0.1f, heavyDistance,
                    heavyDistance * 1e-4f);
                Assert.AreEqual(profile.BendingCompliance() * 0.1f, heavyBending, heavyBending * 1e-4f);
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void RigidStaysExactlyRigid()
        {
            VelaClothProfile profile = Profile(1f, 1f);
            try
            {
                profile.areaDensity = Density;
                Assert.AreEqual(0f, profile.DistanceCompliance(1f));
                Assert.AreEqual(0f, profile.BendingCompliance());
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void SmoothingScaleTracksSpacingSquared(
            [ValueSource(nameof(Sizes))] Vector2 size,
            [ValueSource(nameof(Resolutions))] int resolution)
        {
            VelaClothGrid grid = Make(size, resolution);
            float ratio = VelaClothConstraintScale.ReferenceSpacing / VelaClothConstraintScale.Spacing(grid);
            float expected = ratio * ratio;

            Assert.AreEqual(expected, VelaClothConstraintScale.SmoothingScale(grid), expected * 1e-4f);
        }

        [Test]
        public void SmoothingScaleIsOneOnTheReferenceSheet()
        {
            Assert.AreEqual(1f, VelaClothConstraintScale.SmoothingScale(ReferenceSheet()), 1e-4f);
        }

        // Halving the rest spacing must quadruple the correction, whatever moved the spacing — a resolution
        // change or a size change. This is the invariance the smoothing scale exists for.
        [Test]
        public void SmoothingScaleDependsOnSpacingAloneNotOnHowItWasReached()
        {
            float byResolution = VelaClothConstraintScale.SmoothingScale(Make(new Vector2(4f, 4f), 129));
            float bySize = VelaClothConstraintScale.SmoothingScale(Make(new Vector2(8f, 8f), 257));

            Assert.AreEqual(byResolution, bySize, byResolution * 1e-3f);
        }
    }
}
