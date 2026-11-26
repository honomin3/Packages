// MIT License. Copyright (c) 2026 Armature Scale Sync contributors.
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Honomin.ArmatureScaleSync.Editor
{
    /// <summary>Exact processed-clone inspection uses the real NDMF pipeline in an isolated preview scene.</summary>
    internal sealed class ProcessedAvatarWindow : EditorWindow
    {
        private GameObject source;
        private ProcessedPreviewSnapshot snapshot;
        private RenderTexture texture;
        private Image image;
        private Label result;

        [MenuItem("Tools/Armature Scale Sync/Inspect processed avatar…")]
        private static void ShowWindow() => Open(Selection.activeGameObject);
        internal static void Open(GameObject source)
        {
            var window = GetWindow<ProcessedAvatarWindow>("Processed avatar");
            window.source = source;
            window.CreateGUI();
            window.Show();
        }
        public void CreateGUI()
        {
            rootVisualElement.Clear();
            var stylesheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/dev.honomin.armature-scale-sync/Editor/Inspector.uss");
            if (stylesheet)
                rootVisualElement.styleSheets.Add(stylesheet);
            rootVisualElement.Add(new HelpBox("Build a fresh processed view to verify fit, advanced scale, Bone Proxy placement and preview differences. Rebuild after changing settings.", HelpBoxMessageType.Info));
            var field = new ObjectField("Avatar root") { objectType = typeof(GameObject), allowSceneObjects = true, value = source };
            field.RegisterValueChangedCallback(e => source = e.newValue as GameObject);
            rootVisualElement.Add(field);
            rootVisualElement.Add(new Button(BuildView) { text = "Build processed view" });
            result = new Label();
            rootVisualElement.Add(result);
            image = new Image { scaleMode = UnityEngine.ScaleMode.ScaleToFit };
            image.AddToClassList("processed-image");
            rootVisualElement.Add(image);
        }
        private void BuildView()
        {
            ClearView();
            if (!source || EditorUtility.IsPersistent(source))
            {
                result.text = "Choose an avatar instance in a Scene.";
                return;
            }
            try
            {
                snapshot = ProcessedPreviewSnapshot.Create(source);
                var scene = snapshot.Scene;
                var clone = snapshot.Root;
                var renderers = clone.GetComponentsInChildren<Renderer>(true);
                var bounds = new Bounds(clone.transform.position, Vector3.one * .01f);
                foreach (var renderer in renderers)
                    if (renderer.enabled)
                        bounds.Encapsulate(renderer.bounds);
                var cameraObject = new GameObject("ASS processed view camera") { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(cameraObject, scene);
                var camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                camera.scene = scene;
                camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
                camera.orthographic = true;
                camera.orthographicSize = Mathf.Max(.1f, Mathf.Max(bounds.extents.y, bounds.extents.x) * 1.2f);
                camera.transform.position = bounds.center + Vector3.forward * (bounds.extents.magnitude + 2);
                camera.transform.LookAt(bounds.center);
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.15f, .15f, .15f);
                camera.nearClipPlane = .01f;
                camera.farClipPlane = 1000;
                var lightObject = new GameObject("ASS processed view light") { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(lightObject, scene);
                lightObject.transform.rotation = Quaternion.Euler(30, 150, 0);
                lightObject.AddComponent<Light>().type = LightType.Directional;
                texture = new RenderTexture(640, 640, 24) { hideFlags = HideFlags.HideAndDontSave };
                camera.targetTexture = texture;
                using (var baselineSession = new nadena.dev.ndmf.preview.PreviewSession())
                {
                    baselineSession.OverrideCamera(camera);
                    try
                    {
                        camera.Render();
                    }
                    finally { nadena.dev.ndmf.preview.PreviewSession.ClearCameraOverride(camera); }
                }
                image.image = texture;
                result.text = "Fresh processed build rendered in an isolated preview scene.";
            }
            catch (Exception e) { result.text = e.Message; ClearView(); }
        }
        private void ClearView()
        {
            if (image != null)
                image.image = null;
            snapshot?.Dispose();
            snapshot = null;
            if (texture)
            {
                texture.Release();
                DestroyImmediate(texture);
                texture = null;
            }
        }
        private void OnDisable() => ClearView();
    }
}
