// What every item prefab needs in order to exist as an object lying in the world, in one place.
//
// Nine builder scripts each used to write their own version of this block — a Rigidbody, a
// collider somebody reached for, and a DropItemPhysics with a hand-typed ground mask that seven of
// them documented with the identical comment. That is why a rifle collided as a sphere of radius
// 0.18, why the Grappling Hook shipped with no collider at all, and why no item prefab in the
// project ever got a NetworkTransform: nine copies of a block drift, and the drift is invisible
// until somebody drops the odd one out.
//
// Builders call Apply as they build. The menu command below applies it to the whole shipped roster
// for the prefabs whose builders have been retired or never existed, and re-runs are harmless.
//
// Run from: Tools ▸ SpaceGame ▸ Items ▸ Fix World Item Bodies
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    public static class ItemWorldPresence
    {
        /// <summary>
        /// Give <paramref name="root"/> everything an item needs in order to lie in the world:
        /// a body, a collider that is the shape of the item, the component that sizes and tunes it,
        /// and the two netcode components that let another machine see it move.
        ///
        /// <para>
        /// Idempotent, and safe on a prefab that has been through it before — which is what lets a
        /// builder call it unconditionally on a root it has just constructed and the menu command
        /// call it on the same prefab a year later.
        /// </para>
        /// </summary>
        /// <param name="sizing">
        /// <see cref="ItemWorldSizing.Authored"/> for the few items whose real built size is the
        /// point — a hull module is meant to be the same eleven metres in the sand as it is bolted
        /// to the roof.
        /// </param>
        /// <param name="mass">Kilograms, or 0 to let <see cref="WorldItem"/> derive one from the size.</param>
        public static void Apply(GameObject root,
                                 ItemWorldSizing sizing = ItemWorldSizing.FromGrip,
                                 float mass = 0f)
        {
            if (root == null) return;

            RemoveByName(root, "SpaceGame.Items.DropItemPhysics");

            EnsureBody(root);
            EnsureFittedCollider(root);
            EnsureNetworking(root);
            EnsureWorldItem(root, sizing, mass);
        }

        /// <summary>
        /// The item's own shape, as one box measured off its meshes.
        ///
        /// <para>
        /// A box rather than a hand-picked primitive because the hand-picked ones were wrong: a
        /// sphere on a bazooka is a marble that rolls until it finds a wall, and that rolling is
        /// the whole of what a dropped item "swinging around" looks like. A box fitted to the mesh
        /// lies down on a face and stays there, and it is also what the crosshair finds — the old
        /// sphere covered a fifth of the rifle, so a ray aimed at the visible barrel went straight
        /// past it into the floor.
        /// </para>
        /// <para>
        /// Sized in the root's LOCAL space, which is what makes it survive
        /// <see cref="ItemWorldScale"/> scaling the root at runtime: a collider is authored in local
        /// units, so the same numbers describe the same shape at any scale the world puts the item
        /// at.
        /// </para>
        /// </summary>
        private static void EnsureFittedCollider(GameObject root)
        {
            Bounds local = ItemBounds.Measure(root, null);

            // Nothing to measure — a pure-effect item whose geometry only exists at use time. A box
            // of nothing is worse than the collider somebody authored by hand.
            if (local.size.sqrMagnitude < 1e-8f) return;

            foreach (Collider collider in root.GetComponents<Collider>())
                if (!collider.isTrigger)
                    Object.DestroyImmediate(collider, true);

            BoxCollider box = root.AddComponent<BoxCollider>();
            box.center = local.center;
            box.size = local.size;
        }

        private static void EnsureBody(GameObject root)
        {
            if (!root.TryGetComponent(out Rigidbody body))
                body = root.AddComponent<Rigidbody>();

            // Authored dynamic, because a prefab lying in a chunk scene is an item in the world and
            // that is the state it is in there. The equip path makes its own copy kinematic on every
            // equip (EquipItemSocket.Sanitize), so nothing is lost by not authoring it that way.
            body.isKinematic = false;
            body.useGravity = true;
        }

        private static void EnsureNetworking(GameObject root)
        {
            // A NetworkTransform without a NetworkObject is a component that cannot run. Registering
            // the item is NetworkPrefabRegistrationTests' subject, not this one's.
            if (root.GetComponent<NetworkObject>() == null) return;

            if (root.GetComponent<NetworkTransform>() == null)
            {
                NetworkTransform transform = root.AddComponent<NetworkTransform>();
                transform.Interpolate = true;
            }

            // Freezes the body on machines that do not simulate this item, so a dropped rifle is
            // not simulated four times over and shown in four places. Without it the server's
            // NetworkTransform and the client's own physics write the same transform every step.
            if (root.GetComponent<NetAuthority>() == null)
                root.AddComponent<NetAuthority>();
        }

        private static void EnsureWorldItem(GameObject root, ItemWorldSizing sizing, float mass)
        {
            if (root.GetComponent<WorldItem>() == null) root.AddComponent<WorldItem>();

            var so = new SerializedObject(root.GetComponent<WorldItem>());
            so.FindProperty("sizing").enumValueIndex = (int)sizing;
            so.FindProperty("mass").floatValue = mass;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Remove a component by type name, so this file does not have to reference a type it
        /// exists to delete — and keeps compiling once that type is gone.
        /// </summary>
        private static void RemoveByName(GameObject root, string typeName)
        {
            foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
                if (behaviour != null && behaviour.GetType().FullName == typeName)
                    Object.DestroyImmediate(behaviour, true);
        }

        // ─────────────────────────── The roster pass ───────────────────────────

        [MenuItem("Tools/SpaceGame/Items/Fix World Item Bodies")]
        public static void FixRoster()
        {
            var log = new StringBuilder("[WorldItem] Roster pass\n");
            int changed = 0;

            foreach (GameObject prefab in Roster())
            {
                string path = AssetDatabase.GetAssetPath(prefab);
                GameObject contents = PrefabUtility.LoadPrefabContents(path);

                try
                {
                    // The one family whose real built size is the point rather than an accident.
                    // A hull module hauled across the desert has to be the module that bolts onto
                    // the roof, and drawing it at gear-wall size would make hauling it meaningless.
                    bool shipPart = contents.GetComponent<ShipPartItem>() != null;

                    float mass = shipPart && contents.TryGetComponent(out Rigidbody body) ? body.mass : 0f;

                    Apply(contents, shipPart ? ItemWorldSizing.Authored : ItemWorldSizing.FromGrip, mass);

                    PrefabUtility.SaveAsPrefabAsset(contents, path);
                    changed++;

                    Bounds local = ItemBounds.Measure(contents, null);

                    log.Append("  ").Append(prefab.name.PadRight(24))
                       .Append(" box ").Append(Metres(local.size))
                       .Append("  world ").Append(ItemWorldScale.SizeOf(contents).ToString("0.00"))
                       .Append(" m")
                       .AppendLine(shipPart ? "  (authored size kept)" : string.Empty);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            log.Append("  ").Append(changed).AppendLine(" prefab(s) written.");
            Debug.Log(log.ToString());

            Verify();
        }

        /// <summary>
        /// Read the write back off disk, because an AssetDatabase that has gone read-only discards
        /// a prefab save silently and the pass above would report success having written nothing.
        /// </summary>
        [MenuItem("Tools/SpaceGame/Items/Audit World Item Bodies")]
        public static void Verify()
        {
            var problems = new List<string>();

            foreach (GameObject prefab in Roster())
            {
                foreach (string problem in ProblemsWith(prefab))
                    problems.Add($"{prefab.name}: {problem}");
            }

            if (problems.Count == 0)
            {
                Debug.Log("[WorldItem] Every item prefab is a whole world object.");
                return;
            }

            Debug.LogError("[WorldItem] These item prefabs cannot lie in the world correctly:\n  " +
                           string.Join("\n  ", problems) +
                           "\nRun Tools/SpaceGame/Items/Fix World Item Bodies.");
        }

        /// <summary>What is missing from <paramref name="prefab"/>. Shared with the test that pins it.</summary>
        public static IEnumerable<string> ProblemsWith(GameObject prefab)
        {
            if (prefab == null) yield break;

            if (prefab.GetComponent<WorldItem>() == null)
                yield return "no WorldItem, so it is drawn at the raw prefab scale and its body is never tuned";

            if (prefab.GetComponent<Rigidbody>() == null)
                yield return "no Rigidbody, so nothing can shove it and no rope can drag it";

            // Only where there is something to collide WITH. A prefab with no geometry at all is a
            // template rather than an item — InventoryItemModule is an empty GameObject carrying
            // the block that gets copied onto real items — and `Apply` declines to fit a box to
            // nothing for the same reason. Demanding one here would make the audit fail forever on
            // an asset that is behaving correctly.
            bool measurable = ItemBounds.Measure(prefab, null).size.sqrMagnitude > 1e-8f;
            bool solid = prefab.GetComponentsInChildren<Collider>(true).Any(c => !c.isTrigger);

            if (measurable && !solid)
                yield return "no solid collider, so a dropped one falls through the world";

            if (prefab.GetComponent<NetworkObject>() == null) yield break;

            if (prefab.GetComponent<NetworkTransform>() == null)
                yield return "no NetworkTransform, so it moves only on the machine simulating it";

            if (prefab.GetComponent<NetAuthority>() == null)
                yield return "no NetAuthority, so every machine simulates its own copy";
        }

        /// <summary>
        /// Every prefab that is a thing lying in the world waiting to be picked up.
        ///
        /// <para>
        /// The union of two definitions, because neither alone is the whole set.
        /// <c>InventoryItem.itemPrefab</c> is what a drop spawns, but not every world pickup is
        /// reachable that way — <c>BallLightningWeapon_Pickup</c> and <c>InventoryItemModule</c>
        /// both carry <c>PickupableItem</c> and are placed by hand, and an item-table-only sweep
        /// left them behind with the frozen physics everything else had just been fixed out of.
        /// </para>
        /// </summary>
        public static IEnumerable<GameObject> Roster()
        {
            IEnumerable<GameObject> dropped = AssetDatabase.FindAssets("t:InventoryItem")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<InventoryItem>)
                .Where(item => item != null && item.itemPrefab != null)
                .Select(item => item.itemPrefab);

            IEnumerable<GameObject> placed = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Game/Prefabs" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<GameObject>)
                .Where(prefab => prefab != null && HasComponentNamed(prefab, PickupableItemType));

            return dropped.Concat(placed).Distinct();
        }

        /// <summary>
        /// <c>PickupableItem</c> is internal to Assembly-CSharp, so an editor assembly cannot name
        /// the type. The builders reach it the same way, by name.
        /// </summary>
        private const string PickupableItemType = "SpaceGame.Items.PickupableItem";

        private static bool HasComponentNamed(GameObject root, string typeName)
        {
            foreach (MonoBehaviour behaviour in root.GetComponents<MonoBehaviour>())
                if (behaviour != null && behaviour.GetType().FullName == typeName)
                    return true;

            return false;
        }

        private static string Metres(Vector3 size) =>
            size.x.ToString("0.00") + " x " + size.y.ToString("0.00") + " x " + size.z.ToString("0.00");
    }
}
