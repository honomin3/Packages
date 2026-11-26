// MIT License. Copyright (c) 2026 Armature Scale Sync contributors.
using System.Collections.Generic;
using System.Linq;
using Honomin.ArmatureScaleSync.Core;
using nadena.dev.modular_avatar.core;
using UnityEngine;
using UnityEngine.Animations;

namespace Honomin.ArmatureScaleSync.Editor
{
    internal sealed class BoneProxyProjection
    {
        public Transform Host, Target;
    }

    /// <summary>
    /// Applies only ASS's uniform hierarchy delta to an already represented MA placement baseline.
    /// It neither computes MA placement nor changes its settings, references or source transforms.
    /// </summary>
    internal static class BoneProxyDeltaPreview
    {
        internal static bool TryPlan(ModularAvatarBoneProxy proxy, Transform avatarRoot, IReadOnlyList<ScaleSetting> settings,
            out BoneProxyProjection projection, out string reason)
        {
            projection = null;
            reason = null;
            var host = proxy.transform;
            var target = proxy.target;
            if (!target || !target.IsChildOf(avatarRoot))
            {
                reason = "Bone Proxy target is unresolved or outside this avatar.";
                return false;
            }
            var effects = settings.Where(s => s.Bone && target.IsChildOf(s.Bone)).ToArray();
            if (effects.Length == 0)
                return true;
            if (target.IsChildOf(host))
            {
                reason = "Bone Proxy target is inside its own host hierarchy.";
                return false;
            }
            var keepPose = proxy.attachmentMode == BoneProxyAttachmentMode.AsChildKeepWorldPose;
            var atRoot = proxy.attachmentMode == BoneProxyAttachmentMode.AsChildAtRoot || proxy.attachmentMode == BoneProxyAttachmentMode.Unset;
            var aligned = Vector3.Distance(host.position, target.position) <= 1e-5f && Quaternion.Angle(host.rotation, target.rotation) <= .001f;
            if (host.parent == target && (keepPose || (atRoot && aligned)))
                return true;
            if (effects.Any(s => !s.Multiplier.IsUniform || !s.Multiplier.IsInvertible || !s.Multiplier.IsFloatRepresentable))
            {
                reason = "Bone Proxy live delta requires valid uniform hierarchy multipliers; inspect advanced scale placement on the processed avatar.";
                return false;
            }
            if (host.GetComponentInParent<ModularAvatarMergeArmature>(true) || target.GetComponentInParent<ModularAvatarMergeArmature>(true)
                || (host.parent && host.parent.GetComponentInParent<ModularAvatarBoneProxy>(true)))
            {
                reason = "Nested Bone Proxy / Merge Armature placement needs processed-avatar verification.";
                return false;
            }
            if (settings.Any(s => s.Bone && s.Bone != host && host.IsChildOf(s.Bone) && !target.IsChildOf(s.Bone))
                || host.GetComponentsInParent<Component>(true).Any(c => c is IConstraint))
            {
                reason = "Independent host-ancestor scales or constraints make the proxy delta ambiguous; inspect the processed avatar.";
                return false;
            }
            var originalParent = host.parent ? host.parent.localToWorldMatrix : Matrix4x4.identity;
            var targetMatrix = target.localToWorldMatrix;
            if (!Invertible(targetMatrix) || !EquivalentScaleFrames(originalParent, targetMatrix))
            {
                reason = "Bone Proxy's authored parent and target do not establish an equal scale baseline. MA owns that placement; verify it in the processed view.";
                return false;
            }
            if (!keepPose && !(atRoot && aligned))
            {
                reason = "The authoring pose does not already represent this MA placement mode. Inspect the processed avatar; ASS does not reproduce MA's placement engine.";
                return false;
            }
            if (host.IsChildOf(target))
                return true;
            projection = new BoneProxyProjection { Host = host, Target = target };
            return true;
        }

        private static bool Invertible(Matrix4x4 m) => m.determinant != 0 && !float.IsNaN(m.determinant) && !float.IsInfinity(m.determinant);
        private static bool Near(float a, float b) => Mathf.Abs(a - b) <= 1e-6f * Mathf.Max(float.Epsilon, Mathf.Max(Mathf.Abs(a), Mathf.Abs(b)));
        private static bool EquivalentScaleFrames(Matrix4x4 a, Matrix4x4 b)
        {
            // This is a baseline eligibility test using full basis vectors, never a multiplier source.
            var equal = true;
            for (var i = 0; i < 3; i++)
            {
                Vector3 x = a.GetColumn(i), y = b.GetColumn(i);
                if ((x - y).magnitude > 1e-6f * Mathf.Max(float.Epsilon, Mathf.Max(x.magnitude, y.magnitude)))
                    equal = false;
            }
            if (equal)
                return true;
            if (UnityScalePlan.HasShear(a) || UnityScalePlan.HasShear(b))
                return false;
            Vector3 ax = a.GetColumn(0), ay = a.GetColumn(1), az = a.GetColumn(2), bx = b.GetColumn(0), by = b.GetColumn(1), bz = b.GetColumn(2);
            return ax.sqrMagnitude > 0 && bx.sqrMagnitude > 0
                && Near(ax.sqrMagnitude, ay.sqrMagnitude) && Near(ay.sqrMagnitude, az.sqrMagnitude)
                && Near(bx.sqrMagnitude, by.sqrMagnitude) && Near(by.sqrMagnitude, bz.sqrMagnitude)
                && Near(ax.sqrMagnitude, bx.sqrMagnitude);
        }

        internal sealed class Operation
        {
            private readonly Transform sourceHost, sourceTarget, shadowHost, shadowTarget;
            internal Operation(BoneProxyProjection projection, Transform shadowHost, Transform shadowTarget)
            {
                sourceHost = projection.Host;
                sourceTarget = projection.Target;
                this.shadowHost = shadowHost;
                this.shadowTarget = shadowTarget;
            }
            internal void Apply()
            {
                if (!sourceHost || !sourceTarget || !shadowHost || !shadowTarget)
                    return;
                // The full target matrix supplies pivot translation. Uniform factors themselves came
                // from the origin ledger and are already applied once to the shared local scales.
                var delta = shadowTarget.localToWorldMatrix * sourceTarget.worldToLocalMatrix;
                shadowHost.position = delta.MultiplyPoint3x4(sourceHost.position);
                shadowHost.rotation = shadowTarget.rotation * Quaternion.Inverse(sourceTarget.rotation) * sourceHost.rotation;
            }
        }
    }
}
