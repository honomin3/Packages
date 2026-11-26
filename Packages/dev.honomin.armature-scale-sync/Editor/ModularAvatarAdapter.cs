// MIT License. Copyright (c) 2026 Armature Scale Sync contributors.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Honomin.ArmatureScaleSync.Core;
using nadena.dev.modular_avatar.core;
using nadena.dev.ndmf.preview;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Honomin.ArmatureScaleSync.Editor
{
    internal static class ModularAvatarAdapter
    {
        internal static string Id(Transform t) => t ? t.GetInstanceID().ToString(CultureInfo.InvariantCulture) : "";
        internal static Scale3 Multiplier(HierarchicalBoneScale h) => h.Mode == ScaleMode.Uniform
            ? new Scale3(h.UniformMultiplier, h.UniformMultiplier, h.UniformMultiplier)
            : new Scale3(h.VectorMultiplier.x, h.VectorMultiplier.y, h.VectorMultiplier.z);

        internal static PlanSnapshot Capture(GameObject root, bool preview, ComputeContext ctx = null)
        {
            var snapshot = new PlanSnapshot();
            var prefabStage = preview ? PrefabStageUtility.GetCurrentPrefabStage() : null;
            if (prefabStage != null && root.scene == prefabStage.scene)
                snapshot.Diagnostics.Add(new Diagnostic("PREVIEW_PREFAB_MODE", Severity.Warning, Id(root.transform),
                    "Prefab Mode shows an automatically processed build preview after each change. Use Hierarchy to select a surface that cannot be picked in the preview."));
            bool InScope(Component c)
            {
                var avatar = nadena.dev.ndmf.runtime.RuntimeUtil.FindAvatarInParents(c.transform);
                return !avatar || avatar == root.transform;
            }
            T[] Components<T>() where T : Component => (ctx == null ? root.GetComponentsInChildren<T>(true) : ctx.GetComponentsInChildren<T>(root, true)).Where(InScope).ToArray();
            var syncs = Components<ArmatureScaleSync>();
            foreach (var item in syncs)
                ctx?.Observe(item);
            var sync = syncs.SingleOrDefault(s => s.transform == root.transform);
            if (syncs.Any(s => s != sync))
                snapshot.Diagnostics.Add(new Diagnostic("SYNC_ROOT", Severity.Warning, Id(root.transform), "Armature Scale Sync belongs on the avatar root; nested settings were ignored."));
            snapshot.RebaseAnimation = sync == null || sync.RebaseAnimation;
            var mergeComponents = Components<ModularAvatarMergeArmature>();
            var mapped = new Dictionary<Transform, Transform>();
            var ambiguous = new HashSet<Transform>();
            foreach (var ma in mergeComponents)
            {
                ctx?.Observe(ma);
                var destination = ma.mergeTargetObject;
                if (!destination)
                {
                    snapshot.Diagnostics.Add(new Diagnostic("UNRESOLVED", Severity.Warning, Id(ma.transform), "MA Merge Armature target is unresolved; fix MA configuration."));
                    continue;
                }
                // GetBonesMapping returns (base, merge), and excludes the merge root itself.
                var pairs = ma.GetBonesMapping() ?? new List<(Transform, Transform)>();
                pairs.Add((destination.transform, ma.transform));
                foreach (var pair in pairs)
                {
                    if (!pair.Item1 || !pair.Item2)
                        continue;
                    if (mapped.TryGetValue(pair.Item2, out var existing) && existing != pair.Item1)
                        ambiguous.Add(pair.Item2);
                    else
                        mapped[pair.Item2] = pair.Item1;
                }
            }
            foreach (var bone in ambiguous)
            {
                mapped.Remove(bone);
                snapshot.Diagnostics.Add(new Diagnostic("AMBIGUOUS", Severity.Warning, Id(bone), "MA exposes conflicting correspondence routes; inspect nested Merge Armature configuration."));
            }
            foreach (var h in Components<HierarchicalBoneScale>())
            {
                ctx?.Observe(h);
                if (h.GetComponentInParent<ModularAvatarMergeArmature>(true))
                    snapshot.Diagnostics.Add(new Diagnostic("OUTFIT_COMPONENT", Severity.Warning, Id(h.transform), "HBS on an outfit bone can retain that bone during MA merging. Prefer body HBS and a root binding for independent rigs."));
                if (!h.enabled || !h.ScaleEnabled || EditorOnly(h.transform))
                    continue;
                snapshot.Settings.Add(new ScaleSetting { Bone = h.transform, Origin = Id(h.transform), Multiplier = Multiplier(h), Advanced = h.Mode == ScaleMode.Advanced });
            }
            bool Included(ScaleBinding b) => sync == null || (sync.TargetKinds & b.Kind) != 0;
            if (sync != null)
            {
                foreach (var binding in sync.Bindings ?? new List<ScaleBinding>())
                {
                    if (binding == null || !binding.Enabled || !Included(binding))
                        continue;
                    var source = binding.Source?.Get(sync);
                    var target = binding.Target?.Get(sync);
                    if (!source || !target || !source.transform.IsChildOf(root.transform) || !target.transform.IsChildOf(root.transform))
                    {
                        snapshot.Diagnostics.Add(new Diagnostic("UNRESOLVED", Severity.Warning, Id(root.transform), "Manual binding must resolve both ends inside this avatar."));
                        continue;
                    }
                    // MA owns connected outfits even when a duplicate manual route is present.
                    var directProxy = target.GetComponent<ModularAvatarBoneProxy>();
                    var route = mapped.ContainsKey(target.transform) ? (preview ? Route.ProjectedMerge : Route.Merged)
                        : directProxy ? Route.BoneProxy : target.transform.IsChildOf(source.transform) ? Route.Native : Route.Independent;
                    var actualSource = mapped.TryGetValue(target.transform, out var mappedSource) ? mappedSource
                        : directProxy && directProxy.target ? directProxy.target : source.transform;
                    if (actualSource != source.transform)
                        snapshot.Diagnostics.Add(new Diagnostic("MA_PRIORITY", Severity.Warning, Id(target.transform), "MA's connected target takes priority over the manual source. Existing MA settings were preserved."));
                    snapshot.Bindings.Add(new TargetBinding { Source = actualSource, Target = target.transform, Route = route });
                }
            }
            // Existing MA inheritance is inherent in the actual build, even when additional automatic
            // discovery is disabled. Project that same inheritance in preview to preserve parity.
            foreach (var pair in mapped)
                snapshot.Bindings.Add(new TargetBinding { Source = pair.Value, Target = pair.Key, Route = preview ? Route.ProjectedMerge : Route.Merged });
            if (sync && sync.TargetKinds != TargetKinds.All)
                snapshot.Diagnostics.Add(new Diagnostic("NATIVE_FILTER", Severity.Warning, Id(root.transform), "Category selection filters additional explicit delta sync. MA-merged and native children inherit hierarchy scale regardless of category; use bindings to label independent clothing, hair and accessories."));
            foreach (var proxy in Components<ModularAvatarBoneProxy>())
            {
                ctx?.Observe(proxy);
                if (!proxy.matchScale)
                    snapshot.Diagnostics.Add(new Diagnostic("PROXY_SCALE", Severity.Warning, Id(proxy.transform), "MA Bone Proxy Match Parent Scale is disabled. Verify MA placement; ASS does not overwrite this setting."));
                if (preview && snapshot.Settings.Count > 0)
                {
                    if (BoneProxyDeltaPreview.TryPlan(proxy, root.transform, snapshot.Settings, out var projection, out var reason))
                    {
                        if (projection != null)
                        {
                            snapshot.ProxyProjections.Add(projection);
                            snapshot.Bindings.Add(new TargetBinding { Source = projection.Target, Target = projection.Host, Route = Route.ProjectedProxy });
                        }
                    }
                    else
                        snapshot.Diagnostics.Add(new Diagnostic("PREVIEW_PROXY", Severity.Warning, Id(proxy.transform), reason));
                }
            }
            foreach (var duplicate in snapshot.Bindings.GroupBy(b => (b.Source, b.Target)).Where(g => g.Count() > 1))
                snapshot.Diagnostics.Add(new Diagnostic("DUPLICATE", Severity.Warning, Id(duplicate.Key.Target), "Several automatic/manual routes reach the same target. Contributor accounting applies each origin once."));
            if (snapshot.Settings.Count > 0 && (sync == null || (sync.AutoSync && sync.DiagnosticsEnabled)))
            {
                var nativeSources = snapshot.Settings.Select(s => s.Bone).ToArray();
                bool Known(Transform bone)
                {
                    for (var t = bone; t && t.IsChildOf(root.transform); t = t.parent)
                    {
                        if (nativeSources.Contains(t) || mapped.ContainsKey(t) || snapshot.Bindings.Any(b => b.Target == t))
                            return true;
                        if (t.GetComponent<ModularAvatarBoneProxy>())
                            return true;
                    }
                    return false;
                }
                foreach (var renderer in Components<SkinnedMeshRenderer>())
                    if (renderer.bones.Any(b => b) && !renderer.bones.Where(b => b).Any(Known))
                        snapshot.Diagnostics.Add(new Diagnostic("UNBOUND_RIG", Severity.Warning, Id(renderer.transform), "This skinned rig has no known MA, native or explicit scale route. Add source-to-target bone bindings; bone names are not guessed."));
            }
            if (preview && (!PreviewSupport.SharedAvailable))
                snapshot.Diagnostics.Add(new Diagnostic("PREVIEW_VERSION", Severity.Warning, Id(root.transform), PreviewSupport.Requirements));
            return snapshot;
        }
        private static bool EditorOnly(Transform t)
        {
            for (var p = t; p; p = p.parent)
                if (p.CompareTag("EditorOnly"))
                    return true;
            return false;
        }
    }
}
