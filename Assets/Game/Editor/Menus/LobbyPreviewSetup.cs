// Prepares the project for the lobby's rank of astronauts, and takes the Nomad preview harness out
// of the main menu.
//
// Three things have to happen that runtime code cannot do, and one of them is a deletion:
//
//   1. A lightweight astronaut prefab has to exist under a Resources folder. LobbyPreviewRank is
//      built by a runtime screen with no Inspector, so it loads its figure by name rather than being
//      handed a reference. It cannot be PlayerCharacter.prefab — that carries a NetworkObject, an
//      inventory, a weapon, colliders and a camera rig, none of which belong in a menu.
//   2. SuitRecolor has to be on the player prefab, or the colour a player picks in the lobby shows
//      on the four figures and nowhere else. Added to PlayerCharacter, so its networked variant
//      inherits it.
//   3. MainMenu.unity is carrying a scratch preview harness — a Nomad, a camera, a light and a
//      ground plane parked at y=5000 — whose camera fights the menu's own for the screen. Those four
//      objects are removed BY NAME rather than by reverting the scene, because the same working copy
//      also contains menu button anchor moves that have to survive.
//
// Idempotent and safe to re-run: it rebuilds the prefab, adds the component if missing, strips
// whatever harness is present, and leaves an existing anchor exactly where somebody dragged it.
//
// Run from: Tools ▸ SpaceGame ▸ Menus ▸ Setup Lobby Preview
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using SpaceGame.Characters;
using SpaceGame.Presentation;

namespace SpaceGame.EditorTools
{
    public static class LobbyPreviewSetup
    {
        private const string ScenePath = "Assets/Game/Scenes/Core/MainMenu.unity";

        /// <summary>
        /// The player body. Must stay the same file PlayerCharacter.prefab uses, and the one
        /// astronaut_export.py writes — the menu figure and the player are the same character, so a
        /// second copy of the model means skinning fixes land on one of them and not the other.
        /// </summary>
        private const string ModelPath =
            "Assets/Game/Art/Models/Characters/Astronaut/astronaut.fbx";

        private const string ControllerPath =
            "Assets/Game/Art/Animations/Player/AstronautArmature.controller";

        private const string PlayerPrefabPath =
            "Assets/Game/Prefabs/Characters/Player/PlayerCharacter.prefab";

        /// <summary>Must stay under a Resources folder — LobbyPreviewRank loads it by name.</summary>
        private const string PreviewPrefabPath =
            "Assets/Game/Resources/LobbyPreviewAstronaut.prefab";

        /// <summary>
        /// The scratch harness in the working copy. Matched on this prefix because it named every
        /// piece the same way: __NomadPreview, __NomadPreviewCam, __NomadPreviewLight,
        /// __NomadPreviewGround.
        /// </summary>
        private const string HarnessPrefix = "__NomadPreview";

        [MenuItem("Tools/SpaceGame/Menus/Setup Lobby Preview")]
        public static void Run()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogError("[LobbyPreviewSetup] Exit Play mode first — a scene edited during " +
                               "play mode is discarded when play mode ends.");
                return;
            }

