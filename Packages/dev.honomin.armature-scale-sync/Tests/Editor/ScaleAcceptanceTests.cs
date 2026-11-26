// MIT License. Copyright (c) 2026 Armature Scale Sync contributors.
using System.Linq;
using Honomin.ArmatureScaleSync.Editor;
using nadena.dev.modular_avatar.core;
using NUnit.Framework;
using UnityEngine;

namespace Honomin.ArmatureScaleSync.Tests
{
    public sealed class ScaleAcceptanceTests
    {
        [TestCase(false), TestCase(true), Category("VERTEX_ORACLE")]
        public void BakedWorldSamplesMatchIndependentSkinMatricesWithAScaledRenderer(bool advanced)
        {
            using (var f = new SyntheticAvatar())
            {
                f.Root.transform.localRotation = Quaternion.Euler(17, 31, 9);
                f.Root.transform.localScale = advanced ? new Vector3(.9f, 1.2f, 1.1f) : Vector3.one * 1.1f;
                f.Hips.localScale = Vector3.one * 1.2f;
                var mesh = f.Body.sharedMesh;
                var expected = mesh.vertices.Select((v, i) =>
                {
                    var bone = mesh.boneWeights[i].boneIndex0;
                    return (f.Body.bones[bone].localToWorldMatrix * mesh.bindposes[bone]).MultiplyPoint3x4(v);
                }).ToArray();
                SyntheticAvatar.AssertVertices(expected, SyntheticAvatar.Vertices(f.Body));
            }
        }

        [Test, Category("SCALE_001")]
        public void IdentityEqualsMABaseline()
        {
            using (var f = new SyntheticAvatar())
            {
                f.Scale(f.Hips, 1);
                var a = f.Build(true);
                var b = f.Build();
                foreach (var name in new[] { "Body", "Outfit" })
                    SyntheticAvatar.AssertVertices(SyntheticAvatar.Vertices(SyntheticAvatar.Renderer(a, name)), SyntheticAvatar.Vertices(SyntheticAvatar.Renderer(b, name)));
            }
        }
        [Test, Category("SCALE_002"), Category("SCALE_004")]
        public void ParentAndChildContributorsIntentionallyCompound()
        {
            using (var f = new SyntheticAvatar())
            {
                f.Scale(f.Hips, 1.1f);
                f.Scale(f.Chest, 1.2f);
                var result = f.Build();
                var hips = result.transform.Find("Armature/Hips");
                var chest = hips.Find("Chest");
                Assert.That(hips.localScale.x, Is.EqualTo(1.1f).Within(1e-6));
                Assert.That(chest.localScale.x, Is.EqualTo(1.2f).Within(1e-6));
                var v = chest.localToWorldMatrix.MultiplyVector(Vector3.right);
                Assert.That(v.magnitude, Is.EqualTo(1.32f).Within(1e-5));
            }
        }
        [Test, Category("SCALE_003")]
        public void AdvancedIsPermittedAndWarned()
        {
            using (var f = new SyntheticAvatar())
            {
                var h = f.Scale(f.Hips, 1);
                h.Mode = ScaleMode.Advanced;
                h.VectorMultiplier = new Vector3(1.2f, 1.1f, .9f);
                f.Root.GetComponent<ArmatureScaleSync>().DiagnosticsEnabled = false;
                Assert.That(UnityScalePlan.Resolve(f.Root, false).Diagnostics.Any(d => d.Code == "NONUNIFORM"), Is.True);
                var result = f.Build();
                Assert.That(result.transform.Find("Armature/Hips").localScale, Is.EqualTo(h.VectorMultiplier));
                Assert.That(result.transform.Find("Armature/Hips/Chest").localToWorldMatrix.MultiplyVector(Vector3.right).magnitude, Is.EqualTo(1.2f).Within(1e-5));
            }
        }
        [Test, Category("SCALE_005"), Category("MA_006")]
        public void IndependentRigPreservesFitAndReceivesOnlyMissingLocalEvents()
        {
            using (var f = new SyntheticAvatar(merge: false))
            {
                f.OutfitHips.localScale = new Vector3(.95f, 1, 1);
                f.Scale(f.Hips, 1.1f);
                f.Scale(f.Chest, 1.2f);
                var sync = f.Root.GetComponent<ArmatureScaleSync>();
                Bind(sync, f.Hips, f.OutfitHips);
                Bind(sync, f.Chest, f.OutfitChest);
                var result = f.Build();
                var hips = result.transform.Find("Clothing/Armature/Hips");
                Assert.That(hips.localScale.x, Is.EqualTo(.95f * 1.1f).Within(1e-6));
                Assert.That(hips.Find("Chest").localScale.x, Is.EqualTo(1.2f).Within(1e-6));
            }
        }
        internal static void Bind(ArmatureScaleSync sync, Transform source, Transform target, TargetKinds kind = TargetKinds.Clothing)
        {
            sync.Bindings.Add(new ScaleBinding { Source = new AvatarObjectReference(source.gameObject), Target = new AvatarObjectReference(target.gameObject), Kind = kind });
        }
        [Test, Category("SCALE_006")]
        public void ExistingNonuniformPrefabScaleDoesNotWarnByItself()
        {
            using (var f = new SyntheticAvatar())
            {
                f.Hips.localScale = new Vector3(.95f, 1, 1);
                f.Scale(f.Hips, 1.1f);
                Assert.That(UnityScalePlan.Resolve(f.Root, false).Diagnostics.Any(d => d.Code == "NONUNIFORM" || d.Code == "SHEAR"), Is.False);
            }
        }
        [Test, Category("SCALE_007")]
        public void InvalidAndNegativeScaleDiagnosticsDoNotEraseValidBranches()
        {
            using (var f = new SyntheticAvatar())
            {
                var h = f.Scale(f.Hips, 0);
                var valid = f.Scale(f.OutfitChest, 1.2f);
                foreach (var value in new[] { 0f, float.NaN, float.PositiveInfinity })
                {
                    h.UniformMultiplier = value;
                    var plan = UnityScalePlan.Resolve(f.Root, false);
                    Assert.That(plan.Diagnostics.Any(d => d.Code == "INVALID_SCALE"), Is.True);
                    Assert.That(plan.Writes.ContainsKey(valid.transform), Is.True);
                }
                h.UniformMultiplier = -1;
                Assert.That(UnityScalePlan.Resolve(f.Root, false).Diagnostics.Any(d => d.Code == "NEGATIVE"), Is.True);
            }
        }
        [Test, Category("SCALE_008")]
        public void RotatedDescendantRetainsFullMatrixAndReportsShear()
        {
            using (var f = new SyntheticAvatar())
            {
                f.Chest.localRotation = Quaternion.Euler(0, 0, 37);
                var h = f.Scale(f.Hips, 1);
                h.Mode = ScaleMode.Advanced;
                h.VectorMultiplier = new Vector3(2, 1, .8f);
                var plan = UnityScalePlan.Resolve(f.Root, false);
                Assert.That(plan.Diagnostics.Any(d => d.Code == "SHEAR"), Is.True);
                var result = f.Build();
                var hips = result.transform.Find("Armature/Hips");
                var chest = hips.Find("Chest");
                var expected = hips.localToWorldMatrix * Matrix4x4.TRS(f.Chest.localPosition, f.Chest.localRotation, Vector3.one);
                for (var i = 0; i < 16; i++)
                    Assert.That(chest.localToWorldMatrix[i], Is.EqualTo(expected[i]).Within(1e-5));
                Assert.That(UnityScalePlan.HasShear(chest.localToWorldMatrix), Is.True);
            }
        }
    }
}
