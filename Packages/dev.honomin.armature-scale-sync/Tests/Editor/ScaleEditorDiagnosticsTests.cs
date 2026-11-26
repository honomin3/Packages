// MIT License. Copyright (c) 2026 Armature Scale Sync contributors.
using System.Linq;
using Honomin.ArmatureScaleSync.Editor;
using nadena.dev.modular_avatar.core;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Honomin.ArmatureScaleSync.Tests
{
    public sealed class ScaleEditorDiagnosticsTests
    {
        private sealed class DiagnosticsTestWindow : EditorWindow { }
        private static bool InFoldout(VisualElement element) => element.GetFirstAncestorOfType<Foldout>() != null;

        private static ModularAvatarBoneProxy Proxy(SyntheticAvatar fixture, string name)
        {
            var host = SyntheticAvatar.Child(fixture.Root.transform, name, new Vector3(1, 0, 0));
            var proxy = host.gameObject.AddComponent<ModularAvatarBoneProxy>();
            proxy.target = fixture.Chest;
            proxy.matchScale = false;
            proxy.attachmentMode = BoneProxyAttachmentMode.AsChildAtRoot;
            return proxy;
        }

        [Test, Category("DIAGNOSTICS_UI")]
        public void RepeatedProxyWarningsStartCollapsedAndRetainEveryReasonAndObject()
        {
            using (var fixture = new SyntheticAvatar())
            {
                var scale = fixture.Scale(fixture.Hips, .9f);
                var proxies = Enumerable.Range(0, 20).Select(i => Proxy(fixture, "Proxy" + i)).ToArray();
                var dynamics = Enumerable.Range(0, 20).Select(i =>
                {
                    var bone = SyntheticAvatar.Child(fixture.Chest, "Dynamics" + i);
                    bone.gameObject.AddComponent<UnityEngine.Animations.PositionConstraint>();
                    return bone;
                }).ToArray();
                var before = proxies.Select(p => EditorJsonUtility.ToJson(p)).ToArray();
                var original = UnityScalePlan.Resolve(fixture.Root, true);
                var editor = UnityEditor.Editor.CreateEditor(scale);
                var window = ScriptableObject.CreateInstance<DiagnosticsTestWindow>();
                try
                {
                    var ui = editor.CreateInspectorGUI();
                    window.ShowUtility();
                    window.rootVisualElement.Add(ui);
                    foreach (var code in new[] { "PROXY_SCALE", "PREVIEW_PROXY", "DYNAMICS" })
                    {
                        Assert.That(original.Diagnostics.Count(d => d.Code == code), Is.EqualTo(20), "Real adapter must produce the reported warning flood.");
                        var summaries = ui.Query<HelpBox>().ToList().Where(b => !InFoldout(b) && b.text.StartsWith(code)).ToArray();
                        Assert.That(summaries.Length, Is.EqualTo(1), code + " must have one visible summary.");
                        StringAssert.Contains("20", summaries[0].text);
                        var group = ui.Query<Foldout>().ToList().Single(f => f.name == "ass-diagnostics-" + code);
                        Assert.That(group.value, Is.False, "Repeated warnings must start collapsed.");
                        Assert.That(group.panel, Is.Not.Null, "Exercise real attached UI change events.");
                        group.value = true;
                        CollectionAssert.AreEquivalent(code == "DYNAMICS" ? dynamics : proxies.Select(p => p.transform), group.Query<ObjectField>().ToList().Select(f => f.value));
                        foreach (var diagnostic in original.Diagnostics.Where(d => d.Code == code))
                            Assert.That(group.Query<HelpBox>().ToList().Any(b => b.text == diagnostic.Code + ": " + diagnostic.Message), Is.True);
                    }
                    var rebuiltUi = editor.CreateInspectorGUI();
                    Assert.That(rebuiltUi.Query<Foldout>().ToList().Where(f => f.name.StartsWith("ass-diagnostics-")).All(f => f.value), Is.True, "Open details should remain open when the Inspector is rebuilt.");
                    CollectionAssert.AreEqual(before, proxies.Select(p => EditorJsonUtility.ToJson(p)));
                    Assert.That(proxies.All(p => !p.matchScale), Is.True);
                    Assert.That(fixture.Hips.localScale, Is.EqualTo(Vector3.one));
                    Assert.That(scale.UniformMultiplier, Is.EqualTo(.9f));
                }
                finally { window.Close(); Object.DestroyImmediate(editor); }
            }
        }

        [Test, Category("DIAGNOSTICS_UI")]
        public void NonuniformWarningAndInvalidScaleErrorRemainOutsideCollapsedDetails()
        {
            using (var fixture = new SyntheticAvatar())
            {
                fixture.Root.GetComponent<ArmatureScaleSync>().DiagnosticsEnabled = false;
                var scale = fixture.Scale(fixture.Hips, 1);
                scale.Mode = ScaleMode.Advanced;
                scale.VectorMultiplier = new Vector3(1.1f, 1, .9f);
                fixture.Scale(fixture.Chest, 0);
                Proxy(fixture, "ProxyA");
                Proxy(fixture, "ProxyB");
                var editor = UnityEditor.Editor.CreateEditor(scale);
                try
                {
                    var ui = editor.CreateInspectorGUI();
                    var boxes = ui.Query<HelpBox>().ToList();
                    Assert.That(boxes.Any(b => !InFoldout(b) && b.text.StartsWith("NONUNIFORM:") && b.messageType == HelpBoxMessageType.Warning), Is.True);
                    Assert.That(boxes.Any(b => !InFoldout(b) && b.text.StartsWith("INVALID_SCALE:") && b.messageType == HelpBoxMessageType.Error), Is.True);
                    Assert.That(fixture.Hips.localScale, Is.EqualTo(Vector3.one));
                    Assert.That(fixture.Chest.localScale, Is.EqualTo(Vector3.one));
                }
                finally { Object.DestroyImmediate(editor); }
            }
        }

        [Test, Category("DIAGNOSTICS_UI")]
        public void SingleProxyWarningKeepsItsReasonAndRelatedObjectVisible()
        {
            using (var fixture = new SyntheticAvatar())
            {
                var scale = fixture.Scale(fixture.Hips, .9f);
                var proxy = Proxy(fixture, "SingleProxy");
                var editor = UnityEditor.Editor.CreateEditor(scale);
                try
                {
                    var ui = editor.CreateInspectorGUI();
                    Assert.That(ui.Query<HelpBox>().ToList().Any(b => !InFoldout(b) && b.text.StartsWith("PROXY_SCALE:") && b.text.Contains("Match Parent Scale")), Is.True);
                    Assert.That(ui.Query<ObjectField>().ToList().Any(f => !InFoldout(f) && f.value == proxy.transform), Is.True);
                    Assert.That(ui.Query<Foldout>().ToList().Any(f => f.name.StartsWith("ass-diagnostics-")), Is.False);
                }
                finally { Object.DestroyImmediate(editor); }
            }
        }
    }
}
