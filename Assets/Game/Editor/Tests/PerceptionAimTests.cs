// Where an agent's sight line is aimed. The failure this guards: a target's origin is at its
// feet, ON the ground, so a ray aimed there grazes every rise of terrain between the two and ends
// inside the ground -- and an agent with an 80 m sight range saw the player only at arm's length.
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Agents;

namespace SpaceGame.EditorTools
{
    public class PerceptionAimTests
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

        [Test]
        public void ATargetWithABodyIsAimedAtTheBodysCentre()
        {
            var eye = Make("eye", Vector3.zero).AddComponent<PerceptionModule>();
            var player = Make("player", new Vector3(20f, 0f, 0f));
            var capsule = player.AddComponent<CapsuleCollider>();
            capsule.height = 3f;
            capsule.center = new Vector3(0f, 1.5f, 0f);
            Physics.SyncTransforms();

            Vector3 aim = eye.AimPointOf(player.transform);
            Assert.AreEqual(1.5f, aim.y, 1e-3f, "the centre of a 3 m capsule standing on its feet");
        }

        [Test]
        public void ABodilessTargetIsAimedAboveItsFeet()
        {
            var eye = Make("eye", Vector3.zero).AddComponent<PerceptionModule>();
            var ghost = Make("ghost", new Vector3(20f, 0f, 0f));

            Assert.Greater(eye.AimPointOf(ghost.transform).y, 0.5f, "never the origin, which is on the ground");
        }

        [Test]
        public void ARiseInTheGroundNoLongerHidesAStandingPlayer()
        {
            // Flat ground, a 0.6 m rise halfway, an eye at 2.9 m, a 3 m player 40 m away. The old
            // feet-aimed ray hit the rise; the body-aimed one clears it and reaches the player.
            var eye = Make("clanker", Vector3.zero).AddComponent<PerceptionModule>();
            var so = new UnityEditor.SerializedObject(eye);
            so.FindProperty("eyeHeight").floatValue = 2.9f;
            so.FindProperty("occlusionLayers").intValue = ~0;
            so.ApplyModifiedPropertiesWithoutUndo();

            var ground = Make("ground", new Vector3(20f, -0.5f, 0f)).AddComponent<BoxCollider>();
            ground.size = new Vector3(100f, 1f, 100f);
            var rise = Make("rise", new Vector3(20f, 0.3f, 0f)).AddComponent<BoxCollider>();
            rise.size = new Vector3(4f, 0.6f, 20f);
            var player = Make("player", new Vector3(40f, 0f, 0f));
            var capsule = player.AddComponent<CapsuleCollider>();
            capsule.height = 3f;
            capsule.radius = 0.5f;
            capsule.center = new Vector3(0f, 1.5f, 0f);
            Physics.SyncTransforms();

            Assert.IsTrue(eye.HasLineOfSight(player.transform), "a standing player is visible over a knee-high rise");

            var wall = Make("wall", new Vector3(30f, 2f, 0f)).AddComponent<BoxCollider>();
            wall.size = new Vector3(1f, 4f, 20f);
            Physics.SyncTransforms();
            Assert.IsFalse(eye.HasLineOfSight(player.transform), "and a wall still hides them");
        }
    }
}
