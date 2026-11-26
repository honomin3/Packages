// MIT License. Copyright (c) 2026 Armature Scale Sync contributors.
using System;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using nadena.dev.ndmf;
using nadena.dev.ndmf.platform;
using nadena.dev.ndmf.preview;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;
using VRC.SDK3.Dynamics.Contact.Components;
using VRC.SDK3A.Editor;
using VRC.SDKBase.Editor;
using VRC.Core;

namespace Honomin.ArmatureScaleSync.Tests
{
    public sealed class SdkAcceptanceTests
    {
        public static async Task<string> BuildOnly()
        {
            var previewScene = NDMFPreviewSceneManager.GetPreviewScene();
            var previousSetup = EditorSceneManager.GetSceneManagerSetup()
                .Where(s => UnityEngine.SceneManagement.SceneManager.GetSceneByPath(s.path) != previewScene).ToArray();
            if (previousSetup.Length == 0 || previousSetup.Any(s => string.IsNullOrEmpty(s.path) || UnityEngine.SceneManagement.SceneManager.GetSceneByPath(s.path).isDirty))
                throw new InvalidOperationException("BLOCKED_ENV: Save/resolve original Scenes before SDK validation.");
            var originalScenes = previousSetup.ToDictionary(s => s.path, s => File.ReadAllBytes(s.path));
            var folder = "Assets/ASS-SDK-" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folder.Substring(7));
            var scenePath = folder + "/source.unity";
            // The SDK can reload Scenes and create native humanoid helper roots.
            // Unload the saved originals so all SDK objects stay in our own Scene.
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
            try
            {
            using (var f = SyntheticAvatar.Humanoid())
            {
                var clip = f.Animate(f.Hips);
                var controller = (AnimatorController)f.Root.GetComponent<Animator>().runtimeAnimatorController;
                AssetDatabase.CreateAsset(clip, folder + "/source.anim");
                AssetDatabase.CreateAsset(controller, folder + "/source.controller");
                foreach (var layer in controller.layers)
                {
                    AssetDatabase.AddObjectToAsset(layer.stateMachine, controller);
                    foreach (var state in layer.stateMachine.states)
                        AssetDatabase.AddObjectToAsset(state.state, controller);
                }
                AssetDatabase.SaveAssetIfDirty(controller);
                AssetDatabase.CreateAsset(f.Root.GetComponent<Animator>().avatar, folder + "/source-avatar.asset");
                foreach (var renderer in new[] { f.Body, f.Outfit })
                {
                    var mesh = UnityEngine.Object.Instantiate(renderer.sharedMesh);
                    var material = UnityEngine.Object.Instantiate(renderer.sharedMaterial);
                    AssetDatabase.CreateAsset(mesh, folder + "/" + renderer.name + ".asset");
                    AssetDatabase.CreateAsset(material, folder + "/" + renderer.name + ".mat");
                    renderer.sharedMesh = mesh;
                    renderer.sharedMaterial = material;
                }
                var descriptor = Configure(f, controller, VRCAvatarDescriptor.AnimLayerType.FX);
                descriptor.ViewPosition = new Vector3(0, 1.45f, .05f);
                f.Root.AddComponent<PipelineManager>();
                f.Scale(f.Hips, 1.2f);
                EditorSceneManager.SaveScene(scene, scenePath);
                EditorWindow.GetWindow<VRCSdkControlPanel>();
                await Task.Delay(500);
                if (!VRCSdkControlPanel.TryGetBuilder<IVRCSdkAvatarBuilderApi>(out var builder))
                    throw new InvalidOperationException("BLOCKED_ENV: SDK avatar builder panel is unavailable.");
                var uploads = 0;
                EventHandler onUpload = (_, __) => uploads++;
                builder.OnSdkUploadStart += onUpload;
                try
                {
                    var path = await builder.Build(f.Root);
                    if (!File.Exists(path) || new FileInfo(path).Length == 0 || uploads != 0)
                        throw new InvalidOperationException("SDK Build did not return a nonempty local artifact without uploads.");
                    if (originalScenes.Any(s => !s.Value.SequenceEqual(File.ReadAllBytes(s.Key))))
                        throw new InvalidOperationException("SDK validation changed a previously loaded source Scene file.");
                    var destination = Path.Combine(Environment.GetEnvironmentVariable("ASS_TEST_ARTIFACTS"), "synthetic-avatar.vrca");
                    File.Copy(path, destination, true);
                    return destination;
                }
                finally { builder.OnSdkUploadStart -= onUpload; }
            }
            }
            finally
            {
                // Restore saved originals through the public Scene API, discarding
                // only our generated SDK Scene and any SDK-created helper roots.
                try { EditorSceneManager.RestoreSceneManagerSetup(previousSetup); }
                finally { AssetDatabase.DeleteAsset(folder); }
            }
        }

