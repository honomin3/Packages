// MIT License. Copyright (c) 2026 Armature Scale Sync contributors.
using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf.preview;
using nadena.dev.ndmf.util;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Honomin.ArmatureScaleSync.Editor
{
    /// <summary>Prefab Stage adapter; NDMF still owns all avatar processing.</summary>
    [InitializeOnLoad]
    internal static class PrefabBuildPreview
    {
        private sealed class CameraState
        {
            internal Scene Scene;
            internal ulong Mask;
            internal (Renderer Renderer, bool ForceOff)[] Renderers;
            internal bool Repaint;
        }
        private static GameObject source;
        private static PrefabStage readyStage;
        private static GameObject readyRoot;
        private static bool recoveryQueued;
        private static ProcessedPreviewSnapshot snapshot;
        private static ProcessedPreviewPicking picking;
        private static ComputeContext context;
        private static IDisposable subscription;
        private static Renderer[] sourceRenderers = Array.Empty<Renderer>();
        private static readonly object watcherTarget = new object();
        private static readonly Dictionary<Camera, CameraState> drawing = new Dictionary<Camera, CameraState>();
        private static readonly Dictionary<SceneView, Label> notices = new Dictionary<SceneView, Label>();
        private static bool enabled, pending, building;
        private static double changedAt;
        internal static GameObject Output => snapshot?.Root;
        internal static GameObject Source => source;
        internal static int BuildCount { get; private set; }
        internal static string Failure { get; private set; }
        internal static double LastBuildMilliseconds { get; private set; }
        internal static bool IsPending => pending || building;
        internal static GameObject OriginalFor(UnityEngine.Object output) => snapshot?.SourceFor(output);
        internal static int ActiveCameraScopes => drawing.Count;
        internal static Bounds[] PickingBounds => picking?.CachedBounds ?? Array.Empty<Bounds>();
        internal static Vector3[][] PickingVertices => picking?.CachedVertices ?? Array.Empty<Vector3[]>();
        internal static event Action<Camera, GameObject> FrameRendered;

        static PrefabBuildPreview()
        {
            EditorApplication.update += Update;
            SceneView.beforeSceneGui += BeforeSceneGui;
            SceneView.duringSceneGui += DuringSceneGui;
            Camera.onPostRender += AfterCameraRender;
            Selection.selectionChanged += ResolveSelection;
            HandleUtility.pickGameObjectCustomPasses += Pick;
            EditorSceneManager.sceneSaving += (_, __) => RestoreAllCameras();
            PrefabStage.prefabStageOpened += stage => { readyStage = stage; readyRoot = stage.prefabContentsRoot; Schedule(); };
            PrefabStage.prefabStageClosing += _ => { readyStage = null; readyRoot = null; Reset(); };
            AssemblyReloadEvents.beforeAssemblyReload += Reset;
            EditorApplication.quitting += Reset;
            EditorApplication.playModeStateChanged += _ => { Reset(); QueueRecovery(); };
            QueueRecovery();
        }

        private static void QueueRecovery()
        {
            if (recoveryQueued) return;
            recoveryQueued = true;
            EditorApplication.delayCall += () =>
            {
                recoveryQueued = false;
                var stage = PrefabStageUtility.GetCurrentPrefabStage();
                if (stage == null) return;
                try
                {
                    // A domain reload can preserve an already-open Stage without
                    // emitting prefabStageOpened. Never request the root in Awake.
                    readyRoot = stage.prefabContentsRoot;
                    readyStage = stage;
                }
                catch (InvalidOperationException) { QueueRecovery(); }
            };
        }

        private static void Schedule()
        {
            pending = true;
            changedAt = EditorApplication.timeSinceStartup;
            RestoreAllCameras();
            SceneView.RepaintAll();
            EditorApplication.QueuePlayerLoopUpdate();
        }

        private static void Observe()
        {
            subscription?.Dispose();
            context?.Invalidate();
            context = new ComputeContext("Armature Scale Sync Prefab build preview");
            sourceRenderers = context.GetComponentsInChildren<Renderer>(source, true);
            foreach (var component in context.GetComponentsInChildren<Component>(source, true))
                if (component) context.Observe(component);
            foreach (var go in context.GetComponentsInChildren<Transform>(source, true))
                context.Observe(go.gameObject);
            foreach (var asset in source.ReferencedAssets(traverseSaved: true, includeScene: false))
                if (asset && !(asset is Component) && !(asset is GameObject)) context.Observe(asset);
            subscription = context.InvokeOnInvalidate(watcherTarget, _ => Schedule());
        }

        private static void Update()
        {
            // ExitGUI can bypass duringSceneGui on an input event. Never keep its
            // render-only scope past the next Editor turn.
            RestoreAllCameras();
            // Unity can deliver a selection notification before its cached
            // multi-selection array reflects a native picking command. Resolve
            // an owned active object again on the following Editor turn.
            var selected = Selection.activeGameObject;
            if (snapshot?.Root && selected && (selected == snapshot.Root || selected.transform.IsChildOf(snapshot.Root.transform)))
                ResolveSelection();
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            var nextSource = stage != null && stage == readyStage ? readyRoot : null;
            var nextEnabled = nextSource && !EditorApplication.isPlayingOrWillChangePlaymode
                && !EditorApplication.isCompiling && PreviewSession.Current != null && ScalePreviewFilter.Enable.IsEnabled.Value;
            if (nextSource != source || nextEnabled != enabled)
            {
                Reset();
                source = nextSource;
                enabled = nextEnabled;
                if (enabled) Schedule();
            }
            if (!enabled || !source || !pending || building || EditorApplication.timeSinceStartup - changedAt < .05) return;
            pending = false;
            building = true;
            try
            {
                Observe();
                var declarations = context.GetComponentsInChildren<HierarchicalBoneScale>(source, true);
                var sync = context.GetComponent<ArmatureScaleSync>(source);
                var show = declarations.Length > 0 && (!sync || context.Observe(sync, s => s.PreviewEnabled));
                snapshot?.Dispose();
                snapshot = null;
                picking = null;
                Failure = null;
                if (show)
                {
                    BuildCount++;
                    snapshot = ProcessedPreviewSnapshot.Create(source);
                    picking = new ProcessedPreviewPicking(snapshot);
                    LastBuildMilliseconds = snapshot.ElapsedMilliseconds;
                }
            }
            catch (Exception error)
            {
                snapshot?.Dispose();
                snapshot = null;
                picking = null;
                Failure = error.Message;
            }
            finally
            {
                building = false;
                RefreshNotices();
                SceneView.RepaintAll();
            }
        }

        private static void BeforeSceneGui(SceneView view)
        {
            if (!enabled || !source) return;
            if (Event.current.type == EventType.Repaint) RefreshNotice(view);
            RestoreCamera(view.camera);
            if (pending || building || !snapshot?.Root) return;
            var state = new CameraState
            {
                Scene = view.camera.scene,
                Mask = view.camera.overrideSceneCullingMask,
                Repaint = Event.current.type == EventType.Repaint,
                Renderers = sourceRenderers.Where(r => r).Select(r => (r, r.forceRenderingOff)).ToArray()
            };
            drawing[view.camera] = state;
            // Keep Unity's native isolation/context mask and add only our output.
            // Render-only suppression is scoped exactly like NDMF's camera hook;
            // no authoring transform, enabled flag or serialized field is changed.
            var nativeMask = state.Mask != 0 ? state.Mask : EditorSceneManager.GetSceneCullingMask(source.scene);
            view.camera.scene = default;
            view.camera.overrideSceneCullingMask = nativeMask | EditorSceneManager.GetSceneCullingMask(snapshot.Scene);
            foreach (var original in state.Renderers) original.Renderer.forceRenderingOff = true;
        }

        private static void DuringSceneGui(SceneView view)
        {
            RestoreCamera(view.camera);
        }

        private static void ResolveSelection()
        {
            if (!snapshot?.Root) return;
            var changed = false;
            var resolved = Selection.objects.Select(selected =>
            {
                var go = selected as GameObject;
                if (!go && selected is Component component) go = component.gameObject;
                if (!go || (go != snapshot.Root && !go.transform.IsChildOf(snapshot.Root.transform))) return selected;
                var original = snapshot.SourceFor(selected);
                changed = true;
                return (UnityEngine.Object)original;
            }).Where(o => o).Distinct().ToArray();
            if (changed) Selection.objects = resolved;
        }

        private static GameObject Pick(Camera camera, int layers, Vector2 pixel, GameObject[] ignore, GameObject[] filter, out int materialIndex)
        {
            materialIndex = -1;
            if (!enabled || pending || building || picking == null || !snapshot?.Root
                || SceneView.currentDrawingSceneView?.camera != camera) return null;
            return picking.Pick(camera, layers, pixel, ignore, filter, out materialIndex);
        }

        private static void AfterCameraRender(Camera camera)
        {
            if (!camera || !drawing.TryGetValue(camera, out var state)) return;
            try
            {
                if (state.Repaint && snapshot?.Root && (camera.overrideSceneCullingMask & EditorSceneManager.GetSceneCullingMask(snapshot.Scene)) != 0)
                    FrameRendered?.Invoke(camera, snapshot.Root);
            }
            finally { RestoreCamera(camera); }
        }

        private static void RestoreCamera(Camera camera)
        {
            if (ReferenceEquals(camera, null) || !drawing.TryGetValue(camera, out var state)) return;
            if (camera)
            {
                camera.scene = state.Scene;
                camera.overrideSceneCullingMask = state.Mask;
            }
            foreach (var original in state.Renderers)
                if (original.Renderer) original.Renderer.forceRenderingOff = original.ForceOff;
            drawing.Remove(camera);
        }

        private static void RestoreAllCameras()
        {
            foreach (var camera in drawing.Keys.ToArray())
                RestoreCamera(camera);
            drawing.Clear();
        }

        private static void RefreshNotice(SceneView view)
        {
            var text = pending || building ? "Armature Scale Sync: updating processed preview…"
                : Failure != null ? "Armature Scale Sync preview: " + Failure
                : snapshot?.Root ? "Armature Scale Sync: processed-build preview" : null;
            if (text == null)
            {
                if (notices.TryGetValue(view, out var old)) { old.RemoveFromHierarchy(); notices.Remove(view); }
                return;
            }
            if (!notices.TryGetValue(view, out var label))
            {
                label = new Label { pickingMode = PickingMode.Ignore };
                label.style.position = Position.Absolute;
                label.style.left = 8;
                label.style.bottom = 8;
                label.style.backgroundColor = new Color(.12f, .12f, .12f, .9f);
                label.style.color = Color.white;
                label.style.paddingLeft = label.style.paddingRight = 6;
                label.style.paddingTop = label.style.paddingBottom = 3;
                notices[view] = label;
                view.rootVisualElement.Add(label);
            }
            label.text = text;
        }

        private static void RefreshNotices()
        {
            foreach (var view in notices.Keys.ToArray())
                if (view) RefreshNotice(view);
                else notices.Remove(view);
        }

        private static void Reset()
        {
            RestoreAllCameras();
            subscription?.Dispose();
            subscription = null;
            context?.Invalidate();
            context = null;
            sourceRenderers = Array.Empty<Renderer>();
            snapshot?.Dispose();
            snapshot = null;
            picking = null;
            foreach (var label in notices.Values) label.RemoveFromHierarchy();
            notices.Clear();
            source = null;
            enabled = pending = false;
            Failure = null;
        }
    }
}
