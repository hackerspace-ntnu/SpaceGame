// What a dropped item has to be, pinned where playtesting cannot reach it.
//
// Two of the three failures these cover are invisible on the machine you are testing on. A prefab
// with no NetworkTransform is a dropped rifle that rolls perfectly for the host and never moves an
// inch for anybody else — the same shape of bug NetworkPrefabRegistrationTests exists for. A prefab
// whose world size drifts from the gear wall's is a size pop nobody notices until they carry
// something out of the ship. The third is the one that started this: an item lying in the world was
// the only copy of itself that nothing sized at all.
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.EditorTools;
using SpaceGame.Items;

namespace SpaceGame.Tests
{
    public class WorldItemTests
    {
        private GameObject scratch;

        [TearDown]
        public void TearDown()
        {
            if (scratch != null) Object.DestroyImmediate(scratch);
        }

        /// <summary>
        /// Every shipped item is a whole world object: sized, shaped, pushable, and visible moving
        /// on a machine other than the one simulating it.
        /// </summary>
        [Test]
        public void EveryItemPrefab_CanLieInTheWorld()
        {
            var problems = new List<string>();

            foreach (GameObject prefab in ItemWorldPresence.Roster())
                foreach (string problem in ItemWorldPresence.ProblemsWith(prefab))
                    problems.Add($"{AssetDatabase.GetAssetPath(prefab)}: {problem}");

            Assert.IsEmpty(problems,
                "These item prefabs cannot lie in the world correctly:\n  " +
                string.Join("\n  ", problems) +
                "\nRun Tools/SpaceGame/Items/Fix World Item Bodies.");
        }

        /// <summary>
        /// The decision itself: an item lying in the sand is the size the ship's gear wall draws it.
        ///
        /// <para>
        /// Asserted over the real roster and against the wall's own arithmetic rather than against
        /// the literal 1.908, because the number is not the point — the agreement is. The wall is
        /// where the player last saw the item before they carried it out and put it down, so a
        /// change to either side that quietly stops matching is the bug this catches.
        /// </para>
        /// </summary>
        [Test]
        public void AnItemLiesInTheWorldAtTheSizeTheGearWallDrawsIt()
        {
            var wrong = new List<string>();

            foreach (GameObject prefab in ItemWorldPresence.Roster())
            {
                if (prefab.GetComponent<WorldItem>() is { KeepsAuthoredSize: true }) continue;

                // A grip that sizes to zero means "keep the size the artist built", which the mat
                // honours and the world deliberately does not — see ItemWorldScale.SizeOf. The two
                // are allowed to disagree there, and only there.
                ItemGrip grip = prefab.GetComponentInChildren<ItemGrip>(true);
                if (grip != null && grip.PackSize <= 0f) continue;

                Vector3 onTheMat = ItemFootprint.SizeOf(prefab);
                float drawnOnTheWall =
                    Mathf.Max(onTheMat.x, Mathf.Max(onTheMat.y, onTheMat.z)) * PackScale.WallDisplay;

                float inTheWorld = ItemWorldScale.SizeOf(prefab);

                if (Mathf.Abs(drawnOnTheWall - inTheWorld) > 1e-3f)
                    wrong.Add($"{prefab.name}: wall {drawnOnTheWall:0.000} m, world {inTheWorld:0.000} m");
            }

            Assert.IsEmpty(wrong,
                "These items are a different size on the gear wall and in the sand:\n  " +
                string.Join("\n  ", wrong));
        }

        /// <summary>
        /// Sizing is a fixed point. It has to be: the same instance is measured again by a save
        /// restore writing the recorded scale back over it, and a sizing that multiplied each time
        /// would grow an item by 1.9x per reload.
        /// </summary>
        [Test]
        public void SizingAnItemTwiceLeavesItWhereItWas()
        {
            scratch = Cube(0.5f, packSize: 0.4f);

            scratch.transform.localScale = ItemWorldScale.LocalScaleFor(scratch);
            Vector3 once = scratch.transform.localScale;

            scratch.transform.localScale = ItemWorldScale.LocalScaleFor(scratch);

            Assert.AreEqual(once.x, scratch.transform.localScale.x, 1e-4f,
                "A second pass resized the item, so every save reload would inflate it again.");
        }

        /// <summary>
        /// The trap that makes this whole change safe: the hand's zero-hold-size branch means
        /// literally "whatever scale this instance is carrying", and Instantiate has already run
        /// WorldItem.Awake by the time the hand gets a word in. Without the undo, the four pinned
        /// Fitted items would come out of a drop-size change 1.9x too big in the hand.
        /// </summary>
        [Test]
        public void AnItemGoingIntoAHandLosesTheWorldsSizing()
        {
            scratch = Cube(0.5f, packSize: 0.4f);
            scratch.GetComponent<WorldItem>().Configure();

            Assert.That(scratch.transform.localScale.x, Is.Not.EqualTo(0.5f).Within(1e-4f),
                "The item was never sized for the world, so this test proves nothing.");

            EquipItemSocket.Sanitize(scratch);

            Assert.AreEqual(0.5f, scratch.transform.localScale.x, 1e-4f,
                "A held item kept the size the WORLD draws it at. EquipItemSocket.Sanitize must " +
                "call WorldItem.Suppress.");
        }

        /// <summary>
        /// An item nobody sized is one size everywhere. Three frames used to answer this with three
        /// copies of 0.30f and a comment each asking the next reader to keep them equal.
        /// </summary>
        [Test]
        public void AnUnsizedItemGetsTheSharedDefault()
        {
            scratch = Cube(1f, packSize: 0f, withGrip: false);

            Assert.AreEqual(ItemBounds.DefaultSize * ItemWorldScale.Factor,
                            ItemWorldScale.SizeOf(scratch), 1e-4f);
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        /// <summary>A 1 m cube standing in for an item prefab: real meshes, so ItemBounds measures it.</summary>
        private static GameObject Cube(float scale, float packSize, bool withGrip = true)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.localScale = Vector3.one * scale;

            if (withGrip)
            {
                var so = new SerializedObject(cube.AddComponent<ItemGrip>());
                so.FindProperty("packSize").floatValue = packSize;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            cube.AddComponent<WorldItem>();
            return cube;
        }
    }
}
