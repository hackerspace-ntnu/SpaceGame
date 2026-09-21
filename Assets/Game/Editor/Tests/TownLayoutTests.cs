// A town generator's two promises are that the same seed gives the same town and that buildings do
// not stand inside each other. Both are silent when broken — a drifted seed just looks like a
// different town, and two overlapping buildings look like one odd building — so both are pinned
// here.
//
// TownLayout is pure, so this needs no scene, no terrain and no prefabs: the clearance function is
// a lambda, which is exactly what it was parameterised for.
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.World.Towns;

namespace SpaceGame.EditorTools
{
    public class TownLayoutTests
    {
        private readonly List<Object> assets = new();

        [TearDown]
        public void TearDown()
        {
            foreach (Object asset in assets)
                if (asset != null) Object.DestroyImmediate(asset);

            assets.Clear();
        }

        /// <summary>
        /// A stand-in prefab. Never instantiated — the layout only ever passes it to the clearance
        /// function and records its index, which is the point of it holding no GameObject.
        /// </summary>
        private GameObject Prefab(string prefabName)
        {
            var go = new GameObject(prefabName);
            assets.Add(go);
            return go;
        }

        private static TownRecipe Recipe()
        {
            var recipe = new TownRecipe();
            recipe.innerRadius = 20f;
            recipe.midRadius = 40f;
            recipe.outerRadius = 60f;
            recipe.minStructureSpacing = 8f;
            recipe.buildingPadding = 2f;
            recipe.defaultFootprint = 6f;
            recipe.maxPlacementAttempts = 40;
            return recipe;
        }

        private TownGroup Group(string label, int min, int max, params GameObject[] prefabs) =>
            new TownGroup
            {
                label = label,
                prefabs = prefabs,
                count = new Vector2Int(min, max),
            };

        /// <summary>Every prefab needs the same room, so overlap tests have one number to reason about.</summary>
        private static System.Func<GameObject, float> FixedClearance(float radius) => _ => radius;

        // ── Determinism ──────────────────────────────────────────────────────────

        [Test]
        public void TheSameSeedGivesTheSameTown()
        {
            TownRecipe recipe = Recipe();
            recipe.buildings.Add(Group("huts", 4, 8, Prefab("HutA"), Prefab("HutB")));
            recipe.people.Add(Group("folk", 3, 6, Prefab("Nomad")));

            List<TownSlot> a = TownLayout.Build(recipe, 1701, FixedClearance(5f));
            List<TownSlot> b = TownLayout.Build(recipe, 1701, FixedClearance(5f));

            Assert.AreEqual(a.Count, b.Count, "same seed, same number of things");

            for (int i = 0; i < a.Count; i++)
            {
                Assert.AreEqual(a[i].Section, b[i].Section, $"slot {i} section");
                Assert.AreEqual(a[i].PrefabIndex, b[i].PrefabIndex, $"slot {i} prefab");
                Assert.AreEqual(a[i].LocalXZ.x, b[i].LocalXZ.x, 0.0001f, $"slot {i} x");
                Assert.AreEqual(a[i].LocalXZ.y, b[i].LocalXZ.y, 0.0001f, $"slot {i} z");
                Assert.AreEqual(a[i].Yaw, b[i].Yaw, 0.0001f, $"slot {i} yaw");
            }
        }

        [Test]
        public void ADifferentSeedGivesADifferentTown()
        {
            TownRecipe recipe = Recipe();
            recipe.buildings.Add(Group("huts", 6, 6, Prefab("Hut")));

            List<TownSlot> a = TownLayout.Build(recipe, 1, FixedClearance(5f));
            List<TownSlot> b = TownLayout.Build(recipe, 2, FixedClearance(5f));

            bool anyMoved = false;
            for (int i = 0; i < Mathf.Min(a.Count, b.Count); i++)
                if ((a[i].LocalXZ - b[i].LocalXZ).sqrMagnitude > 0.01f) { anyMoved = true; break; }

            Assert.IsTrue(anyMoved || a.Count != b.Count, "two seeds should not agree");
        }

