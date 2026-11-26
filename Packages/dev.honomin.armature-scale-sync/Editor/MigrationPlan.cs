// MIT License. Copyright (c) 2026 Armature Scale Sync contributors.
using System;
using Honomin.ArmatureScaleSync.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Honomin.ArmatureScaleSync.Editor
{
    internal sealed class MigrationPlan
    {
        internal Transform Target
        {
            get;
        }
        internal Vector3 Before
        {
            get;
        }
        internal Vector3 Baseline
        {
            get;
        }
        internal Vector3 Multiplier
        {
            get;
        }
        internal bool CanApply
        {
            get;
        }
        internal string Reason
        {
            get;
        }
        private readonly string componentBefore;
        private const string PlayModeReason = "Stop Play Mode before migrating authoring data.";
        private static bool PlayModeBlocksEditing => Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode;
        private static bool IsAssetContents(Transform target) => EditorUtility.IsPersistent(target)
            || PrefabStageUtility.GetPrefabStage(target.gameObject) != null;

        internal MigrationPlan(Transform target, Vector3 baseline)
        {
            Target = target;
            Baseline = baseline;
            if (PlayModeBlocksEditing)
            {
                Reason = PlayModeReason;
                return;
            }
            if (!target || IsAssetContents(target))
            {
                Reason = "Select a Scene or Prefab instance transform; Prefab assets and Prefab Mode contents are never edited.";
                return;
            }
            Before = target.localScale;
            var existing = target.GetComponent<HierarchicalBoneScale>();
            componentBefore = existing ? EditorJsonUtility.ToJson(existing) : null;
            if (existing && (!existing.ScaleEnabled || !existing.enabled))
            {
                Reason = "Enable the existing declaration before migrating.";
                return;
            }
            if (existing && existing.HasMigrated && Before.Equals(existing.MigrationBaseline))
            {
                Reason = "Already migrated; no additional transform delta exists.";
                return;
            }
            var a = new Scale3(Before.x, Before.y, Before.z);
            var b = new Scale3(baseline.x, baseline.y, baseline.z);
            if (!a.IsInvertible || !b.IsInvertible)
            {
                Reason = "Source and reference scales must be finite with no zero axes.";
                return;
            }
            var delta = new Scale3(a.X / b.X, a.Y / b.Y, a.Z / b.Z);
            if (existing)
                delta = delta * ModularAvatarAdapter.Multiplier(existing);
            if (!delta.IsFloatRepresentable)
            {
                Reason = "Migration multiplier overflows or underflows a Unity float.";
                return;
            }
            Multiplier = new Vector3((float)delta.X, (float)delta.Y, (float)delta.Z);
            if (Before.Equals(baseline))
            {
                Reason = "Transform already matches the reference scale.";
                return;
            }
            CanApply = true;
            Reason = "Explicit migration preserves the effective scale; Undo restores the prior transform and declaration.";
        }

        internal void Apply()
        {
            if (PlayModeBlocksEditing)
                throw new InvalidOperationException(PlayModeReason);
            if (!CanApply || !Target || IsAssetContents(Target))
                throw new InvalidOperationException(Reason);
            var h = Target.GetComponent<HierarchicalBoneScale>();
            if (!Target.localScale.Equals(Before) || (h ? EditorJsonUtility.ToJson(h) : null) != componentBefore)
                throw new InvalidOperationException("Source changed since preview. Recreate the migration plan.");
            Undo.IncrementCurrentGroup();
            var group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Migrate Armature Scale Sync");
            try
            {
                Undo.RecordObject(Target, "Migrate transform scale");
                if (!h)
                    h = Undo.AddComponent<HierarchicalBoneScale>(Target.gameObject);
                else
                    Undo.RecordObject(h, "Migrate scale declaration");
                Target.localScale = Baseline;
                h.Mode = Multiplier.x == Multiplier.y && Multiplier.y == Multiplier.z ? ScaleMode.Uniform : ScaleMode.Advanced;
                h.UniformMultiplier = Multiplier.x;
                h.VectorMultiplier = Multiplier;
                h.HasMigrated = true;
                h.MigrationBaseline = Baseline;
                PrefabUtility.RecordPrefabInstancePropertyModifications(Target);
                PrefabUtility.RecordPrefabInstancePropertyModifications(h);
                Undo.FlushUndoRecordObjects();
            }
            catch { Undo.RevertAllDownToGroup(group); throw; }
            finally { Undo.CollapseUndoOperations(group); }
        }
    }
}
