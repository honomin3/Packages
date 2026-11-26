// MIT License. Copyright (c) 2026 Armature Scale Sync contributors.
using System;
using System.Collections;
using System.IO;
using System.Linq;
using Honomin.ArmatureScaleSync.Editor;
using nadena.dev.ndmf.preview;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Honomin.ArmatureScaleSync.Tests
{
    internal sealed class PrefabBuildPreviewTests
    {
        [Test, Category("PREVIEW_PREFAB_FALLBACK"), Category("PERF_002")]
        public void RepeatedSnapshotsReleaseTheirOwnAssetsAndKeepPreexistingAssetsAlive()
        {
            using (var fixture = new SyntheticAvatar())
            {
                fixture.Scale(fixture.Hips, 1.2f);
                fixture.Root.AddComponent<ReplacementRequest>().CloneRendererAssets = true;
                var protectedAssets = new Object[] { fixture.Body.sharedMesh, fixture.Body.sharedMaterial, fixture.Outfit.sharedMesh, fixture.Outfit.sharedMaterial };
                var protectedIds = Resources.FindObjectsOfTypeAll<Object>().Where(o => o).Select(o => o.GetInstanceID()).ToHashSet();
                var before = Components(fixture.Root);
                var scenes = EditorSceneManager.previewSceneCount;
                var released = 0;
                for (var cycle = 0; cycle < 5; cycle++)
                {
                    var snapshot = ProcessedPreviewSnapshot.Create(fixture.Root);
                    var output = snapshot.Root;
                    var scene = snapshot.Scene;
                    var outputComponents = output.GetComponentsInChildren<Component>(true).Where(c => c).Cast<Object>().ToArray();
                    var explicitAssets = output.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                        .SelectMany(r => new Object[] { r.sharedMesh }.Concat(r.sharedMaterials.Cast<Object>())).ToArray();
                    var owned = explicitAssets
                        .Where(o => o && !(o is GameObject) && !(o is Component) && !EditorUtility.IsPersistent(o) && !protectedIds.Contains(o.GetInstanceID())).ToArray();
                    try { Assert.That(owned, Is.Not.Empty, "The real test-plugin pass must generate assets to exercise disposal. Actual dependencies="
                        + string.Join(";", EditorUtility.CollectDependencies(outputComponents).Where(o => o is Mesh || o is Material)
                            .Select(o => o.name + ":persistent=" + EditorUtility.IsPersistent(o) + ":preexisting=" + protectedIds.Contains(o.GetInstanceID())))
                        + "; request still present=" + (bool)output.GetComponent<ReplacementRequest>()
                        + "; renderers=" + output.GetComponentsInChildren<Renderer>(true).Length
                        + "; explicit assets=" + string.Join(";", explicitAssets.Where(o => o).Select(o => o.name + ":persistent=" + EditorUtility.IsPersistent(o) + ":preexisting=" + protectedIds.Contains(o.GetInstanceID())))); }
                    finally { snapshot.Dispose(); }
                    Assert.That(output == null && !scene.IsValid(), Is.True);
                    Assert.That(owned.All(o => !o), Is.True, "Every newly reachable asset owned by this snapshot must be destroyed.");
                    Assert.That(protectedAssets.All(o => o), Is.True, "Pre-existing procedural assets are not owned by a snapshot.");
                    Assert.That(Components(fixture.Root), Is.EqualTo(before));
                    Assert.That(EditorSceneManager.previewSceneCount, Is.EqualTo(scenes));
                    released += owned.Length;
                }
                TestContext.WriteLine("Five real snapshot/dispose cycles released " + released + " generated assets and all five owned Scenes.");
            }
        }

        [Test, Category("PREVIEW_PREFAB_FALLBACK")]
        public void FailedProcessedPreviewClosesItsOwnedSceneAndPreservesTheInvalidSource()
        {
            using (var fixture = new SyntheticAvatar())
            {
                fixture.Scale(fixture.Hips, 0);
                var before = Components(fixture.Root);
                var scenes = EditorSceneManager.previewSceneCount;
                Assert.Throws<InvalidOperationException>(() => ProcessedPreviewSnapshot.Create(fixture.Root));
                Assert.That(EditorSceneManager.previewSceneCount, Is.EqualTo(scenes));
                Assert.That(Components(fixture.Root), Is.EqualTo(before));
                Assert.That(fixture.Body.sharedMesh && fixture.Outfit.sharedMesh, Is.True);
            }
        }

        [UnityTest, Category("PREVIEW_PREFAB_FALLBACK")]
        public IEnumerator PublicScenePickerReturnsAuthoringObjectsForProcessedGeometry()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null || PreviewSession.Current == null)
                Assert.Ignore("BLOCKED_ENV: native graphics and enabled NDMF Preview are required.");
            Assert.That(PrefabStageUtility.GetCurrentPrefabStage(), Is.Null);
            var folder = "Assets/ASS-PrefabPicking-" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folder.Substring(7));
            var view = SceneView.lastActiveSceneView;
            Assert.That(view, Is.Not.Null);
            var oldSelection = Selection.objects;
            var oldPivot = view.pivot;
            var oldRotation = view.rotation;
            var oldSize = view.size;
            var oldOrtho = view.orthographic;
            Action<SceneView> locate = null;
            try
            {
                SaveFixture(folder, 3);
                var stage = PrefabStageUtility.OpenPrefab(folder + "/source.prefab");
                var source = stage.prefabContentsRoot;
                var before = Components(source);
                var files = Directory.GetFiles(folder);
                var bytes = files.Select(File.ReadAllBytes).ToArray();
                view.Focus();
                var until = EditorApplication.timeSinceStartup + 5;
                while (!PrefabBuildPreview.Output && EditorApplication.timeSinceStartup < until)
                { view.Repaint(); yield return null; }
                Assert.That(PrefabBuildPreview.Output, Is.Not.Null, PrefabBuildPreview.Failure);
                // The centroid lies outside every original triangle, so picking an
                // unchanged authoring mesh cannot accidentally satisfy this test.
                var outputBody = PrefabBuildPreview.Output.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(r => r.name == "Body");
                var vertices = SyntheticAvatar.Vertices(outputBody);
                var center = vertices.Aggregate(Vector3.zero, (a, b) => a + b) / vertices.Length;
                Assert.That(center.x, Is.GreaterThan(.3f));
                Assert.That(PrefabBuildPreview.PickingBounds.Any(b => b.Contains(center)), Is.True,
                    "Picking cache must contain the actual rendered triangle centroid: " + center
                    + "; cached bounds=" + string.Join(";", PrefabBuildPreview.PickingBounds.Select(b => b.ToString())));
                Assert.That(PrefabBuildPreview.PickingVertices.Any(v => v.Length == vertices.Length
                    && v.Zip(vertices, Vector3.Distance).All(d => d <= 1e-4f)), Is.True,
                    "Cached picking vertices must match actual baked output; rendered=" + string.Join(";", vertices.Select(v => v.ToString("R")))
                    + "; cached=" + string.Join(" / ", PrefabBuildPreview.PickingVertices.Select(vs => string.Join(";", vs.Select(v => v.ToString("R"))))));
                for (var i = 0; i < 5; i++) { view.Repaint(); yield return null; }
                view.orthographic = true;
                view.LookAtDirect(center, Quaternion.Euler(0, 180, 0), .5f);
                Vector2 mouse = default;
                var located = false;
                locate = drawingView =>
                {
                    if (drawingView == view && Event.current.type == EventType.MouseUp)
                    {
                        // SendEvent exercises Scene GUI but does not enter Unity's
                        // OS shortcut dispatcher. Invoke the public picker in its
                        // documented input phase; native clicking is checked separately.
                        // Unity 2022's public wrapper passes an empty internal
                        // filter when null is supplied and rejects custom hits.
                        // Native Scene View selection has its own null-filter path.
                        var allowed = source.GetComponentsInChildren<Renderer>(true).Select(r => r.gameObject).ToArray();
                        Selection.activeGameObject = HandleUtility.PickGameObject(Event.current.mousePosition, false, null, allowed);
                    }
                    if (drawingView != view || Event.current.type != EventType.Repaint || located) return;
                    mouse = GUIUtility.GUIToScreenPoint(HandleUtility.WorldToGUIPoint(center)) - view.position.position;
                    located = true;
                };
                SceneView.beforeSceneGui += locate;
                while (!located && EditorApplication.timeSinceStartup < until) { view.Repaint(); yield return null; }
                Assert.That(located, Is.True);
                Selection.objects = Array.Empty<Object>();
                view.SendEvent(new Event { type = EventType.MouseDown, button = 0, mousePosition = mouse });
                yield return null;
                view.SendEvent(new Event { type = EventType.MouseUp, button = 0, mousePosition = mouse });
                yield return null;
                var selected = Selection.activeGameObject;
                Assert.That(selected, Is.Not.Null, "The public Scene picker must hit the displayed processed triangle.");
                Assert.That(selected.scene, Is.EqualTo(source.scene), "Selection must resolve to the authoring Stage, never an owned build object.");
                Assert.That(selected == source || selected.transform.IsChildOf(source.transform), Is.True);
                Assert.That(PrefabBuildPreview.ActiveCameraScopes, Is.Zero);
                Assert.That(Components(source), Is.EqualTo(before));
                Assert.That(stage.scene.isDirty, Is.False);
                for (var i = 0; i < files.Length; i++) Assert.That(File.ReadAllBytes(files[i]), Is.EqualTo(bytes[i]));
                TestContext.WriteLine("Public API processed-only Scene pick resolved to authoring object: " + selected.name);
            }
            finally
            {
                if (locate != null) SceneView.beforeSceneGui -= locate;
                StageUtility.GoToMainStage();
                Selection.objects = oldSelection.Where(o => o).ToArray();
                view.orthographic = oldOrtho;
                view.LookAtDirect(oldPivot, oldRotation, oldSize);
                AssetDatabase.DeleteAsset(folder);
                PropCacheDebug.InvalidateAllCaches();
                ComputeContext.FlushInvalidates();
            }
        }

        private static void SaveFixture(string folder, float multiplier)
        {
            using (var fixture = new SyntheticAvatar())
            {
                fixture.Scale(fixture.Hips, multiplier);
                foreach (var renderer in new[] { fixture.Body, fixture.Outfit })
                {
                    var mesh = Object.Instantiate(renderer.sharedMesh);
                    var material = Object.Instantiate(renderer.sharedMaterial);
                    AssetDatabase.CreateAsset(mesh, folder + "/" + renderer.name + ".asset");
                    AssetDatabase.CreateAsset(material, folder + "/" + renderer.name + ".mat");
                    renderer.sharedMesh = mesh;
                    renderer.sharedMaterial = material;
                }
                PrefabUtility.SaveAsPrefabAsset(fixture.Root, folder + "/source.prefab");
            }
        }

        [UnityTest, Category("PREVIEW_PREFAB_FALLBACK")]
        public IEnumerator InContextRenderingKeepsNativeSurroundingsAndRestoresRenderOnlyState()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null || PreviewSession.Current == null)
                Assert.Ignore("BLOCKED_ENV: native graphics and enabled NDMF Preview are required.");
            Assert.That(PrefabStageUtility.GetCurrentPrefabStage(), Is.Null);
            var folder = "Assets/ASS-PrefabContext-" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folder.Substring(7));
            var prefabPath = folder + "/source.prefab";
            GameObject instance = null, surroundings = null;
            Action<Camera, GameObject> rendered = null;
            var view = SceneView.lastActiveSceneView;
            Assert.That(view, Is.Not.Null);
            var oldPivot = view.pivot;
            var oldRotation = view.rotation;
            var oldSize = view.size;
            var oldOrtho = view.orthographic;
            try
            {
                using (var fixture = new SyntheticAvatar())
                {
                    fixture.Scale(fixture.Hips, 1.2f);
                    foreach (var renderer in new[] { fixture.Body, fixture.Outfit })
                    {
                        var mesh = Object.Instantiate(renderer.sharedMesh);
                        var material = Object.Instantiate(renderer.sharedMaterial);
                        AssetDatabase.CreateAsset(mesh, folder + "/" + renderer.name + ".asset");
                        AssetDatabase.CreateAsset(material, folder + "/" + renderer.name + ".mat");
                        renderer.sharedMesh = mesh;
                        renderer.sharedMaterial = material;
                    }
                    PrefabUtility.SaveAsPrefabAsset(fixture.Root, prefabPath);
                }
                instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath));
                surroundings = GameObject.CreatePrimitive(PrimitiveType.Cube);
                surroundings.name = "ASS owned context marker";
                surroundings.transform.position = new Vector3(1, .7f, 0);
                surroundings.transform.localScale = Vector3.one * .3f;
                var markerMaterial = new Material(Shader.Find("Unlit/Color")) { color = Color.red };
                AssetDatabase.CreateAsset(markerMaterial, folder + "/context.mat");
                surroundings.GetComponent<Renderer>().sharedMaterial = markerMaterial;
                var contextScene = surroundings.scene;
                var stage = PrefabStageUtility.OpenPrefab(prefabPath, instance, PrefabStage.Mode.InContext);
                Assert.That(stage.mode, Is.EqualTo(PrefabStage.Mode.InContext));
                var source = stage.prefabContentsRoot;
                var before = Components(source);
                var files = Directory.GetFiles(folder);
                var bytes = files.Select(File.ReadAllBytes).ToArray();
                var sourceRenderers = source.GetComponentsInChildren<Renderer>(true);
                var renderFlags = sourceRenderers.Select(r => r.forceRenderingOff).ToArray();
                var frames = 0;
                var contextMaskSeen = false;
                var originalsSuppressed = false;
                var markerPixels = 0;
                var avatarPixels = 0;
                rendered = (camera, output) =>
                {
                    if (camera != view.camera || PrefabStageUtility.GetCurrentPrefabStage() != stage) return;
                    frames++;
                    contextMaskSeen = (camera.overrideSceneCullingMask & EditorSceneManager.GetSceneCullingMask(contextScene)) != 0;
                    originalsSuppressed = sourceRenderers.All(r => r.forceRenderingOff);
                    var texture = new Texture2D(camera.targetTexture.width, camera.targetTexture.height, TextureFormat.RGBA32, false);
                    var previous = RenderTexture.active;
                    try
                    {
                        RenderTexture.active = camera.targetTexture;
                        texture.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
                        texture.Apply();
                        markerPixels = texture.GetPixels32().Count(c => c.r > 140 && c.r > c.g * 1.3 && c.r > c.b * 1.3);
                        avatarPixels = texture.GetPixels32().Count(c => c.b > 150 && c.b > c.g + 20 && c.b > c.r + 30);
                    }
                    finally { RenderTexture.active = previous; Object.DestroyImmediate(texture); }
                };
                PrefabBuildPreview.FrameRendered += rendered;
                view.Focus();
                var until = EditorApplication.timeSinceStartup + 5;
                // Wait for Unity's native delayed framing before setting this test camera.
                for (var i = 0; i < 5; i++) { view.Repaint(); yield return null; }
                view.orthographic = true;
                view.LookAtDirect(new Vector3(.45f, .65f, 0), Quaternion.Euler(0, 180, 0), 1.4f);
                frames = 0;
                while ((frames < 2 || !PrefabBuildPreview.Output) && EditorApplication.timeSinceStartup < until)
                { view.Repaint(); yield return null; }
                Assert.That(frames, Is.GreaterThan(0), PrefabBuildPreview.Failure);
                Assert.That(contextMaskSeen, Is.True, "The real Scene camera must retain the native context Scene culling mask.");
                Assert.That(originalsSuppressed, Is.True, "The source and processed output must not render over each other.");
                Assert.That(markerPixels, Is.GreaterThan(20), "The external red marker must remain visible in the actual Scene View frame.");
                Assert.That(avatarPixels, Is.GreaterThan(20), "The processed avatar must also be visible in the same actual frame.");
                TestContext.WriteLine("Native In Context frames=" + frames + "; red context pixels=" + markerPixels
                    + "; avatar pixels=" + avatarPixels + "; context mask=" + contextMaskSeen + "; originals render-suppressed=" + originalsSuppressed);
                Assert.That(sourceRenderers.Select(r => r.forceRenderingOff), Is.EqualTo(renderFlags));
                Assert.That(PrefabBuildPreview.ActiveCameraScopes, Is.Zero);
                Assert.That(Components(source), Is.EqualTo(before));
                Assert.That(stage.scene.isDirty, Is.False);
                for (var i = 0; i < files.Length; i++) Assert.That(File.ReadAllBytes(files[i]), Is.EqualTo(bytes[i]));
            }
            finally
            {
                if (rendered != null) PrefabBuildPreview.FrameRendered -= rendered;
                StageUtility.GoToMainStage();
                if (instance) Object.DestroyImmediate(instance);
                if (surroundings) Object.DestroyImmediate(surroundings);
                view.orthographic = oldOrtho;
                view.LookAtDirect(oldPivot, oldRotation, oldSize);
                AssetDatabase.DeleteAsset(folder);
                PropCacheDebug.InvalidateAllCaches();
                ComputeContext.FlushInvalidates();
            }
        }

        [UnityTest, Category("PREVIEW_PREFAB_FALLBACK")]
        public IEnumerator OpeningPrefabCreatesARealProcessedPreviewWithoutWritingSource()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null || PreviewSession.Current == null)
                Assert.Ignore("BLOCKED_ENV: native graphics and enabled NDMF Preview are required.");
            Assert.That(PrefabStageUtility.GetCurrentPrefabStage(), Is.Null, "Exit the user's Prefab Stage before this owned fixture.");
            var folder = "Assets/ASS-PrefabPreview-" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folder.Substring(7));
            var prefabPath = folder + "/source.prefab";
            Action<SceneView> nativeGui = null;
            EditorApplication.CallbackFunction afterNativeGuiTick = null;
            var allowAutoSave = EditorSettings.prefabModeAllowAutoSave;
            var oldSelection = Selection.objects;
            var ownUndoGroup = -1;
            try
            {
                using (var fixture = new SyntheticAvatar())
                {
                    fixture.Scale(fixture.Hips, 1.2f);
                    foreach (var renderer in new[] { fixture.Body, fixture.Outfit })
                    {
                        var mesh = Object.Instantiate(renderer.sharedMesh);
                        var material = Object.Instantiate(renderer.sharedMaterial);
                        AssetDatabase.CreateAsset(mesh, folder + "/" + renderer.name + ".asset");
                        AssetDatabase.CreateAsset(material, folder + "/" + renderer.name + ".mat");
                        renderer.sharedMesh = mesh;
                        renderer.sharedMaterial = material;
                    }
                    PrefabUtility.SaveAsPrefabAsset(fixture.Root, prefabPath);
                    var files = Directory.GetFiles(folder);
                    var bytes = files.Select(File.ReadAllBytes).ToArray();
                    var stage = PrefabStageUtility.OpenPrefab(prefabPath);
                    var source = stage.prefabContentsRoot;
                    var before = Components(source);
                    var pivot = source.transform.Find("Armature/Hips").position;
                    var baseline = source.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                        .ToDictionary(r => r.name, SyntheticAvatar.Vertices);
                    var sawNativeGui = false;
                    var updatesAfterGui = 0;
                    nativeGui = view =>
                    {
                        if (Event.current.type == EventType.Repaint && PrefabStageUtility.GetCurrentPrefabStage() == stage && !sawNativeGui)
                        {
                            sawNativeGui = true;
                            updatesAfterGui = 0;
                        }
                    };
                    afterNativeGuiTick = () => { if (sawNativeGui) updatesAfterGui++; };
                    SceneView.duringSceneGui += nativeGui;
                    EditorApplication.update += afterNativeGuiTick;
                    SceneView.lastActiveSceneView?.Focus();
                    var until = EditorApplication.timeSinceStartup + 5;
                    // Unity schedules native Prefab framing for two update turns
                    // after its first Scene GUI. Do not close the Stage before it.
                    while ((!PrefabBuildPreview.Output || !sawNativeGui || updatesAfterGui < 3)
                        && EditorApplication.timeSinceStartup < until)
                    {
                        SceneView.RepaintAll();
                        yield return null;
                    }
                    Assert.That(PrefabBuildPreview.Output, Is.Not.Null, "Opening Prefab Mode must schedule the maintained production preview.");
                    Assert.That(sawNativeGui && updatesAfterGui >= 3, Is.True, "Observe actual Prefab Scene GUI initialization before teardown.");
                    Assert.That(PrefabBuildPreview.BuildCount, Is.GreaterThan(0));
                    foreach (var renderer in PrefabBuildPreview.Output.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        SyntheticAvatar.AssertVertices(baseline[renderer.name].Select(v => pivot + (v - pivot) * 1.2f).ToArray(), SyntheticAvatar.Vertices(renderer));
                        Assert.That(PrefabBuildPreview.OriginalFor(renderer), Is.EqualTo(source.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(r => r.name == renderer.name).gameObject));
                        Assert.That(PrefabBuildPreview.OriginalFor(renderer.gameObject), Is.EqualTo(PrefabBuildPreview.OriginalFor(renderer)),
                            "Native Scene selection returns a GameObject, whose renderer replacement may carry the registry origin.");
                    }
                    var selectedOutput = PrefabBuildPreview.Output.GetComponentsInChildren<SkinnedMeshRenderer>(true).First();
                    var expectedSelection = PrefabBuildPreview.OriginalFor(selectedOutput);
                    Selection.activeGameObject = selectedOutput.gameObject;
                    until = EditorApplication.timeSinceStartup + 5;
                    while (Selection.activeGameObject != expectedSelection && EditorApplication.timeSinceStartup < until)
                    { SceneView.RepaintAll(); yield return null; }
                    Assert.That(Selection.activeGameObject, Is.EqualTo(expectedSelection), "A selected output object must resolve through the actual Selection notification. selected="
                        + Selection.activeGameObject?.GetInstanceID() + "; expected=" + expectedSelection.GetInstanceID()
                        + "; selected scene=" + Selection.activeGameObject?.scene.handle + "; output scene=" + PrefabBuildPreview.Output.scene.handle
                        + "; source scene=" + source.scene.handle + "; selection array=" + string.Join(",", Selection.objects.Select(o => o.name)));
                    Assert.That(Components(source), Is.EqualTo(before));
                    Assert.That(stage.scene.isDirty, Is.False);
                    // Explicit authoring belongs to the test, never to the preview.
                    EditorSettings.prefabModeAllowAutoSave = false;
                    Undo.IncrementCurrentGroup();
                    ownUndoGroup = Undo.GetCurrentGroup();
                    Undo.SetCurrentGroupName("ASS owned Prefab preview input");
                    var declaration = source.GetComponentInChildren<HierarchicalBoneScale>(true);
                    var serialized = new SerializedObject(declaration);
                    var count = PrefabBuildPreview.BuildCount;
                    serialized.FindProperty("UniformMultiplier").floatValue = 1.4f;
                    serialized.ApplyModifiedProperties();
                    Undo.FlushUndoRecordObjects();
                    var authored = Components(source);
                    yield return WaitForBuild(count);
                    AssertPreview(baseline, pivot, Vector3.one * 1.4f);
                    Assert.That(Components(source), Is.EqualTo(authored));
                    count = PrefabBuildPreview.BuildCount;
                    Undo.PerformUndo();
                    yield return WaitForBuild(count);
                    AssertPreview(baseline, pivot, Vector3.one * 1.2f);
                    count = PrefabBuildPreview.BuildCount;
                    Undo.PerformRedo();
                    yield return WaitForBuild(count);
                    AssertPreview(baseline, pivot, Vector3.one * 1.4f);
                    Undo.IncrementCurrentGroup();
                    serialized.Update();
                    serialized.FindProperty("Mode").enumValueIndex = 1;
                    serialized.FindProperty("VectorMultiplier").vector3Value = new Vector3(1.2f, .8f, 1.1f);
                    count = PrefabBuildPreview.BuildCount;
                    serialized.ApplyModifiedProperties();
                    Undo.FlushUndoRecordObjects();
                    yield return WaitForBuild(count);
                    AssertPreview(baseline, pivot, new Vector3(1.2f, .8f, 1.1f));
                    Undo.IncrementCurrentGroup();
                    var sync = new SerializedObject(source.GetComponent<ArmatureScaleSync>());
                    sync.FindProperty("PreviewEnabled").boolValue = false;
                    count = PrefabBuildPreview.BuildCount;
                    sync.ApplyModifiedProperties();
                    Undo.FlushUndoRecordObjects();
                    until = EditorApplication.timeSinceStartup + 5;
                    while ((PrefabBuildPreview.Output || PrefabBuildPreview.IsPending) && EditorApplication.timeSinceStartup < until)
                        yield return null;
                    Assert.That(PrefabBuildPreview.Output, Is.Null);
                    Assert.That(PrefabBuildPreview.BuildCount, Is.EqualTo(count), "OFF must release the snapshot without building another.");
                    Undo.PerformUndo();
                    yield return WaitForBuild(count);
                    AssertPreview(baseline, pivot, new Vector3(1.2f, .8f, 1.1f));
                    count = PrefabBuildPreview.BuildCount;
                    until = EditorApplication.timeSinceStartup + .25;
                    while (EditorApplication.timeSinceStartup < until) yield return null;
                    Assert.That(PrefabBuildPreview.BuildCount, Is.EqualTo(count), "An unchanged Stage must not rebuild every frame.");
                    count = PrefabBuildPreview.BuildCount;
                    Undo.RevertAllDownToGroup(ownUndoGroup);
                    ownUndoGroup = -1;
                    yield return WaitForBuild(count);
                    AssertPreview(baseline, pivot, Vector3.one * 1.2f);
                    Assert.That(Components(source), Is.EqualTo(before));
                    Assert.That(stage.scene.isDirty, Is.False);
                    for (var i = 0; i < files.Length; i++)
                        Assert.That(File.ReadAllBytes(files[i]), Is.EqualTo(bytes[i]), files[i]);
                    StageUtility.GoToMainStage();
                    yield return null;
                    Assert.That(PrefabBuildPreview.Output, Is.Null, "Stage closing must release its output.");
                }
            }
            finally
            {
                if (nativeGui != null) SceneView.duringSceneGui -= nativeGui;
                if (afterNativeGuiTick != null) EditorApplication.update -= afterNativeGuiTick;
                if (ownUndoGroup >= 0) Undo.RevertAllDownToGroup(ownUndoGroup);
                StageUtility.GoToMainStage();
                EditorSettings.prefabModeAllowAutoSave = allowAutoSave;
                Selection.objects = oldSelection.Where(o => o).ToArray();
                AssetDatabase.DeleteAsset(folder);
                PropCacheDebug.InvalidateAllCaches();
                ComputeContext.FlushInvalidates();
            }
        }

        private static IEnumerator WaitForBuild(int previous)
        {
            var until = EditorApplication.timeSinceStartup + 5;
            while ((PrefabBuildPreview.BuildCount <= previous || !PrefabBuildPreview.Output || PrefabBuildPreview.IsPending)
                && EditorApplication.timeSinceStartup < until)
            {
                SceneView.RepaintAll();
                yield return null;
            }
            Assert.That(PrefabBuildPreview.BuildCount, Is.GreaterThan(previous), PrefabBuildPreview.Failure);
            Assert.That(PrefabBuildPreview.Output, Is.Not.Null, PrefabBuildPreview.Failure);
        }

        private static void AssertPreview(System.Collections.Generic.Dictionary<string, Vector3[]> baseline, Vector3 pivot, Vector3 factor)
        {
            foreach (var renderer in PrefabBuildPreview.Output.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                SyntheticAvatar.AssertVertices(baseline[renderer.name].Select(v => pivot + Vector3.Scale(v - pivot, factor)).ToArray(), SyntheticAvatar.Vertices(renderer));
        }

        private static string Components(GameObject root) => string.Join("\n", root.GetComponentsInChildren<Component>(true)
            .Where(c => c).Select(c => EditorJsonUtility.ToJson(c)));
    }
}
