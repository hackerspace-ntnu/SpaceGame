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
        /// <summary>The STOWED wings — folded shut, 1.97 m — worn out in the world.</summary>
        private const string WornModelPath =
            "Assets/Game/Art/Models/Vehicles/Ornithopter/ornithopter_worn.fbx";

        /// <summary>
        /// The OPEN wings — spread, 5.51 m — worn on the gear screen, where the camera is flown
        /// round to look at the player's own back and the wings get to be wings.
        ///
        /// <para>
        /// The two file names read backwards — <c>ornithopter_worn</c> is the one worn in ordinary
        /// play — because the second name predates the split. Both come out of the same generator
        /// (<c>ornithopter_worn.py</c>, with and without <c>--spread</c>) at the same scale and on
        /// the same two rail-tip origins, which is what lets one be swapped for the other with
        /// nothing moving.
        /// </para>
        /// </summary>
        private const string InspectModelPath =
            "Assets/Game/Art/Models/Vehicles/Ornithopter/ornithopter_worn_on_person.fbx";

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
        /// <b>5.51 → 1.97 → 1.86 → 2.64 on 2026-09-05</b>, because the wings are now STOWED rather than spread:
        /// folded shut against their own mounts and hung behind the pack. The change is entirely
        /// in the .blend and this number only follows it — the wings are the same size, the same
        /// twelve parts and the same 8,736 triangles at the same scale, folded. The steps after
        /// 1.97 are hand edits to the .blend, re-opening the fold rather than tightening it: at
        /// 2.64 m the wings set the silhouette's width again, and their pivots have been moved out
        /// to x = ±0.72…1.39 — OFF the lash rail's bar tips at ±0.885 that the mount is measured
        /// onto. See the Ornithopter.md gotcha; this constant only follows the exporter.
        /// </para>
        /// <para>
        /// <b>Never fold or grow the wings by moving this number.</b> It is a uniform scale about
        /// the rail, so changing it walks the two shoulder pivots off the bar tips they are
        /// measured onto. Growing them that way was tried on 2026-09-04 and hung the tips through
        /// the ground; shrinking them that way would have made a smaller MACHINE rather than a
        /// folded one. Both belong in <c>ornithopter_worn.py</c>'s pose, and this value is then
        /// copied from what the exporter prints.
        /// </para>
        /// </summary>
        private const float WornSize = 2.64f;

        /// <summary>
        /// The same measurement for the gear screen's spread wings, whose span is a different
        /// number because they are a different model — not the same model drawn larger.
        ///
        /// <para>
        /// Both are printed by <c>ornithopter_worn_export.py</c>, which ships the pair and refuses
        /// to ship a pair that disagree on part count. Sizing both from <see cref="WornSize"/>
        /// would squeeze 5.51 m of wing into 1.97 m and drag the shoulder pivots off the rail tips
        /// they are authored onto — the same failure as scaling the worn wings by hand.
        /// </para>
        /// </summary>
        private const float WornInspectSize = 5.51f;

        /// <summary>
        /// Rebuild <c>WingPack.prefab</c> from scratch.
        ///
        /// <para>
        /// <b>STILL LOSSY — measured 2026-09-05, against the claim that it stopped being so.</b>
        /// It builds a fresh <c>GameObject</c> and <c>SaveAsPrefabAsset</c>s over the path, so
        /// anything not written below is stripped on every run. The four components a 2026-09-03
        /// pass added are here; four *values* added since are not, and a re-run silently dropped
        /// all four:
        /// </para>
        /// <list type="bullet">
        /// <item><c>ItemGrip.confinedToSurfaces</c> = Rack (6) + WallGrid (7) → empty, so the folded
        /// craft became stowable on the back panels and wings. Caught by
        /// <c>WingPackStowTests.OnlyTheRackAndTheGearWallTakeTheFoldedCraft</c>.</item>
        /// <item><c>SaveableEntity.prefabId</c> → blank. <c>OnValidate</c> stamps it in memory so the
        /// Inspector looks right while the ASSET ships empty, and anything spawned from it can never
        /// be restored. Caught by <c>SaveWiringOnDiskTests</c>; fixed by
        /// <c>Tools ▸ Save System ▸ Wire Saveable Prefabs</c>.</item>
        /// <item><c>NetworkObject.GlobalObjectIdHash</c> 1923410474 → 0, which is the hash a
        /// script-built prefab ships with and which fails only on clients.</item>
        /// <item>The <c>RigidbodySaveable</c> component → gone.</item>
        /// </list>
        /// <para>
        /// So: <b>prefer a surgical patch through <c>PrefabUtility.LoadPrefabContents</c></b> over a
        /// re-run, and if you do re-run this, run the save wiring afterwards and check those four
        /// against git. Anything added to the prefab from now on belongs in this method — that rule
        /// has been stated before and broken four times since.
        /// </para>
        /// </summary>
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
        /// <b>Two of them, and the player sees a different one in each place.</b> Out in the world
        /// the wings are STOWED — folded shut, 1.97 m, so a walking character is not wearing a
        /// wingspan. On the gear screen they are SPREAD, 5.51 m, because that screen is the one
        /// place a player looks at their own back on purpose and the camera is flown round for it.
        /// Both are the same machine at the same scale on the same two rail-tip origins, so
        /// <see cref="WornSeat"/> swaps them by re-seating and nothing moves.
        /// </para>
        /// <para>
        /// Both switched off here, on the asset, and switched on by <see cref="WornSeat"/> through
        /// <see cref="WornVisual"/>. That is not belt-and-braces: <c>ItemBounds</c> measures only
        /// what is switched on within the item, and <see cref="PackSizeForRack"/> below measures
        /// this same root — a visible pair of wings would have the folded craft sized as if it
        /// filled the rack several times over, and the hand size would shrink the bundle to a
        /// splinter. The wingsuit shipped exactly that bug with its flight wings once, and a
        /// second visible model here would be a second way to make it.
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

            // The gear screen's model, nested the same way and switched off the same way. Absent is
            // survivable — WornVisual falls back to the worn model — so this warns rather than
            // failing the build: a pack with no spread wings still works, it just stops being
            // interesting on the one screen built to look at it.
            GameObject inspectModel = AssetDatabase.LoadAssetAtPath<GameObject>(InspectModelPath);
            if (inspectModel == null)
            {
                Debug.LogWarning($"[WingPack] No spread model at {InspectModelPath}. Run " +
                                 "_Source~/models/vehicles/ornithopter_worn_export.py. Without it " +
                                 "the gear screen shows the stowed wings.");
            }
            else
            {
                var inspect = (GameObject)PrefabUtility.InstantiatePrefab(inspectModel);
                inspect.name = WornVisual.InspectChildName;
                inspect.transform.SetParent(root.transform, false);
                inspect.SetActive(false);
            }

            WornFit fit = root.AddComponent<WornFit>();
            var so = new SerializedObject(fit);

            // Only the fallback for a back with no rig shouldered on it. With the rig on — which
            // is every player, always — WornSeat takes the position off the lash rail instead, and
            // the wings' own pivots reach out along it to its two tips.
            SetVector(so, "localPosition", new Vector3(0f, 0.05f, -0.22f));
            SetVector(so, "localEuler", Vector3.zero);
            SetFloat(so, "size", WornSize);
            SetFloat(so, "inspectSize", WornInspectSize);
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
