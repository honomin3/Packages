// MIT License. Copyright (c) 2026 Armature Scale Sync contributors.
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using nadena.dev.ndmf.preview;
using UnityEngine;

namespace Honomin.ArmatureScaleSync.Editor
{
    internal static class PreviewSupport
    {
        internal const string Requirements = "Shared preview requires the supported NDMF 1.15 and MA 1.19 public API families. See the package README for the exact verified stable profile.";
#if ASS_SHARED_SHADOWS && ASS_MA_SHARED_SHADOWS
        internal const bool SharedAvailable=true;
#else
        internal const bool SharedAvailable = false;
#endif
    }

    internal sealed class ScalePreviewFilter : IRenderFilter
    {
        internal static readonly TogglablePreviewNode Enable = TogglablePreviewNode.Create(() => "Armature Scale Sync", "dev.honomin.armature-scale-sync/preview", true);
        public IEnumerable<TogglablePreviewNode> GetPreviewControlNodes()
        {
            yield return Enable;
        }
        public bool IsEnabled(ComputeContext context) => PreviewSupport.SharedAvailable && context.Observe(Enable.IsEnabled);
        public ImmutableList<RenderGroup> GetTargetGroups(ComputeContext context)
        {
#if ASS_SHARED_SHADOWS && ASS_MA_SHARED_SHADOWS
            var groups=new List<RenderGroup>();
            foreach(var root in context.GetAvatarRoots())
            {
                if(!root || (root.transform.parent && context.GetAvatarRoot(root.transform.parent.gameObject))) continue;
                var settings=context.GetComponentsInChildren<HierarchicalBoneScale>(root,true);
                if(settings.Length==0) continue;
                var sync=context.GetComponent<ArmatureScaleSync>(root);
                if(sync && !context.Observe(sync,s=>s.PreviewEnabled)) continue;
                var renderers=context.GetComponentsInChildren<Renderer>(root,true).Where(r=>r is MeshRenderer || r is SkinnedMeshRenderer);
                groups.Add(RenderGroup.For(renderers).WithData(root,(a,b)=>a==b));
            }
            return groups.ToImmutableList();
#else
            return ImmutableList<RenderGroup>.Empty;
#endif
        }

        public Task<IRenderFilterNode> Instantiate(RenderGroup group, IEnumerable<(Renderer, Renderer)> proxyPairs, ComputeContext context)
        {
#if ASS_SHARED_SHADOWS && ASS_MA_SHARED_SHADOWS
            var root=group.GetData<GameObject>();
            var plan=UnityScalePlan.Resolve(root,true,context);
            var manager=PreviewContext.Instance.ShadowBoneManager;
            // Borrow every affected hierarchy reference from NDMF. MA Scale Adjuster's mesh-only child
            // proxies remain children of these same shared bones; ASS does not recreate them.
            foreach(var transform in plan.Objects.Values)
            {
                context.Observe(transform.gameObject,g=>g.name);
                manager.GetBone(transform);
            }
            var writes=plan.Writes.Select(w=>(manager.GetBone(w.Key),w.Value)).ToArray();
            var proxies=plan.ProxyProjections.Select(p=>new BoneProxyDeltaPreview.Operation(p,manager.GetBone(p.Host),manager.GetBone(p.Target))).ToArray();
            return Task.FromResult<IRenderFilterNode>(new Node(writes,proxies));
#else
            return Task.FromResult<IRenderFilterNode>(new EmptyNode());
#endif
        }

        private sealed class EmptyNode : IRenderFilterNode
        {
            public RenderAspects WhatChanged => 0;
            public void Dispose()
            {
            }
        }

        internal sealed class Node : IRenderFilterNode
        {
            private readonly (Transform Bone, Vector3 Factor)[] writes;
            private readonly BoneProxyDeltaPreview.Operation[] proxies;
            internal Node((Transform, Vector3)[] writes, BoneProxyDeltaPreview.Operation[] proxies)
            {
                this.writes = writes;
                this.proxies = proxies;
            }
            public RenderAspects WhatChanged => RenderAspects.Shapes;
            public void OnFrameGroup()
            {
                // NDMF synchronizes shared source poses at frame start. Compose with the current
                // stage pose once per group, so prior public preview stages remain intact.
                foreach (var write in writes)
                    if (write.Bone)
                        write.Bone.localScale = Vector3.Scale(write.Bone.localScale, write.Factor);
                foreach (var proxy in proxies)
                    proxy.Apply();
            }
            public void Dispose()
            { /* NDMF owns the borrowed shadows and renderer proxies. */
            }
        }
    }
}
