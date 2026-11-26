// MIT License. Copyright (c) 2026 Armature Scale Sync contributors.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Honomin.ArmatureScaleSync.Editor;
using nadena.dev.modular_avatar.core;
using nadena.dev.ndmf.preview;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Honomin.ArmatureScaleSync.Tests
{
    internal sealed class PreviewCapture : IRenderFilter, IDisposable
    {
        private readonly GameObject root;
        private readonly PreviewSession session;
        private readonly Camera camera;
        private readonly bool ownsCamera;
        private readonly RenderTexture priorTarget;
        private readonly RenderTexture texture;
        public readonly Dictionary<string, Vector3[]> Vertices = new Dictionary<string, Vector3[]>();
        public readonly Dictionary<string, string> Poses = new Dictionary<string, string>();
        public int Calls;
        public int PreCullCalls, GroupCalls;
        private void CountPreCull(Camera value) { if (value == camera) PreCullCalls++; }
        public PreviewCapture(SyntheticAvatar fixture) : this(fixture.Root) { }
        public PreviewCapture(GameObject root, Camera existingCamera = null)
        {
            if (!PreviewSupport.SharedAvailable)
                Assert.Ignore(PreviewSupport.Requirements);
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null || PreviewSession.Current == null)
                Assert.Ignore("BLOCKED_ENV: real NDMF Scene preview and a graphics device are required. Enable NDMF Preview in the local Editor.");
            this.root = root;
            session = PreviewSession.Current.Fork("ASS acceptance capture");
            session.AddMutator(new SequencePoint(), this);
            // NDMF excludes parented render-to-texture cameras. Use a real scene
            // root camera so the public camera override enters the live pipeline.
            ownsCamera = !existingCamera;
            camera = existingCamera ? existingCamera : new GameObject("ASS TestCamera").AddComponent<Camera>();
            if (ownsCamera)
            {
                camera.enabled = false;
                camera.transform.position = new Vector3(0, .5f, -3);
                camera.transform.LookAt(new Vector3(0, .5f, 0));
            }
            priorTarget = camera.targetTexture;
            texture = new RenderTexture(32, 32, 16);
            camera.targetTexture = texture;
            session.OverrideCamera(camera);
            Camera.onPreCull += CountPreCull;
        }
        public ImmutableList<RenderGroup> GetTargetGroups(ComputeContext ctx)
        {
            GroupCalls++;
            return ImmutableList.Create(RenderGroup.For(ctx.GetComponentsInChildren<SkinnedMeshRenderer>(root, true)));
        }
        public Task<IRenderFilterNode> Instantiate(RenderGroup group, IEnumerable<(Renderer, Renderer)> proxyPairs, ComputeContext ctx) => Task.FromResult<IRenderFilterNode>(new CaptureNode(this));
        private sealed class CaptureNode : IRenderFilterNode
        {
            private readonly PreviewCapture owner;
            public CaptureNode(PreviewCapture owner)
            {
                this.owner = owner;
            }
            public RenderAspects WhatChanged => 0;
            public void OnFrame(Renderer original, Renderer proxy)
            {
                if (proxy is SkinnedMeshRenderer smr)
                {
                    owner.Vertices[original.name] = SyntheticAvatar.Vertices(smr);
                    owner.Poses[original.name] = "renderer=" + smr.localToWorldMatrix.ToString("R") + "; bones=" + string.Join(";", smr.bones.Select(b => b.name + "=" + b.localToWorldMatrix.ToString("R")));
                    owner.Calls++;
                }
            }
            public void Dispose()
            {
            }
        }
        public IEnumerator WaitFor(Func<bool> condition)
        {
            for (var i = 0; i < 180; i++)
            {
                ComputeContext.FlushInvalidates();
                camera.Render();
                if (condition())
                    yield break;
                yield return null;
            }
            Assert.Fail("NDMF preview did not produce the expected samples within 180 editor frames. preCull=" + PreCullCalls + "; groups=" + GroupCalls + "; captures=" + Calls);
        }
        public void Dispose()
        {
            Camera.onPreCull -= CountPreCull;
            PreviewSession.ClearCameraOverride(camera);
            session.Dispose();
            camera.targetTexture = priorTarget;
            if (ownsCamera) Object.DestroyImmediate(camera.gameObject);
            texture.Release();
            Object.DestroyImmediate(texture);
        }
    }

    public sealed class PreviewAcceptanceTests
    {
        [UnityTest, Category("PREVIEW_005")]
        public IEnumerator UniformProxyDeltaUsesMAPlacementAndMatchesTheRealBuild()
        {
            using (var f = new SyntheticAvatar(merge: false))
            {
                var proxy = f.OutfitRig.parent.gameObject.AddComponent<ModularAvatarBoneProxy>();
                proxy.target = f.Hips;
                proxy.matchScale = true;
                proxy.attachmentMode = BoneProxyAttachmentMode.AsChildKeepWorldPose;
                f.OutfitRig.localPosition = Vector3.right * .2f;
                f.OutfitHips.localScale = new Vector3(.95f, 1, 1);
                f.Scale(f.Root.transform, 1.1f);
                f.Scale(f.Hips, 1.2f);
                f.Root.GetComponent<ArmatureScaleSync>().AutoSync = false;
                var plan = UnityScalePlan.Resolve(f.Root, true);
                Assert.That(plan.Diagnostics.Any(d => d.Code == "PREVIEW_PROXY"), Is.False, "The ordinary uniform fixture must have a compatible baseline pose");
                var processed = f.Build();
                using (var capture = new PreviewCapture(f))
                {
                    yield return capture.WaitFor(() => capture.Vertices.ContainsKey("Outfit"));
                    TestContext.WriteLine("Processed vertices=" + string.Join(";", SyntheticAvatar.Vertices(SyntheticAvatar.Renderer(processed, "Outfit")).Select(v => v.ToString("R"))));
                    TestContext.WriteLine("Preview vertices=" + string.Join(";", capture.Vertices["Outfit"].Select(v => v.ToString("R"))));
                    TestContext.WriteLine("Preview poses=" + capture.Poses["Outfit"]);
                    SyntheticAvatar.Dump("PREVIEW-005-processed", processed);
                    SyntheticAvatar.AssertVertices(SyntheticAvatar.Vertices(SyntheticAvatar.Renderer(processed, "Outfit")), capture.Vertices["Outfit"]);
                }
                Assert.That(f.Hips.localScale, Is.EqualTo(Vector3.one));
                Assert.That(proxy.matchScale, Is.True);
            }
        }
        [UnityTest, Category("P0_2"), Category("PREVIEW_003"), Category("PREVIEW_004")]
        public IEnumerator LiveMAPreviewComposesWithScaleAdjusterAndMatchesProcessedVertices()
        {
            return CompareSharedPreview(false);
        }
        [UnityTest, Category("PREVIEW_004")]
        public IEnumerator AdvancedAxisAlignedPreviewAlsoMatchesProcessedVertices()
        {
            return CompareSharedPreview(true);
        }
        private IEnumerator CompareSharedPreview(bool advanced)
        {
            using (var f = new SyntheticAvatar())
            {
                var h = f.Scale(f.Hips, 1.2f);
                if (advanced)
                {
                    h.Mode = ScaleMode.Advanced;
                    h.VectorMultiplier = new Vector3(1.2f, .9f, 1.1f);
                }
                f.Hips.gameObject.AddComponent<ModularAvatarScaleAdjuster>().Scale = Vector3.one * 1.05f;
                var source = f.Root.GetComponentsInChildren<Transform>(true).ToDictionary(t => t, t => EditorJsonUtility.ToJson(t));
                var meshJson = EditorJsonUtility.ToJson(f.Body.sharedMesh);
                var processed = f.Build();
                var expected = SyntheticAvatar.Vertices(SyntheticAvatar.Renderer(processed, "Body"));
                using (var capture = new PreviewCapture(f))
                {
                    yield return capture.WaitFor(() => capture.Vertices.ContainsKey("Body") && capture.Vertices.ContainsKey("Outfit"));
                    SyntheticAvatar.AssertVertices(expected, capture.Vertices["Body"]);
                    SyntheticAvatar.AssertVertices(SyntheticAvatar.Vertices(SyntheticAvatar.Renderer(processed, "Outfit")), capture.Vertices["Outfit"]);
                }
                foreach (var pair in source)
                    Assert.That(EditorJsonUtility.ToJson(pair.Key), Is.EqualTo(pair.Value));
                Assert.That(EditorJsonUtility.ToJson(f.Body.sharedMesh), Is.EqualTo(meshJson));
                Assert.That(h.UniformMultiplier, Is.EqualTo(1.2f));
                SyntheticAvatar.Dump("PREVIEW-004-advanced-" + advanced, processed);
            }
        }
        [UnityTest, Category("PREVIEW_001"), Category("PREVIEW_002")]
        public IEnumerator SettingsUndoRedoAndDisableInvalidateLivePreviewWithoutSourceScaleWrites()
        {
            using (var f = new SyntheticAvatar())
            {
                var h = f.Scale(f.Hips, 1);
                var sync = f.Root.GetComponent<ArmatureScaleSync>();
                using (var capture = new PreviewCapture(f))
                {
                    yield return capture.WaitFor(() => capture.Vertices.ContainsKey("Body"));
                    var baseline = capture.Vertices["Body"].ToArray();
                    var serialized = new SerializedObject(h);
                    serialized.FindProperty("UniformMultiplier").floatValue = 1.2f;
                    serialized.ApplyModifiedProperties();
                    var scaled = baseline.Select(v => f.Hips.position + (v - f.Hips.position) * 1.2f).ToArray();
                    yield return capture.WaitFor(() => Matches(scaled, capture.Vertices["Body"]));
                    Undo.PerformUndo();
                    yield return capture.WaitFor(() => Matches(baseline, capture.Vertices["Body"]));
                    Undo.PerformRedo();
                    yield return capture.WaitFor(() => Matches(scaled, capture.Vertices["Body"]));
                    var rootSerialized = new SerializedObject(sync);
                    rootSerialized.FindProperty("PreviewEnabled").boolValue = false;
                    rootSerialized.ApplyModifiedProperties();
                    yield return capture.WaitFor(() => Matches(baseline, capture.Vertices["Body"]));
                    Assert.That(f.Hips.localScale, Is.EqualTo(Vector3.one));
                }
            }
        }
        [Test, Category("PERF_001")]
        public void FrameApplicationIsBoundedAndDoesNotAccumulate()
        {
#if ASS_SHARED_SHADOWS && ASS_MA_SHARED_SHADOWS
            using(var f=new SyntheticAvatar())
            using(var shadows=new ShadowBoneTestFixture())
            using(new PreviewContext {ShadowBoneManager=shadows.Handle}.Activate())
            {
                f.Scale(f.Hips,1.2f);
                var proxy=Object.Instantiate(f.Body.gameObject).GetComponent<SkinnedMeshRenderer>();
                try
                {
                    var map=new Dictionary<Renderer,Renderer>{{f.Body,proxy}};
                    var filter=new ScalePreviewFilter();
                    var node=shadows.ExecuteInStageSync(map,()=>filter.Instantiate(RenderGroup.For(f.Body).WithData(f.Root,(a,b)=>a==b),new[]{((Renderer)f.Body,(Renderer)proxy)},new ComputeContext("ASS bounded-frame test")).Result);
                    using(node)
                        for(var frame=0;frame<200;frame++)
                        {
                            shadows.SyncPoses(); node.OnFrameGroup();
                            Assert.That(shadows.Handle.GetBone(f.Hips).localScale.x,Is.EqualTo(1.2f).Within(1e-6));
                        }
                }
                finally { Object.DestroyImmediate(proxy.gameObject); }
            }
#else
            Assert.Ignore("BLOCKED_ENV: shared shadow API candidate required.");
#endif
        }
        [UnityTest, Category("PERF_002")]
        public IEnumerator RepeatedLivePreviewSessionsReleaseTheirProxiesAndShadows()
        {
            int Count() => Resources.FindObjectsOfTypeAll<Transform>().Count(t => t && t.root.name == "SyntheticAvatar");
            var before = Count();
            for (var i = 0; i < 8; i++)
            {
                using (var f = new SyntheticAvatar())
                {
                    f.Scale(f.Hips, 1.2f);
                    using (var capture = new PreviewCapture(f))
                        yield return capture.WaitFor(() => capture.Vertices.ContainsKey("Body"));
                }
                yield return null;
            }
            // NDMF releases pipelines asynchronously. Thirty Test Runner frames
            // can finish before a queued disposal runs; bound wall time instead.
            var deadline = EditorApplication.timeSinceStartup + 5;
            while (Count() > before && EditorApplication.timeSinceStartup < deadline)
            {
                ComputeContext.FlushInvalidates();
                yield return null;
            }
            Assert.That(Count(), Is.LessThanOrEqualTo(before), "Preview resources survive disposed sessions and source avatars");
        }
        private static bool Matches(Vector3[] a, Vector3[] b) => a.Length == b.Length && a.Zip(b, (x, y) => Vector3.Distance(x, y)).All(d => d <= 1e-4f);
    }
}