        internal static VRCAvatarDescriptor Configure(SyntheticAvatar f, RuntimeAnimatorController controller, VRCAvatarDescriptor.AnimLayerType type)
        {
            var descriptor = f.Root.AddComponent<VRCAvatarDescriptor>();
            descriptor.customizeAnimationLayers = true;
            descriptor.baseAnimationLayers = new[] { VRCAvatarDescriptor.AnimLayerType.Base, VRCAvatarDescriptor.AnimLayerType.Additive, VRCAvatarDescriptor.AnimLayerType.Gesture, VRCAvatarDescriptor.AnimLayerType.Action, VRCAvatarDescriptor.AnimLayerType.FX }
                .Select(layer => new VRCAvatarDescriptor.CustomAnimLayer { type = layer, isDefault = layer != type, animatorController = layer == type ? controller : null }).ToArray();
            descriptor.specialAnimationLayers = Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
            f.Root.GetComponent<Animator>().runtimeAnimatorController = null;
            return descriptor;
        }

        [TestCase(VRCAvatarDescriptor.AnimLayerType.FX, Category = "ANIM_SDK_FX")]
        [TestCase(VRCAvatarDescriptor.AnimLayerType.Gesture, Category = "ANIM_SDK_GESTURE")]
        [Test, Category("P0_3"), Category("ANIM_001")]
        public void SdkLayersPreserveRemappedAnimatedFitAndOriginalClips(VRCAvatarDescriptor.AnimLayerType type)
        {
            using (var f = new SyntheticAvatar())
            {
                var clip = f.Animate(f.OutfitHips);
                var original = EditorJsonUtility.ToJson(clip);
                var controller = f.Root.GetComponent<Animator>().runtimeAnimatorController;
                Configure(f, controller, type);
                var platform = PlatformRegistry.GetPrimaryPlatformForAvatar(f.Root);
                Assert.That(platform.QualifiedName, Is.EqualTo(WellKnownPlatforms.VRChatAvatar30));
                var oldPath = AnimationUtility.CalculateTransformPath(f.OutfitHips, f.Root.transform);
                var baseline = f.Build(true, platform);
                var h = f.Scale(f.Hips, 1.2f);
                var output = f.Build(false, platform);
                AnimationClip Find(GameObject root) => root.GetComponent<VRCAvatarDescriptor>().baseAnimationLayers.Single(l => l.type == type).animatorController.animationClips.Single(c => c.name == clip.name);
                var before = Find(baseline);
                var after = Find(output);
                var bindings = AnimationUtility.GetCurveBindings(after).Where(b => b.propertyName.StartsWith("m_LocalScale.")).ToArray();
                Assert.That(bindings.Length, Is.EqualTo(3));
                Assert.That(bindings.All(b => b.path != oldPath && output.transform.Find(b.path)), Is.True);
                foreach (var time in new[] { 0f, .25f, .5f, .75f, 1f })
                {
                    before.SampleAnimation(baseline, time);
                    after.SampleAnimation(output, time);
                    var expected = SyntheticAvatar.Vertices(SyntheticAvatar.Renderer(baseline, "Outfit")).Select(v => f.Hips.position + (v - f.Hips.position) * h.UniformMultiplier).ToArray();
                    TestContext.WriteLine("time=" + time + "; baseline hips=" + baseline.transform.Find("Armature/Hips").localScale + "; output hips=" + output.transform.Find("Armature/Hips").localScale);
                    foreach (var binding in bindings)
                        TestContext.WriteLine(binding.path + "/" + binding.propertyName + "; baseline=" + AnimationUtility.GetEditorCurve(before, binding)?.Evaluate(time) + "; output=" + AnimationUtility.GetEditorCurve(after, binding)?.Evaluate(time));
                    TestContext.WriteLine("expected=" + expected[0].ToString("R") + "; actual=" + SyntheticAvatar.Vertices(SyntheticAvatar.Renderer(output, "Outfit"))[0].ToString("R"));
                    SyntheticAvatar.Dump("SDK-" + type + "-baseline", baseline);
                    SyntheticAvatar.Dump("SDK-" + type + "-output", output);
                    SyntheticAvatar.AssertVertices(expected, SyntheticAvatar.Vertices(SyntheticAvatar.Renderer(output, "Outfit")));
                }
                Assert.That(EditorJsonUtility.ToJson(clip), Is.EqualTo(original));
                TestContext.WriteLine("SDK " + type + "; final paths=" + string.Join(",", bindings.Select(b => b.path).Distinct()));
            }
        }

