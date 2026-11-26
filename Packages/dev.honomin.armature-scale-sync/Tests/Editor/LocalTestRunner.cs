// MIT License. Copyright (c) 2026 Armature Scale Sync contributors.
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Honomin.ArmatureScaleSync.Tests
{
    // Public Test Framework API bridge for a real graphics-enabled Editor.
    public static class LocalTestRunner
    {
        private static TestRunnerApi api;
        private static Callbacks callbacks;

        public static void Start(string resultPath, string artifactDirectory, string category, string group)
        {
            if (api || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("The Editor must be idle before starting acceptance tests.");
            for (var i = 0; i < EditorSceneManager.sceneCount; i++)
                if (EditorSceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save or resolve dirty scenes before running acceptance tests.");
            if (File.Exists(resultPath))
                throw new InvalidOperationException("Acceptance tests require a fresh result path.");
            Directory.CreateDirectory(artifactDirectory);
            Environment.SetEnvironmentVariable("ASS_TEST_ARTIFACTS", artifactDirectory);
            Debug.Log("ASS acceptance start: " + Path.GetFileName(artifactDirectory));
            api = ScriptableObject.CreateInstance<TestRunnerApi>();
            callbacks = new Callbacks(resultPath);
            api.RegisterCallbacks(callbacks);
            api.Execute(new ExecutionSettings(new Filter
            {
                testMode = TestMode.EditMode,
                groupNames = new[] { "^" + System.Text.RegularExpressions.Regex.Escape(group) + "(?:\\.|$)" },
                categoryNames = string.IsNullOrEmpty(category) ? null : new[] { category }
            }));
        }

        private sealed class Callbacks : ICallbacks
        {
            private readonly string path;
            public Callbacks(string path) { this.path = path; }
            public void RunStarted(ITestAdaptor testsToRun) { }
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result) { }
            public void RunFinished(ITestResultAdaptor result)
            {
                try
                {
                    File.WriteAllText(path + ".tmp", result.ToXml().OuterXml);
                    File.Move(path + ".tmp", path);
                }
                finally
                {
                    api.UnregisterCallbacks(this);
                    UnityEngine.Object.DestroyImmediate(api);
                    api = null;
                    callbacks = null;
                }
            }
        }
    }
}
