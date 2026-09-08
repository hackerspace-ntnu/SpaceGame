// The conjurer staff: that it exists as a complete artifact, that the creature drops it, and that
// its ring actually follows the ground.
//
// The first two are asset-wiring tests, which is worth saying plainly because asset wiring is
// exactly where this feature can break without anything failing. An InventoryItem whose prefab
// pointer has gone null does not raise a compile error; a prefab missing from the network list
// works perfectly for the host and silently spawns nothing for every client; an item asset outside
// Resources/Items is never registered and every save slot holding it comes back empty. None of
// those are visible from playing solo.
//
// The third is the real behaviour. The ring's whole promise is that it lies ON the ground rather
// than through it, so the test builds a staircase of colliders under one and checks the vertices
// followed them -- which is a claim about geometry and needs no terrain, no player and no frame.
//
// In Editor/ rather than beside the asmdef'd EditMode tests because these touch Assembly-CSharp
// types, and an asmdef cannot reference Assembly-CSharp.
using System.Linq;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Gameplay;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    public class ConjurerStaffTests
    {
        private const string ItemPath = "Assets/Game/Resources/Items/Artifacts/ConjurerStaff.asset";
        private const string PrefabPath =
            "Assets/Game/Prefabs/Items/Artifacts/Gadgets/ConjurerStaff.prefab";
        private const string ConjurerPath =
            "Assets/Game/Prefabs/Agents/creatures/LightningConjurer.prefab";
        private const string NetworkPrefabsPath =
            "Assets/Game/ScriptableObjects/Networking/DefaultNetworkPrefabs.asset";

        // ── The item ───────────────────────────────────────────────────────────

        [Test]
        public void ItemAsset_IsUnderResourcesItems_AndPointsAtItsPrefab()
        {
            var item = AssetDatabase.LoadAssetAtPath<InventoryItem>(ItemPath);
            Assert.IsNotNull(item,
                $"No item at {ItemPath}. Run Tools/Build Conjurer Staff Artifact. " +
                "It must live under Resources/Items or RegistryLoader never sees it.");

            Assert.IsNotNull(item.itemPrefab, "The staff item has no prefab.");
            Assert.AreEqual(PrefabPath, AssetDatabase.GetAssetPath(item.itemPrefab));
        }

        [Test]
        public void Prefab_PointsBackAtItsItem()
        {
            GameObject prefab = LoadPrefab();
            var item = AssetDatabase.LoadAssetAtPath<InventoryItem>(ItemPath);

            Component pickup = prefab.GetComponents<Component>()
                .FirstOrDefault(c => c != null &&
                                     c.GetType().FullName == "SpaceGame.Items.PickupableItem");

            Assert.IsNotNull(pickup, "The staff prefab has no PickupableItem, so it cannot be taken.");

            var so = new SerializedObject(pickup);
            Assert.AreSame(item, so.FindProperty("item").objectReferenceValue,
                "PickupableItem.item does not point back at the staff asset. The two files " +
                "reference each other and both links have to be made.");
        }

        [Test]
        public void Prefab_CarriesEverythingADroppableArtifactNeeds()
        {
            GameObject prefab = LoadPrefab();

            Assert.IsNotNull(prefab.GetComponent<NetworkObject>(),
                "No NetworkObject: World.Spawn refuses to drop it.");
            Assert.IsNotNull(prefab.GetComponent<Collider>(),
                "No collider: it cannot be aimed at to pick up.");
            Assert.IsNotNull(prefab.GetComponent<Rigidbody>(),
                "No Rigidbody: DropItemPhysics has nothing to throw.");
            Assert.IsNotNull(prefab.GetComponent<ConjurerStaffArtifact>(),
                "No artifact script: it would be a stick.");
            Assert.IsNotNull(prefab.GetComponent<ItemGrip>(),
                "No ItemGrip: the hold pose would be a guess from the renderer bounds.");
            Assert.IsNotNull(prefab.GetComponent<SpaceGame.Core.NetRelay>(),
                "No NetRelay.");
            Assert.IsNotNull(prefab.GetComponent<SpaceGame.Core.Persistence.SaveableEntity>(),
                "No SaveableEntity: a dropped staff would not survive a reload.");

            Assert.Greater(prefab.GetComponentsInChildren<MeshRenderer>(true).Length, 0,
                "No renderers, so the icon generator has nothing to frame and the world has " +
                "nothing to draw.");
        }

        [Test]
        public void Prefab_IsRegisteredAsANetworkPrefab()
        {
            GameObject prefab = LoadPrefab();

            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabsPath);
            Assert.IsNotNull(list, $"No network prefab list at {NetworkPrefabsPath}.");

            Assert.IsTrue(list.PrefabList.Any(entry => entry != null && entry.Prefab == prefab),
                "The staff is not in the network prefab list. Dropping one routes through " +
                "World.Spawn, so this fails on CLIENTS ONLY and solo playtesting cannot find it.");
        }

        // ── The drop ───────────────────────────────────────────────────────────

        [Test]
        public void Conjurer_DropsTheStaff_Always()
        {
            var conjurer = AssetDatabase.LoadAssetAtPath<GameObject>(ConjurerPath);
            Assert.IsNotNull(conjurer, $"No conjurer prefab at {ConjurerPath}.");

            var loot = conjurer.GetComponent<EntityLootTable>();
            Assert.IsNotNull(loot,
                "The conjurer has no EntityLootTable, so it drops nothing. Run " +
                "Tools/Creatures/Build Lightning Conjurer (prefab only).");

            var item = AssetDatabase.LoadAssetAtPath<InventoryItem>(ItemPath);
            var so = new SerializedObject(loot);
            SerializedProperty entries = so.FindProperty("lootEntries");

            bool found = false;
            for (int i = 0; i < entries.arraySize; i++)
            {
                SerializedProperty entry = entries.GetArrayElementAtIndex(i);
                if (entry.FindPropertyRelative("item").objectReferenceValue != item) continue;

                found = true;
                Assert.AreEqual(1f, entry.FindPropertyRelative("dropChance").floatValue, 0.0001f,
                    "The staff is the reward for the fight; a coin flip on it reads as a bug.");
                Assert.AreEqual(1, entry.FindPropertyRelative("quantity").intValue);
            }

            Assert.IsTrue(found, "The conjurer's loot table does not contain its own staff.");
        }

        [Test]
        public void Conjurer_TakesTheStaffOffItsCorpse()
        {
            var conjurer = AssetDatabase.LoadAssetAtPath<GameObject>(ConjurerPath);
            Assert.IsNotNull(conjurer, $"No conjurer prefab at {ConjurerPath}.");

            var shed = conjurer.GetComponent<HidePartsOnDeath>();
            Assert.IsNotNull(shed,
                "Without HidePartsOnDeath the corpse keeps its staff for the twelve seconds it " +
                "takes to fade, so the kill pays out two staffs and one of them is a lie.");

            var so = new SerializedObject(shed);
            SerializedProperty parts = so.FindProperty("partNames");

            var named = new string[parts.arraySize];
            for (int i = 0; i < parts.arraySize; i++)
                named[i] = parts.GetArrayElementAtIndex(i).stringValue;

            foreach (string part in ConjurerStaffBuilder.StaffParts)
                CollectionAssert.Contains(named, part);
        }

        // ── The ring ───────────────────────────────────────────────────────────

        [Test]
        public void GroundRing_HasOneVertexPairPerSegment()
        {
            var (go, ring) = MakeRing(segments: 32);

            try
            {
                ring.Show(Vector3.zero, 4f);
                ring.Rebuild();

                Mesh mesh = go.GetComponent<MeshFilter>().sharedMesh;
                Assert.AreEqual(32 * 2, mesh.vertexCount,
                    "An outer and an inner vertex per segment, and nothing else.");

                // Two triangles per quad, emitted twice for the two facings: the ring lies
                // centimetres off the ground and the camera gets under it on any rise, where a
                // single-sided ring would simply vanish.
                Assert.AreEqual(32 * 12, mesh.triangles.Length);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void GroundRing_BandGrowsInwardsFromTheRadius()
        {
            var (go, ring) = MakeRing(segments: 16);

            try
            {
                const float radius = 5f;
                ring.Show(Vector3.zero, radius);
                ring.Rebuild();

                Vector3[] verts = go.GetComponent<MeshFilter>().sharedMesh.vertices;

                float outer = new Vector2(verts[0].x, verts[0].z).magnitude;
                float inner = new Vector2(verts[16].x, verts[16].z).magnitude;

                // The outer edge IS the radius being described. A band straddling it would cover
                // ground the strike does not, which is the one thing a blast marker must not do.
                Assert.AreEqual(radius, outer, 0.001f);
                Assert.Less(inner, outer);
                Assert.Greater(inner, outer * 0.5f, "The band should be a band, not a disc.");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void GroundRing_FollowsGroundThatIsNotFlat()
        {
            var (go, ring) = MakeRing(segments: 16);

            // A step: everything at x > 0 is two metres higher than everything at x < 0. With no
            // terrain in an edit-mode scene the ring falls to its raycast path, which is the same
            // path a cave or a rooftop takes it down.
            GameObject low = Slab(new Vector3(-6f, 0f, 0f), new Vector3(10f, 1f, 20f));
            GameObject high = Slab(new Vector3(6f, 2f, 0f), new Vector3(10f, 1f, 20f));

            try
            {
                ring.Show(new Vector3(0f, 0.5f, 0f), 4f);
                ring.Rebuild();

                Vector3[] verts = go.GetComponent<MeshFilter>().sharedMesh.vertices;

                // Vertex 0 sits at angle 0, i.e. +X, over the high slab; vertex 8 is at 180°, over
                // the low one. Local space, and the object sits at the centre — so these are heights
                // relative to the aim point.
                float east = verts[0].y;
                float west = verts[8].y;

                Assert.Greater(east - west, 1.5f,
                    "The ring did not climb the step. A marker that stays flat over a rise is " +
                    "describing a circle nobody is standing in.");

                // Both ends still on their own surface rather than splitting the difference.
                Assert.AreEqual(2.5f - 0.5f, east, 0.2f);
                Assert.AreEqual(0.5f - 0.5f, west, 0.2f);
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(low);
                Object.DestroyImmediate(high);
            }
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private static GameObject LoadPrefab()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab,
                $"No staff prefab at {PrefabPath}. Run Tools/Build Conjurer Staff Artifact.");
            return prefab;
        }

        private static (GameObject, GroundRing) MakeRing(int segments)
        {
            var go = new GameObject("GroundRing", typeof(MeshFilter), typeof(MeshRenderer));
            var ring = go.AddComponent<GroundRing>();

            var so = new SerializedObject(ring);
            so.FindProperty("segments").intValue = segments;
            so.FindProperty("groundOffset").floatValue = 0f;   // exact heights, not garnished ones
            so.ApplyModifiedPropertiesWithoutUndo();

            return (go, ring);
        }

        /// <summary>A box collider standing in for a piece of ground.</summary>
        private static GameObject Slab(Vector3 centre, Vector3 size)
        {
            var go = new GameObject("Slab", typeof(BoxCollider));
            go.transform.position = centre;
            go.GetComponent<BoxCollider>().size = size;
            return go;
        }
    }
}
