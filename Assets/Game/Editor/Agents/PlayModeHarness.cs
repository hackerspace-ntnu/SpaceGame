// Gets an unattended play-mode harness (the agent benchmark, the residents baseline) from a menu
// click or a bridge call into play mode, and puts the editor back as it was afterwards.
//
// Why the SessionState hand-off instead of entering play mode directly: a bridge command may not
// set isPlaying itself, and a domain reload while the command is still on the stack is how the
// bridge wedges. So Request only records a pending run and returns. An EditorApplication.update pump
// (never delayCall: an unfocused editor does not drain it) enters play mode on a later tick, and the
// owner's [InitializeOnLoadMethod] calls Install, which re-arms that pump if a recompile lands in
// between. SessionState is what survives the reload into play mode, so the EnteredPlayMode handler
// on the far side can find the pending settings and start the run.
//
// The user's scenes: a harness that stages its own scene (the benchmark's empty one) refuses to start
// over unsaved scene changes, since opening another scene would discard them, and puts the saved scene
// setup back when play mode ends. A harness without a stage plays whatever is open and moves to its
// scene at runtime instead; play mode keeps and restores unsaved edits by itself, so it needs neither.
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpaceGame.EditorTools
{
    public sealed class PlayModeHarness
    {
        [Serializable]
        private struct SavedScene
        {
            public string path;
            public bool isLoaded;
            public bool isActive;
        }

        [Serializable]
        private sealed class SavedSceneSetup
        {
            public SavedScene[] scenes;
        }

        private readonly string name;
        private readonly string pendingKey;
        private readonly string runningKey;
        private readonly string sceneSetupKey;
        private readonly Action<string> stageScenes;
        private readonly Action<string> start;
        private readonly Action<string, string> refuse;

        /// <param name="name">Log prefix and SessionState namespace; one per harness.</param>
        /// <param name="stageScenes">
        /// Opens the scene the run plays in, given the settings JSON; called after the user's setup is saved.
        /// Null: the run plays whatever is open and leaves the user's scenes alone.
        /// </param>
        /// <param name="start">Called in play mode with the settings JSON given to <see cref="Request"/>.</param>
        /// <param name="refuse">Called with that JSON and the reason when the run cannot start.</param>
        public PlayModeHarness(string name, Action<string> stageScenes, Action<string> start, Action<string, string> refuse)
        {
            this.name = name;
            pendingKey = $"SpaceGame.{name}.PendingSettings";
            runningKey = $"SpaceGame.{name}.Running";
            sceneSetupKey = $"SpaceGame.{name}.SceneSetup";
            this.stageScenes = stageScenes;
            this.start = start;
            this.refuse = refuse;
        }

        /// <summary>Hands control back to the editor. The scene setup is restored once edit mode is back.</summary>
        public static void Leave() => EditorApplication.isPlaying = false;

        /// <summary>Arms a run and returns at once; play mode starts on a later editor tick.</summary>
        public void Request(string settingsJson)
        {
            string refusal = RefusalReason();
            if (refusal != null)
            {
                Refuse(settingsJson, refusal);
                return;
            }

            SessionState.SetString(pendingKey, settingsJson);
            Arm();
        }

        /// <summary>Call from the owner's [InitializeOnLoadMethod]: hooks play-mode changes and re-arms a pending run.</summary>
        public void Install()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;

            if (!string.IsNullOrEmpty(SessionState.GetString(pendingKey, string.Empty))
                && !EditorApplication.isPlayingOrWillChangePlaymode)
                Arm();
        }

        private void Arm()
        {
            EditorApplication.update -= EnterPlayModeWhenIdle;
            EditorApplication.update += EnterPlayModeWhenIdle;
        }

        private void EnterPlayModeWhenIdle()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            EditorApplication.update -= EnterPlayModeWhenIdle;

            string json = SessionState.GetString(pendingKey, string.Empty);
            if (string.IsNullOrEmpty(json)) return;

            // Checked again here: scenes may have been edited between Request and this tick.
            string refusal = RefusalReason();
            if (refusal != null)
            {
                SessionState.EraseString(pendingKey);
                Refuse(json, refusal);
                return;
            }

            SessionState.SetBool(runningKey, true);
            if (stageScenes != null)
            {
                SaveSceneSetup();
                stageScenes(json);
            }
            EditorApplication.EnterPlaymode();
        }

        private string RefusalReason()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return "the editor is already in play mode";
            if (SessionState.GetBool(runningKey, false))
                return $"a {name} run is already in progress";
            if (EditorUtility.scriptCompilationFailed)
                return "scripts have compile errors, so play mode cannot start";

            PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage != null && prefabStage.scene.isDirty)
                return $"prefab '{prefabStage.assetPath}' is open in Prefab Mode with unsaved changes";

            for (int i = 0; stageScenes != null && i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isDirty)
                    return $"scene '{scene.name}' has unsaved changes; the run swaps scenes and would discard them";
            }

            return null;
        }

        private void Refuse(string json, string reason)
        {
            Debug.LogError($"[{name}] Not started: {reason}");
            refuse(json, reason);
        }

        private void OnPlayModeChanged(PlayModeStateChange change)
        {
            switch (change)
            {
                case PlayModeStateChange.EnteredPlayMode:
                    StartPendingRun();
                    break;

                case PlayModeStateChange.EnteredEditMode:
                    if (!SessionState.GetBool(runningKey, false)) break;
                    SessionState.EraseBool(runningKey);
                    EditorApplication.update -= KeepUnpaused;
                    EditorApplication.update += RestoreSceneSetupOnce;
                    break;
            }
        }

        private void StartPendingRun()
        {
            string json = SessionState.GetString(pendingKey, string.Empty);
            if (string.IsNullOrEmpty(json)) return;
            SessionState.EraseString(pendingKey);

            // Error Pause would stop an unattended run on the first error anything logs, and the run
            // would then sit at that frame until someone looked. The errors stay in the console.
            EditorApplication.update -= KeepUnpaused;
            EditorApplication.update += KeepUnpaused;

            start(json);
        }

        private static void KeepUnpaused()
        {
            if (EditorApplication.isPlaying && EditorApplication.isPaused)
                EditorApplication.isPaused = false;
        }

        private void SaveSceneSetup()
        {
            var setup = new SavedSceneSetup
            {
                scenes = EditorSceneManager.GetSceneManagerSetup()
                                          .Select(s => new SavedScene { path = s.path, isLoaded = s.isLoaded, isActive = s.isActive })
                                          .ToArray(),
            };
            SessionState.SetString(sceneSetupKey, JsonUtility.ToJson(setup));
        }

        private void RestoreSceneSetupOnce()
        {
            EditorApplication.update -= RestoreSceneSetupOnce;

            string json = SessionState.GetString(sceneSetupKey, string.Empty);
            SessionState.EraseString(sceneSetupKey);
            if (string.IsNullOrEmpty(json)) return;

            SavedScene[] saved = JsonUtility.FromJson<SavedSceneSetup>(json).scenes;
            if (saved == null || saved.Length == 0) return;

            // An untitled scene has no path to reopen; it was empty or clean (dirty ones refuse the
            // run), so the scene the run left behind stands in for it.
            if (saved.Any(s => string.IsNullOrEmpty(s.path)))
            {
                Debug.LogWarning($"[{name}] The scene open before the run was untitled; leaving the run's scene open.");
                return;
            }

            EditorSceneManager.RestoreSceneManagerSetup(
                saved.Select(s => new SceneSetup { path = s.path, isLoaded = s.isLoaded, isActive = s.isActive })
                     .ToArray());
        }
    }
}