        [TestCase(VRCAvatarDescriptor.AnimLayerType.FX)]
        [TestCase(VRCAvatarDescriptor.AnimLayerType.Gesture)]
        [Category("ANIM_001"), Category("ANIM_SDK_HUMANOID")]
        public void SdkHumanoidPlaybackPreservesNativeScaleBehaviorTimesMultiplier(VRCAvatarDescriptor.AnimLayerType type)
        {
            using (var f = SyntheticAvatar.Humanoid())
            {
                var clip = f.Animate(f.Hips);
                var original = EditorJsonUtility.ToJson(clip);
                var sourceAvatar = EditorJsonUtility.ToJson(f.Root.GetComponent<Animator>().avatar);
                Configure(f, f.Root.GetComponent<Animator>().runtimeAnimatorController, type);
                var platform = PlatformRegistry.GetPrimaryPlatformForAvatar(f.Root);
                var baseline = f.Build(true, platform);
                f.Scale(f.Hips, 1.2f);
                var result = f.Build(false, platform);
                Vector3 Sample(GameObject root, double time)
                {
                    var controller = root.GetComponent<VRCAvatarDescriptor>().baseAnimationLayers.Single(l => l.type == type).animatorController;
                    var animator = root.GetComponent<Animator>();
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    var graph = PlayableGraph.Create("ASS deterministic humanoid sampling");
                    try
                    {
                        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                        var outputClip = controller.animationClips.Single(c => c.name == clip.name);
                        var binding = EditorCurveBinding.FloatCurve("Armature/Hips", typeof(Transform), "m_LocalScale.x");
                        var value = AnimationUtility.GetEditorCurve(outputClip, binding).Evaluate((float)time);
                        Assert.That(value, Is.EqualTo(AnimationUtility.GetEditorCurve(clip, binding).Evaluate((float)time) * (root == result ? 1.2f : 1)).Within(1e-5));
                        TestContext.WriteLine("SDK " + type + "; time=" + time + "; curve=" + value);
                        var playable = AnimationClipPlayable.Create(graph, outputClip);
                        AnimationPlayableOutput.Create(graph, "SDK " + type, animator).SetSourcePlayable(playable);
                        graph.Play();
                        playable.SetTime(time);
                        graph.Evaluate(0);
                        return animator.GetBoneTransform(HumanBodyBones.Hips).localScale;
                    }
                    finally { graph.Destroy(); }
                }
                foreach (var time in new[] { 0.0, .5, 1.0 })
                {
                    var native = Sample(baseline, time);
                    var scaled = Sample(result, time);
                    TestContext.WriteLine("Native humanoid scale=" + native + "; ASS=" + scaled);
                    Assert.That(Vector3.Distance(scaled, native * 1.2f), Is.LessThan(1e-4f));
                }
                Assert.That(EditorJsonUtility.ToJson(clip), Is.EqualTo(original));
                Assert.That(EditorJsonUtility.ToJson(f.Root.GetComponent<Animator>().avatar), Is.EqualTo(sourceAvatar));
            }
        }