        [Test]
        public void TheLayoutNeverTouchesUnityRandom()
        {
            // If anything in the layout drew from the global generator, seeding it differently
            // either side would change the result. This is the regression guard for the bug
            // RobotSettlementGenerator actually has.
            TownRecipe recipe = Recipe();
            recipe.buildings.Add(Group("huts", 5, 5, Prefab("Hut")));

            Random.InitState(1);
            List<TownSlot> a = TownLayout.Build(recipe, 99, FixedClearance(5f));

            Random.InitState(999999);
            List<TownSlot> b = TownLayout.Build(recipe, 99, FixedClearance(5f));

            Assert.AreEqual(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++)
                Assert.AreEqual(a[i].LocalXZ, b[i].LocalXZ, $"slot {i} moved with UnityEngine.Random");
        }

        // ── Counts ───────────────────────────────────────────────────────────────

        [Test]
        public void AGroupPlacesInsideItsCountRange()
        {
            TownRecipe recipe = Recipe();
            recipe.organizedLayout = false;
            recipe.people.Add(Group("folk", 2, 5, Prefab("Nomad")));

            for (int seed = 0; seed < 25; seed++)
            {
                int placed = TownLayout.Build(recipe, seed, FixedClearance(1f))
                                      .FindAll(s => s.Section == TownSection.Person).Count;

                Assert.GreaterOrEqual(placed, 2, $"seed {seed}");
                Assert.LessOrEqual(placed, 5, $"seed {seed}");
            }
        }

        [Test]
        public void ACountWhoseLowEndIsZeroCanLegallyPlaceNothing()
        {
            // Not a bug — but it is the one that looks exactly like a broken generator, because
            // "At least 1, At most 0" reads as "one of them" and means "a coin flip". Seed 1701
            // with a 0–1 count is the case that was actually hit: it placed nothing at all.
            //
            // Pinned so that nobody "fixes" the layout by quietly clamping the range. The fix lives
            // where the mistake is made: labelled boxes in the inspector, and TownGenerator.Verify
            // failing loudly and naming the group.
            TownRecipe recipe = Recipe();
            recipe.buildings.Add(Group("barracks", 1, 0, Prefab("Barracks")));

            Assert.AreEqual(0, TownLayout.Build(recipe, 1701, FixedClearance(5f)).Count,
                            "a 0-to-1 count rolled zero, which is the whole point of this test");

            bool everPlaced = false;
            for (int seed = 0; seed < 40; seed++)
                if (TownLayout.Build(recipe, seed, FixedClearance(5f)).Count > 0) { everPlaced = true; break; }

            Assert.IsTrue(everPlaced, "and some other seed does place it — the range really is 0..1");
        }

        [Test]
        public void ACountWithTheSameNumberInBothBoxesAlwaysPlacesThatMany()
        {
            TownRecipe recipe = Recipe();
            recipe.buildings.Add(Group("barracks", 1, 1, Prefab("Barracks")));

            for (int seed = 0; seed < 40; seed++)
                Assert.AreEqual(1, TownLayout.Build(recipe, seed, FixedClearance(5f)).Count,
                                $"seed {seed} should place exactly one");
        }

        [Test]
        public void AGroupWithNoPrefabsPlacesNothingRatherThanThrowing()
        {
            TownRecipe recipe = Recipe();
            recipe.buildings.Add(Group("empty", 5, 5));

            Assert.AreEqual(0, TownLayout.Build(recipe, 7, FixedClearance(5f)).Count);
        }

        [Test]
        public void TheCentrepieceGoesInTheMiddle()
        {
            TownRecipe recipe = Recipe();
            recipe.centrepiecePrefab = Prefab("Refinery");

            List<TownSlot> slots = TownLayout.Build(recipe, 3, FixedClearance(5f));

            Assert.AreEqual(1, slots.Count);
            Assert.AreEqual(TownSection.Centrepiece, slots[0].Section);
            Assert.AreEqual(Vector2.zero, slots[0].LocalXZ);
        }

        [Test]
        public void ClustersPlaceTheirMembersTogether()
        {
            TownRecipe recipe = Recipe();
            recipe.organizedLayout = false;

            TownGroup group = Group("scrap", 1, 1, Prefab("Scrap"));
            group.clusterSize = new Vector2Int(4, 4);
            group.clusterSpread = 3f;
            recipe.scatter.Add(group);

            List<TownSlot> slots = TownLayout.Build(recipe, 11, FixedClearance(1f));

            Assert.AreEqual(4, slots.Count, "one cluster of four");

            // Every member within spread of the first, which sits on the cluster centre.
            Vector2 centre = slots[0].LocalXZ;
            for (int i = 1; i < slots.Count; i++)
                Assert.LessOrEqual((slots[i].LocalXZ - centre).magnitude, 3.001f, $"member {i} strayed");
        }

