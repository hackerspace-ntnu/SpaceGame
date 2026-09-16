// What may and may not stand between an agent's eye and what it is looking at. Two failures, both
// seen in play: a ship's breathable-air TRIGGER volume hid a player 370 m away from twelve of
// fifteen NPCs, because the project queries triggers by default; and a waist-high wall hid a
// standing player completely, because only the body point was ever tried.
using NUnit.Framework;
using UnityEngine;
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
    }
}
