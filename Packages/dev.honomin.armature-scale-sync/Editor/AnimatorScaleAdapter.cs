// MIT License. Copyright (c) 2026 Armature Scale Sync contributors.
using System;
using System.Collections.Generic;
using System.Linq;
using Honomin.ArmatureScaleSync.Core;
using nadena.dev.ndmf.animator;
using UnityEditor;
using UnityEngine;

namespace Honomin.ArmatureScaleSync.Editor
{
    internal static class AnimatorScaleAdapter
    {
        internal static void Rebase(AnimatorServicesContext services, UnityScalePlan plan)
        {
            var processed = new HashSet<(VirtualClip, EditorCurveBinding)>();
            foreach (var write in plan.Writes)
            {
                var paths = services.ObjectPathRemapper.GetAllPathsForObject(write.Key).ToArray();
                if (paths.Length == 0)
                    ScaleDiagnostics.Report(new Diagnostic("ANIMATION_PATH_UNRESOLVED", Severity.Warning, "", "Animator Services has no virtual path for a replaced scale target. Verify that the intervening plugin registered its animation-path replacement."), write.Key);
                foreach (var path in paths)
                    for (var axis = 0; axis < 3; axis++)
                    {
                        var binding = EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalScale." + "xyz"[axis]);
                        foreach (var clip in services.AnimationIndex.GetClipsForBinding(binding).ToArray())
                            Edit(clip, binding, write.Value[axis], write.Key);
                    }
            }
            void Edit(VirtualClip clip, EditorCurveBinding binding, float factor, Transform related)
            {
                if (!processed.Add((clip, binding)))
                    return;
                var curve = clip.GetFloatCurve(binding);
                if (curve == null)
                    return;
                if (!plan.RebaseAnimation || clip.IsMarkerClip)
                {
                    ScaleDiagnostics.Report(new Diagnostic("ANIMATION_RESET", Severity.Warning, "", "A localScale curve can override a hierarchy multiplier. Curve rebasing is disabled or this is an immutable marker clip."), related);
                    return;
                }
                var rebased = RebaseCurve(curve, factor);
                if (rebased == null)
                {
                    ScaleDiagnostics.Report(new Diagnostic("ANIMATION_OVERFLOW", Severity.Error, "", "Scale animation values or tangents overflowed. The virtual curve was left unchanged."), related);
                    return;
                }
                // GetFloatCurve may return a cached original curve. Always replace it with our own curve.
                clip.SetFloatCurve(binding, rebased);
                if (clip.AdditiveReferencePoseClip is VirtualClip reference)
                    Edit(reference, binding, factor, related);
            }
        }

        internal static AnimationCurve RebaseCurve(AnimationCurve source, float k)
        {
            var keys = source.keys;
            for (var i = 0; i < keys.Length; i++)
            {
                var key = keys[i];
                key.value *= k;
                if (!float.IsInfinity(key.inTangent))
                {
                    key.inTangent *= k;
                    if (float.IsInfinity(key.inTangent))
                        return null;
                }
                if (!float.IsInfinity(key.outTangent))
                {
                    key.outTangent *= k;
                    if (float.IsInfinity(key.outTangent))
                        return null;
                }
                if (float.IsNaN(key.value) || float.IsInfinity(key.value) || float.IsNaN(key.inTangent) || float.IsNaN(key.outTangent))
                    return null;
                keys[i] = key;
            }
            return new AnimationCurve(keys) { preWrapMode = source.preWrapMode, postWrapMode = source.postWrapMode };
        }
    }
}
