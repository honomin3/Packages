// MIT License. Copyright (c) 2026 Armature Scale Sync contributors.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using nadena.dev.modular_avatar.core;
using nadena.dev.ndmf;
using nadena.dev.ndmf.platform;
using nadena.dev.ndmf.preview;
using nadena.dev.ndmf.runtime.components;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;
using Object = UnityEngine.Object;

namespace Honomin.ArmatureScaleSync.Tests
{
    // All geometry, weights, rigs and animations are generated here. No external avatar is required.
    internal sealed class SyntheticAvatar : IDisposable
    {
        public readonly GameObject Root;
        public readonly Transform Armature, Hips, Chest, OutfitRig, OutfitHips, OutfitChest;
        public readonly SkinnedMeshRenderer Body, Outfit;
        public readonly List<GameObject> Builds = new List<GameObject>();
        public BuildContext LastBuildContext
        {
            get; private set;
        }
        public readonly Scene Scene;
        private readonly List<Object> owned = new List<Object>();
        public T Own<T>(T value) where T : Object { owned.Add(value); return value; }

        public SyntheticAvatar(bool retained = false, bool merge = true)
        {
            // The EditMode runner owns its temporary scene. It can be untitled, so
            // additive NewScene is forbidden even when the user's scene is saved.
            // Own and dispose only our fixture objects, leaving runner scenes alone.
            Scene = SceneManager.GetActiveScene();
            Root = new GameObject("SyntheticAvatar");
            SceneManager.MoveGameObjectToScene(Root, Scene);
            Root.AddComponent<NDMFAvatarRoot>();
            Root.AddComponent<Animator>();
            Root.AddComponent<ArmatureScaleSync>();
            Armature = Child(Root.transform, "Armature");
            Hips = Child(Armature, "Hips", new Vector3(0, .5f, 0));
            Chest = Child(Hips, "Chest", new Vector3(0, .3f, 0));
            OutfitRig = Child(Child(Root.transform, "Clothing"), "Armature");
            OutfitHips = Child(OutfitRig, "Hips", Hips.localPosition);
            OutfitChest = Child(OutfitHips, "Chest", Chest.localPosition);
            Body = Skin("Body", Hips, Chest);
            var outfitBone = retained ? Child(OutfitHips, "Hem", new Vector3(.1f, -.15f, .05f)) : OutfitHips;
            Outfit = Skin("Outfit", outfitBone, OutfitChest);
            if (merge)
            {
                var ma = OutfitRig.gameObject.AddComponent<ModularAvatarMergeArmature>();
                ma.mergeTarget.Set(Armature.gameObject);
                ma.mangleNames = false;
            }
        }

