// MIT License. Copyright (c) 2026 Armature Scale Sync contributors.
using System.Linq;
using Honomin.ArmatureScaleSync.Editor;
using nadena.dev.modular_avatar.core;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Honomin.ArmatureScaleSync.Tests
{
    public sealed class P0CompatibilityTests
    {
        [TestCase(false, false, Category = "P0_1A")]
        [TestCase(true, false, Category = "P0_1B")]
        [TestCase(true, true, Category = "P0_1C")]
        public void PostMergeScalePreservesBakedFit(bool fit, bool retained)
        {
            using (var f = new SyntheticAvatar(retained))
            {
                if (fit)
                    f.OutfitHips.localScale = new Vector3(.95f, 1, 1);
                var baseline = f.Build(true);
                f.Scale(f.Hips, 1.2f);
                var result = f.Build();
                var a = SyntheticAvatar.Renderer(baseline, "Outfit");
                var b = SyntheticAvatar.Renderer(result, "Outfit");
                Assert.That(a.sharedMesh.bindposes.Length, Is.EqualTo(b.sharedMesh.bindposes.Length));
                var expected = SyntheticAvatar.Vertices(a).Select(v => f.Hips.position + (v - f.Hips.position) * 1.2f).ToArray();
                SyntheticAvatar.AssertVertices(expected, SyntheticAvatar.Vertices(b));
                Assert.That(result.transform.Find("Armature/Hips").localScale, Is.EqualTo(Vector3.one * 1.2f));
                SyntheticAvatar.Dump("P0-fit-" + fit + "-retained-" + retained, result);
                SyntheticAvatar.Dump("P0-fit-" + fit + "-retained-" + retained + "-baseline", baseline);
                Assert.That(f.Hips.localScale, Is.EqualTo(Vector3.one));
            }
        }

        [Test, Category("P0_3")]
        public void AnimatorServicesRebasesAfterRemappingWithoutTouchingClip()
        {
            using (var f = new SyntheticAvatar())
            {
                var original = f.Animate(f.OutfitHips);
                var before = EditorJsonUtility.ToJson(original);
                var oldPath = AnimationUtility.CalculateTransformPath(f.OutfitHips, f.Root.transform);
                var baseline = f.Build(true);
                f.Scale(f.Hips, 1.2f);
                var result = f.Build();
                var clips = result.GetComponent<Animator>().runtimeAnimatorController.animationClips;
                var curves = clips.SelectMany(c => AnimationUtility.GetCurveBindings(c).Where(b => b.type == typeof(Transform) && b.propertyName == "m_LocalScale.x").Select(b => (b, AnimationUtility.GetEditorCurve(c, b)))).ToArray();
                Assert.That(curves.Length, Is.GreaterThan(0));
                Assert.That(curves.All(c => result.transform.Find(c.b.path)), Is.True, "All final bindings must resolve after MA remapping");
                Assert.That(curves.Any(c => c.b.path != oldPath), Is.True, "The fixture must actually exercise MA path remapping");
                foreach (var c in baseline.GetComponent<Animator>().runtimeAnimatorController.animationClips)
                    c.SampleAnimation(baseline, .5f);
                foreach (var c in clips)
                    c.SampleAnimation(result, .5f);
                var expected = SyntheticAvatar.Vertices(SyntheticAvatar.Renderer(baseline, "Outfit")).Select(v => f.Hips.position + (v - f.Hips.position) * 1.2f).ToArray();
                SyntheticAvatar.AssertVertices(expected, SyntheticAvatar.Vertices(SyntheticAvatar.Renderer(result, "Outfit")));
                Assert.That(EditorJsonUtility.ToJson(original), Is.EqualTo(before));
                SyntheticAvatar.Dump("P0-3", result);
            }
        }

        [Test, Category("P0_4")]
        public void BodyPlacementDoesNotRetainOutfitBonesAndOutfitPlacementIsDiagnosed()
        {
            using (var f = new SyntheticAvatar())
            {
                var baseline = f.Build(true);
                var baselineCount = f.LastBuildContext.GetState<ProbeState>().TransformCountBefore;
                f.Scale(f.Hips, 1);
                var bodyPlaced = f.Build();
                var bodyCount = f.LastBuildContext.GetState<ProbeState>().TransformCountBefore;
                Assert.That(bodyCount, Is.EqualTo(baselineCount));
                Assert.That(bodyPlaced.GetComponentsInChildren<Transform>(true).Length, Is.EqualTo(baseline.GetComponentsInChildren<Transform>(true).Length));
                f.Scale(f.OutfitHips, 1.2f);
                var plan = UnityScalePlan.Resolve(f.Root, false);
                Assert.That(plan.Diagnostics.Any(d => d.Code == "OUTFIT_COMPONENT"), Is.True);
                var outfitPlaced = f.Build();
                var outfitCount = f.LastBuildContext.GetState<ProbeState>().TransformCountBefore;
                Assert.That(outfitCount, Is.GreaterThan(bodyCount), "Measure MA core retention before ASS removes the processed declaration and MA late can prune it.");
                TestContext.WriteLine("MA core transforms: baseline=" + baselineCount + "; body=" + bodyCount + "; outfit=" + outfitCount);
                SyntheticAvatar.Dump("P0-4-baseline", baseline);
                SyntheticAvatar.Dump("P0-4-body", bodyPlaced);
                SyntheticAvatar.Dump("P0-4-outfit", outfitPlaced);
            }
        }
        [Test, Category("P0_4")]
        public void ADisabledOutfitComponentIsStillDiagnosedForMergeRetention()
        {
            using (var f = new SyntheticAvatar())
            {
                var baseline = f.Build(true);
                var baselineCount = f.LastBuildContext.GetState<ProbeState>().TransformCountBefore;
                var h = f.Scale(f.OutfitHips, 1.2f);
                h.enabled = false;
                Assert.That(UnityScalePlan.Resolve(f.Root, false).Diagnostics.Any(d => d.Code == "OUTFIT_COMPONENT"), Is.True);
                var result = f.Build();
                Assert.That(f.LastBuildContext.GetState<ProbeState>().TransformCountBefore, Is.GreaterThan(baselineCount));
                Assert.That(result.GetComponentsInChildren<HierarchicalBoneScale>(true), Is.Empty);
            }
        }
    }
}
