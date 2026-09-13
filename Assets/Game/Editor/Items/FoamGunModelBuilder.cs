// Puts the foam gun on the squirter model, and re-seats everything the prefab hangs off it.
//
// The gun's geometry moved from `foam_gun.fbx` — a 0.50 m one-hander built from the sprayer kit —
// to `squirter.fbx`, a hand-built 1.30 m two-hander with its own tank, hoses and a second handle.
// Nothing about how the gun BEHAVES changed: the artifact, the reservoir, the spray VFX and the
// netcode are the same components on the same root. What changed is every number that was measured
// against the old mesh, and there are five of them — the muzzle, the palm, the gauge, the collider
// and the hold pose. A model swap that leaves any one behind looks like a working gun with its jet
// coming out of the middle of the barrel.
//
// A script rather than hand-edited YAML because the model is nested as a prefab instance: the
// fileIDs of the FBX's own nodes are assigned by the importer, so the swap cannot be written by
// hand without guessing them. Re-running is harmless and is how the numbers below get re-applied
// after the model is re-exported.
//
// Run from: Tools ▸ SpaceGame ▸ Items ▸ Rebuild Foam Gun Model
using SpaceGame.Items;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    /// <summary>Re-seats the foam gun prefab on the squirter model.</summary>
    public static class FoamGunModelBuilder
    {
        private const string PrefabPath = "Assets/Game/Prefabs/Items/Artifacts/Gadgets/FoamGun.prefab";
        private const string ModelPath = "Assets/Game/Art/Models/Items/squirter.fbx";

        /// <summary>The name the rest of the prefab, and this script, know the mesh subtree by.</summary>
        private const string ModelName = "Model";

        // ── The markers ────────────────────────────────────────────────────────
        //
        // Printed by models/gear/squirter_export.py, in the prefab root's own space. The hand-built
        // .blend carries no Marker_* meshes to adopt the way the kit-built models do, so the export
        // measures them off the geometry — the bell's mouth on the barrel axis, the palm inside the
        // rear handle, the readable flank of the tank — and prints them for this file. Re-export and
        // they are printed again; nothing here has to be re-derived by eye.

        /// <summary>The bell's mouth, on the barrel axis. Where a dab leaves the gun.</summary>
        private static readonly Vector3 MuzzleLocal = new(0f, 0f, 0.650f);

        /// <summary>
        /// The palm, inside the rear handle rather than on its surface — a hand closes AROUND a
        /// grip, and a point on the skin holds the gun a hand's thickness away from itself.
        /// </summary>
        private static readonly Vector3 PalmLocal = new(0f, -0.142f, -0.261f);

        /// <summary>
        /// The gauge face, standing 5 mm proud of the tank's outboard flank. The flank is a
        /// cylinder, so a flat bar laid on the tangent plane sinks into it at both ends; 5 mm is
        /// that sag over the bar's own half-length.
        /// </summary>
        private static readonly Vector3 GaugeLocal = new(-0.144f, -0.330f, 0.030f);

        /// <summary>
        /// The gauge reads from the side of the tank, so its bar lies along the gun rather than
        /// across it. <see cref="SupplyGauge"/> fills the anchor along its own local +X, which this
        /// turn puts on the prefab's +Z.
        /// </summary>
        private static readonly Vector3 GaugeEuler = new(0f, -90f, 0f);

        /// <summary>
        /// Half the bar's length, and where <c>Gauge_Anchor</c> sits: the fill has to grow from the
        /// LOW end of the track, so the pivot is one half-length back along the bar from its centre.
        /// </summary>
        private const float GaugeHalfLength = 0.034f;

        // ── The hand ───────────────────────────────────────────────────────────

        /// <summary>
        /// Longest axis in the hand, in metres. Between the flamethrower's 0.9 and the dragon
        /// bazooka's 1.25, which is where the silhouette belongs: this is visibly the bigger of the
        /// two sprayers and just as visibly not a shoulder-fired launcher (<c>GDC-L1-UX-0003</c> —
        /// a bracket buys the player a readable silhouette, so a new item earns its own place on
        /// the ladder rather than borrowing a neighbour's).
        ///
        /// <para>
        /// The old one-handed gun was 0.83. The model is 1.5x the length it was and needs two hands
        /// on it, so the number moves with the mesh — leaving it would hold a two-handed sprayer at
        /// a hand tool's size.
        /// </para>
        /// </summary>
        private const float HoldSize = 1.05f;

        /// <summary>
        /// Size on the pack, on the gear wall and lying in the sand — 0 meaning "the same as the
        /// hand". The old gun carried 0.63 against a hand size of 0.83, a divergence
        /// <c>PackSizeTests</c> allows only for an item listed there with its reason, and the gun
        /// never was. It is cleared rather than re-guessed: the flamethrower, this gun's sibling
        /// two-handed sprayer, follows the hand, and the number 0.63 was authored for a 0.5 m
        /// one-hander that no longer exists.
        /// </summary>
        private const float PackSize = 0f;

        [MenuItem("Tools/SpaceGame/Items/Rebuild Foam Gun Model")]
        public static void Build()
        {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
            {
                Debug.LogError($"[FoamGun] No model at {ModelPath}. Run " +
                               "models/gear/squirter_export.py first.");
                return;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            if (root == null) { Debug.LogError($"[FoamGun] No prefab at {PrefabPath}."); return; }

            try
            {
                if (!SwapModel(root, model)) return;

                Seat(root, "Muzzle", MuzzleLocal, Vector3.zero);
                Seat(root, "GripPoint", PalmLocal, Vector3.zero);

                // The bar and the pivot it grows from are two objects a half-length apart, along
                // the bar's own axis — which the turn above puts on +Z.
                Seat(root, "Gauge_Track", GaugeLocal, GaugeEuler);
                Seat(root, "Gauge_Anchor", GaugeLocal - new Vector3(0f, 0f, GaugeHalfLength),
                     GaugeEuler);

                WireGrip(root);
                ClearIris(root);

                // The collider is the shape of the item and is measured off the meshes, so the new
                // model has to be in place before this runs. Also re-derives the world size and the
                // body, all of which were fitted to the old mesh.
                ItemWorldPresence.Apply(root);

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            Verify();
        }

        /// <summary>
        /// Replace the nested model with the squirter, at the root's own origin — the export puts
        /// the model's origin on the barrel axis precisely so this is a zero transform.
        /// </summary>
        private static bool SwapModel(GameObject root, GameObject model)
        {
            Transform existing = root.transform.Find(ModelName);
            if (existing != null) Object.DestroyImmediate(existing.gameObject);

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            if (instance == null)
            {
                Debug.LogError("[FoamGun] Could not instantiate the squirter model.");
                return false;
            }

            instance.name = ModelName;
            instance.transform.SetParent(root.transform, false);
            instance.transform.SetSiblingIndex(0);
            return true;
        }

        private static void Seat(GameObject root, string name, Vector3 position, Vector3 euler)
        {
            Transform target = root.transform.Find(name);
            if (target == null)
            {
                Debug.LogWarning($"[FoamGun] No '{name}' on the prefab; nothing re-seated.");
                return;
            }

            target.localPosition = position;
            target.localRotation = Quaternion.Euler(euler);
        }

        /// <summary>
        /// The hand: the palm it closes on, the pose the upper body takes, the size it is drawn at,
        /// and the subtree all of that is measured against.
        /// </summary>
        private static void WireGrip(GameObject root)
        {
            var grip = root.GetComponent<ItemGrip>();
            if (grip == null) { Debug.LogError("[FoamGun] No ItemGrip on the prefab."); return; }

            var so = new SerializedObject(grip);
            so.FindProperty("gripPoint").objectReferenceValue = root.transform.Find("GripPoint");
            so.FindProperty("holdStyle").enumValueIndex = (int)ItemGrip.HoldStyle.TwoHanded;
            so.FindProperty("holdSize").floatValue = HoldSize;
            so.FindProperty("packSize").floatValue = PackSize;

            // Measured against the mesh alone. The prefab also carries particle systems, and the
            // sizing walks meshes only — but the size reference is what says which meshes, and it
            // pointed at the model that is no longer there.
            so.FindProperty("sizeReference").objectReferenceValue = root.transform.Find(ModelName);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// The squirter has no shutter. <see cref="FoamGunNozzle"/> treats a missing iris as "this
        /// gun does not have one" and skips it, so this is a cleared reference rather than a
        /// dangling one into a model that is gone.
        /// </summary>
        private static void ClearIris(GameObject root)
        {
            var nozzle = root.GetComponent<FoamGunNozzle>();
            if (nozzle == null) { Debug.LogError("[FoamGun] No FoamGunNozzle on the prefab."); return; }

            var so = new SerializedObject(nozzle);
            so.FindProperty("iris").objectReferenceValue = null;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Read back what landed on disk. Unity discards prefab saves outright when the
        /// AssetDatabase is read-only and says nothing about it, so the only proof a rebuild
        /// happened is the saved asset itself.
        /// </summary>
        private static void Verify()
        {
            AssetDatabase.SaveAssets();

            var saved = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (saved == null) { Debug.LogError("[FoamGun] The prefab did not reload."); return; }

            Transform model = saved.transform.Find(ModelName);
            var grip = saved.GetComponent<ItemGrip>();
            Bounds bounds = ItemBounds.Measure(saved, model);

            Debug.Log($"[FoamGun] Model '{(model == null ? "MISSING" : model.name)}' " +
                      $"{bounds.size.x:F3} x {bounds.size.y:F3} x {bounds.size.z:F3} m, " +
                      $"held {grip.Style} at {grip.HoldSize:F2} m, " +
                      $"muzzle {saved.transform.Find("Muzzle").localPosition}, " +
                      $"palm {grip.GripPoint.localPosition}.");
        }
    }
}