        public static SyntheticAvatar Humanoid()
        {
            var f = new SyntheticAvatar();
            try
            {
                foreach (var bone in f.OutfitRig.GetComponentsInChildren<Transform>(true))
                    bone.name = "Outfit_" + bone.name;
                f.OutfitRig.GetComponent<ModularAvatarMergeArmature>().prefix = "Outfit_";
                f.Hips.localPosition = new Vector3(0, .9f, 0);
                var map = new Dictionary<HumanBodyBones, Transform> { { HumanBodyBones.Hips, f.Hips } };
                Transform Add(HumanBodyBones id, Transform parent, Vector3 pos)
                {
                    var t = Child(parent, id.ToString(), pos);
                    map.Add(id, t);
                    return t;
                }
                var spine = Add(HumanBodyBones.Spine, f.Hips, new Vector3(0, .12f, 0));
                f.Chest.SetParent(spine, false);
                f.Chest.localPosition = new Vector3(0, .18f, 0);
                map.Add(HumanBodyBones.Chest, f.Chest);
                var neck = Add(HumanBodyBones.Neck, f.Chest, new Vector3(0, .16f, 0));
                Add(HumanBodyBones.Head, neck, new Vector3(0, .12f, 0));
                for (var side = 0; side < 2; side++)
                {
                    var left = side == 0;
                    var x = left ? -1f : 1f;
                    var upperLeg = Add(left ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg, f.Hips, new Vector3(x * .08f, -.05f, 0));
                    var lowerLeg = Add(left ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg, upperLeg, new Vector3(0, -.4f, 0));
                    var foot = Add(left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot, lowerLeg, new Vector3(0, -.4f, .04f));
                    Add(left ? HumanBodyBones.LeftToes : HumanBodyBones.RightToes, foot, new Vector3(0, 0, .12f));
                    var shoulder = Add(left ? HumanBodyBones.LeftShoulder : HumanBodyBones.RightShoulder, f.Chest, new Vector3(x * .1f, .1f, 0));
                    var upperArm = Add(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm, shoulder, new Vector3(x * .12f, 0, 0));
                    var lowerArm = Add(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm, upperArm, new Vector3(x * .23f, 0, 0));
                    Add(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand, lowerArm, new Vector3(x * .18f, 0, 0));
                }
                var description = new HumanDescription
                {
                    human = map.Select(p => new HumanBone { boneName = p.Value.name, humanName = HumanTrait.BoneName[(int)p.Key], limit = new HumanLimit { useDefaultValues = true } }).ToArray(),
                    skeleton = new[] { f.Root.transform }.Concat(f.Armature.GetComponentsInChildren<Transform>(true)).Select(t => new SkeletonBone { name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale }).ToArray(),
                    armStretch = .05f,
                    legStretch = .05f,
                    upperArmTwist = .5f,
                    lowerArmTwist = .5f,
                    upperLegTwist = .5f,
                    lowerLegTwist = .5f,
                    feetSpacing = 0
                };
                var avatar = AvatarBuilder.BuildHumanAvatar(f.Root, description);
                f.owned.Add(avatar);
                Assert.That(avatar.isValid && avatar.isHuman, Is.True, "The generated humanoid must be valid before testing SDK/MA behavior");
                f.Root.GetComponent<Animator>().avatar = avatar;
                f.Body.sharedMesh.bindposes = f.Body.bones.Select(b => b.worldToLocalMatrix * f.Body.transform.localToWorldMatrix).ToArray();
                return f;
            }
            catch { f.Dispose(); throw; }
        }

        public static Transform Child(Transform parent, string name, Vector3 position = default)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = position;
            return t;
        }

        public SkinnedMeshRenderer Skin(string name, params Transform[] bones)
        {
            var go = Child(Root.transform, name).gameObject;
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            var mesh = new Mesh { name = "SyntheticTriangle" };
            owned.Add(mesh);
            mesh.vertices = new[] { new Vector3(.12f, .55f, 0), new Vector3(.2f, .65f, 0), new Vector3(.1f, .7f, .02f) };
            mesh.triangles = new[] { 0, 1, 2 };
            mesh.boneWeights = Enumerable.Range(0, 3).Select(i => new BoneWeight { boneIndex0 = i % bones.Length, weight0 = 1 }).ToArray();
            mesh.bindposes = bones.Select(bone => bone.worldToLocalMatrix * go.transform.localToWorldMatrix).ToArray();
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
            smr.sharedMesh = mesh;
            smr.bones = bones;
            smr.rootBone = bones[0];
            var shader = Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
            if (shader)
            {
                var material = new Material(shader) { name = "SyntheticMaterial", color = new Color(.6f, .7f, 1) };
                owned.Add(material);
                smr.sharedMaterial = material;
            }
            smr.updateWhenOffscreen = true;
            return smr;
        }

        public HierarchicalBoneScale Scale(Transform bone, float k)
        {
            var h = bone.gameObject.AddComponent<HierarchicalBoneScale>();
            h.UniformMultiplier = k;
            return h;
        }

        public GameObject Build(bool withoutScale = false, INDMFPlatformProvider platform = null)
        {
            var clone = Object.Instantiate(Root);
            Builds.Add(clone);
            if (withoutScale)
            {
                foreach (var h in clone.GetComponentsInChildren<HierarchicalBoneScale>(true))
                    Object.DestroyImmediate(h);
                foreach (var s in clone.GetComponentsInChildren<ArmatureScaleSync>(true))
                    Object.DestroyImmediate(s);
            }
            BuildContext context;
            // Default generated assets are keyed by avatar name and the next build
            // deletes the baseline's meshes. Public null-saver scope keeps both
            // actual NDMF outputs alive for vertex/bindpose comparisons.
            using (new OverrideTemporaryDirectoryScope(null))
                context = AvatarProcessor.ProcessAvatar(clone, platform ?? GenericPlatform.Instance);
            LastBuildContext = context;
            Assert.That(context.Successful, Is.True, "NDMF reported errors; inspect the Unity log");
            return clone;
        }

