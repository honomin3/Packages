// MIT License. Copyright (c) 2026 Armature Scale Sync contributors.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.platform;
using nadena.dev.ndmf.util;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Honomin.ArmatureScaleSync.Editor
{
    /// <summary>One real NDMF output, with an explicitly owned Scene and transient assets.</summary>
    internal sealed class ProcessedPreviewSnapshot : IDisposable
    {
        private readonly HashSet<Object> assets = new HashSet<Object>();
        private readonly Dictionary<int, GameObject> sourceByCloneId = new Dictionary<int, GameObject>();
        private IObjectRegistry registry;
        internal Scene Scene { get; private set; }
        internal GameObject Root { get; private set; }
        internal double ElapsedMilliseconds { get; private set; }

        internal static ProcessedPreviewSnapshot Create(GameObject source)
        {
            if (!source || EditorUtility.IsPersistent(source))
                throw new ArgumentException("Preview requires an authoring instance.", nameof(source));
            var snapshot = new ProcessedPreviewSnapshot();
            var timer = Stopwatch.StartNew();
            var protectedObjects = new HashSet<int>(Resources.FindObjectsOfTypeAll<Object>().Where(o => o).Select(o => o.GetInstanceID()));
            foreach (var dependency in source.ReferencedAssets(traverseSaved: true, includeScene: false))
                if (dependency) protectedObjects.Add(dependency.GetInstanceID());
            try
            {
                snapshot.Scene = EditorSceneManager.NewPreviewScene();
                snapshot.Root = Object.Instantiate(source);
                snapshot.Root.name = source.name;
                snapshot.Root.hideFlags = HideFlags.HideAndDontSave;
                SceneManager.MoveGameObjectToScene(snapshot.Root, snapshot.Scene);
                snapshot.CaptureSourceIdentities(source);
                var platform = PlatformRegistry.GetPrimaryPlatformForAvatar(snapshot.Root) ?? GenericPlatform.Instance;
                BuildContext context;
                using (new OverrideTemporaryDirectoryScope(null))
                    context = AvatarProcessor.ProcessAvatar(snapshot.Root, platform);
                snapshot.registry = context.ObjectRegistry;
                if (!context.Successful)
                    throw new InvalidOperationException("The processed preview reported build errors. See the NDMF diagnostics.");
                // Avatar cameras must not start unrelated Editor camera rendering.
                foreach (var camera in snapshot.Root.GetComponentsInChildren<Camera>(true)) camera.enabled = false;
                return snapshot;
            }
            catch
            {
                snapshot.CollectAssets(protectedObjects);
                snapshot.Dispose();
                throw;
            }
            finally
            {
                // Only new, nonpersistent assets reachable from this output are ours.
                // Protect pre-existing procedural assets as well as saved source assets.
                snapshot.CollectAssets(protectedObjects);
                timer.Stop();
                snapshot.ElapsedMilliseconds = timer.Elapsed.TotalMilliseconds;
            }
        }

        private void CollectAssets(HashSet<int> protectedObjects)
        {
            if (!Root) return;
            // Unity's CollectDependencies can omit transient build assets. Use
            // the same public graph traversal as NDMF's own serializer.
            foreach (var asset in Root.ReferencedAssets(traverseSaved: false, includeScene: false))
                if (asset && !(asset is GameObject) && !(asset is Component)
                    && !EditorUtility.IsPersistent(asset) && !protectedObjects.Contains(asset.GetInstanceID()))
                    assets.Add(asset);
        }

        private void CaptureSourceIdentities(GameObject source)
        {
            var originals = source.GetComponentsInChildren<Transform>(true);
            var copies = Root.GetComponentsInChildren<Transform>(true);
            if (originals.Length != copies.Length) return;
            for (var i = 0; i < originals.Length; i++)
            {
                sourceByCloneId[copies[i].gameObject.GetInstanceID()] = originals[i].gameObject;
                var originalComponents = originals[i].GetComponents<Component>();
                var copiedComponents = copies[i].GetComponents<Component>();
                if (originalComponents.Length != copiedComponents.Length) continue;
                for (var c = 0; c < originalComponents.Length; c++)
                    if (originalComponents[c] && copiedComponents[c]
                        && originalComponents[c].GetType() == copiedComponents[c].GetType())
                        sourceByCloneId[copiedComponents[c].GetInstanceID()] = originals[i].gameObject;
            }
        }

        internal GameObject SourceFor(Object output)
        {
            if (!output) return null;
            var origin = registry?.GetReference(output, false)?.Object ?? output;
            if (ReferenceEquals(origin, null)) return null;
            return sourceByCloneId.TryGetValue(origin.GetInstanceID(), out var source) && source ? source : null;
        }

        public void Dispose()
        {
            if (Scene.IsValid()) EditorSceneManager.ClosePreviewScene(Scene);
            Scene = default;
            Root = null;
            foreach (var asset in assets) if (asset) Object.DestroyImmediate(asset);
            assets.Clear();
            sourceByCloneId.Clear();
            registry = null;
        }
    }
}
