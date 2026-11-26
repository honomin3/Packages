// MIT License. Copyright (c) 2026 Armature Scale Sync contributors.
using System.Linq;
using System.Reflection;
using System;
using Honomin.ArmatureScaleSync.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Honomin.ArmatureScaleSync.Tests
{
    internal sealed class ProcessedAvatarWindowTests
    {
        // Exercise our own UI command; no private MA/NDMF API is invoked.
        private static void Build(ProcessedAvatarWindow window) => typeof(ProcessedAvatarWindow)
            .GetMethod("BuildView", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, null);

        private static ProcessedAvatarWindow Open(GameObject source)
        {
            var window = ScriptableObject.CreateInstance<ProcessedAvatarWindow>();
            window.Show();
            window.CreateGUI();
            window.rootVisualElement.Q<ObjectField>().value = source;
            return window;
        }

        [Test, Category("PERF_002"), Category("DATA_001"), Category("INSPECTION_001")]
        public void ProcessedInspectionRebuildAndCloseReleaseGeneratedAssetsAndPreserveSources()
        {
            using (var fixture = new SyntheticAvatar())
            {
                fixture.Scale(fixture.Hips, 1.2f);
                fixture.Root.AddComponent<ReplacementRequest>().CloneRendererAssets = true;
                var protectedAssets = new Object[] { fixture.Body.sharedMesh, fixture.Body.sharedMaterial, fixture.Outfit.sharedMesh, fixture.Outfit.sharedMaterial };
                var originalIds = Resources.FindObjectsOfTypeAll<Object>().Where(o => o).Select(o => o.GetInstanceID()).ToHashSet();
                var before = fixture.Root.GetComponentsInChildren<Component>(true).Where(c => c).Select(EditorJsonUtility.ToJson).ToArray();
                var scenes = EditorSceneManager.previewSceneCount;
                var window = Open(fixture.Root);
                var temporaryFolder = "Assets/ASS-Inspection-" + Guid.NewGuid().ToString("N");
                Object[] previous = System.Array.Empty<Object>();
                // Bound any pre-fix NDMF persistence to this test's unique folder.
                using (new nadena.dev.ndmf.OverrideTemporaryDirectoryScope(temporaryFolder))
                try
                {
                    for (var cycle = 0; cycle < 3; cycle++)
                    {
                        Build(window);
                        Assert.That(previous.All(o => !o), Is.True, "Rebuild must release the previous output assets and texture.");
                        var renderers = Resources.FindObjectsOfTypeAll<SkinnedMeshRenderer>()
                            .Where(r => r && EditorSceneManager.IsPreviewScene(r.gameObject.scene)
                                && r.transform.root.name.StartsWith(fixture.Root.name) && !originalIds.Contains(r.GetInstanceID())).ToArray();
                        var generated = renderers.SelectMany(r => new Object[] { r.sharedMesh }.Concat(r.sharedMaterials.Cast<Object>()))
                            .Where(o => o && !originalIds.Contains(o.GetInstanceID())).Distinct().ToArray();
                        Assert.That(generated, Is.Not.Empty, "Real NDMF test pass must produce assets; an empty list cannot prove cleanup.");
                        Assert.That(generated.All(o => !EditorUtility.IsPersistent(o)), Is.True, "Inspection must not persist generated build assets into the project.");
                        var texture = window.rootVisualElement.Q<Image>(className: "processed-image").image as RenderTexture;
                        Assert.That(texture && texture.IsCreated(), Is.True, "The actual inspection image must render.");
                        var priorTarget = RenderTexture.active;
                        var pixels = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false);
                        try
                        {
                            RenderTexture.active = texture;
                            pixels.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
                            pixels.Apply();
                            var visible = pixels.GetPixels32().Count(c => c.b > 100 && c.b > c.r + 20);
                            Assert.That(visible, Is.GreaterThan(0), "The processed inspection must contain actual synthetic avatar pixels.");
                            TestContext.WriteLine("Inspection render " + cycle + ": " + visible + " avatar pixels.");
                        }
                        finally { RenderTexture.active = priorTarget; Object.DestroyImmediate(pixels); }
                        previous = generated.Concat(new Object[] { texture }).ToArray();
                    }
                }
                finally { window.Close(); AssetDatabase.DeleteAsset(temporaryFolder); }
                Assert.That(previous.All(o => !o), Is.True, "Closing inspection must release its final assets and texture.");
                Assert.That(protectedAssets.All(o => o), Is.True);
                Assert.That(fixture.Root.GetComponentsInChildren<Component>(true).Where(c => c).Select(EditorJsonUtility.ToJson).ToArray(), Is.EqualTo(before));
                Assert.That(EditorSceneManager.previewSceneCount, Is.EqualTo(scenes));
                TestContext.WriteLine("Three real inspection renders/rebuilds released generated assets and RenderTextures; source serialization and procedural assets preserved.");
            }
        }

        [Test, Category("DATA_001"), Category("INSPECTION_001")]
        public void FailedProcessedInspectionImmediatelyClosesItsOwnedScene()
        {
            using (var fixture = new SyntheticAvatar())
            {
                fixture.Scale(fixture.Hips, 0);
                var scenes = EditorSceneManager.previewSceneCount;
                var window = Open(fixture.Root);
                var temporaryFolder = "Assets/ASS-Inspection-Failed-" + Guid.NewGuid().ToString("N");
                using (new nadena.dev.ndmf.OverrideTemporaryDirectoryScope(temporaryFolder))
                try
                {
                    Build(window);
                    Assert.That(EditorSceneManager.previewSceneCount, Is.EqualTo(scenes), "Failed output must be released before closing the window.");
                    Assert.That(window.rootVisualElement.Q<Image>(className: "processed-image").image, Is.Null);
                    Assert.That(fixture.Hips.localScale, Is.EqualTo(Vector3.one));
                    Assert.That(fixture.Body.sharedMesh && fixture.Outfit.sharedMesh, Is.True);
                }
                finally { window.Close(); AssetDatabase.DeleteAsset(temporaryFolder); }
            }
        }
    }
}
