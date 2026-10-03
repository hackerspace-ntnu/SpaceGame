// Gives every resident the tools of its profession: a tool in the hand and the rest on the belt.
//
// Two halves. The ARCHETYPES say what each profession carries (heldItem, beltItems), because the
// Raxy prefab is shared by every job. The PREFABS get the machinery that turns that into a drawn
// item: a bag, the hand that draws from it, the belt that hangs the rest, the two savers that keep
// both across a reload, and ResidentCarry, which fills the bag from the archetype. Both are
// idempotent; a profession with no row (storyteller, elder, villager) carries nothing.
//
// "The belt" is a worn garment: BeltCarrier hangs things on the mount points of whatever
// GarmentMounts the character wears, so a Raxy in no belt hangs nothing. EnsureBelt dresses every
// resident that wears none, the way a garment is put on by hand: a garment prefab instance as a
// child of the character's root.
//
// Run from: Tools ▸ SpaceGame ▸ Items ▸ Hand Tools ▸ Equip Residents
using System.Linq;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Agents.Residents;
using SpaceGame.Core.Persistence;
using SpaceGame.Items;
using SpaceGame.Presentation;

namespace SpaceGame.EditorTools
{
    public static class RaxyToolLoadouts
    {
        private const string PrefabFolder = "Assets/Game/Prefabs/agents/Characters/Raxy";
        private const string ArchetypeFolder = "Assets/Game/ScriptableObjects/Residents/Archetypes/";
        private const int BagSize = 4;

        /// <summary>The belt a Raxy gets when it wears none, and the one cut to sit over a poncho.</summary>
        private const string StandardBeltName = "Clothes_Belt.standard";
        private const string PonchoBeltName = "Clothes_Belt.poncho";
        private const string PonchoGarmentPrefix = "Clothes_Poncho";

        /// <summary>Archetype asset name → the item in the hand, then the belt items.</summary>
        private static readonly (string Archetype, string Hand, string[] Belt)[] Professions =
        {
            ("WaterRunner", "Carry_Bucket_Wood", new[] { "Tool_WaterSkin" }),
            ("Waterkeeper", "Tool_HandPump", new[] { "Tool_Wrench_Open" }),
            ("Forager", "Carry_Basket_Open", new[] { "Tool_Sickle", "Tool_HerbKnife" }),
            ("Gardener", "Tool_WateringCan", new[] { "Tool_Dibber", "Tool_Sickle" }),
            ("Smith", "Tool_Hammer", new[] { "Tool_Chisel", "Tool_Whetstone" }),
            ("Apprentice", "Tool_Hammer", new[] { "Tool_Chisel", "Tool_Trowel" }),
            ("Tinker", "Tool_Wrench_Ring", new[] { "Tool_Pliers", "Tool_PatchKit" }),
            ("Salvager", "Tool_Crowbar", new[] { "Tool_WireCutters", "Tool_Wrench_Open" }),
            ("Cook", "Tool_Ladle", new[] { "Tool_Cleaver", "Tool_Flask" }),
            ("Brewer", "Tool_Ladle", new[] { "Tool_Flask", "Carry_Tank_Water" }),
            ("Herder", "Tool_HerdingCrook", new[] { "Tool_Sling" }),
            ("Drover", "Tool_Lasso", System.Array.Empty<string>()),
            ("Hunter", "Tool_ElectricHarpoonGun", new[] { "Tool_SkinningKnife", "Tool_HuntingKnife" }),
            ("Scout", "Tool_Spyglass", new[] { "Tool_HuntingKnife" }),
            ("Guard", "Tool_Spear_Stone", new[] { "Tool_SignalHorn" }),
            ("Lookout", "Tool_SignalFlag", new[] { "Tool_SignalHorn" }),
            ("Trader", "Tool_HandScales", System.Array.Empty<string>()),
        };

