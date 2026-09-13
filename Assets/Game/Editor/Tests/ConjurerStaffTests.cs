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
using System.Reflection;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Characters;
using SpaceGame.Core;
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
                "No Rigidbody: PlayerDropService has nothing to toss.");
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
        public void Conjurer_HoldsItsDropUntilTheBodyGoes()
        {
            var conjurer = AssetDatabase.LoadAssetAtPath<GameObject>(ConjurerPath);
            Assert.IsNotNull(conjurer, $"No conjurer prefab at {ConjurerPath}.");

            var loot = conjurer.GetComponent<EntityLootTable>();
            Assert.IsNotNull(loot, "The conjurer has no EntityLootTable.");
            Assert.IsTrue(new SerializedObject(loot).FindProperty("dropOnDespawn").boolValue,
                "The conjurer's staff drops the instant its health hits zero, which now lands " +
                "the pickup on the floor while the creature is still three seconds into falling " +
                "over on top of it. The drop is meant to be what REPLACES the body.");

            // And the half of that arrangement the loot table cannot check for itself: with no
            // despawn there is nothing to wait for, and EntityLootTable falls back to dropping on
            // death rather than never dropping at all — correct, but not what is wanted here.
            var reaction = conjurer.GetComponent<HealthReactionModule>();
            Assert.IsNotNull(reaction, "The conjurer has no HealthReactionModule to despawn it.");
            Assert.IsTrue(reaction.Despawns,
                "The conjurer's despawn delay is zero, so the body never goes away and the drop " +
                "it is waiting on would never come.");
        }

        [Test]
        public void Conjurer_TakesTheStaffOffItsCorpse()
        {
            var conjurer = AssetDatabase.LoadAssetAtPath<GameObject>(ConjurerPath);
            Assert.IsNotNull(conjurer, $"No conjurer prefab at {ConjurerPath}.");

            var shed = conjurer.GetComponent<HidePartsOnDeath>();
            Assert.IsNotNull(shed,
                "Without HidePartsOnDeath the corpse keeps its staff through the whole collapse " +
                "— and the collapse drops the hand holding it by five metres onto a knee, which " +
                "puts a third of a fourteen-metre staff through the floor.");

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

            // A collider that has been MOVED is not where the physics scene thinks it is until this
            // is called — autoSyncTransforms is off, and nothing steps physics in an edit-mode test.
            // Without it every ray misses, the ring falls back to the centre height, and the test
            // fails claiming the ring is flat when the ground is what was missing.
            Physics.SyncTransforms();

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

        // ── The wind-up ────────────────────────────────────────────────────────
        //
        // The one thing that separates this staff from LightningSpell: the bolt is not instant. The
        // ground it covers is meant to be walkable-out-of, which is only true if the damage really
        // is deferred — and "deferred" is the kind of claim that stays true in the inspector while
        // being false in the build.

        [Test]
        public void ThePress_DoesNotStrikeYet_AndTheStrikeLandsWhenTheWindUpRunsOut()
        {
            var rig = new CastRig();

            try
            {
                int before = rig.TargetHealth;

                rig.Fire();
                Assert.AreEqual(before, rig.TargetHealth,
                    "The bolt billed on the press. There is then no wind-up to dodge and the ring " +
                    "is decoration.");

                // Most of the way through, and still nothing.
                rig.AdvanceBilling(rig.CastSeconds * 0.9f);
                Assert.AreEqual(before, rig.TargetHealth, "Billed early.");

                rig.AdvanceBilling(rig.CastSeconds * 0.2f);
                Assert.Less(rig.TargetHealth, before, "The bolt never landed.");
            }
            finally { rig.Dispose(); }
        }

        [Test]
        public void DroppingTheStaffMidCast_CancelsTheBolt()
        {
            var rig = new CastRig();

            try
            {
                int before = rig.TargetHealth;

                rig.Fire();
                rig.AdvanceBilling(rig.CastSeconds * 0.5f);

                // Scrolling to the next hotbar slot destroys the held instance, and this is the hook
                // that runs first. A staff that has left the hand must not finish its cast.
                rig.Artifact.OnUnequipped(rig.Player);
                rig.AdvanceBilling(rig.CastSeconds * 2f);

                Assert.AreEqual(before, rig.TargetHealth,
                    "The bolt landed after the staff was put away.");
            }
            finally { rig.Dispose(); }
        }

        [Test]
        public void MashingTheButton_DoesNotRestartTheWindUpOrDoubleTheBolt()
        {
            var rig = new CastRig();

            try
            {
                int before = rig.TargetHealth;

                rig.Fire();
                rig.AdvanceBilling(rig.CastSeconds * 0.5f);

                // Half way through, hit it again. PlayUse is not gated on CanUse, so this reaches
                // Present regardless — and Present accepting it would both restart the cast and,
                // through the flag it sets, wave the second press past the authority gate.
                rig.Fire();

                rig.AdvanceBilling(rig.CastSeconds * 0.6f);
                Assert.AreEqual(before - rig.Damage, rig.TargetHealth,
                    "The second press moved the goalposts: one press should bill exactly once, on " +
                    "the clock the first one started.");

                // And the cast really is over rather than restarted.
                rig.AdvanceBilling(rig.CastSeconds * 2f);
                Assert.AreEqual(before - rig.Damage, rig.TargetHealth, "It billed twice.");
            }
            finally { rig.Dispose(); }
        }

        [Test]
        public void AimedAtOpenSky_CastsNothing()
        {
            var rig = new CastRig();

            try
            {
                rig.ClearTarget();      // nothing under the crosshair, so the aim ray hits nothing

                var arg = new NetArg();
                rig.Artifact.OnRequestUse(ref arg);

                Assert.AreEqual(Vector3.zero, arg.P,
                    "A miss has to travel as the zero sentinel. Read as a position it is the world " +
                    "origin, and the bolt lands there.");

                rig.Artifact.TryUse(rig.Player, arg);
                Assert.IsFalse(rig.IsBilling, "A miss started a cast anyway.");
            }
            finally { rig.Dispose(); }
        }

        /// <summary>
        /// A staff in a hand, aimed at something with health, with the clock under our control.
        ///
        /// The wind-up is driven by pumping the private billing tick rather than by waiting, because
        /// an edit-mode test has no frames — and Time.deltaTime is not writable, so the elapsed
        /// counter is advanced directly and the tick is asked what it makes of it.
        /// </summary>
        private class CastRig
        {
            public readonly GameObject Player;
            public readonly ConjurerStaffArtifact Artifact;

            private readonly GameObject _staff;
            private GameObject _target;

            public CastRig()
            {
                Player = new GameObject("player", typeof(AimProvider));

                var cam = new GameObject("cam", typeof(Camera));
                cam.transform.SetParent(Player.transform, false);
                cam.transform.SetPositionAndRotation(Vector3.zero,
                                                     Quaternion.LookRotation(Vector3.forward));

                typeof(AimProvider)
                    .GetField("playerCamera", BindingFlags.NonPublic | BindingFlags.Instance)
                    .SetValue(Player.GetComponent<AimProvider>(), cam.GetComponent<Camera>());

                _target = GameObject.CreatePrimitive(PrimitiveType.Cube);
                _target.transform.position = new Vector3(0f, 0f, 12f);
                _target.transform.localScale = Vector3.one * 4f;
                // Set directly rather than healed up to it: the strike hits for 90 and the default
                // pool is 100, so a target left at its default would be one point from dying and
                // any retune of the damage would turn these tests into death tests.
                var health = _target.AddComponent<HealthComponent>();
                foreach (string field in new[] { "maxHealth", "currentHealth" })
                    typeof(HealthComponent)
                        .GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)
                        ?.SetValue(health, 500);

                _staff = new GameObject("conjurer staff");
                Artifact = _staff.AddComponent<ConjurerStaffArtifact>();

                // No ring prefab is wired, which is deliberate: these tests are about the damage
                // clock, and a null ring is the same code path a staff lying in the sand takes.
                Artifact.OnEquipped(Player);

                Physics.SyncTransforms();
            }

            public float CastSeconds => Get<float>("castSeconds");
            public int Damage => Get<int>("damage");
            public bool IsBilling => Get<bool>("_billing");
            public int TargetHealth => _target != null ? _target.GetComponent<HealthComponent>().GetHealth : 0;

            /// <summary>The press, as EquipmentController sends it.</summary>
            public void Fire()
            {
                var arg = new NetArg();
                Artifact.OnRequestUse(ref arg);
                Assert.AreNotEqual(Vector3.zero, arg.P, "The rig failed to aim at its own target.");

                Artifact.PlayUse(Player, arg);
                Artifact.TryUse(Player, arg);
            }

            /// <summary>Push the authority's wind-up clock forward and let it decide.</summary>
            public void AdvanceBilling(float seconds)
            {
                typeof(ConjurerStaffArtifact)
                    .GetField("_billElapsed", BindingFlags.NonPublic | BindingFlags.Instance)
                    .SetValue(Artifact, Get<float>("_billElapsed") + seconds);

                typeof(ConjurerStaffArtifact)
                    .GetMethod("TickBilling", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(Artifact, null);
            }

            public void ClearTarget()
            {
                if (_target != null) Object.DestroyImmediate(_target);
                _target = null;
                Physics.SyncTransforms();
            }

            public void Dispose()
            {
                if (_target != null) Object.DestroyImmediate(_target);
                if (_staff != null) Object.DestroyImmediate(_staff);
                if (Player != null) Object.DestroyImmediate(Player);
            }

            private T Get<T>(string field) => (T)typeof(ConjurerStaffArtifact)
                .GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(Artifact);
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
