// MIT License. Copyright (c) 2026 Armature Scale Sync contributors.
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Honomin.ArmatureScaleSync.Tests
{
    public sealed class HumanoidAcceptanceTests
    {
        [Test, Category("HUMANOID_001")]
        public void HumanoidRebindAndAnimatorSamplingPreserveHierarchyScale()
        {
            using (var f = SyntheticAvatar.Humanoid())
            {
                var original = f.Root.GetComponent<Animator>().avatar;
                var before = EditorJsonUtility.ToJson(original);
                f.Animate(f.Hips);
                f.Scale(f.Hips, 1.2f);
                var output = f.Build();
                var animator = output.GetComponent<Animator>();
                Assert.That(animator.avatar.isValid && animator.avatar.isHuman, Is.True);
                animator.Rebind();
                animator.Update(0);
                Assert.That(animator.GetBoneTransform(HumanBodyBones.Hips).localScale.x, Is.EqualTo(1.2f).Within(1e-5));
                Assert.That(EditorJsonUtility.ToJson(original), Is.EqualTo(before));
                Assert.That(f.Hips.localScale, Is.EqualTo(Vector3.one));
                SyntheticAvatar.Dump("HUMANOID-001", output);
            }
        }
    }
}