        [MenuItem("Tools/SpaceGame/Items/Hand Tools/Equip Residents")]
        public static void Apply()
        {
            int archetypes = Professions.Count(AssignArchetype);

            int prefabs = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabFolder }))
                if (WirePrefab(AssetDatabase.GUIDToAssetPath(guid))) prefabs++;

            AssetDatabase.SaveAssets();
            Debug.Log($"[HandTools] {archetypes} professions carry tools; {prefabs} Raxy prefabs can draw them.");
        }

        private static bool AssignArchetype((string Archetype, string Hand, string[] Belt) row)
        {
            var archetype = AssetDatabase.LoadAssetAtPath<ResidentArchetype>(ArchetypeFolder + row.Archetype + ".asset");
            InventoryItem hand = Item(row.Hand);
            InventoryItem[] belt = row.Belt.Select(Item).ToArray();
            if (archetype == null || hand == null || belt.Any(b => b == null))
            {
                Debug.LogError($"[HandTools] {row.Archetype}: missing archetype or item. Build the tools first.");
                return false;
            }

            archetype.heldItem = hand;
            archetype.beltItems = belt;
            EditorUtility.SetDirty(archetype);
            return true;
        }

        private static InventoryItem Item(string id) =>
            AssetDatabase.LoadAssetAtPath<InventoryItem>($"Assets/Game/Resources/Items/Tools/{id}.asset");

        /// <summary>Only prefabs that already are residents; a Raxy that never leaves its spot carries nothing.</summary>
        private static bool WirePrefab(string path)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (!root.TryGetComponent(out Resident _)) return false;

                var inventory = Ensure<EntityInventoryComponent>(root);
                var so = new SerializedObject(inventory);
                so.FindProperty("inventorySize").intValue = BagSize;
                so.FindProperty("startingItems").arraySize = 0;
                so.ApplyModifiedPropertiesWithoutUndo();

                // A tool is held in a pose an animation owns; an aim point would swing it at whoever
                // the resident is looking at.
                var equipment = Ensure<EntityEquipmentController>(root);
                var equipmentSo = new SerializedObject(equipment);
                equipmentSo.FindProperty("startingSlot").intValue = 0;
                equipmentSo.FindProperty("aimHeldItem").boolValue = false;
                equipmentSo.ApplyModifiedPropertiesWithoutUndo();

                EnsureBelt(root, path);
                Ensure<BeltCarrier>(root);
                Ensure<ResidentCarry>(root);
                Ensure<EntityInventorySaveable>(root);
                Ensure<EntityEquipmentSaveable>(root);

                PrefabUtility.SaveAsPrefabAsset(root, path);
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>
        /// Puts a belt on a resident that wears none. A belt is any worn garment whose mount points
        /// ride on the hips, found the way <see cref="BeltSeat.CreateAnchors"/> finds them (active
        /// garments only), so a belt switched off in the prefab does not count. A Raxy in a poncho
        /// gets the poncho belt; every other gets the standard one.
        /// </summary>
        private static void EnsureBelt(GameObject root, string characterPath)
        {
            bool wearsBelt = root.GetComponentsInChildren<GarmentMounts>()
                .Any(g => g.Bone == HumanBodyBones.Hips && g.Mounts.Any(m => m.point != null));
            if (wearsBelt) return;

            bool inPoncho = root.GetComponentsInChildren<SkinnedGarment>()
                .Any(g => g.name.StartsWith(PonchoGarmentPrefix, System.StringComparison.Ordinal));
            string beltName = inPoncho ? PonchoBeltName : StandardBeltName;

            var belt = AssetDatabase.LoadAssetAtPath<GameObject>($"{DrifterClothes.FolderFor(characterPath)}/{beltName}.prefab");
            if (belt == null)
            {
                Debug.LogError($"[HandTools] {root.name}: no garment prefab {beltName}; it stays beltless.");
                return;
            }

            var worn = (GameObject)PrefabUtility.InstantiatePrefab(belt, root.transform);
            worn.layer = root.layer;
            if (worn.GetComponent<SkinnedGarment>().Bind()) return;

            // A skeleton the belt was not modelled on (the Classic head's) cannot wear it: an unbound
            // belt draws nothing and its mount points have no bone to ride on.
            Object.DestroyImmediate(worn);
            Debug.LogError($"[HandTools] {root.name}: its skeleton cannot wear {beltName}; it stays beltless.");
        }

        private static T Ensure<T>(GameObject root) where T : Component =>
            root.TryGetComponent(out T existing) ? existing : root.AddComponent<T>();
    }
}