        public static SkinnedMeshRenderer Renderer(GameObject avatar, string name) => avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(r => r.name == name);
        public static Vector3[] Vertices(SkinnedMeshRenderer smr)
        {
            var mesh = new Mesh();
            try
            {
                smr.BakeMesh(mesh, true);
                return mesh.vertices.Select(v => smr.transform.TransformPoint(v)).ToArray();
            }
            finally { Object.DestroyImmediate(mesh); }
        }
        public static void AssertVertices(Vector3[] expected, Vector3[] actual, float tolerance = 1e-4f)
        {
            Assert.That(actual.Length, Is.EqualTo(expected.Length));
            for (var i = 0; i < expected.Length; i++)
                Assert.That(Vector3.Distance(expected[i], actual[i]), Is.LessThanOrEqualTo(tolerance), "vertex " + i);
            TestContext.WriteLine("Measured maximum vertex delta=" + expected.Zip(actual, Vector3.Distance).DefaultIfEmpty(0).Max().ToString("R", System.Globalization.CultureInfo.InvariantCulture) + " m; tolerance=" + tolerance.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + " m");
        }
        public static void Dump(string id, GameObject avatar)
        {
            var directory = Environment.GetEnvironmentVariable("ASS_TEST_ARTIFACTS") ?? TestContext.CurrentContext.WorkDirectory;
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, id + ".txt");
            var lines = new List<string>();
            foreach (var r in avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                lines.Add(r.name + " bones=" + string.Join(",", r.bones.Select(b => b ? AnimationUtility.CalculateTransformPath(b, avatar.transform) : "null")));
                lines.Add("bindposes=" + string.Join(";", r.sharedMesh.bindposes.Select(b => b.ToString("R"))));
                lines.Add("baked=" + string.Join(";", Vertices(r).Select(v => v.ToString("R"))));
            }
            File.WriteAllLines(path, lines);
            // Unity Test Framework 1.1.33's NUnit does not expose AddTestAttachment.
            // Keep the file beside the XML/log and expose its name in NUnit output.
            TestContext.WriteLine("Fixture artifact: " + Path.GetFileName(path));
        }

        public AnimationClip Animate(Transform bone)
        {
            var clip = new AnimationClip { name = "SyntheticScale" };
            owned.Add(clip);
            var path = AnimationUtility.CalculateTransformPath(bone, Root.transform);
            foreach (var axis in new[] { "x", "y", "z" })
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalScale." + axis), AnimationCurve.Linear(0, 1, 1, 2));
            var sm = new AnimatorStateMachine { name = "SyntheticStates" };
            owned.Add(sm);
            var state = sm.AddState("Scale");
            owned.Add(state);
            state.motion = clip;
            sm.defaultState = state;
            var controller = new AnimatorController { name = "SyntheticController" };
            owned.Add(controller);
            controller.AddLayer(new AnimatorControllerLayer { name = "Base", stateMachine = sm, defaultWeight = 1 });
            Root.GetComponent<Animator>().runtimeAnimatorController = controller;
            return clip;
        }

        public void Dispose()
        {
            foreach (var build in Builds)
                if (build)
                    Object.DestroyImmediate(build);
            if (Root)
                Object.DestroyImmediate(Root);
            foreach (var o in owned)
                if (o && !EditorUtility.IsPersistent(o))
                    Object.DestroyImmediate(o);
            // Direct synthetic creation/destruction can run several times before
            // Unity publishes its Editor change stream. Do not let the next test
            // inherit cached avatar roots referencing the objects just destroyed.
            // This public test-isolation reset is never used by production preview.
            PropCacheDebug.InvalidateAllCaches();
            ComputeContext.FlushInvalidates();
        }
    }
}
