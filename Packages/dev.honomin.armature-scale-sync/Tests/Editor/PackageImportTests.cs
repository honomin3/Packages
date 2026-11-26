// MIT License. Copyright (c) 2026 Armature Scale Sync contributors.
using Honomin.ArmatureScaleSync.Editor;
using System.IO;
using nadena.dev.modular_avatar.core;
using nadena.dev.ndmf;
using NUnit.Framework;
using UnityEditor.PackageManager;

namespace Honomin.ArmatureScaleSync.Tests
{
    public sealed class PackageImportTests
    {
        [System.Serializable]
        private sealed class ManifestVersion
        {
            public string name = "";
            public string version = "";
        }
        [Test, Category("PKG_003")]
        public void InstalledProjectLoadsOwnRuntimeCoreEditorAndPluginAssemblies()
        {
            Assert.That(typeof(HierarchicalBoneScale).Assembly.GetName().Name, Is.EqualTo("Honomin.ArmatureScaleSync.Runtime"));
            Assert.That(typeof(Core.Scale3).Assembly.GetName().Name, Is.EqualTo("Honomin.ArmatureScaleSync.Core"));
            Assert.That(new ScaleSyncPlugin().QualifiedName, Is.EqualTo("dev.honomin.armature-scale-sync"));
            var installed = PackageInfo.FindForAssembly(typeof(HierarchicalBoneScale).Assembly);
            var declared = UnityEngine.JsonUtility.FromJson<ManifestVersion>(File.ReadAllText(Path.Combine(installed.resolvedPath, "package.json")));
            Assert.That(installed.name, Is.EqualTo("dev.honomin.armature-scale-sync"));
            Assert.That(declared.name, Is.EqualTo(installed.name));
            Assert.That(declared.version, Is.Not.Empty);
            Assert.That(installed.version, Is.EqualTo(declared.version), "Registered metadata must match the actual imported manifest, including future versions.");
        }
        [Test, Category("COMPAT_001")]
        public void InstalledVersionsAndSharedPreviewAPIGateAreRecorded()
        {
            var ma = PackageInfo.FindForAssembly(typeof(ModularAvatarMergeArmature).Assembly);
            var ndmf = PackageInfo.FindForAssembly(typeof(BuildContext).Assembly);
            TestContext.WriteLine("MA=" + ma.version + "; NDMF=" + ndmf.version + "; Unity=" + UnityEngine.Application.unityVersion);
            Assert.That(UnityEngine.Application.unityVersion, Is.EqualTo("2022.3.22f1"));
            Assert.That(PreviewSupport.SharedAvailable, Is.True, PreviewSupport.Requirements);
            Assert.That(ma.version.StartsWith("1.19."), Is.True);
            Assert.That(ndmf.version.StartsWith("1.15."), Is.True);
        }
    }
}
