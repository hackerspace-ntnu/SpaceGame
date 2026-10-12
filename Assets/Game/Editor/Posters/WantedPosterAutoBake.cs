using System;
using System.Collections.Generic;
using SpaceGame.World.Posters;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpaceGame.EditorTools.Posters
{
    /// <summary>
    /// Makes a <see cref="WantedPoster"/> look after itself in the editor. Adding the component
    /// fits a paper sheet and a collider; any edit or load of a poster whose picture no longer
    /// matches its fields queues a bake, run one per editor tick outside play mode and test runs.
    /// </summary>
    [InitializeOnLoad]
    public static class WantedPosterAutoBake
    {
        public const string SheetMaterialPath = "Assets/Game/Art/Materials/Posters/WantedPoster_Base.mat";

        private static readonly HashSet<WantedPoster> Pending = new();

        // Held so the callbacks stay registered. A test that loads a poster prefab must not set
        // off a bake: it writes assets mid-run, which the Test Runner reports as files left behind.
        private static readonly TestRunnerApi TestRuns = ScriptableObject.CreateInstance<TestRunnerApi>();
        private static bool testsRunning;

        static WantedPosterAutoBake()
        {
            WantedPoster.Edited += Queue;
            ObjectFactory.componentWasAdded += OnComponentAdded;
            EditorApplication.update += Pump;
            TestRuns.hideFlags = HideFlags.HideAndDontSave;
            TestRuns.RegisterCallbacks(new TestRunWatch());
        }

        private sealed class TestRunWatch : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun) => testsRunning = true;
            public void RunFinished(ITestResultAdaptor result) => testsRunning = false;
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result) { }
        }

        private static void OnComponentAdded(Component component)
        {
            if (component is not WantedPoster poster) return;
            Fit(poster);
            Queue(poster);
        }

        private static void Queue(WantedPoster poster)
        {
            if (poster != null) Pending.Add(poster);
        }

        private static void Pump()
        {
            if (Pending.Count == 0 || testsRunning || EditorApplication.isPlayingOrWillChangePlaymode ||
                EditorApplication.isCompiling || EditorApplication.isUpdating) return;

            WantedPoster poster = null;
            foreach (WantedPoster p in Pending)
            {
                poster = p;
                break;
            }

            Pending.Remove(poster);
            if (!NeedsBake(poster)) return;

            try
            {
                Debug.Log("[Posters] " + WantedPosterBaker.Bake(poster), poster);
            }
            catch (Exception e)
            {
                Debug.LogException(e, poster);
            }
        }

        private static bool NeedsBake(WantedPoster poster)
        {
            if (poster == null || poster.Agent == null || WantedPosterBaker.IsCurrent(poster)) return false;
            if (EditorUtility.IsPersistent(poster)) return true;

            // A copy inside a temporary preview scene (an editor tool's LoadPrefabContents) is thrown
            // away; only a real scene or the prefab being edited in Prefab Mode keeps a bake.
            Scene scene = poster.gameObject.scene;
            return scene.IsValid() &&
                   (!EditorSceneManager.IsPreviewScene(scene) || PrefabStageUtility.GetPrefabStage(poster.gameObject) != null);
        }

        /// <summary>
        /// Gives a new poster its paper: a quad child facing the poster's +Z with the shared sheet
        /// material, and a thin box collider to be read by. Does nothing to a poster that has a sheet.
        /// </summary>
        public static void Fit(WantedPoster poster)
        {
            if (poster.Sheet != null) return;

            var style = AssetDatabase.LoadAssetAtPath<WantedPosterStyle>(WantedPosterBaker.StylePath);
            var material = AssetDatabase.LoadAssetAtPath<Material>(SheetMaterialPath);
            if (style == null || material == null)
                throw new InvalidOperationException($"[Posters] Missing {WantedPosterBaker.StylePath} or {SheetMaterialPath}.");

            var sheet = new GameObject("Sheet", typeof(MeshFilter), typeof(MeshRenderer));
            Undo.RegisterCreatedObjectUndo(sheet, "Fit wanted poster");
            sheet.transform.SetParent(poster.transform, false);
            // The built-in quad faces -Z; turned round, the printed face looks along the poster's +Z.
            sheet.transform.localPosition = new Vector3(0f, 0f, style.sheetDepth.x);
            sheet.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            sheet.transform.localScale = new Vector3(style.sheetSize.x, style.sheetSize.y, 1f);
            sheet.GetComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            var renderer = sheet.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var box = poster.GetComponent<BoxCollider>();
            if (box == null) box = Undo.AddComponent<BoxCollider>(poster.gameObject);
            box.size = new Vector3(style.sheetSize.x, style.sheetSize.y, style.sheetDepth.y);
            box.center = new Vector3(0f, 0f, style.sheetDepth.y * 0.5f);

            Undo.RecordObject(poster, "Fit wanted poster");
            poster.SetSheet(renderer);
            EditorUtility.SetDirty(poster);
        }
    }
}
