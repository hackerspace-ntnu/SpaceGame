// Builds the body screen's two placeholder ("ghost") prefabs from their FBX files, and puts the
// component that drives the whole screen onto the player:
//
//   Assets/Game/Prefabs/Items/Equipment/Ghosts/GhostGauntlet.prefab   what an empty forearm shows
//   Assets/Game/Prefabs/Items/Equipment/Ghosts/GhostBack.prefab       what an empty back shows
//   Assets/Game/Prefabs/Characters/Player/PlayerCharacter.prefab      gains a BodyFocusSession
//
// Neither ghost prefab is ever instantiated as itself. BodySite makes a DisplayCopy of it — no
// scripts, no colliders, no network identity — seats that copy through the very call that wears the
// real thing (ForearmSeat.Apply / WornSeat.Apply, reading the fit off the PREFAB because the copy
// has had its components stripped) and repaints it translucent. So these prefabs carry a model and
// a fit and nothing else: no PickupableItem, no NetworkObject, no SaveableEntity.
//
// That also settles the two questions every new thing here has to answer. Multiplayer: nothing is
// networked and nothing needs registering, because a ghost only ever exists on the machine whose
// player opened the screen — the peers see that player standing still, and gear only moves through
// IBodyEquipment.RequestMove, which has its own server RPC. Persistence: there is no runtime state
// to save; a ghost dies with the session, and what the player is actually WEARING is saved by the
// body equipment slots, not by this.
//
// Re-runnable. The two ghost prefabs are rebuilt wholesale; the player prefab is only ADDED to, so
// a BodyFocusSession whose shot and feel have already been tuned in the Inspector keeps its
// numbers and merely has its two placeholder references re-pointed.
//
// Re-run from: Tools ▸ SpaceGame ▸ Items ▸ Build Gear Ghosts
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    public static class GearGhostBuilder
    {
        private const string DeviceModelPath = "Assets/Game/Art/Models/Items/ghost_device.fbx";
        private const string FrameModelPath = "Assets/Game/Art/Models/Items/ghost_mount_frame.fbx";
        private const string GauntletPrefabPath = "Assets/Game/Prefabs/Items/Equipment/Ghosts/GhostGauntlet.prefab";
        private const string BackPrefabPath = "Assets/Game/Prefabs/Items/Equipment/Ghosts/GhostBack.prefab";
        private const string PlayerPrefabPath = "Assets/Game/Prefabs/Characters/Player/PlayerCharacter.prefab";

        // The gauntlet ghost carries NO numbers of its own. It is a blank device standing on the
        // deck of the bracer the player is already wearing — authored at true suit scale in the
        // gauntlet family's frame against that same deck — so it wears GauntletFit's own family
        // defaults, which is what makes the ghost a promise rather than a lookalike: a player who
        // sees it and then places a real gauntlet watches the device land in exactly the same place
        // at exactly the same size, because both went through ForearmSeat.Apply with the same fit.
        // A number typed here would be a second source of truth for where a gauntlet sits.
        // rollDegrees is left at its zero default for the same reason: roll exists to correct a
        // model whose dorsal face is not its own +Y, and this one's is, by construction.
        //
        // It stopped being a ghost BRACER on 2026-09-04, when the bracer became permanent. A
        // translucent copy of a solid thing six centimetres away says nothing; what an empty slot
        // is missing is the device, so that is what is drawn.

        /// <summary>
        /// Where the mount frame stands on the spine: the wing pack's own seat, taken from its
        /// WornFit, so the ghost of an empty back promises the place the one back item the game
        /// has actually lands in.
        /// </summary>
        private static readonly Vector3 BackLocalPosition = new(0f, 0.05f, -0.22f);

        /// <summary>
        /// No correction. The frame is modelled standing up its own +Z in Blender, which the FBX
        /// axis conversion turns into +Y — already along the spine. <see cref="VerifyFrame"/> is
        /// what stops that assumption rotting silently.
        /// </summary>
        private static readonly Vector3 BackLocalEuler = Vector3.zero;

        /// <summary>
        /// Metres across the shoulders, which is the frame's own authored width — so 1:1. Unlike
        /// the wing pack (1.26 m, a pack drawn to a chosen size) this model was built at the size
        /// it is meant to be seen at; the fit still names it, because <see cref="WornFit"/> means
        /// "draw it this big" and a silent 0 there would wear the raw prefab scale instead.
        /// </summary>
        private const float BackSize = 0.9f;

        /// <summary>
        /// The name every object in the gauntlet ghost's FBX must carry. See
        /// <see cref="VerifyGauntlet"/> for why that is checked.
        /// </summary>
        private const string GhostDeviceMeshPrefix = "Mesh_GhostDevice_";

        [MenuItem("Tools/SpaceGame/Items/Build Gear Ghosts")]
        public static void BuildAll()
        {
            // Both are attempted even when the first fails, so one run reports everything that is
            // wrong rather than one thing at a time.
            GameObject gauntlet = BuildGauntlet();
            GameObject back = BuildBack();
            if (gauntlet == null || back == null) return;

            if (!WireSession(gauntlet, back)) return;

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[GearGhosts] Built {GauntletPrefabPath} and {BackPrefabPath}, and wired " +
                      $"BodyFocusSession onto {PlayerPrefabPath}.");
        }

        // ─────────────────────────── The two ghosts ───────────────────────────

        private static GameObject BuildGauntlet()
        {
            GameObject model = LoadModel(DeviceModelPath);
            if (model == null) return null;

            var root = new GameObject("GhostGauntlet");
            NestModel(model, root.transform);

            if (!VerifyGauntlet(root)) { Object.DestroyImmediate(root); return null; }

            GauntletFit fit = root.AddComponent<GauntletFit>();
            var so = new SerializedObject(fit);
            SerializedFields.SetFloat(so, "cuffScale", GauntletFit.DefaultCuffScale);
            SerializedFields.SetFloat(so, "lengthScale", GauntletFit.DefaultLengthScale);
            SerializedFields.SetFloat(so, "wristGap", GauntletFit.DefaultWristGap);
            so.ApplyModifiedPropertiesWithoutUndo();

            return SaveTo(root, GauntletPrefabPath);
        }

        private static GameObject BuildBack()
        {
            GameObject model = LoadModel(FrameModelPath);
            if (model == null) return null;

            var root = new GameObject("GhostBack");
            NestModel(model, root.transform);

            if (!VerifyFrame(root)) { Object.DestroyImmediate(root); return null; }

            WornFit fit = root.AddComponent<WornFit>();
            var so = new SerializedObject(fit);
            SerializedFields.SetVector3(so, "localPosition", BackLocalPosition);
            SerializedFields.SetVector3(so, "localEuler", BackLocalEuler);
            SerializedFields.SetFloat(so, "size", BackSize);
            so.ApplyModifiedPropertiesWithoutUndo();

            return SaveTo(root, BackPrefabPath);
        }

        // ─────────────────────────── The player ───────────────────────────

        /// <summary>
        /// Add a <see cref="BodyFocusSession"/> to the BASE player prefab and point it at the two
        /// ghosts. The base rather than the <c>PlayerCharacterNetworked</c> variant because that is
        /// where this project keeps savers and controllers — <c>BodyEquipmentController</c>, which
        /// the session reads its bones and sockets off, is on the base too — and only the network
        /// components live on the variant.
        ///
        /// <para>
        /// Through <c>LoadPrefabContents</c> rather than by instantiating and re-saving: the player
        /// prefab is a large hand-wired asset, and <c>SaveAsPrefabAsset</c> over an instance is
        /// lossy. This edits the asset in place and adds nothing but the one component.
        /// </para>
        /// </summary>
        private static bool WireSession(GameObject gauntlet, GameObject back)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            if (root == null)
            {
                Debug.LogError($"[GearGhosts] No player prefab at {PlayerPrefabPath}.");
                return false;
            }

            try
            {
                // Kept if it is already there, so a session tuned in the Inspector — the shot, the
                // fly-out, the hit padding — survives a rebuild of the ghosts. Only the two
                // references below are (re)written.
                var session = root.GetComponent<BodyFocusSession>();
                if (session == null) session = root.AddComponent<BodyFocusSession>();

                var so = new SerializedObject(session);
                SerializedFields.Set(so, "gauntletPlaceholder", gauntlet);
                SerializedFields.Set(so, "backPlaceholder", back);
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath, out bool saved);
                if (!saved) LogSaveFailure(PlayerPrefabPath);
                return saved;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // ─────────────────────────── The FBX ───────────────────────────

        private static GameObject LoadModel(string path)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (model == null)
                Debug.LogError($"[GearGhosts] No model at {path}. Run the matching export in " +
                               "_Source~/models/gear (ghost_device_export.py, ghost_mount_frame_export.py) first.");
            return model;
        }

        /// <summary>
        /// Nest the FBX under the prefab root and unpack it, so a model reimport cannot silently
        /// rearrange a prefab wired against it. The instance keeps the FBX's own frame — for the
        /// gauntlet that frame IS the fit (origin at the wrist, arm down -Z, dorsal face +Y), so
        /// nothing here may pose it.
        /// </summary>
        private static void NestModel(GameObject model, Transform parent)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely,
                                               InteractionMode.AutomatedAction);
            instance.transform.SetParent(parent, false);
            instance.name = "Model";
        }

        /// <summary>
        /// Every mesh in the gauntlet ghost's FBX must belong to the ghost device.
        ///
        /// <para>
        /// The trap this catches is the export being pointed back at <c>gauntlet_base.blend</c>,
        /// which is what it shipped before the bracer became permanent. That would draw a
        /// translucent bracer over the solid one the player is already wearing — a shimmering
        /// double image on both arms that reads as a rendering fault rather than as an empty slot.
        /// An export that shipped nothing at all would draw an empty site, which on screen is
        /// indistinguishable from a slot the screen does not know about. Both inspect perfectly and
        /// are only visible in play — exactly the silent failure this feature exists to avoid.
        /// </para>
        /// <para>
        /// Checked by the shape of the names rather than against a list of the two, so a part added
        /// to the ghost later does not fail a build it has not broken.
        /// </para>
        /// </summary>
        private static bool VerifyGauntlet(GameObject root)
        {
            string[] meshes = root.GetComponentsInChildren<MeshFilter>(true)
                                  .Where(f => f.sharedMesh != null)
                                  .Select(f => f.gameObject.name)
                                  .ToArray();

            if (meshes.Length == 0)
            {
                Debug.LogError($"[GearGhosts] {DeviceModelPath} has no meshes. The export ran against " +
                               "the wrong collection, or ghost_device.blend has been renamed.");
                return false;
            }

            string[] strangers = meshes
                .Where(n => !n.StartsWith(GhostDeviceMeshPrefix))
                .ToArray();

            if (strangers.Length > 0)
            {
                Debug.LogError($"[GearGhosts] {DeviceModelPath} carries parts that are not the ghost " +
                               $"device: {string.Join(", ", strangers)}. Fix ghost_device_export.py " +
                               "rather than deleting them here — in particular, this is what it looks " +
                               "like when the export has been pointed back at gauntlet_base.blend.");
                return false;
            }

            return true;
        }

        /// <summary>
        /// The frame must have arrived standing across the shoulders: wider than it is tall, and
        /// thinner than either — a plate facing the same way the player does.
        ///
        /// <para>
        /// <see cref="BackLocalEuler"/> applies no rotation, so the FBX's own axis conversion is
        /// the only thing putting the frame the right way up. If that changes, the frame is worn
        /// edge-on or lying flat, and a rack drawn edge-on over the shoulders is a thin line most
        /// players would read as nothing at all. Measured with <see cref="ItemBounds"/>, which is
        /// the same measurement <c>WornSeat.Apply</c> will make of it.
        /// </para>
        /// </summary>
        private static bool VerifyFrame(GameObject root)
        {
            Vector3 size = ItemBounds.Measure(root, null).size;
            if (size.x > size.y && size.z < size.y) return true;

            Debug.LogError($"[GearGhosts] {FrameModelPath} did not arrive standing across the " +
                           $"shoulders — measured {size.x:F2} x {size.y:F2} x {size.z:F2} m, and it " +
                           "should be widest on X, tallest on Y and thinnest on Z. The FBX axis " +
                           "conversion has changed; fix ghost_mount_frame_export.py, or set " +
                           "BackLocalEuler here if the model itself was re-authored.");
            return false;
        }

        // ─────────────────────────── Assets ───────────────────────────

        private static GameObject SaveTo(GameObject root, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path, out bool saved);
            Object.DestroyImmediate(root);

            if (!saved) LogSaveFailure(path);
            return saved ? prefab : null;
        }

        /// <summary>
        /// A save that reports failure rather than being trusted. An MPPM clone opens the project
        /// with a READ-ONLY AssetDatabase, where every write is discarded without an exception —
        /// the build then logs that it succeeded and nothing on disk has changed.
        /// </summary>
        private static void LogSaveFailure(string path) =>
            Debug.LogError($"[GearGhosts] Saving {path} failed. If this editor is an MPPM clone its " +
                           "AssetDatabase is read-only and every write is discarded; run this from the " +
                           "main editor.");
    }
}
