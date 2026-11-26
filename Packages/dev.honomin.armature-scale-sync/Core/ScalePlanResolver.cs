// MIT License. Copyright (c) 2026 Armature Scale Sync contributors.
using System;
using System.Collections.Generic;
using System.Linq;

namespace Honomin.ArmatureScaleSync.Core
{
    /// <summary>
    /// Tracks declaration identities through native and explicit hierarchies. An origin can reach a
    /// target through several routes, but contributes at most once. No scene objects are retained.
    /// </summary>
    public static class ScalePlanResolver
    {
        public static ScalePlan Resolve(IEnumerable<Bone> bones, IEnumerable<Declaration> declarations, IEnumerable<Binding> bindings)
        {
            var diagnostics = new List<Diagnostic>();
            void Warn(string code, string target, string message) => diagnostics.Add(new Diagnostic(code, Severity.Warning, target, message));
            void Error(string code, string target, string message) => diagnostics.Add(new Diagnostic(code, Severity.Error, target, message));
            var graph = new Dictionary<string, Bone>(StringComparer.Ordinal);
            foreach (var group in bones.GroupBy(b => b.Id, StringComparer.Ordinal))
            {
                if (string.IsNullOrEmpty(group.Key) || group.Count() != 1)
                {
                    Error("AMBIGUOUS", group.Key, "Bone identity must be unique.");
                    continue;
                }
                graph.Add(group.Key, group.Single());
            }
            var events = new Dictionary<string, Declaration>(StringComparer.Ordinal);
            foreach (var group in declarations.GroupBy(d => d.Id, StringComparer.Ordinal))
            {
                if (string.IsNullOrEmpty(group.Key) || group.Count() != 1)
                {
                    Error("AMBIGUOUS", group.Key, "Declaration identity must be unique.");
                    continue;
                }
                var d = group.Single();
                if (!graph.ContainsKey(d.Bone))
                {
                    Error("UNRESOLVED", d.Bone, "Declaration bone is outside this hierarchy.");
                    continue;
                }
                if (!d.Multiplier.IsInvertible || !d.Multiplier.IsFloatRepresentable)
                {
                    Error("INVALID_SCALE", d.Bone, "Multiplier must be finite, nonzero and representable as a Unity float.");
                    continue;
                }
                if (d.Advanced && !d.Multiplier.IsUniform)
                    Warn("NONUNIFORM", d.Bone, "Advanced nonuniform scale may introduce shear in rotated descendants.");
                if (d.Multiplier.IsNegative)
                    Warn("NEGATIVE", d.Bone, "Negative scale can reverse handedness and affect physics or culling.");
                events.Add(d.Id, d);
            }

            var candidates = new List<Binding>();
            foreach (var group in bindings.GroupBy(b => (b.Source, b.Target)))
            {
                if (group.Count() > 1)
                    Warn("DUPLICATE", group.Key.Target, "Several routes reference the same source and target; only the highest priority is used.");
                var b = group.OrderByDescending(x => Priority(x.Route)).First();
                if (!graph.ContainsKey(b.Source) || !graph.ContainsKey(b.Target))
                {
                    Warn("UNRESOLVED", b.Target, "Binding could not be resolved inside this avatar.");
                    continue;
                }
                candidates.Add(b);
            }
            var selected = new Dictionary<string, Binding>(StringComparer.Ordinal);
            foreach (var group in candidates.GroupBy(b => b.Target, StringComparer.Ordinal))
            {
                var priority = group.Max(b => Priority(b.Route));
                var best = group.Where(b => Priority(b.Route) == priority).ToArray();
                if (best.Length != 1)
                {
                    Warn("AMBIGUOUS", group.Key, "Several source bones map to this target. Set one explicit binding.");
                    continue;
                }
                if (best[0].Route == Route.Independent || best[0].Route == Route.ProjectedMerge || best[0].Route == Route.ProjectedProxy)
                    selected.Add(group.Key, best[0]);
            }

            // Dependency edges include parent inheritance. This catches cross-rig cycles and mapping
            // a descendant back onto its own ancestor without iterating until arbitrary convergence.
            var edges = graph.Keys.ToDictionary(id => id, _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
            foreach (var bone in graph.Values)
            {
                if (bone.Parent == null)
                    continue;
                if (graph.ContainsKey(bone.Parent))
                    edges[bone.Parent].Add(bone.Id);
                else
                    Warn("UNRESOLVED", bone.Id, "Parent is outside the supplied hierarchy.");
            }
            foreach (var b in selected.Values)
                if (b.Source != b.Target)
                    edges[b.Source].Add(b.Target);
            bool Reachable(string start, string goal)
            {
                var visited = new HashSet<string>(StringComparer.Ordinal);
                var stack = new Stack<string>();
                stack.Push(start);
                while (stack.Count > 0)
                {
                    var id = stack.Pop();
                    if (id == goal)
                        return true;
                    if (!visited.Add(id))
                        continue;
                    foreach (var child in edges[id])
                        stack.Push(child);
                }
                return false;
            }
            var cyclic = selected.Values.Where(b => b.Source != b.Target && Reachable(b.Target, b.Source)).ToArray();
            foreach (var b in cyclic)
            {
                selected.Remove(b.Target);
                Error("CYCLE", b.Target, "Sync dependencies contain a cycle; this binding was skipped.");
            }
            // Recreate the graph after rejecting cyclic mappings. Native hierarchy cycles are reported
            // by the topological traversal; unrelated hierarchies remain processable.
            foreach (var list in edges.Values)
                list.Clear();
            foreach (var b in graph.Values)
                if (b.Parent != null && graph.ContainsKey(b.Parent))
                    edges[b.Parent].Add(b.Id);
            foreach (var b in selected.Values)
                if (b.Source != b.Target)
                    edges[b.Source].Add(b.Target);
            var indegree = graph.Keys.ToDictionary(id => id, _ => 0, StringComparer.Ordinal);
            foreach (var list in edges.Values)
                foreach (var child in list)
                    indegree[child]++;
            var ready = new SortedSet<string>(indegree.Where(p => p.Value == 0).Select(p => p.Key), StringComparer.Ordinal);
            var ownEvents = events.Values.GroupBy(d => d.Bone).ToDictionary(g => g.Key, g => g.ToArray());
            var contributors = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var writes = new List<ScaleWrite>();
            while (ready.Count > 0)
            {
                var id = ready.Min;
                ready.Remove(id);
                var bone = graph[id];
                var inherited = bone.Parent != null && contributors.TryGetValue(bone.Parent, out var parentOrigins)
                    ? new HashSet<string>(parentOrigins, StringComparer.Ordinal) : new HashSet<string>(StringComparer.Ordinal);
                var local = new HashSet<string>(StringComparer.Ordinal);
                if (ownEvents.TryGetValue(id, out var own))
                    foreach (var d in own)
                        if (!inherited.Contains(d.Id))
                            local.Add(d.Id);
                if (selected.TryGetValue(id, out var binding) && binding.Source != id && contributors.TryGetValue(binding.Source, out var wanted))
                    foreach (var origin in wanted)
                        if (!inherited.Contains(origin))
                            local.Add(origin);
                var k = Scale3.One;
                foreach (var origin in local.OrderBy(x => x, StringComparer.Ordinal))
                    k = k * events[origin].Multiplier;
                if (!k.IsInvertible || !k.IsFloatRepresentable)
                    Error("OVERFLOW", id, "Combined multipliers overflow or underflow a Unity localScale; this local write was skipped.");
                else
                {
                    inherited.UnionWith(local);
                    if (!k.Equals(Scale3.One))
                        writes.Add(new ScaleWrite(id, k, local));
                }
                contributors.Add(id, inherited);
                foreach (var child in edges[id])
                    if (--indegree[child] == 0)
                        ready.Add(child);
            }
            foreach (var id in graph.Keys.Where(id => !contributors.ContainsKey(id)))
                Error("CYCLE", id, "Hierarchy contains a cycle or depends on one; this branch was skipped.");
            return new ScalePlan(writes, diagnostics);
        }

        private static int Priority(Route r)
        {
            switch (r)
            {
                case Route.Merged:
                    return 5;
                case Route.ProjectedMerge:
                    return 5;
                case Route.ProjectedProxy:
                    return 4;
                case Route.Native:
                    return 3;
                case Route.BoneProxy:
                    return 3;
                default:
                    return 1;
            }
        }
    }
}
