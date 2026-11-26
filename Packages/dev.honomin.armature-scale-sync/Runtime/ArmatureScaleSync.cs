// MIT License. Copyright (c) 2026 Armature Scale Sync contributors.
using System;
using System.Collections.Generic;
using nadena.dev.modular_avatar.core;
using nadena.dev.ndmf;
using UnityEngine;

namespace Honomin.ArmatureScaleSync
{
    [Flags]
    public enum TargetKinds
    {
        Clothing = 1, Hair = 2, Accessories = 4, All = 7
    }

    [Serializable]
    public sealed class ScaleBinding
    {
        public bool Enabled = true;
        public TargetKinds Kind = TargetKinds.Clothing;
        public AvatarObjectReference Source = new AvatarObjectReference();
        public AvatarObjectReference Target = new AvatarObjectReference();
    }

    [AddComponentMenu("Armature Scale Sync/Armature Scale Sync")]
    [DisallowMultipleComponent]
    public sealed class ArmatureScaleSync : MonoBehaviour, INDMFEditorOnly
    {
        [Tooltip("Discover independent rigs needing bindings. Existing MA/native inheritance always applies.")]
        public bool AutoSync = true;
        public TargetKinds TargetKinds = TargetKinds.All;
        public List<ScaleBinding> Bindings = new List<ScaleBinding>();
        public bool PreviewEnabled = true;
        public bool RebaseAnimation = true;
        [Tooltip("Show independent-rig discovery guidance. Advanced mode and correctness warnings are always shown.")]
        public bool DiagnosticsEnabled = true;
    }
}
