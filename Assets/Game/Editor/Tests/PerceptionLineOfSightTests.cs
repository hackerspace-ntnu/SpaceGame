// What may and may not stand between an agent's eye and what it is looking at. Two failures, both
// seen in play: a ship's breathable-air TRIGGER volume hid a player 370 m away from twelve of
// fifteen NPCs, because the project queries triggers by default; and a waist-high wall hid a
// standing player completely, because only the body point was ever tried.
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;
using SpaceGame.Agents;

namespace SpaceGame.EditorTools
{
    public class PerceptionLineOfSightTests
    {
        private readonly System.Collections.Generic.List<GameObject> made = new();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in made) if (go != null) Object.DestroyImmediate(go);
            made.Clear();
        }

        private GameObject Make(string name, Vector3 at)
        {
            var go = new GameObject(name);
            go.transform.position = at;
            made.Add(go);
            return go;
        }

        // Faces +Z (the default forward), sees through nothing. Awake does not run in EditMode, so
        // the mask is written here rather than left to the runtime fallback.
        private PerceptionModule Eye()
        {
            var eye = Make("eye", Vector3.zero).AddComponent<PerceptionModule>();
            var so = new UnityEditor.SerializedObject(eye);
            so.FindProperty("occlusionLayers").intValue = ~0;
            so.ApplyModifiedPropertiesWithoutUndo();
            return eye;
        }

        // Built like PlayerCharacter: nothing on the root, the capsule on a child called "Collider".
        private Transform Player(Vector3 at)
        {
            GameObject root = Make("player", at);
            var body = new GameObject("Collider");
            body.transform.SetParent(root.transform, false);
            var capsule = body.AddComponent<CapsuleCollider>();
            capsule.height = 1.8f;
            capsule.radius = 0.4f;
            capsule.center = new Vector3(0f, 0.9f, 0f);
            return root.transform;
        }

        private BoxCollider Wall(Vector3 centre, Vector3 size)
        {
            var wall = Make("wall", centre).AddComponent<BoxCollider>();
            wall.size = size;
            return wall;
        }

        [Test]
        public void ATriggerVolumeIsNotAWall()
        {
            PerceptionModule eye = Eye();
            Transform player = Player(new Vector3(0f, 0f, 20f));
            BoxCollider air = Wall(new Vector3(0f, 2f, 10f), new Vector3(20f, 4f, 1f));
            air.isTrigger = true;
            Physics.SyncTransforms();

            Assert.IsTrue(eye.HasLineOfSightFrom(eye.EyePosition, player),
                          "breathable air, an interaction zone or a streaming volume hides nothing");

            air.isTrigger = false;
            Physics.SyncTransforms();
            Assert.IsFalse(eye.HasLineOfSightFrom(eye.EyePosition, player), "the same box made solid does");
        }

        [Test]
        public void AStandingPlayerIsSeenOverLowCover()
        {
            // Eye at 1.6 m, player 20 m out, a 1.2 m wall three quarters of the way. The ray to the
            // 1.0 m body point passes the wall at ~1.15 m and hits it; the ray to the head clears it.
            PerceptionModule eye = Eye();
            Transform player = Player(new Vector3(0f, 0f, 20f));
            Wall(new Vector3(0f, 0.6f, 15f), new Vector3(20f, 1.2f, 1f));
            Physics.SyncTransforms();

            Assert.IsFalse(eye.HasLineOfSightFrom(eye.EyePosition, player),
                           "a weapon aimed at the body would hit the wall");
            Assert.IsTrue(eye.HasLineOfSight(player), "but the head shows over it");
            Assert.IsTrue(eye.IsVisible(player), "and so the agent sees them");
        }

        [Test]
        public void ATallWallStillHidesThem()
        {
            PerceptionModule eye = Eye();
            Transform player = Player(new Vector3(0f, 0f, 20f));
            Wall(new Vector3(0f, 1.5f, 15f), new Vector3(20f, 3f, 1f));
            Physics.SyncTransforms();

            Assert.IsFalse(eye.IsVisible(player));
        }

        // ─────────── Seated crew: the house they ride in is not a wall ───────────

        // A walking house with crew seated inside its walls: the eye is parented under the hull
        // root (as NpcSeating.Attach parents it under the carrier's NetworkObject) and flagged as
        // cargo, and a hull wall stands between it and the player.
        private (PerceptionModule eye, BoxCollider hullWall) SeatedInHouse()
        {
            GameObject house = Make("house", Vector3.zero);
            var wall = new GameObject("Wall");
            wall.transform.SetParent(house.transform, false);
            wall.transform.localPosition = new Vector3(0f, 1.5f, 3f);
            BoxCollider hullWall = wall.AddComponent<BoxCollider>();
            hullWall.size = new Vector3(20f, 3f, 0.5f);

            PerceptionModule eye = Eye();
            eye.transform.SetParent(house.transform, false);
            eye.gameObject.AddComponent<AgentController>().RidesAsPassenger = true;
            return (eye, hullWall);
        }

        [Test]
        public void SeatedCrewSeeOutOfTheirOwnHouse()
        {
            (PerceptionModule eye, _) = SeatedInHouse();
            Transform player = Player(new Vector3(0f, 0f, 20f));
            Physics.SyncTransforms();

            Assert.IsTrue(eye.HasLineOfSightFrom(eye.EyePosition, player),
                "crew seated inside the house's walls see and shoot out of it (the user's call)");
        }

        [Test]
        public void SeatedCrewAreStillBlindedByAWallThatIsNotTheirHouse()
        {
            (PerceptionModule eye, _) = SeatedInHouse();
            Transform player = Player(new Vector3(0f, 0f, 20f));
            Wall(new Vector3(0f, 1.5f, 12f), new Vector3(20f, 3f, 1f));
            Physics.SyncTransforms();

            Assert.IsFalse(eye.HasLineOfSightFrom(eye.EyePosition, player));
        }

        [Test]
        public void AnNpcStandingInAHouseIsBlindedByItsWalls()
        {
            (PerceptionModule eye, _) = SeatedInHouse();
            eye.GetComponent<AgentController>().RidesAsPassenger = false;
            Transform player = Player(new Vector3(0f, 0f, 20f));
            Physics.SyncTransforms();

            Assert.IsFalse(eye.HasLineOfSightFrom(eye.EyePosition, player),
                "only cargo looks through its carrier; a parent alone is not a carrier");
        }

        [Test]
        public void AWallOnAKinematicBodyBlocksAsTheWallNotAsItsRoot()
        {
            // RaycastHit.transform is the Rigidbody's transform, so a wall collider on a child of a
            // kinematic root reads as the root. Filtering on that would let a target parented under
            // the same root count the wall as itself.
            GameObject machine = Make("machine", Vector3.zero);
            var body = machine.AddComponent<Rigidbody>();
            body.isKinematic = true;
            var wall = new GameObject("Wall");
            wall.transform.SetParent(machine.transform, false);
            wall.transform.localPosition = new Vector3(0f, 1.5f, 10f);
            wall.AddComponent<BoxCollider>().size = new Vector3(20f, 3f, 1f);

            PerceptionModule eye = Eye();
            Transform player = Player(new Vector3(0f, 0f, 20f));
            player.SetParent(machine.transform, true);
            Physics.SyncTransforms();

            Assert.IsFalse(eye.HasLineOfSightFrom(eye.EyePosition, player));
        }

        [Test]
        public void ASightCheckAllocatesNothing()
        {
            PerceptionModule eye = Eye();
            Transform player = Player(new Vector3(0f, 0f, 20f));
            Wall(new Vector3(0f, 0.6f, 15f), new Vector3(20f, 1.2f, 1f));
            Physics.SyncTransforms();

            eye.IsVisible(player);   // warm up: JIT, static buffers
            // Is.Not.AllocatingGCMemory, not GC.GetAllocatedBytesForCurrentThread: Unity's Mono always
            // reports 0 for the latter, so a test built on it can never fail (Diagnostics.md).
            TestDelegate checks = () => { for (int i = 0; i < 1000; i++) eye.IsVisible(player); };
            Assert.That(checks, Is.Not.AllocatingGCMemory(),
                "RaycastAll allocates a hit array per ray, per agent, per frame");
        }

        // ─────────── The throttled check AgentTargeting uses ───────────

        [Test]
        public void TheCachedCheckHoldsItsAnswerUntilTheIntervalRunsOut()
        {
            PerceptionModule eye = Eye();
            Transform player = Player(new Vector3(0f, 0f, 20f));
            Physics.SyncTransforms();
            Assert.IsTrue(eye.CanSeeCached(player));

            BoxCollider wall = Wall(new Vector3(0f, 1.5f, 15f), new Vector3(20f, 3f, 1f));
            Physics.SyncTransforms();
            Assert.IsTrue(eye.CanSeeCached(player), "within the interval the last answer stands");

            eye.TickSightRecheck(eye.SightRecheckInterval);
            Assert.IsFalse(eye.CanSeeCached(player), "past it, the ray is cast again");
            Object.DestroyImmediate(wall.gameObject);
        }

        [Test]
        public void ANewTargetIsCheckedAtOnce()
        {
            PerceptionModule eye = Eye();
            Transform near = Player(new Vector3(0f, 0f, 20f));
            Transform hidden = Player(new Vector3(5f, 0f, 20f));
            // Covers the line to `hidden` (x = 3.75 where it crosses z = 15), clear of the one to `near`.
            Wall(new Vector3(5f, 1.5f, 15f), new Vector3(6f, 3f, 1f));
            Physics.SyncTransforms();

            Assert.IsTrue(eye.CanSeeCached(near));
            Assert.IsFalse(eye.CanSeeCached(hidden), "a cached answer belongs to the target it was cast at");
        }

        [Test]
        public void TheNomadPrefabRechecksSightOnAnInterval()
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/agents/Characters/Nomad.prefab");
            Assert.IsNotNull(prefab);
            var perception = prefab.GetComponent<PerceptionModule>();
            Assert.IsNotNull(perception);
            Assert.Greater(perception.SightRecheckInterval, 0f,
                "a prefab saved before the field existed must take the class default, not 0 (every frame)");
        }
    }
}
