// MIT License. Copyright (c) 2026 Armature Scale Sync contributors.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Honomin.ArmatureScaleSync.Editor
{
    // Unity 2022's native picker excludes objects in an unrelated Preview Scene.
    // Add hits from the already processed geometry through its public fallback.
    // This does not create bones, evaluate skinning, or modify authoring meshes.
    internal sealed class ProcessedPreviewPicking
    {
        private sealed class Surface
        {
            internal Renderer Renderer;
            internal GameObject Original;
            internal Vector3[] Vertices;
            internal int[][] Submeshes;
            internal Bounds Bounds;
        }
        private readonly List<Surface> surfaces = new List<Surface>();
        internal Bounds[] CachedBounds => surfaces.Select(s => s.Bounds).ToArray();
        internal Vector3[][] CachedVertices => surfaces.Select(s => s.Vertices).ToArray();

        internal ProcessedPreviewPicking(ProcessedPreviewSnapshot snapshot)
        {
            foreach (var renderer in snapshot.Root.GetComponentsInChildren<Renderer>(true))
            {
                var original = snapshot.SourceFor(renderer);
                if (!original) continue;
                Mesh temporary = null;
                try
                {
                    Mesh mesh;
                    if (renderer is SkinnedMeshRenderer skinned)
                    {
                        temporary = new Mesh { hideFlags = HideFlags.HideAndDontSave };
                        skinned.BakeMesh(temporary);
                        mesh = temporary;
                    }
                    else mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                    if (!mesh || !mesh.isReadable) continue;
                    var vertices = mesh.vertices.Select(renderer.localToWorldMatrix.MultiplyPoint3x4).ToArray();
                    if (vertices.Length == 0) continue;
                    var bounds = new Bounds(vertices[0], Vector3.zero);
                    foreach (var vertex in vertices) bounds.Encapsulate(vertex);
                    surfaces.Add(new Surface
                    {
                        Renderer = renderer, Original = original, Vertices = vertices, Bounds = bounds,
                        Submeshes = Enumerable.Range(0, mesh.subMeshCount).Select(i =>
                            mesh.GetTopology(i) == MeshTopology.Triangles ? mesh.GetTriangles(i) : Array.Empty<int>()).ToArray()
                    });
                }
                finally { if (temporary) Object.DestroyImmediate(temporary); }
            }
        }

        internal GameObject Pick(Camera camera, int layers, Vector2 screenPixel, GameObject[] ignore, GameObject[] filter, out int materialIndex)
        {
            materialIndex = -1;
            var ray = camera.ScreenPointToRay(screenPixel);
            var distance = float.PositiveInfinity;
            GameObject picked = null;
            foreach (var surface in surfaces)
            {
                var original = surface.Original;
                var renderer = surface.Renderer;
                if (!original || !renderer || !renderer.enabled || renderer.forceRenderingOff || !renderer.gameObject.activeInHierarchy
                    || (layers & (1 << renderer.gameObject.layer)) == 0
                    || (ignore != null && ignore.Contains(original)) || (filter != null && !filter.Contains(original))
                    || SceneVisibilityManager.instance.IsHidden(original) || SceneVisibilityManager.instance.IsPickingDisabled(original)
                    || !surface.Bounds.IntersectRay(ray, out var near) || near > distance) continue;
                var materials = renderer.sharedMaterials;
                for (var submesh = 0; submesh < surface.Submeshes.Length; submesh++)
                {
                    var indices = surface.Submeshes[submesh];
                    var material = submesh < materials.Length ? materials[submesh] : null;
                    var cull = material && material.HasProperty("_Cull") ? (CullMode)material.GetInt("_Cull") : CullMode.Back;
                    for (var i = 0; i < indices.Length; i += 3)
                    {
                        var a = surface.Vertices[indices[i]];
                        var edge1 = surface.Vertices[indices[i + 1]] - a;
                        var edge2 = surface.Vertices[indices[i + 2]] - a;
                        var cross = Vector3.Cross(ray.direction, edge2);
                        var determinant = Vector3.Dot(edge1, cross);
                        if (Mathf.Abs(determinant) < 1e-8f) continue;
                        if ((cull == CullMode.Back && determinant < 0) || (cull == CullMode.Front && determinant > 0)) continue;
                        var fromA = ray.origin - a;
                        var u = Vector3.Dot(fromA, cross) / determinant;
                        if (u < 0 || u > 1) continue;
                        var q = Vector3.Cross(fromA, edge1);
                        var v = Vector3.Dot(ray.direction, q) / determinant;
                        if (v < 0 || u + v > 1) continue;
                        var hit = Vector3.Dot(edge2, q) / determinant;
                        if (hit < 0 || hit >= distance) continue;
                        distance = hit;
                        picked = original;
                        materialIndex = submesh;
                    }
                }
            }
            return picked;
        }
    }
}
