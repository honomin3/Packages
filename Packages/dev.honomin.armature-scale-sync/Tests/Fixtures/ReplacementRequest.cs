// MIT License. Copyright (c) 2026 Armature Scale Sync contributors.
using UnityEngine;
namespace Honomin.ArmatureScaleSync.Tests
{
    // Synthetic intervening-plugin instruction, used only inside the Editor test assembly.
    public sealed class ReplacementRequest : MonoBehaviour
    {
        public bool CloneRendererAssets;
        public string Path = "Armature/Hips";
    }
}
