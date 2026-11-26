// MIT License. Copyright (c) 2026 Armature Scale Sync contributors.
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using Honomin.ArmatureScaleSync.Editor;

[assembly: ExportsPlugin(typeof(ScaleSyncPlugin))]
namespace Honomin.ArmatureScaleSync.Editor
{
    [RunsOnAllPlatforms]
    public sealed class ScaleSyncPlugin : Plugin<ScaleSyncPlugin>
    {
        public override string QualifiedName => "dev.honomin.armature-scale-sync";
        public override string DisplayName => "Armature Scale Sync";
        protected override void Configure()
        {
            InPhase(BuildPhase.Resolving).BeforePlugin("nadena.dev.modular-avatar")
                .Run("Capture scale declarations and binding identities", BuildScaleApplier.Prepare);
            InPhase(BuildPhase.Transforming)
                .AfterPlugin("nadena.dev.modular-avatar")
                .BeforePlugin("nadena.dev.modular-avatar.late-transform-stages")
                .WithRequiredExtension(typeof(AnimatorServicesContext), sequence =>
                {
                    sequence.Run("Apply hierarchical scale and rebase virtual curves", BuildScaleApplier.Apply)
                        .PreviewingWith(new ScalePreviewFilter());
                });
        }
    }
}