        // ── Clearance ────────────────────────────────────────────────────────────

        [Test]
        public void ClearanceReservingThingsNeverOverlap()
        {
            TownRecipe recipe = Recipe();
            recipe.centrepiecePrefab = Prefab("Refinery");

            TownGroup group = Group("halls", 6, 6, Prefab("Hall"));
            group.reservesClearance = true;
            recipe.buildings.Add(group);

            const float radius = 6f;

            for (int seed = 0; seed < 20; seed++)
            {
                List<TownSlot> slots = TownLayout.Build(recipe, seed, FixedClearance(radius));

                for (int i = 0; i < slots.Count; i++)
                for (int j = i + 1; j < slots.Count; j++)
                {
                    float gap = (slots[i].LocalXZ - slots[j].LocalXZ).magnitude;
                    Assert.GreaterOrEqual(gap, radius * 2f - 0.001f,
                        $"seed {seed}: slots {i} and {j} are {gap:F2} apart, closer than {radius * 2f}");
                }
            }
        }

        [Test]
        public void PeopleDoNotReserveSpaceAndMayStandAnywhere()
        {
            // A guard standing in the square is not an obstruction. If people reserved clearance
            // they would push the buildings apart, and a crowded town would silently lose houses.
            TownRecipe recipe = Recipe();
            recipe.organizedLayout = false;

            TownGroup crowd = Group("crowd", 30, 30, Prefab("Nomad"));
            crowd.reservesClearance = false;
            recipe.people.Add(crowd);

            List<TownSlot> slots = TownLayout.Build(recipe, 5, FixedClearance(20f));

            Assert.AreEqual(30, slots.Count, "nobody was dropped for want of room");
        }

        [Test]
        public void EverythingLandsInsideItsOwnBand()
        {
            TownRecipe recipe = Recipe();
            recipe.organizedLayout = false;

            TownGroup group = Group("ring", 12, 12, Prefab("Shed"));
            group.band = new Vector2(30f, 45f);
            recipe.buildings.Add(group);

            foreach (TownSlot slot in TownLayout.Build(recipe, 21, FixedClearance(1f)))
            {
                float r = slot.LocalXZ.magnitude;
                Assert.GreaterOrEqual(r, 29.99f, "inside the band's inner edge");
                Assert.LessOrEqual(r, 45.01f, "outside the band's outer edge");
            }
        }

        [Test]
        public void AnUnsetBandMeansTheWholeTown()
        {
            TownRecipe recipe = Recipe();
            recipe.organizedLayout = false;
            recipe.scatter.Add(Group("debris", 20, 20, Prefab("Scrap")));

            foreach (TownSlot slot in TownLayout.Build(recipe, 33, FixedClearance(1f)))
                Assert.LessOrEqual(slot.LocalXZ.magnitude, recipe.outerRadius + 0.01f);
        }

        // ── Facing ───────────────────────────────────────────────────────────────

        [Test]
        public void FaceCentreSnappedOnlyEverProducesQuarterTurns()
        {
            TownRecipe recipe = Recipe();
            recipe.organizedLayout = false;

            TownGroup group = Group("sheds", 20, 20, Prefab("Shed"));
            group.yaw = TownYaw.FaceCentreSnapped;
            recipe.buildings.Add(group);

            foreach (TownSlot slot in TownLayout.Build(recipe, 4, FixedClearance(1f)))
            {
                float remainder = Mathf.Abs(slot.Yaw % 90f);
                Assert.IsTrue(remainder < 0.001f || Mathf.Abs(remainder - 90f) < 0.001f,
                              $"yaw {slot.Yaw} is not a quarter turn");
            }
        }

        [Test]
        public void KeepLeavesTheYawAlone()
        {
            TownRecipe recipe = Recipe();
            recipe.organizedLayout = false;

            TownGroup group = Group("poles", 10, 10, Prefab("Pole"));
            group.yaw = TownYaw.Keep;
            recipe.props.Add(group);

            foreach (TownSlot slot in TownLayout.Build(recipe, 6, FixedClearance(1f)))
                Assert.AreEqual(0f, slot.Yaw, "Keep means the generator writes no rotation of its own");
        }

        // ── Null safety ──────────────────────────────────────────────────────────

        [Test]
        public void ANullRecipeGivesAnEmptyTownRatherThanThrowing()
        {
            Assert.AreEqual(0, TownLayout.Build(null, 1, FixedClearance(1f)).Count);
        }
    }
}
