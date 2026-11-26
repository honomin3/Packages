// MIT License. Copyright (c) 2026 Armature Scale Sync contributors.
using System;
using System.IO;
using System.Linq;
using Honomin.ArmatureScaleSync.Editor;
using nadena.dev.ndmf.runtime.components;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Honomin.ArmatureScaleSync.Tests
{
    public sealed class InspectorAcceptanceTests
    {
        [Test, Category("DATA_001"), Category("DATA_002")]
        public void PrefabModeMigrationRejectsAssetContentsBeforeAnyUndoWrite()
        {
            var folder = "Assets/ASS-MigrationStage-" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folder.Substring(7));
            var root = new GameObject("SyntheticMigrationStage");
            SyntheticAvatar.Child(root.transform, "Bone").localScale = Vector3.one * 1.2f;
            try
            {
                var path = folder + "/source.prefab";
                PrefabUtility.SaveAsPrefabAsset(root, path);
                var before = File.ReadAllBytes(path);
                var stage = PrefabStageUtility.OpenPrefab(path);
                var target = stage.prefabContentsRoot.transform.Find("Bone");
                var plan = new MigrationPlan(target, Vector3.one);
                Assert.That(plan.CanApply, Is.False, "Prefab Stage contents represent the source asset, even though IsPersistent is false.");
                Assert.Throws<InvalidOperationException>(() => plan.Apply());
                Assert.That(target.localScale, Is.EqualTo(Vector3.one * 1.2f));
                Assert.That(target.GetComponent<HierarchicalBoneScale>(), Is.Null);
                Assert.That(stage.scene.isDirty, Is.False);
                CollectionAssert.AreEqual(before, File.ReadAllBytes(path));
            }
            finally
            {
                StageUtility.GoToMainStage();
                Object.DestroyImmediate(root);
                AssetDatabase.DeleteAsset(folder);
            }
        }

        [Test, Category("PREVIEW_006")]
        public void PrefabModeInspectorExplainsTheLivePreviewLimitWithoutEditingTheAsset()
        {
            var folder = "Assets/ASS-Inspector-" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folder.Substring(7));
            var root = new GameObject("SyntheticAvatarInspector");
            root.AddComponent<NDMFAvatarRoot>();
            var bone = SyntheticAvatar.Child(root.transform, "Bone");
            bone.gameObject.AddComponent<HierarchicalBoneScale>().UniformMultiplier = 1.2f;
            UnityEditor.Editor editor = null;
            try
            {
                var path = folder + "/source.prefab";
                PrefabUtility.SaveAsPrefabAsset(root, path);
                var before = File.ReadAllBytes(path);
                var stage = PrefabStageUtility.OpenPrefab(path);
                var declaration = stage.prefabContentsRoot.GetComponentInChildren<HierarchicalBoneScale>();
                editor = UnityEditor.Editor.CreateEditor(declaration);
                var ui = editor.CreateInspectorGUI();
                Assert.That(ui.Query<HelpBox>().ToList().Any(b => b.text.Contains("PREVIEW_PREFAB_MODE")), Is.True);
                Assert.That(declaration.transform.localScale, Is.EqualTo(Vector3.one));
                Assert.That(stage.scene.isDirty, Is.False);
                CollectionAssert.AreEqual(before, File.ReadAllBytes(path));
            }
            finally
            {
                if (editor) Object.DestroyImmediate(editor);
                StageUtility.GoToMainStage();
                Object.DestroyImmediate(root);
                AssetDatabase.DeleteAsset(folder);
            }
        }
    }
}