            bool builtPrefab = BuildPreviewPrefab();
            bool wiredPlayer = AddRecolorToPlayer();

            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            int stripped = StripNomadHarness(scene);
            bool placedView = EnsureCameraView(scene);
            bool placedAnchor = EnsureAnchor(scene);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            Debug.Log($"[LobbyPreviewSetup] Preview prefab {(builtPrefab ? "built" : "FAILED")}; " +
                      $"SuitRecolor on the player {(wiredPlayer ? "present" : "FAILED")}; " +
                      $"removed {stripped} '{HarnessPrefix}*' object(s); camera view " +
                      $"{(placedView ? "created" : "already in place")}; anchor " +
                      $"{(placedAnchor ? "created" : "already in place")}.");
        }

        // ───────────────────────────────────────────────────────────────── preview prefab

        /// <summary>
        /// Builds the figure the lobby stands in the sand: the astronaut model, an Animator, and
        /// SuitRecolor. Nothing else.
        /// </summary>
        private static bool BuildPreviewPrefab()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
            {
                Debug.LogError($"[LobbyPreviewSetup] No astronaut model at {ModelPath}.");
                return false;
            }

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            instance.name = "LobbyPreviewAstronaut";

            // Unpacked so the saved prefab owns its own hierarchy. Left as a model instance, the
            // Animator and SuitRecolor added below would be overrides on an imported asset, and a
            // reimport of the FBX drops them.
            //
            // The cost of unpacking is that this prefab holds its own copy of the mesh hierarchy and
            // therefore does NOT follow the model: re-exporting the .blend updates PlayerCharacter
            // but leaves the menu figure on the old geometry until this tool is re-run. If the player
            // and the lobby astronaut ever look different, that is the reason.
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely,
                                               InteractionMode.AutomatedAction);

            var animator = instance.GetComponent<Animator>();
            if (animator == null) animator = instance.AddComponent<Animator>();

            animator.runtimeAnimatorController =
                AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
            animator.avatar = AssetDatabase.LoadAssetAtPath<Avatar>(ModelPath);
            animator.applyRootMotion = false;

            // The menu is not gameplay, and a figure standing still does not need to be animated
            // when it is off screen — but it IS on screen the whole time the lobby is open, so
            // culling is left at the default rather than set to CullCompletely, which would freeze
            // the idle whenever the camera framing changed.
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

            if (instance.GetComponent<SuitRecolor>() == null) instance.AddComponent<SuitRecolor>();

            // The model ships with no colliders, but a stray one would sit in the menu catching
            // clicks meant for the buttons behind it.
            foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true))
                Object.DestroyImmediate(collider);

            System.IO.Directory.CreateDirectory(
                System.IO.Path.GetDirectoryName(PreviewPrefabPath) ?? string.Empty);

            PrefabUtility.SaveAsPrefabAsset(instance, PreviewPrefabPath, out bool saved);
            Object.DestroyImmediate(instance);

            if (!saved) Debug.LogError($"[LobbyPreviewSetup] Could not save {PreviewPrefabPath}.");
            return saved;
        }

        // ──────────────────────────────────────────────────────────────────── player prefab

        /// <summary>
        /// Puts SuitRecolor on the player body.
        ///
        /// On the root, where PlayerIdentity also lives, so its GetComponentInChildren finds it
        /// without a serialized reference to leave dangling. On PlayerCharacter rather than on
        /// PlayerCharacterNetworked, so the variant inherits it and there is one place it can be.
        /// </summary>
        private static bool AddRecolorToPlayer()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            if (root == null)
            {
                Debug.LogError($"[LobbyPreviewSetup] No player prefab at {PlayerPrefabPath}.");
                return false;
            }

            try
            {
                var recolor = root.GetComponent<SuitRecolor>();
                if (recolor == null)
                {
                    root.AddComponent<SuitRecolor>();
                    PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
                }

                return true;
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        // ──────────────────────────────────────────────────────────────────────── the scene

        private static int StripNomadHarness(Scene scene)
        {
            int removed = 0;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (!root.name.StartsWith(HarnessPrefix)) continue;

                Object.DestroyImmediate(root);
                removed++;
            }

            return removed;
        }

        /// <summary>
        /// How far the lobby's shot swings off the menu's own heading, in degrees.
        ///
        /// Negative turns left, onto the open dunes and pyramids. The menu's framing points at the
        /// ruin and the three decorative astronauts, which is a good menu picture and a bad lobby one:
        /// there is nowhere in it for four figures to stand that is not already occupied by set
        /// dressing or by the control column.
        /// </summary>
        private const float LobbyViewYaw = -38f;

        /// <summary>
        /// The lobby shot's pitch. Level, unlike the menu's own ~11.6° upward tilt.
        ///
        /// <para>
        /// This is the number that makes the nameplates legible, and it was measured rather than
        /// chosen. A level camera puts the horizon exactly across the middle of the frame, which is
        /// what lifts the astronauts' heads into SKY — white names read there. The menu's upward tilt
        /// pushes the whole rank down the frame so the heads sit against bright sand instead, where
        /// white text disappears.
        /// </para>
        ///
        /// <para>
        /// It also buys the room the footer needs: level at <see cref="RankDistance"/> the feet land
        /// around 76% down the canvas, leaving the bottom band free for Start / Leave.
        /// </para>
        /// </summary>
        private const float LobbyViewPitch = 2f;

        /// <summary>
        /// How far in front of the lobby camera the rank stands.
        ///
        /// Close, because the player picked the framing where the astronauts are the picture. At 5 m
        /// with the project's 60° vertical field of view a figure is about a third of the screen high,
        /// and four of them fill the middle of the frame without the outermost two leaving it.
        /// </summary>
        private const float RankDistance = 4.7f;

        /// <summary>
        /// How far right of centre the rank sits, in metres.
        ///
        /// The left of the frame is not free: the code, Copy and Private controls run down it. Centred
        /// on the camera the leftmost astronaut stood behind them.
        /// </summary>
        private const float RankRightOffset = 0.3f;

        /// <summary>
        /// Creates the pose the camera takes while the lobby is open, if it is not already there.
        ///
        /// Derived from the menu camera rather than authored blind, so the lobby keeps the menu's own
        /// eye height and pitch and only the heading changes. An existing one is never touched — every
        /// re-run would otherwise undo the framing somebody spent time composing.
        /// </summary>
        private static bool EnsureCameraView(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.name == LobbyPreviewRank.CameraViewName)
                    return false;

            Camera camera = FindMenuCamera(scene);
            var view = new GameObject(LobbyPreviewRank.CameraViewName);

            if (camera != null)
            {
                Vector3 euler = camera.transform.rotation.eulerAngles;
                view.transform.SetPositionAndRotation(
                    camera.transform.position,
                    Quaternion.Euler(LobbyViewPitch, euler.y + LobbyViewYaw, 0f));
            }
            else
            {
                Debug.LogWarning("[LobbyPreviewSetup] No camera in the menu scene, so the lobby view " +
                                 "was created at the origin.");
            }

            SceneManager.MoveGameObjectToScene(view, scene);
            return true;
        }

        /// <summary>
        /// Creates the anchor the rank stands on, if it is not already there.
        ///
        /// Placed in front of the LOBBY's camera pose rather than the menu's, because that is the shot
        /// the rank will actually be seen in, and dropped onto whatever is under it so it does not
        /// start hovering at eye height. It is meant to be dragged afterwards — an existing one is
        /// never moved, or every re-run would undo somebody's framing.
        /// </summary>
        private static bool EnsureAnchor(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.name == LobbyPreviewRank.AnchorName)
                    return false;

            Transform eye = FindCameraView(scene) ?? FindMenuCamera(scene)?.transform;
            Vector3 position = Vector3.zero;
            Quaternion rotation = Quaternion.identity;

            if (eye != null)
            {
                Vector3 forward = eye.forward;
                forward.y = 0f;
                if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
                forward.Normalize();

                // Right vector across the view, which is the line the figures are spread along.
                rotation = Quaternion.LookRotation(forward, Vector3.up);

                position = eye.position + forward * RankDistance
                           + rotation * Vector3.right * RankRightOffset;

                position.y = Physics.Raycast(position + Vector3.up * 30f, Vector3.down,
                                             out RaycastHit hit, 100f)
                    ? hit.point.y
                    : eye.position.y - 1.6f;
            }
            else
            {
                Debug.LogWarning("[LobbyPreviewSetup] No camera in the menu scene, so the anchor " +
                                 "was created at the origin. Drag it somewhere the camera can see.");
            }

            var anchor = new GameObject(LobbyPreviewRank.AnchorName);
            anchor.transform.SetPositionAndRotation(position, rotation);
            SceneManager.MoveGameObjectToScene(anchor, scene);

            return true;
        }

        private static Transform FindCameraView(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.name == LobbyPreviewRank.CameraViewName)
                    return root.transform;

            return null;
        }

        /// <summary>
        /// The menu's camera.
        ///
        /// Camera.main is not used: it depends on the MainCamera tag being resolvable, which is
        /// unreliable in the editor immediately after a scene is opened, and this runs at exactly
        /// that moment.
        /// </summary>
        private static Camera FindMenuCamera(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                var camera = root.GetComponentInChildren<Camera>(true);
                if (camera != null) return camera;
            }

            return null;
        }
    }
}
