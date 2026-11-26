// MIT License. Copyright (c) 2026 Armature Scale Sync contributors.
using System.Collections.Generic;
using System.Linq;
using Honomin.ArmatureScaleSync.Core;
using nadena.dev.ndmf.preview;
using UnityEngine;

namespace Honomin.ArmatureScaleSync.Editor
{
    internal sealed class UnityScalePlan
    {
        public readonly Dictionary<Transform, Vector3> Writes = new Dictionary<Transform, Vector3>();
        public readonly List<Diagnostic> Diagnostics = new List<Diagnostic>();
        public readonly Dictionary<string, Transform> Objects = new Dictionary<string, Transform>();
        public readonly List<BoneProxyProjection> ProxyProjections = new List<BoneProxyProjection>();
        public bool RebaseAnimation = true;

        internal static UnityScalePlan Resolve(GameObject root, bool preview, ComputeContext context = null)
            => Resolve(root, ModularAvatarAdapter.Capture(root, preview, context), context);

        internal static UnityScalePlan Resolve(GameObject root, PlanSnapshot snapshot, ComputeContext context = null)
        {
            var result = new UnityScalePlan { RebaseAnimation = snapshot.RebaseAnimation };
            result.ProxyProjections.AddRange(snapshot.ProxyProjections);
            var transforms = context == null ? root.GetComponentsInChildren<Transform>(true) : context.GetComponentsInChildren<Transform>(root, true);
            // A nested avatar is a separate build/preview scope.
            foreach (var t in transforms)
                result.Objects[ModularAvatarAdapter.Id(t)] = t;
            var bones = transforms.Select(t => new Bone(ModularAvatarAdapter.Id(t), t == root.transform ? null : ModularAvatarAdapter.Id(t.parent))).ToArray();
            var declarations = snapshot.Settings.Where(s => s.Bone).Select(s => new Declaration(s.Origin, ModularAvatarAdapter.Id(s.Bone), s.Multiplier, s.Advanced));
            var bindings = snapshot.Bindings.Where(b => b.Source && b.Target).Select(b => new Binding(ModularAvatarAdapter.Id(b.Source), ModularAvatarAdapter.Id(b.Target), b.Route));
            var plan = ScalePlanResolver.Resolve(bones, declarations, bindings);
            result.Diagnostics.AddRange(snapshot.Diagnostics);
            result.Diagnostics.AddRange(plan.Diagnostics);
            foreach (var binding in snapshot.Bindings.Where(b => b.Source && b.Target && (b.Route == Route.Independent || b.Route == Route.ProjectedMerge)))
            {
                if (!snapshot.Settings.Any(s => s.Bone && binding.Source.IsChildOf(s.Bone) && !s.Multiplier.IsUniform))
                    continue;
                var a = binding.Source.localToWorldMatrix;
                var b = binding.Target.localToWorldMatrix;
                if (Enumerable.Range(0, 3).Any(i => Mathf.Abs(Vector3.Dot(((Vector3)a.GetColumn(i)).normalized, ((Vector3)b.GetColumn(i)).normalized)) < .99999f))
                    result.Diagnostics.Add(new Diagnostic("AXIS_MISMATCH", Severity.Warning, ModularAvatarAdapter.Id(binding.Target), "The mapped rigs have different axis frames under nonuniform scale. Local fit multipliers are preserved; exact world deformation parity requires processed-avatar verification."));
            }
            foreach (var write in plan.Writes)
            {
                var t = result.Objects[write.Target];
                var k = new Vector3((float)write.Multiplier.X, (float)write.Multiplier.Y, (float)write.Multiplier.Z);
                var final = Vector3.Scale(t.localScale, k);
                if (!new Scale3(final.x, final.y, final.z).IsInvertible)
                {
                    result.Diagnostics.Add(new Diagnostic("INVALID_RESULT", Severity.Error, write.Target, "Baseline scale multiplied by the declaration is zero or nonfinite. This bone was skipped."));
                    continue;
                }
                result.Writes.Add(t, k);
            }
            // Keep the full local matrix product. Only diagnose shear; never decompose a sheared world
            // transform into a Vector3 ratio, and never overwrite an outfit's authored fit pose.
            var matrices = new Dictionary<Transform, Matrix4x4>();
            var hasNonuniform = new Dictionary<Transform, bool>();
            foreach (var t in transforms.OrderBy(Depth))
            {
                context?.ObserveTransformPosition(t);
                result.Writes.TryGetValue(t, out var k);
                var changed = result.Writes.ContainsKey(t);
                var nonuniform = changed && (k.x != k.y || k.y != k.z);
                if (t.parent && hasNonuniform.TryGetValue(t.parent, out var parentNonuniform))
                    nonuniform |= parentNonuniform;
                hasNonuniform[t] = nonuniform;
                var local = Matrix4x4.TRS(t.localPosition, t.localRotation, changed ? Vector3.Scale(t.localScale, k) : t.localScale);
                matrices[t] = t.parent && matrices.TryGetValue(t.parent, out var parent) ? parent * local : local;
                if (nonuniform && HasShear(matrices[t]))
                    result.Diagnostics.Add(new Diagnostic("SHEAR", Severity.Warning, ModularAvatarAdapter.Id(t), "Nonuniform hierarchy scale creates shear at a rotated descendant. Verify processed vertices, skinning and physics."));
            }
            foreach (var t in result.Writes.Keys)
            {
                // Detection through Unity's public component model also covers VRC PhysBone/Contact types
                // without linking editor adapters to a particular SDK DLL layout.
                var affected = t.GetComponentsInChildren<Component>(true).Where(c => c && (c is UnityEngine.Animations.IConstraint || IsDynamics(c))).ToArray();
                foreach (var component in affected)
                    result.Diagnostics.Add(new Diagnostic("DYNAMICS", Severity.Warning, ModularAvatarAdapter.Id(component.transform), "Hierarchy scaling affects a PhysBone, collider, contact or constraint. Validate its behavior on the processed avatar."));
            }
            var unique = result.Diagnostics.GroupBy(d => (d.Code, d.Target, d.Severity)).Select(g => g.First()).ToArray();
            result.Diagnostics.Clear();
            result.Diagnostics.AddRange(unique);
            return result;
        }

        private static bool IsDynamics(Component c)
        {
            var ns = c.GetType().Namespace ?? "";
            return ns.StartsWith("VRC.SDK3.Dynamics.", System.StringComparison.Ordinal) || c is Collider;
        }
        internal static int Depth(Transform t)
        {
            var d = 0;
            for (var p = t.parent; p; p = p.parent)
                d++;
            return d;
        }
        internal static bool HasShear(Matrix4x4 m)
        {
            Vector3 a = m.GetColumn(0), b = m.GetColumn(1), c = m.GetColumn(2);
            const float tolerance = 1e-5f;
            return Mathf.Abs(Vector3.Dot(a.normalized, b.normalized)) > tolerance
                || Mathf.Abs(Vector3.Dot(a.normalized, c.normalized)) > tolerance
                || Mathf.Abs(Vector3.Dot(b.normalized, c.normalized)) > tolerance;
        }
    }
}
