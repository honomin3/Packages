// MIT License. Copyright (c) 2026 Armature Scale Sync contributors.
using System.Collections.Generic;
using System.Linq;
using Honomin.ArmatureScaleSync.Core;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Honomin.ArmatureScaleSync.Editor
{
    internal static class BuildScaleApplier
    {
        internal static void Prepare(BuildContext context)
        {
            var state = context.GetState<ScaleBuildState>();
            if (state.Prepared)
                return;
            if (context.AvatarRootObject.GetComponentsInChildren<HierarchicalBoneScale>(true).Length == 0 && !context.AvatarRootObject.GetComponent<ArmatureScaleSync>())
            {
                state.Prepared = true;
                return;
            }
            var snapshot = ModularAvatarAdapter.Capture(context.AvatarRootObject, false);
            foreach (var s in snapshot.Settings)
                state.Settings.Add(new ScaleBuildState.SavedSetting
                {
                    Bone = ObjectRegistry.GetReference(s.Bone.gameObject),
                    Origin = s.Origin,
                    Multiplier = s.Multiplier,
                    Advanced = s.Advanced
                });
            foreach (var b in snapshot.Bindings)
            {
                // Merged/manual routes receive no additional copy, including when MA subsequently
                // deletes the authoring bone. This avoids relying on a deleted path or private mapping.
                if (b.Route == Route.Merged || b.Route == Route.Native || b.Route == Route.BoneProxy)
                    continue;
                state.Bindings.Add(new ScaleBuildState.SavedBinding { Source = ObjectRegistry.GetReference(b.Source.gameObject), Target = ObjectRegistry.GetReference(b.Target.gameObject), Route = b.Route });
            }
            foreach (var d in snapshot.Diagnostics)
                if (state.ReportedDiagnostics.Add(d.Code + "|" + d.Severity + "|" + d.Target))
                    ScaleDiagnostics.Report(d, context.AvatarRootObject.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => ModularAvatarAdapter.Id(t) == d.Target));
            state.RebaseAnimation = snapshot.RebaseAnimation;
            state.Prepared = true;
        }

        internal static void Apply(BuildContext context)
        {
            var state = context.GetState<ScaleBuildState>();
            if (state.Applied)
                return;
            if (!state.Prepared)
                Prepare(context);
            if (state.Settings.Count == 0 && state.Bindings.Count == 0 && context.AvatarRootObject.GetComponentsInChildren<ArmatureScaleSync>(true).Length == 0
                && context.AvatarRootObject.GetComponentsInChildren<HierarchicalBoneScale>(true).Length == 0)
            {
                state.Applied = true;
                return;
            }
            var current = context.AvatarRootObject.GetComponentsInChildren<Transform>(true);
            var byIdentity = current.GroupBy(t => ObjectRegistry.GetReference(t.gameObject)).ToDictionary(g => g.Key, g => g.ToArray());
            Transform Resolve(ObjectReference reference)
            {
                if (reference != null && byIdentity.TryGetValue(reference, out var matches) && matches.Length == 1)
                    return matches[0];
                ScaleDiagnostics.Report(new Diagnostic("REPLACEMENT_UNRESOLVED", Severity.Warning, "", "An intervening plugin removed or ambiguously replaced a configured object. Register replacements with NDMF; no bone-name fallback was applied."), context.AvatarRootObject);
                return null;
            }
            var snapshot = new PlanSnapshot { RebaseAnimation = state.RebaseAnimation };
            foreach (var s in state.Settings)
            {
                var bone = Resolve(s.Bone);
                if (!bone)
                    continue;
                snapshot.Settings.Add(new ScaleSetting { Bone = bone, Origin = s.Origin, Multiplier = s.Multiplier, Advanced = s.Advanced });
            }
            foreach (var b in state.Bindings)
            {
                var source = Resolve(b.Source);
                var target = Resolve(b.Target);
                if (source && target)
                    snapshot.Bindings.Add(new TargetBinding { Source = source, Target = target, Route = target.IsChildOf(source) ? Route.Native : b.Route });
            }
            var plan = UnityScalePlan.Resolve(context.AvatarRootObject, snapshot);
            foreach (var d in plan.Diagnostics)
                if (state.ReportedDiagnostics.Add(d.Code + "|" + d.Severity + "|" + d.Target))
                    ScaleDiagnostics.Report(d, plan.Objects.TryGetValue(d.Target, out var related) ? related : context.AvatarRootTransform);
            foreach (var write in plan.Writes)
                write.Key.localScale = Vector3.Scale(write.Key.localScale, write.Value);
            AnimatorScaleAdapter.Rebase(context.Extension<AnimatorServicesContext>(), plan);
            state.Applied = true;
            foreach (var h in context.AvatarRootObject.GetComponentsInChildren<HierarchicalBoneScale>(true))
                Object.DestroyImmediate(h);
            foreach (var s in context.AvatarRootObject.GetComponentsInChildren<ArmatureScaleSync>(true))
                Object.DestroyImmediate(s);
        }
    }
}
