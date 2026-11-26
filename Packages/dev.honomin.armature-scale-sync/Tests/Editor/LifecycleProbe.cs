// MIT License. Copyright (c) 2026 Armature Scale Sync contributors.
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Honomin.ArmatureScaleSync.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Honomin.ArmatureScaleSync.Tests
{
    // A disk checkpoint survives real Editor reloads. Only first-party fixture assets
    // under the uniquely named folder are created/removed by this explicit probe.
    [InitializeOnLoad]
    public static class LifecycleProbe
    {
        private const string ReloadKey = "dev.honomin.armature-scale-sync.lifecycle-reload";
        private const string TransitionKey = ReloadKey + ".transitions";
        [Serializable] private sealed class ReloadResult { public int before, after; public string unity; }
        static LifecycleProbe()
        {
            PrefabStage.prefabStageClosing += stage => TraceStage("closing", stage);
            PrefabStage.prefabStageOpened += stage => TraceStage("opened", stage);
            AssemblyReloadEvents.beforeAssemblyReload += () => TraceStage("before-reload", PrefabStageUtility.GetCurrentPrefabStage());
            AssemblyReloadEvents.afterAssemblyReload += () =>
            {
                TraceStage("after-reload", PrefabStageUtility.GetCurrentPrefabStage());
                var before = SessionState.GetInt(ReloadKey + ".count", 0);
                SessionState.SetInt(ReloadKey + ".count", before + 1);
                var directory = SessionState.GetString(ReloadKey, "");
                if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
                {
                    File.WriteAllText(Path.Combine(directory, "reload-observed.json"), JsonUtility.ToJson(new ReloadResult { before = before, after = before + 1, unity = Application.unityVersion }, true));
                    SessionState.EraseString(ReloadKey);
                }
            };
        }
        private static void TraceStage(string kind, PrefabStage stage)
        {
            var directory = SessionState.GetString(TransitionKey, "");
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return;
            File.AppendAllText(Path.Combine(directory, "stage-transitions.log"), DateTime.UtcNow.ToString("O") + " " + kind
                + " prefab=" + (stage != null ? stage.assetPath : "<none>") + "\n" + Environment.StackTrace + "\n");
        }
        [Serializable] private sealed class AssetHash { public string path, sha256; }
        [Serializable] private sealed class State
        {
            public string folder, scene, measurement, prefab, controlPrefab, transforms, overrides;
            public AssetHash[] assets;
        }
        [Serializable] private sealed class Result
        {
            public string phase, status, preview_status, reason;
            public bool transforms_unchanged, overrides_unchanged, assets_unchanged, scene_clean;
            public float max_vertex_error;
            public int native_scene_view_frames, native_colored_pixels;
            public bool prefab_source_unchanged, camera_scopes_restored;
            public bool prefab_stage_expected, prefab_stage_open;
        }
        private static string Hash(string path)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
        }
        private static string Transforms(GameObject root) => string.Join("\n", root.GetComponentsInChildren<Transform>(true).Select(t => AnimationUtility.CalculateTransformPath(t, root.transform) + "|" + t.localPosition.ToString("R") + "|" + t.localRotation.ToString("R") + "|" + t.localScale.ToString("R")));
        private static string Overrides(GameObject root) => string.Join("\n", (PrefabUtility.GetPropertyModifications(root) ?? Array.Empty<PropertyModification>()).Select(m => m.target.GetType().FullName + "|" + m.target.name + "|" + m.propertyPath + "|" + m.value + "|" + (m.objectReference ? m.objectReference.name : "")).OrderBy(s => s, StringComparer.Ordinal));
        private static State Read(string directory) => JsonUtility.FromJson<State>(File.ReadAllText(Path.Combine(directory, "state.json")));
        private static GameObject Root(State state) => SceneManager.GetSceneByPath(state.scene).GetRootGameObjects().Single(g => g.name == "SyntheticAvatarLifecycle");

        public static void RequestEditorReady(string directory, string phase)
        {
            // Observe a real GUI initialization turn instead of assuming that an
            // assembly-reload event means NDMF's deferred camera hooks ran.
            EditorApplication.delayCall += () => File.WriteAllText(
                Path.Combine(directory, phase + "-ready.json"), "{\"ready\":true}");
            SceneView.RepaintAll();
            EditorApplication.QueuePlayerLoopUpdate();
        }

        public static string Prepare(string directory, float multiplier = 1.2f)
        {
            for (var i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty || string.IsNullOrEmpty(SceneManager.GetSceneAt(i).path))
                    throw new InvalidOperationException("Resolve dirty/untitled scenes before lifecycle preparation.");
            Directory.CreateDirectory(directory);
            SessionState.SetString(TransitionKey, directory);
            var state = new State { folder = "Assets/ASS-Lifecycle-" + Guid.NewGuid().ToString("N") };
            AssetDatabase.CreateFolder("Assets", state.folder.Substring(7));
            state.scene = state.folder + "/source.unity";
            state.measurement = state.folder + "/measurement.unity";
            state.prefab = state.folder + "/source.prefab";
            state.controlPrefab = state.folder + "/ma-only.prefab";
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            GameObject instance;
            using (var f = new SyntheticAvatar())
            {
                f.Scale(f.Hips, multiplier);
                foreach (var renderer in new[] { f.Body, f.Outfit })
                {
                    var mesh = Object.Instantiate(renderer.sharedMesh);
                    var material = Object.Instantiate(renderer.sharedMaterial);
                    AssetDatabase.CreateAsset(mesh, state.folder + "/" + renderer.name + ".asset");
                    AssetDatabase.CreateAsset(material, state.folder + "/" + renderer.name + ".mat");
                    renderer.sharedMesh = mesh;
                    renderer.sharedMaterial = material;
                }
                PrefabUtility.SaveAsPrefabAsset(f.Root, state.prefab);
                var control = Object.Instantiate(f.Root);
                try
                {
                    foreach (var declaration in control.GetComponentsInChildren<HierarchicalBoneScale>(true)) Object.DestroyImmediate(declaration);
                    foreach (var sync in control.GetComponentsInChildren<ArmatureScaleSync>(true)) Object.DestroyImmediate(sync);
                    PrefabUtility.SaveAsPrefabAsset(control, state.controlPrefab);
                }
                finally { Object.DestroyImmediate(control); }
                instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(state.prefab), scene);
                instance.name = "SyntheticAvatarLifecycle";
                var fit = instance.transform.Find("Clothing/Armature/Hips");
                fit.localScale = new Vector3(.95f, 1, 1);
                PrefabUtility.RecordPrefabInstancePropertyModifications(fit);
            }
            EditorSceneManager.SaveScene(scene, state.scene);
            state.transforms = Transforms(instance);
            state.overrides = Overrides(instance);
            var measurement = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(measurement);
            var camera = new GameObject("ASS Lifecycle Camera").AddComponent<Camera>();
            camera.enabled = false;
            camera.transform.position = new Vector3(0, .5f, -3);
            camera.transform.LookAt(new Vector3(0, .5f, 0));
            EditorSceneManager.SaveScene(measurement, state.measurement);
            state.assets = Directory.GetFiles(state.folder).Where(p => !p.EndsWith(".meta")).OrderBy(p => p).Select(p => new AssetHash { path = p, sha256 = Hash(p) }).ToArray();
            File.WriteAllText(Path.Combine(directory, "state.json"), JsonUtility.ToJson(state, true));
            return "Prepared saved synthetic Scene, Prefab, fitted instance and asset hashes.";
        }

        public static void StartProbe(string directory, string phase, int expectedPrefabStage = -1)
        {
            var routine = phase == "native-picking" ? NativePicking(directory) : Probe(directory, phase, expectedPrefabStage);
            EditorApplication.CallbackFunction advance = null;
            advance = () =>
            {
                var pending = false;
                try { pending = routine.MoveNext(); }
                catch (Exception exception)
                {
                    File.WriteAllText(Path.Combine(directory, phase + ".json"), JsonUtility.ToJson(new Result { phase = phase, status = "FAIL", reason = exception.GetType().Name + ": " + exception.Message }, true));
                }
                finally
                {
                    if (!pending)
                    {
                        EditorApplication.update -= advance;
                        (routine as IDisposable)?.Dispose();
                    }
                }
            };
            EditorApplication.update += advance;
            SceneView.RepaintAll();
            EditorApplication.QueuePlayerLoopUpdate();
        }

        private static IEnumerator NativePicking(string directory)
        {
            var state = Read(directory);
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage == null || stage.assetPath != state.prefab) throw new InvalidOperationException("Open the owned picking Prefab first.");
            var source = stage.prefabContentsRoot;
            var serialized = string.Join("\n", source.GetComponentsInChildren<Component>(true).Where(c => c).Select(EditorJsonUtility.ToJson));
            var view = SceneView.lastActiveSceneView;
            var oldPivot = view.pivot; var oldRotation = view.rotation; var oldSize = view.size; var oldOrtho = view.orthographic;
            var oldSelection = Selection.objects;
            var result = new Result { phase = "native-picking", status = "FAIL", preview_status = "FAIL", prefab_stage_expected = true };
            Action<Camera, GameObject> rendered = null;
            try
            {
                var deadline = EditorApplication.timeSinceStartup + 10;
                while (!PrefabBuildPreview.Output && EditorApplication.timeSinceStartup < deadline) { view.Repaint(); yield return null; }
                if (!PrefabBuildPreview.Output) throw new InvalidOperationException("Owned preview did not build: " + PrefabBuildPreview.Failure);
                for (var i = 0; i < 5; i++) { view.Repaint(); yield return null; }
                var body = PrefabBuildPreview.Output.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(r => r.name == "Body");
                var vertices = SyntheticAvatar.Vertices(body);
                var center = vertices.Aggregate(Vector3.zero, (a, b) => a + b) / vertices.Length;
                if (center.x <= .3f) throw new InvalidOperationException("The click must lie outside the original triangle footprint.");
                view.orthographic = true;
                view.LookAtDirect(center, Quaternion.Euler(0, 180, 0), .5f);
                Selection.objects = Array.Empty<Object>();
                rendered = (camera, output) =>
                {
                    if (camera != view.camera || output != PrefabBuildPreview.Output) return;
                    var target = camera.activeTexture ? camera.activeTexture : camera.targetTexture;
                    if (!target) return;
                    var previous = RenderTexture.active;
                    var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
                    try
                    {
                        RenderTexture.active = target;
                        image.ReadPixels(new Rect(0, 0, image.width, image.height), 0, 0);
                        image.Apply();
                        result.native_scene_view_frames++;
                        result.native_colored_pixels = image.GetPixels32().Count(p => p.b > 150 && p.b > p.g + 20 && p.b > p.r + 30);
                        if (result.native_colored_pixels > 20)
                            File.WriteAllText(Path.Combine(directory, "native-picking-ready.json"), "{\"ready\":true,\"blue_pixels\":" + result.native_colored_pixels + "}");
                    }
                    finally { RenderTexture.active = previous; Object.DestroyImmediate(image); }
                };
                PrefabBuildPreview.FrameRendered += rendered;
                deadline = EditorApplication.timeSinceStartup + 120;
                var settled = 0;
                while (settled < 3 && EditorApplication.timeSinceStartup < deadline)
                {
                    var candidate = Selection.activeGameObject;
                    var matches = candidate && candidate.scene == source.scene && (candidate == source || candidate.transform.IsChildOf(source.transform));
                    settled = matches ? settled + 1 : 0;
                    view.Repaint(); yield return null;
                }
                var selected = Selection.activeGameObject;
                result.prefab_stage_open = PrefabStageUtility.GetCurrentPrefabStage() == stage;
                result.prefab_source_unchanged = serialized == string.Join("\n", source.GetComponentsInChildren<Component>(true).Where(c => c).Select(EditorJsonUtility.ToJson));
                result.transforms_unchanged = Transforms(Root(state)) == state.transforms;
                result.overrides_unchanged = Overrides(Root(state)) == state.overrides;
                result.assets_unchanged = state.assets.All(a => Hash(a.path) == a.sha256);
                result.scene_clean = !stage.scene.isDirty;
                result.camera_scopes_restored = PrefabBuildPreview.ActiveCameraScopes == 0;
                var authoringSelection = selected && selected.scene == source.scene && (selected == source || selected.transform.IsChildOf(source.transform));
                if (authoringSelection && result.native_colored_pixels > 20 && result.prefab_stage_open && result.prefab_source_unchanged
                    && result.transforms_unchanged && result.overrides_unchanged && result.assets_unchanged && result.scene_clean && result.camera_scopes_restored)
                    result.status = result.preview_status = "PASS";
                result.reason = authoringSelection ? "OS mouse selected authoring object " + selected.name + "; stable across three Editor turns; the observer never assigned a selection."
                    : "No matching authoring selection observed from OS input. Last selection=" + (selected ? selected.name + "; scene=" + selected.scene.name : "<none>");
                File.WriteAllText(Path.Combine(directory, "native-picking.json"), JsonUtility.ToJson(result, true));
            }
            finally
            {
                if (rendered != null) PrefabBuildPreview.FrameRendered -= rendered;
                Selection.objects = oldSelection.Where(o => o).ToArray();
                view.orthographic = oldOrtho; view.LookAtDirect(oldPivot, oldRotation, oldSize);
            }
        }

        private static IEnumerator Probe(string directory, string phase, int expectedPrefabStage)
        {
            var state = Read(directory);
            var root = Root(state);
            var report = new Result { phase = phase, status = "PASS", preview_status = "PASS" };
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            report.prefab_stage_expected = expectedPrefabStage < 0 ? phase.StartsWith("prefab-", StringComparison.Ordinal) : expectedPrefabStage == 1;
            report.prefab_stage_open = stage != null;
            report.camera_scopes_restored = stage == null;
            if (report.prefab_stage_expected != (stage != null))
            {
                report.status = report.preview_status = "FAIL";
                report.reason = "Current Stage does not match this checkpoint's explicit expectation; a different mode cannot replace it.";
                File.WriteAllText(Path.Combine(directory, phase + ".json"), JsonUtility.ToJson(report, true));
                yield break;
            }
            if (expectedPrefabStage == 0)
            {
                report.reason = "The independently observed MA-only baseline returned to Main Stage after reload; measure the same natural transition without reopening.";
                if (PrefabBuildPreview.Output || PrefabBuildPreview.ActiveCameraScopes != 0)
                {
                    report.status = report.preview_status = "FAIL";
                    report.reason = "Prefab preview resources or camera overrides remained after the natural Stage transition.";
                }
            }
            var previewRoot = stage != null ? stage.prefabContentsRoot : root;
            var previewTransforms = Transforms(previewRoot);
            var previewOverrides = Overrides(previewRoot);
            var sourceMesh = previewRoot.GetComponentInChildren<SkinnedMeshRenderer>(true).sharedMesh;
            var beforeMesh = EditorJsonUtility.ToJson(sourceMesh);
            var renderers = previewRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var hips = previewRoot.transform.Find("Armature/Hips");
            var expected = renderers.ToDictionary(r => r.name, r => SyntheticAvatar.Vertices(r).Select(v => hips.position + (v - hips.position) * 1.2f).ToArray());
            var camera = SceneManager.GetSceneByPath(state.measurement).GetRootGameObjects().Single(g => g.name == "ASS Lifecycle Camera").GetComponent<Camera>();
            if (stage != null)
            {
                var sampled = false;
                Action<Camera, GameObject> onFrame = (renderedCamera, output) =>
                {
                    if (PrefabBuildPreview.Source != previewRoot
                        || (renderedCamera.overrideSceneCullingMask & EditorSceneManager.GetSceneCullingMask(output.scene)) == 0) return;
                    try
                    {
                        report.native_scene_view_frames++;
                        foreach (var renderer in output.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                            report.max_vertex_error = Mathf.Max(report.max_vertex_error,
                                expected[renderer.name].Zip(SyntheticAvatar.Vertices(renderer), Vector3.Distance).Max());
                        var target = renderedCamera.activeTexture ? renderedCamera.activeTexture : renderedCamera.targetTexture;
                        if (target)
                        {
                            var previous = RenderTexture.active;
                            Texture2D image = null;
                            try
                            {
                                RenderTexture.active = target;
                                image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
                                image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                                image.Apply();
                                report.native_colored_pixels = image.GetPixels32().Count(p => p.b > 150 && p.b > p.g + 20 && p.b > p.r + 30);
                            }
                            finally
                            {
                                RenderTexture.active = previous;
                                if (image) Object.DestroyImmediate(image);
                            }
                        }
                        sampled = report.native_colored_pixels > 0;
                    }
                    catch (Exception exception)
                    {
                        report.preview_status = "FAIL";
                        report.reason = exception.GetType().Name + ": " + exception.Message;
                    }
                };
                PrefabBuildPreview.FrameRendered += onFrame;
                try
                {
                    SceneView.lastActiveSceneView?.Focus();
                    for (var frame = 0; frame < 180 && !sampled && report.preview_status != "FAIL"; frame++)
                    {
                        SceneView.RepaintAll();
                        EditorApplication.QueuePlayerLoopUpdate();
                        yield return null;
                    }
                    if (!sampled && report.preview_status != "FAIL")
                    {
                        report.preview_status = "FAIL";
                        report.reason = "No native Scene View raster/vertex sample from the maintained Prefab preview. " + PrefabBuildPreview.Failure;
                    }
                    report.camera_scopes_restored = PrefabBuildPreview.ActiveCameraScopes == 0;
                }
                finally { PrefabBuildPreview.FrameRendered -= onFrame; }
            }
            else using (var capture = new PreviewCapture(previewRoot, camera))
            {
                SceneView.RepaintAll();
                EditorApplication.QueuePlayerLoopUpdate();
                var wait = capture.WaitFor(() => renderers.All(r => capture.Vertices.ContainsKey(r.name)));
                while (true)
                {
                    var pending = false;
                    try { pending = wait.MoveNext(); }
                    catch (NUnit.Framework.AssertionException exception)
                    {
                        report.preview_status = stage != null && capture.Calls == 0 ? "BLOCKED_ENV" : "FAIL";
                        report.reason = stage != null && capture.Calls == 0
                            ? "Installed NDMF suppresses camera preview hooks while Prefab Mode is open; no proxy samples were produced."
                            : exception.Message;
                    }
                    if (!pending) break;
                    yield return null;
                }
                if (report.preview_status == "PASS")
                    foreach (var renderer in renderers)
                        report.max_vertex_error = Mathf.Max(report.max_vertex_error, expected[renderer.name].Zip(capture.Vertices[renderer.name], Vector3.Distance).Max());
            }
            report.transforms_unchanged = Transforms(root) == state.transforms;
            report.prefab_source_unchanged = Transforms(previewRoot) == previewTransforms && Overrides(previewRoot) == previewOverrides;
            report.overrides_unchanged = Overrides(root) == state.overrides;
            report.assets_unchanged = state.assets.All(a => File.Exists(a.path) && Hash(a.path) == a.sha256) && EditorJsonUtility.ToJson(sourceMesh) == beforeMesh;
            report.scene_clean = !root.scene.isDirty && (stage == null || !stage.scene.isDirty);
            if (!report.transforms_unchanged || !report.prefab_source_unchanged || !report.overrides_unchanged || !report.assets_unchanged || !report.scene_clean
                || (stage != null && !report.camera_scopes_restored) || report.max_vertex_error > 1e-4f || report.preview_status == "FAIL")
                report.status = "FAIL";
            if (report.status == "PASS" && report.preview_status == "BLOCKED_ENV")
                report.status = "BLOCKED_ENV";
            var json = JsonUtility.ToJson(report, true);
            File.WriteAllText(Path.Combine(directory, phase + ".json"), json);
        }

        public static void ReloadScene(string directory)
        {
            var state = Read(directory);
            var scene = SceneManager.GetSceneByPath(state.scene);
            if (scene.isDirty) throw new InvalidOperationException("Lifecycle source Scene unexpectedly dirty.");
            EditorSceneManager.CloseScene(scene, true);
            EditorSceneManager.OpenScene(state.scene, OpenSceneMode.Additive);
        }
        public static void OpenPrefab(string directory) => PrefabStageUtility.OpenPrefab(Read(directory).prefab);
        public static void OpenControlPrefab(string directory) => PrefabStageUtility.OpenPrefab(Read(directory).controlPrefab);
        public static void RecordStageMode(string directory, string phase)
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(phase, "^[a-z-]+$")) throw new ArgumentException("Invalid checkpoint name");
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            File.WriteAllText(Path.Combine(directory, phase + "-mode.json"),
                "{\"prefab_stage_open\":" + (stage != null ? "true" : "false") + ",\"ass_output_present\":" + (PrefabBuildPreview.Output ? "true" : "false") + "}");
        }
        public static void ClosePrefab() => StageUtility.GoToMainStage();
        public static string RequestReload(string directory)
        {
            var state = Read(directory);
            var token = "ReloadToken" + SessionState.GetInt(ReloadKey + ".count", 0);
            var path = state.folder + "/Editor/" + token + ".cs";
            if (File.Exists(path)) throw new InvalidOperationException("A real reload needs a fresh token.");
            if (!AssetDatabase.IsValidFolder(state.folder + "/Editor")) AssetDatabase.CreateFolder(state.folder, "Editor");
            var observed = Path.Combine(directory, "reload-observed.json");
            if (File.Exists(observed)) File.Delete(observed);
            SessionState.SetString(ReloadKey, directory);
            File.WriteAllText(path, "namespace Honomin.ArmatureScaleSync.Tests.Generated { internal static class " + token + " { internal const int Value = 1; } }");
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            return path;
        }
        public static void ResetMeasurement(string directory)
        {
            var state = Read(directory);
            var scene = SceneManager.GetSceneByPath(state.measurement);
            if (scene.IsValid()) EditorSceneManager.CloseScene(scene, true);
            EditorSceneManager.OpenScene(state.measurement, OpenSceneMode.Additive);
        }
        public static void Cleanup(string directory)
        {
            var state = Read(directory);
            var name = Path.GetFileName(state.folder);
            var target = Path.GetFullPath(state.folder);
            var expected = Path.GetFullPath(Path.Combine(Application.dataPath, name));
            if (state.folder != "Assets/" + name || !System.Text.RegularExpressions.Regex.IsMatch(name, "^ASS-Lifecycle-[0-9a-f]{32}$")
                || target != expected || state.scene != state.folder + "/source.unity")
                throw new InvalidOperationException("Refuse cleanup outside the uniquely owned fixture folder.");
            StageUtility.GoToMainStage();
            var scene = SceneManager.GetSceneByPath(state.scene);
            if (scene.IsValid()) EditorSceneManager.CloseScene(scene, true);
            if (!string.IsNullOrEmpty(state.measurement))
            {
                var measurement = SceneManager.GetSceneByPath(state.measurement);
                if (measurement.IsValid()) EditorSceneManager.CloseScene(measurement, true);
            }
            AssetDatabase.DeleteAsset(state.folder);
            SessionState.EraseString(TransitionKey);
        }
    }
}
