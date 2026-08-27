using System.Linq;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using SpaceGame.Items;
using SpaceGame.Gear.JumpingRod;

namespace SpaceGame.Tests
{
    /// <summary>
    /// The jumping rod's two prefabs, checked on disk.
    ///
    /// <para>
    /// None of this duplicates <see cref="NetworkPrefabRegistrationTests"/>, which already sweeps
    /// every item and every networked prefab in the project. What is here is what is specific to
    /// this item and would fail silently: a planted rod that quietly became a networked object, a
    /// carried rod too big to go on the pack, and the reference between the two going null.
    /// </para>
    /// </summary>
    public class JumpingRodWiringTests
    {
        private const string DeployedPath = "Assets/Game/Prefabs/Items/Artifacts/Gadgets/JumpingRodDeployed.prefab";
        private const string ItemPrefabPath = "Assets/Game/Prefabs/Items/Artifacts/Gadgets/JumpingRod.prefab";
        private const string ItemAssetPath = "Assets/Game/Resources/Items/Artifacts/JumpingRod.asset";
        private const string RigPath = "Assets/Game/Prefabs/Items/Equipment/ExpeditionRig.prefab";

        private static GameObject Load(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(prefab, $"No prefab at {path}. Run Tools ▸ Items ▸ Build Jumping Rod.");
            return prefab;
        }

        // ── The planted rod ────────────────────────────────────────────────────

        [Test]
        public void PlantedRod_IsAPlainVisual()
        {
            GameObject rod = Load(DeployedPath);

            // It is instantiated by every machine from Present(). A NetworkObject would have the
            // host spawn a second one and would put a cosmetic child into the save system's reach;
            // a collider would catch on every doorway the player it hangs under walks through.
            Assert.IsNull(rod.GetComponent<NetworkObject>(),
                "the planted rod must not be a networked object — every machine makes its own");
            Assert.IsNull(rod.GetComponentInChildren<Collider>(true), "the planted rod needs no collider");
            Assert.IsNull(rod.GetComponentInChildren<Rigidbody>(true), "the planted rod is not simulated");
        }

        [Test]
        public void PlantedRod_SpringRigIsWiredToPartsInsideThePrefab()
        {
            GameObject rod = Load(DeployedPath);
            var rig = rod.GetComponent<JumpingRodSpring>();
            Assert.IsNotNull(rig, "no spring rig, so the coil would never move");

            var so = new SerializedObject(rig);
            foreach (string field in new[] { "rod", "piston", "coil" })
            {
                var value = so.FindProperty(field).objectReferenceValue as Transform;
                Assert.IsNotNull(value, $"JumpingRodSpring.{field} is unassigned");
                Assert.IsTrue(value.IsChildOf(rod.transform),
                    $"JumpingRodSpring.{field} points outside the prefab");
            }
        }

        [Test]
        public void PlantedRod_PistonCarriesTheFootAndTheSpringSeat()
        {
            GameObject rod = Load(DeployedPath);
            Transform piston = Find(rod, "Mesh_JumpingRod_Piston");

            // One driven transform moves all three. Left as siblings, the foot would stay planted
            // in the sand while the piston it is bolted to slid up the shaft without it.
            Assert.IsTrue(Find(rod, "Mesh_JumpingRod_Foot").IsChildOf(piston));
            Assert.IsTrue(Find(rod, "Mesh_JumpingRod_SpringSeat").IsChildOf(piston));
        }

        [Test]
        public void PlantedRod_IsNotARegisteredNetworkPrefab()
        {
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(
                "Assets/Game/ScriptableObjects/Networking/DefaultNetworkPrefabs.asset");
            Assert.IsNotNull(list);

            bool registered = list.PrefabList
                .Where(e => e != null && e.Prefab != null)
                .Any(e => AssetDatabase.GetAssetPath(e.Prefab) == DeployedPath);

            Assert.IsFalse(registered,
                "the planted rod is a cosmetic child, not something the server spawns — registering " +
                "it would give every player two rods and put one of them in the save file");
        }

        // ── The carried item ───────────────────────────────────────────────────

