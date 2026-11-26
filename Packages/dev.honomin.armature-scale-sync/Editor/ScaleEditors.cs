// MIT License. Copyright (c) 2026 Armature Scale Sync contributors.
using System.Collections.Generic;
using System.Linq;
using Honomin.ArmatureScaleSync.Core;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Honomin.ArmatureScaleSync.Editor
{
    [CustomEditor(typeof(HierarchicalBoneScale)), CanEditMultipleObjects]
    internal sealed class HierarchicalBoneScaleEditor : ScaleEditor
    {
    }
    [CustomEditor(typeof(ArmatureScaleSync)), CanEditMultipleObjects]
    internal sealed class ArmatureScaleSyncEditor : ScaleEditor
    {
    }

    internal abstract class ScaleEditor : UnityEditor.Editor
    {
        private VisualElement diagnostics;
        private readonly HashSet<string> expandedWarnings = new HashSet<string>();
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            root.Add(new HelpBox("Scale declarations affect descendants. Original transforms and assets are preserved during preview and build.", HelpBoxMessageType.Info));
            InspectorElement.FillDefaultInspector(root, serializedObject, this);
            diagnostics = new VisualElement();
            root.Add(diagnostics);
            root.Add(new Button(() => MigrationWindow.Open((target as Component)?.transform)) { text = "Migrate an existing transform scale…" });
            root.Add(new Button(() => ProcessedAvatarWindow.Open(AvatarRoot())) { text = "Inspect processed avatar…" });
            root.TrackSerializedObjectValue(serializedObject, _ => Refresh());
            Undo.undoRedoPerformed -= Refresh;
            Undo.undoRedoPerformed += Refresh;
            EditorApplication.hierarchyChanged -= Refresh;
            EditorApplication.hierarchyChanged += Refresh;
            Refresh();
            return root;
        }
        private GameObject AvatarRoot()
        {
            var component = target as Component;
            if (!component)
                return null;
            var avatar = nadena.dev.ndmf.runtime.RuntimeUtil.FindAvatarInParents(component.transform);
            return avatar ? avatar.gameObject : component.transform.root.gameObject;
        }
        private void Refresh()
        {
            if (diagnostics == null || !target)
                return;
            diagnostics.Clear();
            if (serializedObject.isEditingMultipleObjects)
            {
                diagnostics.Add(new HelpBox("Select one avatar to inspect its complete sync diagnostics.", HelpBoxMessageType.Info));
                return;
            }
            var avatar = AvatarRoot();
            if (!avatar)
                return;
            var plan = UnityScalePlan.Resolve(avatar, true);
            if (plan.Diagnostics.Count == 0)
                return;
            diagnostics.Add(new Label("Avatar-wide diagnostics"));
            // Only repetitive compatibility guidance is grouped. Errors and all
            // other warnings (including advanced scale/shear) stay expanded.
            var groups = plan.Diagnostics.Where(d => d.Severity == Severity.Warning && Summary(d.Code) != null)
                .GroupBy(d => d.Code).Where(g => g.Count() > 1).ToArray();
            var groupedCodes = new HashSet<string>(groups.Select(g => g.Key));
            foreach (var d in plan.Diagnostics.Where(d => d.Severity == Severity.Error || !groupedCodes.Contains(d.Code)))
                AddDiagnostic(diagnostics, plan, d);
            foreach (var group in groups)
            {
                diagnostics.Add(new HelpBox(group.Key + " (" + group.Count() + "): " + Summary(group.Key), HelpBoxMessageType.Warning));
                var code = group.Key;
                var details = new Foldout
                {
                    name = "ass-diagnostics-" + code,
                    text = "Show affected objects and reasons (" + group.Count() + ")",
                    value = expandedWarnings.Contains(code)
                };
                details.RegisterValueChangedCallback(e =>
                {
                    if (e.newValue) expandedWarnings.Add(code);
                    else expandedWarnings.Remove(code);
                });
                foreach (var d in group)
                    AddDiagnostic(details, plan, d);
                diagnostics.Add(details);
            }
        }
        private static string Summary(string code)
        {
            switch (code)
            {
                case "PROXY_SCALE": return "Bone Proxy Match Parent Scale is disabled. This may be intentional for existing accessories. ASS preserves the MA setting; check placement if needed.";
                case "PREVIEW_PROXY": return "Some Bone Proxy placements cannot be reproduced reliably in the live preview. Check their appearance in the processed avatar.";
                case "DYNAMICS": return "Hierarchy scaling affects PhysBones, colliders, contacts or constraints. Validate their behavior on the processed avatar.";
                default: return null;
            }
        }
        private static void AddDiagnostic(VisualElement container, UnityScalePlan plan, Diagnostic d)
        {
            container.Add(new HelpBox(d.Code + ": " + d.Message, d.Severity == Severity.Error ? HelpBoxMessageType.Error : HelpBoxMessageType.Warning));
            if (plan.Objects.TryGetValue(d.Target, out var related))
            {
                var field = new ObjectField("Related object") { objectType = typeof(Transform), allowSceneObjects = true, value = related };
                field.SetEnabled(false);
                container.Add(field);
            }
        }
        protected virtual void OnDisable()
        {
            Undo.undoRedoPerformed -= Refresh;
            EditorApplication.hierarchyChanged -= Refresh;
        }
    }
}
