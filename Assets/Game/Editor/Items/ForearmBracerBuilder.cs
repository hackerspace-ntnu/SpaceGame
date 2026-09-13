// Builds the bracer every player wears on both forearms, and puts the component that seats it onto
// the player:
//
//   Assets/Game/Prefabs/Items/Equipment/ForearmBracer.prefab      the bracer itself
//   Assets/Game/Prefabs/Characters/Player/PlayerCharacter.prefab  gains a ForearmBracers
//
// The prefab carries a model and a GauntletFit and nothing else — no PickupableItem, no
// NetworkObject, no SaveableEntity, no collider. It is not an item and cannot be taken off, so
// there is nothing to pick up, nothing to replicate and nothing to save; ForearmBracers says why at
// length. A collider would be the one that mattered: two of them on every player's arms would sit
// in front of the interaction raycast for the whole game.
//
// Re-runnable. The bracer prefab is rebuilt wholesale; the player prefab is only ADDED to, so a
// ForearmBracers already on it merely has its prefab reference re-pointed.
//
// Re-run from: Tools ▸ SpaceGame ▸ Items ▸ Build Forearm Bracers
using System.Linq;
using UnityEditor;
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    public static class ForearmBracerBuilder
    {
        private const string ModelPath = "Assets/Game/Art/Models/Items/gauntlet_base.fbx";
        private const string BracerPrefabPath = "Assets/Game/Prefabs/Items/Equipment/ForearmBracer.prefab";
        private const string PlayerPrefabPath = "Assets/Game/Prefabs/Characters/Player/PlayerCharacter.prefab";

        /// <summary>
        /// The bracer carries NO numbers of its own, exactly as the gear screen's ghosts do not: it
        /// is modelled at true suit scale in the gauntlet family's frame, so <see cref="GauntletFit"/>'s
        /// family defaults are what seats it. A number typed here would be a second source of truth
        /// for where the arm's hardware sits, and the device standing on this deck is seated by the
        /// same defaults through the same call — which is the whole reason a gauntlet lands ON the
        /// deck rather than near it.
        /// </summary>
        private const string BaseMeshPrefix = "Mesh_GauntletBase_";

        /// <summary>
        /// The Mount variation, not Plain: Mount is Plain plus the <c>Deck</c> and its four
        /// <c>Bosses</c>. Those are the hardpoint, and the hardpoint belongs to the arm — an empty
        /// forearm should show a bare deck with somewhere obvious to bolt a device, not a smooth
        /// shell. See <see cref="VerifyBracer"/> for why that is checked rather than assumed.
        /// </summary>
        private const string MountMeshSuffix = "_Mount";

        [MenuItem("Tools/SpaceGame/Items/Build Forearm Bracers")]
        public static void BuildAll()
        {
            GameObject bracer = BuildBracer();
            if (bracer == null) return;
            if (!WireBracers(bracer)) return;

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[ForearmBracers] Built {BracerPrefabPath} and wired ForearmBracers onto " +
                      $"{PlayerPrefabPath}.");
        }

        private static GameObject BuildBracer()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
            {
                Debug.LogError($"[ForearmBracers] No model at {ModelPath}. Run " +
                               "_Source~/models/gear/gauntlet_base_export.py first.");
                return null;
            }

            var root = new GameObject("ForearmBracer");

            // Nested and unpacked, so a model reimport cannot silently rearrange a prefab wired
            // against it. The instance keeps the FBX's own frame — for a gauntlet that frame IS the
            // fit (origin at the wrist, arm down -Z, dorsal face +Y) — so nothing here poses it.
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely,
                                               InteractionMode.AutomatedAction);
            instance.transform.SetParent(root.transform, false);
            instance.name = "Model";

            if (!VerifyBracer(root)) { Object.DestroyImmediate(root); return null; }

            GauntletFit fit = root.AddComponent<GauntletFit>();
            var so = new SerializedObject(fit);
            SerializedFields.SetFloat(so, "cuffScale", GauntletFit.DefaultCuffScale);
            SerializedFields.SetFloat(so, "lengthScale", GauntletFit.DefaultLengthScale);
            SerializedFields.SetFloat(so, "wristGap", GauntletFit.DefaultWristGap);
            so.ApplyModifiedPropertiesWithoutUndo();

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, BracerPrefabPath, out bool ok);
            Object.DestroyImmediate(root);
            if (!ok)
            {
                Debug.LogError($"[ForearmBracers] Unity refused to save {BracerPrefabPath}. This is " +
                               "usually a read-only AssetDatabase, which fails silently otherwise.");
                return null;
            }
            return saved;
        }

        /// <summary>
        /// Every mesh must be one of the shared base's MOUNT parts.
        ///
        /// <para>
        /// An export that let the Plain variation through would put a bracer with no deck on both
        /// arms, and every gauntlet would then stand on nothing — the devices would still be in the
        /// right place, so it would look merely odd rather than broken, and only in play. An export
        /// that let a device through would weld that device to the arm permanently. Both inspect
        /// perfectly. Checked by the shape of the names rather than against a list of the ten, so a
        /// part added to the base later does not fail a build it has not broken.
        /// </para>
        /// </summary>
        private static bool VerifyBracer(GameObject root)
        {
            string[] meshes = root.GetComponentsInChildren<MeshFilter>(true)
                                  .Where(f => f.sharedMesh != null)
                                  .Select(f => f.gameObject.name)
                                  .ToArray();

            if (meshes.Length == 0)
            {
                Debug.LogError($"[ForearmBracers] {ModelPath} has no meshes. The export ran against " +
                               "the wrong collection, or gauntlet_base.blend has been renamed.");
                return false;
            }

            string[] strangers = meshes
                .Where(n => !n.StartsWith(BaseMeshPrefix) || !n.EndsWith(MountMeshSuffix))
                .ToArray();

            if (strangers.Length > 0)
            {
                Debug.LogError($"[ForearmBracers] {ModelPath} carries parts that are not the Mount " +
                               $"variation of the gauntlet base: {string.Join(", ", strangers)}. Fix " +
                               "the 'keep' list in gauntlet_base_export.py rather than deleting them " +
                               "here — the bracer is meant to BE the shared base, not a copy of it.");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Add <see cref="ForearmBracers"/> to the BASE player prefab and point it at the bracer.
        /// The base rather than the <c>PlayerCharacterNetworked</c> variant because that is where
        /// this project keeps controllers — <c>BodyEquipmentController</c>, whose resolved arm sites
        /// this reads, is on the base too — and only network components live on the variant. Both
        /// prefabs get it either way, the variant by inheritance.
        /// </summary>
        private static bool WireBracers(GameObject bracer)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            if (root == null)
            {
                Debug.LogError($"[ForearmBracers] No player prefab at {PlayerPrefabPath}.");
                return false;
            }

            try
            {
                var bracers = root.GetComponent<ForearmBracers>();
                if (bracers == null) bracers = root.AddComponent<ForearmBracers>();

                var so = new SerializedObject(bracers);
                SerializedFields.Set(so, "bracerPrefab", bracer);
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath, out bool saved);
                if (!saved)
                    Debug.LogError($"[ForearmBracers] Unity refused to save {PlayerPrefabPath}. This " +
                                   "is usually a read-only AssetDatabase, which fails silently otherwise.");
                return saved;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
