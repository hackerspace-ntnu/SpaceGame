// Builds the inventory side of the ornithopter: the folded pack the player carries, and the
// InventoryItem that puts it in their hotbar.
//
//   Assets/Game/Prefabs/items/WingPack.prefab            the thing held in hand
//   Assets/Game/Resources/Items/Artifacts/WingPack.asset the InventoryItem that references it
//
// The held pack is the actual craft in its stowed configuration: wings swept back along the boom,
// digit spars collapsed onto each other, tail telescoped. That pose is baked to a single static
// mesh in Blender (`_Source~/models/vehicles/wing_pack_folded.py` — the skinned wings make it
// impossible to pose at build time here) and exported hand-sized, so nesting the FBX is all this
// builder has to do.
//
// Re-run from: Tools ▸ Vehicles ▸ Build Wing Pack Item.
using System.Linq;
using UnityEditor;
using UnityEngine;
using SpaceGame.Items;
using SpaceGame.Vehicles.Ornithopter;
using SpaceGame.World;

namespace SpaceGame.EditorTools
{
    public static class WingPackBuilder
    {
        private const string CraftPath =
            "Assets/Game/Prefabs/Agents/Vehicles/Aircraft/DuneOrnithopter.prefab";
        private const string PrefabPath = "Assets/Game/Prefabs/Items/Equipment/WingPack.prefab";
        private const string ItemPath = "Assets/Game/Resources/Items/Artifacts/WingPack.asset";
        private const string FoldedModelPath =
            "Assets/Game/Art/Models/Vehicles/Ornithopter/wing_pack_folded.fbx";
        private const string WornModelPath =
            "Assets/Game/Art/Models/Vehicles/Ornithopter/ornithopter_worn.fbx";

        /// <summary>
        /// How wide the worn wings are drawn across the wearer's back, in metres — and therefore
        /// the scale correction, because the model is authored at exactly this span.
        ///
        /// <para>
        /// Pinned rather than left at zero ("keep the prefab's own scale") on purpose. The model's
        /// two shoulder pivots are placed on the expedition rig's two protruding bar tips, which
        /// were MEASURED off the game at x = ±0.885 m in the spine bone's frame — so the span and
        /// the mount are one number, and an export that changed the span would walk the wings off
        /// the bar silently. Written here, a changed export shows up as wings the wrong size next
        /// to a value somebody can check against the exporter's own printout
        /// (`ornithopter_worn_export.py` prints "pin WornFit.size to ...").
        /// </para>
        /// <para>
        /// <b>3.47 → 5.51 on 2026-09-04</b>, and the enlargement is in the MODEL, not here. The
        /// wings were re-posed at half again their reach and opened out to the side
        /// (`ornithopter_worn.py`'s <c>TARGET_REACH</c> and <c>FLAP</c>), which is the only way to
        /// grow them and keep their roots on the bar tips: this number is a uniform scale about
        /// the rail, so raising IT moves the roots outboard and hangs the tips through the ground.
        /// That was tried first, on 2026-09-04, and both failures were visible immediately. If the
        /// wings need to be bigger again, the change belongs in the .blend.
        /// </para>
        /// </summary>
        private const float WornSize = 5.51f;

