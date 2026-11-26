// MIT License. Copyright (c) 2026 Armature Scale Sync contributors.
using System.Linq;
using Honomin.ArmatureScaleSync.Editor;
using nadena.dev.modular_avatar.core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Animations;

namespace Honomin.ArmatureScaleSync.Tests
{
    public sealed class MaAcceptanceTests
    {
        [Test, Category("MA_001"), Category("MA_007")]
        public void DuplicateManualRouteCannotDoubleScaleMergedClothing()
        {
            using (var f = new SyntheticAvatar())
            {
                f.Scale(f.Hips, 1.2f);
                var sync = f.Root.GetComponent<ArmatureScaleSync>();
                ScaleAcceptanceTests.Bind(sync, f.Hips, f.OutfitHips);
                ScaleAcceptanceTests.Bind(sync, f.Hips, f.OutfitHips);
                var expected = f.Build(true);
                var result = f.Build();
                var baseline = SyntheticAvatar.Vertices(SyntheticAvatar.Renderer(expected, "Outfit"));
                SyntheticAvatar.AssertVertices(baseline.Select(v => f.Hips.position + (v - f.Hips.position) * 1.2f).ToArray(), SyntheticAvatar.Vertices(SyntheticAvatar.Renderer(result, "Outfit")));
                Assert.That(f.LastBuildContext.ErrorReport.Errors.Any(e => e.TheError.ToMessage().Contains("DUPLICATE")), Is.True, "The build must diagnose duplicate routes even when MA consumes the outfit target");
            }
        }
        [Test, Category("MA_002"), Category("MA_008")]
        public void NestedInactiveMergeAndRetainedChildInheritOnce()
        {
            using (var f = new SyntheticAvatar(retained: true))
            {
                var extra = SyntheticAvatar.Child(f.OutfitHips, "Nested");
                var nested = extra.gameObject.AddComponent<ModularAvatarMergeArmature>();
                nested.mergeTarget.Set(f.OutfitHips.gameObject);
                nested.mangleNames = false;
                var loose = SyntheticAvatar.Child(extra, "Ribbon", Vector3.right * .1f);
                f.Skin("RibbonVisual", loose);
                extra.gameObject.SetActive(false);
                f.Scale(f.Hips, 1.2f);
                var baseline = f.Build(true);
                var result = f.Build();
                Assert.That(result.GetComponentsInChildren<Transform>(true).Any(t => t.name == loose.name), Is.True);
                var a = SyntheticAvatar.Vertices(SyntheticAvatar.Renderer(baseline, "Outfit"));
                SyntheticAvatar.AssertVertices(a.Select(v => f.Hips.position + (v - f.Hips.position) * 1.2f).ToArray(), SyntheticAvatar.Vertices(SyntheticAvatar.Renderer(result, "Outfit")));
            }
        }
        [Test, Category("MA_003")]
        public void ScaleAdjusterComposesWithoutChangingSourceMeshOrSettings()
        {
            using (var f = new SyntheticAvatar())
            {
                var adjuster = f.Hips.gameObject.AddComponent<ModularAvatarScaleAdjuster>();
                adjuster.Scale = new Vector3(1.05f, .9f, 1);
                var mesh = f.Body.sharedMesh;
                var baseline = f.Build(true);
                f.Scale(f.Hips, 1.2f);
                var result = f.Build();
                var a = SyntheticAvatar.Vertices(SyntheticAvatar.Renderer(baseline, "Body"));
                SyntheticAvatar.AssertVertices(a.Select(v => f.Hips.position + (v - f.Hips.position) * 1.2f).ToArray(), SyntheticAvatar.Vertices(SyntheticAvatar.Renderer(result, "Body")));
                Assert.That(f.Body.sharedMesh, Is.SameAs(mesh));
                Assert.That(adjuster.Scale, Is.EqualTo(new Vector3(1.05f, .9f, 1)));
            }
        }
        [TestCase(true), TestCase(false), Category("MA_004")]
        public void BoneProxyKeepsMAPlacementAndInheritsWithoutAdditionalCopy(bool match)
        {
            using (var f = new SyntheticAvatar())
            {
                var host = SyntheticAvatar.Child(f.Root.transform, "Proxy", Vector3.right * .2f);
                f.Skin("ProxyVisual", host);
                var proxy = host.gameObject.AddComponent<ModularAvatarBoneProxy>();
                proxy.target = f.Hips;
                proxy.matchScale = match;
                var baseline = f.Build(true);
                f.Scale(f.Hips, 1.2f);
                Assert.That(UnityScalePlan.Resolve(f.Root, false).Diagnostics.Any(d => d.Code == "PROXY_SCALE"), Is.EqualTo(!match));
                var result = f.Build();
                var before = baseline.GetComponentsInChildren<Transform>(true).Single(t => t.name == "Proxy");
                var after = result.GetComponentsInChildren<Transform>(true).Single(t => t.name == "Proxy");
                Assert.That(after.localScale, Is.EqualTo(before.localScale));
                Assert.That(after.IsChildOf(result.transform.Find("Armature/Hips")), Is.True);
                Assert.That(proxy.matchScale, Is.EqualTo(match));
            }
        }
        [Test, Category("MA_005")]
        public void DirectAccessoryInheritsWithoutLocalWrite()
        {
            using (var f = new SyntheticAvatar())
            {
                var accessory = SyntheticAvatar.Child(f.Hips, "Hat", Vector3.up * .1f);
                f.Skin("AccessoryVisual", accessory);
                accessory.localScale = Vector3.one * .8f;
                f.Scale(f.Hips, 1.2f);
                ScaleAcceptanceTests.Bind(f.Root.GetComponent<ArmatureScaleSync>(), f.Hips, accessory, TargetKinds.Accessories);
                var result = f.Build();
                Assert.That(result.transform.Find("Armature/Hips/Hat").localScale, Is.EqualTo(accessory.localScale));
            }
        }
        [Test, Category("MA_007")]
        public void AmbiguousIndependentMappingIsReportedAndNotApplied()
        {
            using (var f = new SyntheticAvatar(merge: false))
            {
                f.Scale(f.Hips, 1.2f);
                var sync = f.Root.GetComponent<ArmatureScaleSync>();
                ScaleAcceptanceTests.Bind(sync, f.Hips, f.OutfitHips);
                ScaleAcceptanceTests.Bind(sync, f.Chest, f.OutfitHips);
                var plan = UnityScalePlan.Resolve(f.Root, false);
                Assert.That(plan.Diagnostics.Any(d => d.Code == "AMBIGUOUS"), Is.True);
                Assert.That(plan.Writes.ContainsKey(f.OutfitHips), Is.False);
            }
        }
        [Test, Category("MA_007")]
        public void ManualSourceCannotAddAnExtraFactorToTheMABoneProxyHost()
        {
            using (var f = new SyntheticAvatar())
            {
                var host = SyntheticAvatar.Child(f.Root.transform, "Proxy", Vector3.right * .2f);
                f.Skin("ProxyVisual", host);
                var proxy = host.gameObject.AddComponent<ModularAvatarBoneProxy>();
                proxy.target = f.Hips;
                proxy.matchScale = true;
                proxy.attachmentMode = BoneProxyAttachmentMode.AsChildKeepWorldPose;
                f.Scale(f.Hips, 1.2f);
                f.Scale(f.Chest, 3);
                ScaleAcceptanceTests.Bind(f.Root.GetComponent<ArmatureScaleSync>(), f.Chest, host);
                var baseline = f.Build(true);
                var output = f.Build();
                var before = baseline.GetComponentsInChildren<Transform>(true).Single(t => t.name == "Proxy");
                var after = output.GetComponentsInChildren<Transform>(true).Single(t => t.name == "Proxy");
                Assert.That(after.localScale, Is.EqualTo(before.localScale));
                Assert.That(f.LastBuildContext.ErrorReport.Errors.Any(e => e.TheError.ToMessage().Contains("MA_PRIORITY")), Is.True);
            }
        }
        [Test, Category("MA_009")]
        public void RendererAnimatorColliderAndConstraintReferencesSurvive()
        {
            using (var f = new SyntheticAvatar(retained: true))
            {
                var hem = f.Outfit.bones[0];
                var collider = hem.gameObject.AddComponent<SphereCollider>();
                var follower = SyntheticAvatar.Child(f.Root.transform, "Follower").gameObject.AddComponent<PositionConstraint>();
                follower.AddSource(new ConstraintSource { sourceTransform = hem, weight = 1 });
                follower.weight = 1;
                f.Animate(f.Hips);
                f.Scale(f.Hips, 1.2f);
                var result = f.Build();
                var constraint = result.GetComponentInChildren<PositionConstraint>(true);
                Assert.That(constraint.GetSource(0).sourceTransform, Is.Not.Null);
                Assert.That(result.GetComponentInChildren<SphereCollider>(true), Is.Not.Null);
                Assert.That(SyntheticAvatar.Renderer(result, "Outfit").bones.All(b => b), Is.True);
                Assert.That(result.GetComponent<Animator>().runtimeAnimatorController, Is.Not.Null);
                Assert.That(collider.transform, Is.SameAs(hem));
            }
        }
    }
}
