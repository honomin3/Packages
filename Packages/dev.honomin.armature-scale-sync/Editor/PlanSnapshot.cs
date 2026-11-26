// MIT License. Copyright (c) 2026 Armature Scale Sync contributors.
using System.Collections.Generic;
using Honomin.ArmatureScaleSync.Core;
using nadena.dev.ndmf;
using UnityEngine;

namespace Honomin.ArmatureScaleSync.Editor
{
    internal sealed class ScaleSetting
    {
        public Transform Bone;
        public string Origin;
        public Scale3 Multiplier;
        public bool Advanced;
    }
    internal sealed class TargetBinding
    {
        public Transform Source, Target;
        public Route Route;
    }
    internal sealed class PlanSnapshot
    {
        public readonly List<ScaleSetting> Settings = new List<ScaleSetting>();
        public readonly List<TargetBinding> Bindings = new List<TargetBinding>();
        public readonly List<BoneProxyProjection> ProxyProjections = new List<BoneProxyProjection>();
        public readonly List<Diagnostic> Diagnostics = new List<Diagnostic>();
        public bool RebaseAnimation = true;
    }
    internal sealed class ScaleBuildState
    {
        // Only registry identities and immutable settings cross pass boundaries. Resolution is repeated
        // immediately before writes; object replacement by intervening plugins is not guessed by name.
        internal sealed class SavedSetting
        {
            public ObjectReference Bone;
            public Scale3 Multiplier;
            public bool Advanced;
            public string Origin;
        }
        internal sealed class SavedBinding
        {
            public ObjectReference Source, Target;
            public Route Route;
        }
        public readonly List<SavedSetting> Settings = new List<SavedSetting>();
        public readonly List<SavedBinding> Bindings = new List<SavedBinding>();
        public readonly HashSet<string> ReportedDiagnostics = new HashSet<string>();
        public bool RebaseAnimation = true;
        public bool Prepared, Applied;
    }
}
