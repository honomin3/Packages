// MIT License. Copyright (c) 2026 Armature Scale Sync contributors.
using nadena.dev.ndmf;
using UnityEngine;

namespace Honomin.ArmatureScaleSync
{
    public enum ScaleMode
    {
        Uniform, Advanced
    }

    /// <summary>Authoring data only. Preview and build adapters never mutate this transform.</summary>
    [AddComponentMenu("Armature Scale Sync/Hierarchical Bone Scale")]
    [DisallowMultipleComponent]
    public sealed class HierarchicalBoneScale : MonoBehaviour, INDMFEditorOnly
    {
        public bool ScaleEnabled = true;
        public ScaleMode Mode = ScaleMode.Uniform;
        public float UniformMultiplier = 1;
        public Vector3 VectorMultiplier = Vector3.one;
        // Serialized migration bookkeeping participates in the same Undo transaction as the scale.
        [HideInInspector] public bool HasMigrated;
        [HideInInspector] public Vector3 MigrationBaseline = Vector3.one;
    }
}
