// MIT License. Copyright (c) 2026 Armature Scale Sync contributors.
using System.Linq;
using Honomin.ArmatureScaleSync.Editor;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

[assembly: ExportsPlugin(typeof(Honomin.ArmatureScaleSync.Tests.BeforeScaleProbe))]
[assembly: ExportsPlugin(typeof(Honomin.ArmatureScaleSync.Tests.AfterScaleProbe))]
[assembly: ExportsPlugin(typeof(Honomin.ArmatureScaleSync.Tests.ReplacementProbe))]
namespace Honomin.ArmatureScaleSync.Tests
{
    internal sealed class ProbeState
    {
        public float Before, After;
        public bool MergedBefore, AppliedAfter;
        public int TransformCountBefore;
    }
    [RunsOnAllPlatforms]
    public sealed class BeforeScaleProbe : Plugin<BeforeScaleProbe>
    {
        public override string QualifiedName => "dev.honomin.armature-scale-sync.tests.before-scale";
        protected override void Configure()
        {
            InPhase(BuildPhase.Transforming).AfterPlugin("nadena.dev.modular-avatar").BeforePlugin("dev.honomin.armature-scale-sync")
                .Run("Observe real MA output before ASS", ctx =>
                {
                    if (!ctx.AvatarRootObject.name.StartsWith("SyntheticAvatar"))
                        return;
                    var state = ctx.GetState<ProbeState>();
                    state.TransformCountBefore = ctx.AvatarRootObject.GetComponentsInChildren<Transform>(true).Length;
                    var hips = ctx.AvatarRootTransform.Find("Armature/Hips");
                    if (!hips)
                        return;
                    state.Before = hips.localScale.x;
                    var outfit = ctx.AvatarRootObject.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(r => r.name == "Outfit");
                    state.MergedBefore = outfit && outfit.bones.All(b => b && b.IsChildOf(hips));
                });
        }
    }
    [RunsOnAllPlatforms]
    public sealed class AfterScaleProbe : Plugin<AfterScaleProbe>
    {
        public override string QualifiedName => "dev.honomin.armature-scale-sync.tests.after-scale";
        protected override void Configure()
        {
            InPhase(BuildPhase.Transforming).AfterPlugin("dev.honomin.armature-scale-sync").BeforePlugin("nadena.dev.modular-avatar.late-transform-stages")
                .Run("Observe ASS output before MA late", ctx =>
                {
                    if (!ctx.AvatarRootObject.name.StartsWith("SyntheticAvatar"))
                        return;
                    var hips = ctx.AvatarRootTransform.Find("Armature/Hips");
                    if (!hips)
                        return;
                    var state = ctx.GetState<ProbeState>();
                    state.After = hips.localScale.x;
                    state.AppliedAfter = ctx.GetState<ScaleBuildState>().Applied && ctx.AvatarRootObject.GetComponentsInChildren<HierarchicalBoneScale>(true).Length == 0;
                });
        }
    }
    [RunsOnAllPlatforms]
    public sealed class ReplacementProbe : Plugin<ReplacementProbe>
    {
        public override string QualifiedName => "dev.honomin.armature-scale-sync.tests.replace";
        protected override void Configure()
        {
            InPhase(BuildPhase.Transforming).BeforePlugin("nadena.dev.modular-avatar")
                .WithRequiredExtension(typeof(AnimatorServicesContext), s => s.Run("Replace a synthetic object through public NDMF registries", ctx =>
                {
                    var request = ctx.AvatarRootObject.GetComponent<ReplacementRequest>();
                    if (!request)
                        return;
                    if (request.CloneRendererAssets)
                    {
                        foreach (var renderer in ctx.AvatarRootObject.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                        {
                            var originalMesh = renderer.sharedMesh;
                            renderer.sharedMesh = Object.Instantiate(originalMesh);
                            ObjectRegistry.RegisterReplacedObject(originalMesh, renderer.sharedMesh);
                            renderer.sharedMaterials = renderer.sharedMaterials.Select(material => Object.Instantiate(material)).ToArray();
                        }
                        Object.DestroyImmediate(request);
                        return;
                    }
                    var old = ctx.AvatarRootTransform.Find(request.Path);
                    var next = new GameObject(old.name).transform;
                    next.SetParent(old.parent, false);
                    next.localPosition = old.localPosition;
                    next.localRotation = old.localRotation;
                    next.localScale = old.localScale;
                    foreach (var child in old.Cast<Transform>().ToArray())
                        child.SetParent(next, false);
                    foreach (var renderer in ctx.AvatarRootObject.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        renderer.bones = renderer.bones.Select(b => b == old ? next : b).ToArray();
                        if (renderer.rootBone == old)
                            renderer.rootBone = next;
                    }
                    ctx.Extension<AnimatorServicesContext>().ObjectPathRemapper.ReplaceObject(old.gameObject, next.gameObject);
                    ObjectRegistry.RegisterReplacedObject(old.gameObject, next.gameObject);
                    ObjectRegistry.RegisterReplacedObject(old, next);
                    Object.DestroyImmediate(old.gameObject);
                    Object.DestroyImmediate(request);
                }));
        }
    }
    public sealed class PipelineProbeTests
    {
        [Test, Category("PIPELINE_001")]
        public void RealPassOrderingIsBetweenMACoreAndMALate()
        {
            using (var f = new SyntheticAvatar())
            {
                f.Scale(f.Hips, 1.2f);
                var result = f.Build();
                var observed = f.LastBuildContext.GetState<ProbeState>();
                Assert.That(observed.MergedBefore, Is.True, "MA has not actually merged the outfit before ASS");
                Assert.That(observed.Before, Is.EqualTo(1).Within(1e-6));
                Assert.That(observed.After, Is.EqualTo(1.2f).Within(1e-6));
                Assert.That(observed.AppliedAfter, Is.True);
                SyntheticAvatar.Dump("PIPELINE-001", result);
            }
        }
        [Test, Category("PIPELINE_001")]
        public void SettingsAreReResolvedAfterAnotherPluginReplacesTheirBone()
        {
            using (var f = new SyntheticAvatar(merge: false))
            {
                f.Scale(f.Hips, 1.2f);
                f.Root.AddComponent<ReplacementRequest>();
                var result = f.Build();
                Assert.That(result.transform.Find("Armature/Hips").localScale.x, Is.EqualTo(1.2f).Within(1e-6));
                Assert.That(SyntheticAvatar.Renderer(result, "Body").bones[0], Is.SameAs(result.transform.Find("Armature/Hips")));
                Assert.That(f.Hips.localScale, Is.EqualTo(Vector3.one));
            }
        }
    }
}
