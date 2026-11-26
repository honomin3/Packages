// MIT License. Copyright (c) 2026 Armature Scale Sync contributors.
using System;
using System.Linq;
using Honomin.ArmatureScaleSync.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Honomin.ArmatureScaleSync.Tests
{
    public sealed class DataAndAnimationTests
    {
        [Test, Category("ANIM_001")]
        public void VirtualAnimationRebasesValuesAndSourceCurveRemainsImmutable()
        {
            using (var f = new SyntheticAvatar())
            {
                var clip = f.Animate(f.Hips);
                var original = EditorJsonUtility.ToJson(clip);
                f.Scale(f.Hips, 1.2f);
                var result = f.Build();
                var output = result.GetComponent<Animator>().runtimeAnimatorController.animationClips;
                var curve = output.SelectMany(c => AnimationUtility.GetCurveBindings(c).Where(b => b.path == "Armature/Hips" && b.propertyName == "m_LocalScale.x").Select(b => AnimationUtility.GetEditorCurve(c, b))).Single();
                Assert.That(curve.Evaluate(.5f), Is.EqualTo(1.8f).Within(1e-5));
                Assert.That(curve.keys[0].outTangent, Is.EqualTo(1.2f).Within(1e-5));
                Assert.That(EditorJsonUtility.ToJson(clip), Is.EqualTo(original));
            }
        }
        [Test, Category("ANIM_001")]
        public void RebaseOptOutKeepsCurvesAndReportsResetRisk()
        {
            using (var f = new SyntheticAvatar())
            {
                f.Animate(f.Hips);
                f.Scale(f.Hips, 1.2f);
                f.Root.GetComponent<ArmatureScaleSync>().RebaseAnimation = false;
                var result = f.Build();
                var output = result.GetComponent<Animator>().runtimeAnimatorController.animationClips;
                var curve = output.SelectMany(c => AnimationUtility.GetCurveBindings(c).Where(b => b.path == "Armature/Hips" && b.propertyName == "m_LocalScale.x").Select(b => AnimationUtility.GetEditorCurve(c, b))).Single();
                Assert.That(curve.Evaluate(.5f), Is.EqualTo(1.5f).Within(1e-5));
                Assert.That(f.LastBuildContext.ErrorReport.Errors.Any(e => e.TheError.ToMessage().Contains("ANIMATION_RESET")), Is.True);
            }
        }
        [Test, Category("ANIM_001")]
        public void WeightedAndStepTangentsArePreservedWithoutMutatingInput()
        {
            var a = new Keyframe(0, 1, 2, 3, .2f, .4f) { weightedMode = WeightedMode.Both };
            var b = new Keyframe(1, 2, float.PositiveInfinity, float.PositiveInfinity);
            var original = new AnimationCurve(a, b) { preWrapMode = WrapMode.Loop, postWrapMode = WrapMode.PingPong };
            var result = AnimatorScaleAdapter.RebaseCurve(original, -2);
            Assert.That(result.keys[0].value, Is.EqualTo(-2));
            Assert.That(result.keys[0].outTangent, Is.EqualTo(-6));
            Assert.That(result.keys[0].weightedMode, Is.EqualTo(WeightedMode.Both));
            Assert.That(result.keys[0].outWeight, Is.EqualTo(.4f));
            Assert.That(float.IsInfinity(result.keys[1].inTangent), Is.True);
            Assert.That(result.preWrapMode, Is.EqualTo(WrapMode.Loop));
            Assert.That(result.postWrapMode, Is.EqualTo(WrapMode.PingPong));
            Assert.That(original.keys[0].value, Is.EqualTo(1));
            Assert.That(AnimatorScaleAdapter.RebaseCurve(new AnimationCurve(new Keyframe(0, 1, float.MaxValue, float.MaxValue)), 2), Is.Null);
        }
        [Test, Category("DATA_001"), Category("DATA_003")]
        public void FreshBuildsAreDeterministicAndSourceDataIsUnchanged()
        {
            using (var f = new SyntheticAvatar(retained: true))
            {
                f.Scale(f.Hips, 1.2f);
                var clip = f.Animate(f.Hips);
                var original = f.Root.GetComponentsInChildren<Transform>(true).ToDictionary(t => t, t => EditorJsonUtility.ToJson(t));
                var originalMesh = EditorJsonUtility.ToJson(f.Outfit.sharedMesh);
                var originalClip = EditorJsonUtility.ToJson(clip);
                var a = f.Build();
                var b = f.Build();
                SyntheticAvatar.AssertVertices(SyntheticAvatar.Vertices(SyntheticAvatar.Renderer(a, "Outfit")), SyntheticAvatar.Vertices(SyntheticAvatar.Renderer(b, "Outfit")));
                foreach (var pair in original)
                    Assert.That(EditorJsonUtility.ToJson(pair.Key), Is.EqualTo(pair.Value));
                Assert.That(EditorJsonUtility.ToJson(f.Outfit.sharedMesh), Is.EqualTo(originalMesh));
                Assert.That(EditorJsonUtility.ToJson(clip), Is.EqualTo(originalClip));
                Assert.That(a.GetComponentsInChildren<HierarchicalBoneScale>(true), Is.Empty);
                Assert.That(a.GetComponentsInChildren<ArmatureScaleSync>(true), Is.Empty);
            }
        }
        [TestCase(true), TestCase(false), Category("DATA_003")]
        public void DisabledStandaloneDeclarationsAreStillRemovedFromTheBuild(bool componentDisabled)
        {
            using (var f = new SyntheticAvatar())
            {
                Object.DestroyImmediate(f.Root.GetComponent<ArmatureScaleSync>());
                var h = f.Scale(f.Hips, 1.2f);
                if (componentDisabled)
                    h.enabled = false;
                else
                    h.ScaleEnabled = false;
                var result = f.Build();
                Assert.That(result.GetComponentsInChildren<HierarchicalBoneScale>(true), Is.Empty);
                Assert.That(result.transform.Find("Armature/Hips").localScale, Is.EqualTo(Vector3.one));
                Assert.That(f.Hips.GetComponent<HierarchicalBoneScale>(), Is.SameAs(h));
            }
        }
        [Test, Category("DATA_002")]
        public void MigrationIsIdempotentAndUndoRedoRestoresTheWholeOperation()
        {
            using (var f = new SyntheticAvatar())
            {
                f.Hips.localScale = new Vector3(.95f, 1.1f, 1.2f);
                var plan = new MigrationPlan(f.Hips, Vector3.one);
                Assert.That(plan.CanApply, Is.True);
                Assert.That(f.Hips.GetComponent<HierarchicalBoneScale>(), Is.Null);
                plan.Apply();
                var h = f.Hips.GetComponent<HierarchicalBoneScale>();
                Assert.That(f.Hips.localScale, Is.EqualTo(Vector3.one));
                Assert.That(h.VectorMultiplier, Is.EqualTo(plan.Before));
                Assert.That(new MigrationPlan(f.Hips, Vector3.one).CanApply, Is.False);
                Undo.PerformUndo();
                Assert.That(f.Hips.localScale, Is.EqualTo(plan.Before));
                Assert.That(f.Hips.GetComponent<HierarchicalBoneScale>(), Is.Null);
                Undo.PerformRedo();
                Assert.That(f.Hips.localScale, Is.EqualTo(Vector3.one));
                Assert.That(f.Hips.GetComponent<HierarchicalBoneScale>().VectorMultiplier, Is.EqualTo(plan.Before));
            }
        }
        [Test, Category("DATA_002")]
        public void ReviewedMigrationRejectsPendingPlayModeBeforeWriting()
        {
            var root = new GameObject("ASS Migration transition fixture");
            root.transform.localScale = Vector3.one * 1.2f;
            try
            {
                var plan = new MigrationPlan(root.transform, Vector3.one);
                Assert.That(plan.CanApply, Is.True);
                // Unity defers the actual transition until this frame finishes;
                // cancel it in finally before any Scene/Domain Reload can occur.
                EditorApplication.isPlaying = true;
                Assert.That(EditorApplication.isPlayingOrWillChangePlaymode, Is.True);
                Assert.Throws<InvalidOperationException>(() => plan.Apply());
                Assert.That(new MigrationPlan(root.transform, Vector3.one).CanApply, Is.False);
                Assert.That(root.transform.localScale, Is.EqualTo(Vector3.one * 1.2f));
                Assert.That(root.GetComponent<HierarchicalBoneScale>(), Is.Null);
            }
            finally
            {
                EditorApplication.isPlaying = false;
                Object.DestroyImmediate(root);
            }
        }
        [Test, Category("DATA_002")]
        public void MigrationRejectsStalePreviewAndComposesExistingDeclaration()
        {
            using (var f = new SyntheticAvatar())
            {
                var h = f.Scale(f.Hips, 1.1f);
                f.Hips.localScale = Vector3.one * 1.2f;
                var plan = new MigrationPlan(f.Hips, Vector3.one);
                Assert.That(plan.Multiplier.x, Is.EqualTo(1.32f).Within(1e-5));
                h.UniformMultiplier = 1.3f;
                Assert.Throws<InvalidOperationException>(() => plan.Apply());
            }
        }
        [Test, Category("DATA_002")]
        public void AOneUlpSourceChangeInvalidatesMigrationPreview()
        {
            using (var f = new SyntheticAvatar())
            {
                f.Hips.localScale = Vector3.one * 1.2f;
                var plan = new MigrationPlan(f.Hips, Vector3.one);
                f.Hips.localScale = new Vector3(1.2000002f, 1.2f, 1.2f);
                Assert.Throws<InvalidOperationException>(() => plan.Apply());
                Assert.That(f.Hips.GetComponent<HierarchicalBoneScale>(), Is.Null);
            }
        }
        [Test, Category("DATA_001"), Category("DATA_002")]
        public void PrefabVariantMigrationStoresOnlyInstanceOverridesAndNeverWritesAssets()
        {
            var folder = "Assets/ASS-Synthetic-" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folder.Substring(7));
            GameObject instance = null;
            try
            {
                using (var f = new SyntheticAvatar())
                {
                    var basePath = folder + "/base.prefab";
                    var variantPath = folder + "/variant.prefab";
                    PrefabUtility.SaveAsPrefabAsset(f.Root, basePath);
                    instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(basePath), f.Scene);
                    instance.transform.Find("Armature/Hips").localScale = Vector3.one * 1.1f;
                    PrefabUtility.SaveAsPrefabAsset(instance, variantPath);
                    Object.DestroyImmediate(instance);
                    var beforeBase = System.IO.File.ReadAllBytes(basePath);
                    var beforeVariant = System.IO.File.ReadAllBytes(variantPath);
                    var asset = AssetDatabase.LoadAssetAtPath<GameObject>(variantPath);
                    instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, f.Scene);
                    var hips = instance.transform.Find("Armature/Hips");
                    hips.localScale = Vector3.one * 1.2f;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(hips);
                    var plan = new MigrationPlan(hips, Vector3.one * 1.1f);
                    plan.Apply();
                    Assert.That(PrefabUtility.HasPrefabInstanceAnyOverrides(instance, false), Is.True);
                    Assert.That(new MigrationPlan(asset.transform.Find("Armature/Hips"), Vector3.one).CanApply, Is.False);
                    Undo.PerformUndo();
                    Assert.That(hips.localScale.x, Is.EqualTo(1.2f).Within(1e-6));
                    CollectionAssert.AreEqual(beforeBase, System.IO.File.ReadAllBytes(basePath));
                    CollectionAssert.AreEqual(beforeVariant, System.IO.File.ReadAllBytes(variantPath));
                }
            }
            finally { if (instance) Object.DestroyImmediate(instance); AssetDatabase.DeleteAsset(folder); }
        }
    }
}