        [Test]
        public void Item_PointsAtTheRodItPlants()
        {
            GameObject held = Load(ItemPrefabPath);
            var item = held.GetComponent<JumpingRodItem>();
            Assert.IsNotNull(item, "the item prefab has no JumpingRodItem");

            var prefab = new SerializedObject(item).FindProperty("deployedPrefab").objectReferenceValue;

            // A deleted or renamed prefab nulls this field with no compile error and no warning;
            // the item then bounces the player around with nothing visible under them.
            Assert.IsNotNull(prefab, "JumpingRodItem.deployedPrefab is unassigned");
            Assert.AreEqual(DeployedPath, AssetDatabase.GetAssetPath(prefab));
        }

        [Test]
        public void Item_ActsOnItsOwnHolderAndSaysSo()
        {
            GameObject held = Load(ItemPrefabPath);
            var item = held.GetComponent<JumpingRodItem>();

            // The bounce writes the holder's own Rigidbody, and the player's transform is
            // owner-authoritative. Server authority here would have the impulse overwritten within
            // a tick, silently — the single most common way an item like this ships broken.
            Assert.AreEqual(UseAuthority.Owner, item.Authority);
        }

        [Test]
        public void Item_AssetAndPrefabReferenceEachOther()
        {
            var asset = AssetDatabase.LoadAssetAtPath<InventoryItem>(ItemAssetPath);
            Assert.IsNotNull(asset, $"No InventoryItem at {ItemAssetPath}. It must live under " +
                                    "Resources/Items or RegistryLoader never finds it.");

            GameObject held = Load(ItemPrefabPath);
            Assert.AreEqual(held, asset.itemPrefab, "the item asset does not point at its prefab");

            Component pickup = held.GetComponents<Component>()
                .FirstOrDefault(c => c != null &&
                                     c.GetType().FullName == "SpaceGame.Items.PickupableItem");
            Assert.IsNotNull(pickup, "the item prefab cannot be picked back up");

            Assert.AreEqual(asset, new SerializedObject(pickup).FindProperty("item").objectReferenceValue,
                "PickupableItem.item does not point back at the InventoryItem");
        }

        /// <summary>
        /// The rod has to fit on the back of the pack. It is the longest thing the player carries
        /// after the laser staff, and unlike the staff it is not slender — the handlebar is a third
        /// of a metre across — so it cannot ride the 1.62 x 0.09 m LongGoods strip and has to fit a
        /// real face.
        ///
        /// Checked against the surfaces actually wired on the rig rather than against numbers typed
        /// here, so re-proportioning the pack re-runs the question instead of leaving this passing
        /// against a face that no longer exists.
        /// </summary>
        [Test]
        public void Item_FitsOnAPackSurface()
        {
            var asset = AssetDatabase.LoadAssetAtPath<InventoryItem>(ItemAssetPath);
            Assert.IsNotNull(asset);

            ItemFootprint.ClearCache();
            Vector2 footprint = ItemFootprint.FootprintOf(asset);
            Vector2Int cells = new(Mathf.CeilToInt(footprint.x / PackGrid.Cell),
                                   Mathf.CeilToInt(footprint.y / PackGrid.Cell));

            GameObject rig = Load(RigPath);
            PackSurface[] surfaces = rig.GetComponentsInChildren<PackSurface>(true);
            Assert.IsNotEmpty(surfaces, $"{RigPath} has no PackSurface to stow anything on");

            // Either way round: the pack turns a placement in quarter turns.
            bool fits = surfaces.Any(s =>
            {
                Vector2Int face = s.Cells;
                return (cells.x <= face.x && cells.y <= face.y) ||
                       (cells.y <= face.x && cells.x <= face.y);
            });

            string faces = string.Join(", ", surfaces.Select(s => $"{s.Id} {s.Cells.x}x{s.Cells.y}"));
            Assert.IsTrue(fits,
                $"the jumping rod measures {footprint.x:F2} x {footprint.y:F2} m ({cells.x}x{cells.y} " +
                $"cells) and fits none of the pack's faces: {faces}. Lower ItemGrip.holdSize on " +
                $"{ItemPrefabPath} — JumpingRodBuilder.HoldSize owns that number.");
        }

        // ── helpers ────────────────────────────────────────────────────────────

        private static Transform Find(GameObject root, string name)
        {
            Transform found = root.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(t => t.name == name);

            Assert.IsNotNull(found, $"{root.name} has no part called {name}");
            return found;
        }
    }
}
