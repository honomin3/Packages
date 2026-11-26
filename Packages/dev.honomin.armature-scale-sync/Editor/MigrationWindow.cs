// MIT License. Copyright (c) 2026 Armature Scale Sync contributors.
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Honomin.ArmatureScaleSync.Editor
{
    internal sealed class MigrationWindow : EditorWindow
    {
        private Transform selection;
        private Vector3 baseline = Vector3.one;
        private VisualElement review;
        private Button apply;
        private MigrationPlan plan;

        [MenuItem("Tools/Armature Scale Sync/Migrate transform scale…")]
        private static void ShowWindow() => Open(Selection.activeTransform);
        internal static void Open(Transform target)
        {
            var window = GetWindow<MigrationWindow>("Migrate scale");
            window.selection = target;
            window.SetDefaultBaseline();
            window.CreateGUI();
            window.Show();
        }
        private void SetDefaultBaseline()
        {
            var source = selection ? PrefabUtility.GetCorrespondingObjectFromSource(selection) : null;
            baseline = source ? source.localScale : Vector3.one;
        }
        public void CreateGUI()
        {
            var root = rootVisualElement;
            root.Clear();
            root.Add(new HelpBox("Review the reference scale and resulting declaration. Only Migrate changes the selected Scene or Prefab instance. Undo restores the entire operation.", HelpBoxMessageType.Info));
            var field = new ObjectField("Transform") { objectType = typeof(Transform), allowSceneObjects = true, value = selection };
            root.Add(field);
            var reference = new Vector3Field("Reference local scale") { value = baseline };
            root.Add(reference);
            field.RegisterValueChangedCallback(e => { selection = e.newValue as Transform; SetDefaultBaseline(); reference.SetValueWithoutNotify(baseline); Review(); });
            reference.RegisterValueChangedCallback(e => { baseline = e.newValue; Review(); });
            review = new VisualElement();
            root.Add(review);
            apply = new Button(() =>
            {
                try
                {
                    plan.Apply();
                    Review();
                }
                catch (System.Exception e) { review.Add(new HelpBox(e.Message, HelpBoxMessageType.Error)); apply.SetEnabled(false); }
            })
            {
                text = "Migrate (Undo supported)"
            };
            root.Add(apply);
            Review();
        }
        private void Review()
        {
            if (review == null)
                return;
            plan = new MigrationPlan(selection, baseline);
            review.Clear();
            review.Add(new Label("Current local scale: " + plan.Before));
            review.Add(new Label("New local scale: " + plan.Baseline));
            review.Add(new Label("New declared multiplier: " + plan.Multiplier));
            review.Add(new HelpBox(plan.Reason, plan.CanApply ? HelpBoxMessageType.Info : HelpBoxMessageType.Warning));
            apply?.SetEnabled(plan.CanApply);
        }
    }
}