        [Test, Category("ANIM_001"), Category("ANIM_SDK_ADDITIVE")]
        public void SdkAdditiveReferencePoseWeightedAndStepCurvesAreRebasedTogether()
        {
            using (var f = new SyntheticAvatar())
            {
                var clip = f.Animate(f.Hips);
                var reference = f.Own(new AnimationClip { name = "ASS Synthetic Reference" });
                var k = new Vector3(1.2f, .9f, 1.1f);
                foreach (var axis in new[] { "x", "y", "z" })
                {
                    var binding = EditorCurveBinding.FloatCurve("Armature/Hips", typeof(Transform), "m_LocalScale." + axis);
                    var first = new Keyframe(0, 1, 1, 2, .2f, .4f) { weightedMode = WeightedMode.Both };
                    var last = new Keyframe(1, 2, float.PositiveInfinity, float.PositiveInfinity);
                    AnimationUtility.SetEditorCurve(clip, binding, new AnimationCurve(first, last));
                    AnimationUtility.SetEditorCurve(reference, binding, AnimationCurve.Constant(0, 1, .8f));
                }
                AnimationUtility.SetAdditiveReferencePose(clip, reference, .25f);
                var original = EditorJsonUtility.ToJson(clip);
                var originalReference = EditorJsonUtility.ToJson(reference);
                Configure(f, f.Root.GetComponent<Animator>().runtimeAnimatorController, VRCAvatarDescriptor.AnimLayerType.Additive);
                var h = f.Scale(f.Hips, 1);
                h.Mode = ScaleMode.Advanced;
                h.VectorMultiplier = k;
                var result = f.Build(false, PlatformRegistry.GetPrimaryPlatformForAvatar(f.Root));
                var output = result.GetComponent<VRCAvatarDescriptor>().baseAnimationLayers.Single(l => l.type == VRCAvatarDescriptor.AnimLayerType.Additive).animatorController.animationClips.Single(c => c.name == clip.name);
                var settings = AnimationUtility.GetAnimationClipSettings(output);
                Assert.That(settings.additiveReferencePoseClip, Is.Not.Null);
                Assert.That(settings.additiveReferencePoseTime, Is.EqualTo(.25f));
                for (var axis = 0; axis < 3; axis++)
                {
                    var binding = EditorCurveBinding.FloatCurve("Armature/Hips", typeof(Transform), "m_LocalScale." + "xyz"[axis]);
                    var curve = AnimationUtility.GetEditorCurve(output, binding);
                    var referenceCurve = AnimationUtility.GetEditorCurve(settings.additiveReferencePoseClip, binding);
                    var originalCurve = AnimationUtility.GetEditorCurve(clip, binding);
                    Assert.That(curve.keys[0].value, Is.EqualTo(k[axis]).Within(1e-5));
                    Assert.That(curve.keys[0].outTangent, Is.EqualTo(originalCurve.keys[0].outTangent * k[axis]).Within(1e-5));
                    Assert.That(curve.keys[0].weightedMode, Is.EqualTo(originalCurve.keys[0].weightedMode));
                    Assert.That(curve.keys[0].outWeight, Is.EqualTo(originalCurve.keys[0].outWeight));
                    Assert.That(float.IsInfinity(curve.keys[1].inTangent), Is.True);
                    Assert.That(referenceCurve.Evaluate(.25f), Is.EqualTo(.8f * k[axis]).Within(1e-5));
                    // Unity additive scale is relative to its reference; rebasing both preserves that ratio.
                    Assert.That(curve.Evaluate(.5f) / referenceCurve.Evaluate(.25f), Is.EqualTo(originalCurve.Evaluate(.5f) / .8f).Within(1e-5));
                }
                Assert.That(EditorJsonUtility.ToJson(clip), Is.EqualTo(original));
                Assert.That(EditorJsonUtility.ToJson(reference), Is.EqualTo(originalReference));
            }
        }

        [Test, Category("MA_009"), Category("SDK_REFERENCES")]
        public void RealPhysBoneColliderContactAndConstraintReferencesSurvive()
        {
            using (var f = new SyntheticAvatar(retained: true))
            {
                f.Animate(f.Hips);
                Configure(f, f.Root.GetComponent<Animator>().runtimeAnimatorController, VRCAvatarDescriptor.AnimLayerType.FX);
                var hem = f.Outfit.bones[0];
                var collider = f.Chest.gameObject.AddComponent<VRCPhysBoneCollider>();
                collider.rootTransform = f.Chest;
                collider.radius = .03f;
                var pb = hem.gameObject.AddComponent<VRCPhysBone>();
                pb.rootTransform = hem;
                pb.colliders.Add(collider);
                var sender = hem.gameObject.AddComponent<VRCContactSender>();
                sender.rootTransform = hem;
                sender.collisionTags.Add("ASS Synthetic");
                var receiver = hem.gameObject.AddComponent<VRCContactReceiver>();
                receiver.rootTransform = hem;
                receiver.collisionTags.Add("ASS Synthetic");
                receiver.parameter = "ASS_SyntheticContact";
                var follower = SyntheticAvatar.Child(f.Root.transform, "Follower").gameObject.AddComponent<PositionConstraint>();
                follower.AddSource(new ConstraintSource { sourceTransform = hem, weight = 1 });
                follower.weight = 1;
                follower.constraintActive = true;
                f.Scale(f.Hips, 1.2f);
                var result = f.Build(false, PlatformRegistry.GetPrimaryPlatformForAvatar(f.Root));
                var output = result.GetComponentInChildren<VRCPhysBone>(true);
                Assert.That(output.rootTransform.IsChildOf(result.transform), Is.True);
                Assert.That(output.colliders.Single().rootTransform.IsChildOf(result.transform), Is.True);
                Assert.That(result.GetComponentInChildren<VRCContactSender>(true).rootTransform.IsChildOf(result.transform), Is.True);
                Assert.That(result.GetComponentInChildren<VRCContactReceiver>(true).rootTransform.IsChildOf(result.transform), Is.True);
                var constraint = result.GetComponentInChildren<PositionConstraint>(true);
                Assert.That(constraint.GetSource(0).sourceTransform.IsChildOf(result.transform), Is.True);
                Assert.That(pb.rootTransform, Is.SameAs(hem));
                Assert.That(pb.colliders.Single(), Is.SameAs(collider));
                TestContext.WriteLine("Real SDK PhysBone/collider/contact roots and Unity constraint source stay inside the processed clone.");
            }
        }
    }
}