        [MenuItem("Tools/Vehicles/Build Wing Pack Item")]
        public static void Build()
        {
            GameObject craft = AssetDatabase.LoadAssetAtPath<GameObject>(CraftPath);
            if (craft == null)
            {
                Debug.LogError($"[WingPack] No craft at {CraftPath}. Run " +
                               "Tools ▸ Vehicles ▸ Build Dune Ornithopter Prefab first.");
                return;
            }

            var root = new GameObject("WingPack");
            BuildFoldedBundle(root);
            BuildWornWings(root);

            WingPackItem item = root.AddComponent<WingPackItem>();
            var so = new SerializedObject(item);
            Set(so, "ornithopterPrefab", craft);
            SetFloat(so, "groundClearance", 0.6f);
            SetFloat(so, "minLaunchClearance", 6f);
            SetFloat(so, "ledgeProbeForward", 1.5f);
            SetInt(so, "groundMask", ~0);
            SetFloat(so, "speedCarry", 1f);
            SetFloat(so, "launchLift", 1.2f);
            // Unlimited: the pack is equipment, not a consumable. -1 is UsableItem's sentinel.
            SetInt(so, "maxUses", -1);
            so.ApplyModifiedPropertiesWithoutUndo();

            // 1.26 m in the hand — tuned by eye against a hand roughly 1.7x a human's, and pinned
            // by ItemScaleLadder as Fitted because a pack worn across the back is sized by the
            // wearer, not by the item ladder. Without a grip it would measure at the 0.30 m
            // no-grip default. The size on the mat is a separate number; see PackSizeForRack.
            ItemGrip grip = root.AddComponent<ItemGrip>();
            var gripSo = new SerializedObject(grip);
            SetFloat(gripSo, "holdSize", 1.26f);
            SetFloat(gripSo, "packSize", PackSizeForRack(root));
            gripSo.ApplyModifiedPropertiesWithoutUndo();

            AddIfPresent(root, "SpaceGame.Items.PickupableItem");

            // Without a NetworkObject a dropped pack exists only on the host and never survives a
            // reload. It was on the prefab and NOT in this builder, so every re-run silently took
            // it away again - see Ornithopter.md.
            Unity.Netcode.NetworkObject netObject = root.AddComponent<Unity.Netcode.NetworkObject>();
            netObject.SynchronizeTransform = true;

            // The body, the collider measured off the folded bundle, the sizing and the netcode
            // that lets another machine watch it be shoved about. One shared block - see
            // ItemWorldPresence. The box it replaces here was three numbers typed by hand.
            ItemWorldPresence.Apply(root);

            // Same story as the NetworkObject above: on the prefab, absent from the builder, and
            // silently stripped by every re-run. prefabId and instanceId are left blank on purpose
            // — SaveableEntity.OnValidate stamps them, and a hand-written id is how two prefabs end
            // up sharing one.
            root.AddComponent<SpaceGame.Core.Persistence.SaveableEntity>();
            root.AddComponent<SpaceGame.Core.Persistence.TransformSaveable>();

            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PrefabPath));
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);

            BuildInventoryItem(saved);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[WingPack] Built {PrefabPath} and {ItemPath}.");
        }

        /// <summary>
        /// The size the folded craft is drawn at once stowed, in the metres
        /// <see cref="ItemGrip.PackSize"/> is authored in — that is, BEFORE
        /// <see cref="PackScale.Factor"/> is multiplied in by <c>ItemFootprint</c>.
        ///
        /// <para>
        /// The wing pack is the one item whose stowed size is decided by the surface rather than by
        /// the hand. It is the whole aircraft, folded, and it is meant to read that way: strapped
        /// across the pack's back it fills the rack edge to edge and hangs off the top and bottom,
        /// so the only place it goes is roughly centred. The rack allows exactly that — overhang
        /// along its own long axis, never across the width the lashing has to reach around
        /// (<c>PackOverhang</c>) — so the width is the constraint and the length is free.
        /// </para>
        /// <para>
        /// Hence: solve for the width, do not type a length. The craft is a sliver of fixed
        /// proportions, so naming its footprint's short side sizes the whole thing, and the number
        /// tracks the rack through a <c>PackScale</c> change or a re-export of the folded mesh
        /// instead of going quietly stale — which is what happened to the hand-computed 1.26 this
        /// replaced, left behind by the 1.5x enlargement.
        /// </para>
        /// <para>
        /// This is also the ceiling, not a preference. Past the rack's width the shape rounds to a
        /// tenth column, which the rack refuses outright and the ship's gear wall — strict on both
        /// axes — has no room to take either. An item that fills the back of the pack is as large
        /// as the wing pack can be and still be storable anywhere.
        /// </para>
        /// </summary>
        private static float PackSizeForRack(GameObject root)
        {
            // SURF_Rack is 9 x 9 cells; the metres are ExpeditionRigWiring.SurfaceTable's, and
            // PackGrid's own doc table mirrors them. Written as a cell count so it follows the cell.
            const int RackColumns = 9;

            // How much of that width to occupy. Short of 1 on purpose: the derived shape ceils the
            // footprint to whole cells, so landing exactly on the ninth column's edge is a coin
            // flip between 9 cells and 10, and 10 is refused by every face on the rig. 0.96 reads
            // as full width and leaves the folded mesh's proportions room to drift on a re-export.
            const float RackWidthFill = 0.96f;

            Vector3 size = ItemBounds.Measure(root, null).size;

            // The footprint is the two widest axes (ItemFootprint.FootprintOf), and its short side
            // is what lies across the rack. PackSize names the LONGEST axis of all three, so the
            // ratio between them is what converts one into the other.
            float acrossTheRack = Mathf.Min(size.x, size.z);
            float longest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));

            if (acrossTheRack < 1e-4f)
            {
                Debug.LogWarning("[WingPack] The folded craft measures nothing across; leaving its " +
                                 "pack size at the hand size. Check the FBX imported.");
                return 0f;
            }

            return PackGrid.Cell * RackColumns * RackWidthFill * longest
                   / (acrossTheRack * PackScale.Factor);
        }

        /// <summary>
        /// Nest the baked folded-craft model. It is exported already hand-sized (~0.95 m long), so
        /// no scale correction belongs here — a wrong size means the export is what to fix. Axes
        /// are this wiring's job though: a bare static mesh arrives in Blender's frame (length on
        /// Y, up on Z), so it gets the standard -90° X that puts the nose on +Z and up on +Y.
        /// </summary>
        private static void BuildFoldedBundle(GameObject root)
        {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(FoldedModelPath);
            if (model == null)
            {
                Debug.LogError($"[WingPack] No folded model at {FoldedModelPath}. Run " +
                               "_Source~/models/vehicles/wing_pack_folded_export.py first.");
                return;
            }

            var visual = (GameObject)PrefabUtility.InstantiatePrefab(model);
            visual.name = "FoldedCraft";
            visual.transform.SetParent(root.transform, false);
            visual.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        }

        /// <summary>
        /// Nest the worn wings — what the pack looks like on somebody's back, as opposed to the
        /// folded bundle above, which is what it looks like in their hand.
        ///
        /// <para>
        /// Switched off here, on the asset, and switched on by <see cref="WornSeat"/> through
        /// <see cref="WornVisual"/>. That is not belt-and-braces: <c>ItemBounds</c> measures only
        /// what is switched on within the item, and <see cref="PackSizeForRack"/> below measures
        /// this same root — a visible 3.5 m pair of wings would have the folded craft sized as if
        /// it filled the rack five times over, and the hand size would shrink the bundle to a
        /// splinter. The wingsuit shipped exactly that bug with its flight wings once.
        /// </para>
        /// <para>
        /// <b>No rotation, unlike the bundle above, and that is the fix for a real bug.</b> An FBX
        /// from `_exportlib` arrives ALREADY converted: every mesh node carries the Blender-to-Unity
        /// position (`(x, y, z) → (−x, z, −y)`) and its own −90 X, with the vertices left in
        /// Blender's axes. So a −90 X on the parent is a SECOND conversion. On the bundle that is
        /// deliberate — it is a hand-held object and the extra turn is what points its length out
        /// of the fist — but this model is authored in the wearer's own frame and any turn at all
        /// moves it off the wearer. Applied here it put the wings 0.6 m below the shoulders and
        /// half a metre behind them, and everything still looked plausibly like a pair of wings.
        /// </para>
        /// </summary>
        private static void BuildWornWings(GameObject root)
        {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(WornModelPath);
            if (model == null)
            {
                Debug.LogError($"[WingPack] No worn model at {WornModelPath}. Run " +
                               "_Source~/models/vehicles/ornithopter_worn_export.py first. " +
                               "Without it the pack is worn as a folded aircraft.");
                return;
            }

            var visual = (GameObject)PrefabUtility.InstantiatePrefab(model);
            visual.name = WornVisual.ChildName;
            visual.transform.SetParent(root.transform, false);
            visual.SetActive(false);

            WornFit fit = root.AddComponent<WornFit>();
            var so = new SerializedObject(fit);

            // Only the fallback for a back with no rig shouldered on it. With the rig on — which
            // is every player, always — WornSeat takes the position off the lash rail instead, and
            // the wings' own pivots reach out along it to its two tips.
            SetVector(so, "localPosition", new Vector3(0f, 0.05f, -0.22f));
            SetVector(so, "localEuler", Vector3.zero);
            SetFloat(so, "size", WornSize);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void BuildInventoryItem(GameObject prefab)
        {
            InventoryItem item = AssetDatabase.LoadAssetAtPath<InventoryItem>(ItemPath);
            bool isNew = item == null;
            if (isNew)
                item = ScriptableObject.CreateInstance<InventoryItem>();

            item.itemName = "Wing Pack";
            item.itemPrefab = prefab;

            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ItemPath));
            if (isNew)
                AssetDatabase.CreateAsset(item, ItemPath);
            else
                EditorUtility.SetDirty(item);
        }

        /// <summary>Add a component by type name if the project has it, so a missing optional
        /// system does not fail the whole build.</summary>
        private static void AddIfPresent(GameObject go, string typeName)
        {
            System.Type t = System.AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType(typeName))
                .FirstOrDefault(x => x != null);
            if (t != null) go.AddComponent(t);
            else Debug.LogWarning($"[WingPack] No type '{typeName}'; skipped.");
        }

        private static void Set(SerializedObject so, string field, Object value)
        {
            SerializedProperty p = Find(so, field);
            if (p != null) p.objectReferenceValue = value;
        }

        private static void SetFloat(SerializedObject so, string field, float value)
        {
            SerializedProperty p = Find(so, field);
            if (p != null) p.floatValue = value;
        }

        private static void SetInt(SerializedObject so, string field, int value)
        {
            SerializedProperty p = Find(so, field);
            if (p != null) p.intValue = value;
        }

        private static void SetVector(SerializedObject so, string field, Vector3 value)
        {
            SerializedProperty p = Find(so, field);
            if (p != null) p.vector3Value = value;
        }

        private static SerializedProperty Find(SerializedObject so, string field)
        {
            SerializedProperty p = so.FindProperty(field);
            if (p == null)
                Debug.LogWarning($"[WingPack] {so.targetObject.GetType().Name} has no serialized " +
                                 $"field '{field}' -- it was renamed; this value is unset.");
            return p;
        }
    }
}
