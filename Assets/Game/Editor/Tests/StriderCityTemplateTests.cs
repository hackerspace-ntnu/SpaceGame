using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using SpaceGame.Agents;
using SpaceGame.World;

namespace SpaceGame.EditorTools
{
    /// The permanent walking city, read back from the persistent scene WireStriderCity writes it into.
    public class StriderCityTemplateTests
    {
        private const string ScenePath = "Assets/Game/Scenes/world/persistentScene.unity";
        // The column's shape, as WireStriderCity writes it (Striders.md "The city").
        private const int CityLanes = 2;
        private const float CityRowSpacing = 35f;
        private const float CityLaneSpacing = 30f;

        /// <summary>The template <paramref name="id"/> in the persistent scene, opened (and closed again) if needed.</summary>
        internal static NpcGroupTemplate ReadTemplate(string id) => ReadTemplates().Single(t => t.id == id);

        /// <summary>Every template in the persistent scene's NpcWorldSim, opened (and closed again) if needed.</summary>
        internal static NpcGroupTemplate[] ReadTemplates()
        {
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool opened = !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                NpcWorldSim sim = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<NpcWorldSim>(true)).Single();
                var templates = (NpcGroupTemplate[])typeof(NpcWorldSim)
                    .GetField("templates", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(sim);
                return templates;
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }

        [Test]
        public void TheCity_IsItsHousesTwoWorkersTwoCrabsThreeBargesEightScoutsOfEveryKind_AndACrewPerPost()
        {
            NpcGroupTemplate city = ReadTemplate(RosterAuthoring.StriderCityTemplateId);

            Assert.IsFalse(city.runtimeOnly, "the city is seeded at startup");
            Assert.AreEqual(RosterAuthoring.StriderFactionPath, AssetDatabase.GetAssetPath(city.tribe));

            int Count(System.Func<NpcGroupMemberSpec, bool> which) => city.members.Where(which).Sum(m => m.count);
            bool IsCrab(NpcGroupMemberSpec m) => AssetDatabase.GetAssetPath(m.prefab) == StriderCrabOutriderBuilder.PrefabPath;
            Assert.AreEqual(RosterAuthoring.StriderCityHouses, Count(m => AssetDatabase.GetAssetPath(m.prefab) == StriderCityBuilder.HabitatPath));
            Assert.AreEqual(2, RosterAuthoring.StriderCityHouses, "two walking houses (the user, 2026-10-05: three were too many)");
            Assert.AreEqual(1, city.members.Count(m => m.isLeader));
            Assert.AreEqual(StriderCityBuilder.HabitatPath, AssetDatabase.GetAssetPath(city.members[0].prefab), "carriers before crew");
            Assert.AreEqual(2, Count(m => AssetDatabase.GetAssetPath(m.prefab) == DesertCrawlerBuilder.PrefabPath));
            // A fixed prefab: the roster's Rider role is the monowheels now, and the crabs stay the flank escort.
            Assert.AreEqual(2, Count(m => IsCrab(m) && !m.crew));
            Assert.IsFalse(city.members.Any(m => m.prefab == null && !m.crew), "only crew are drawn from the roster");
            string[] barges = StriderBargeBuilder.Barges.Select(b => StriderBargeBuilder.PrefabPath(b.Variant)).ToArray();
            bool IsBarge(NpcGroupMemberSpec m) => barges.Contains(AssetDatabase.GetAssetPath(m.prefab));
            CollectionAssert.AreEquivalent(barges, city.members.Where(IsBarge).Select(m => AssetDatabase.GetAssetPath(m.prefab)),
                                           "one barge of each kind (the user, 2026-09-25)");
            Assert.IsTrue(city.members.Where(IsBarge).All(m => m.count == 1 && !m.crew && !m.isLeader));
            Assert.AreEqual(RosterAuthoring.StriderCityHouses * StriderCityBuilder.CrewPosts
                            + barges.Length * StriderBargeBuilder.CrewPosts, Count(m => m.crew && !IsElder(m)), "one crew member per post");
            Assert.Greater(System.Array.FindIndex(city.members, m => m.crew), System.Array.FindLastIndex(city.members, IsBarge),
                           "crew after every carrier, or they spawn with nowhere to sit");

            // Every kind of monowheel rides with the city (the user, 2026-10-05: some were too rare).
            string[] wheels = StriderMonowheelBuilder.Singles.Concat(StriderMonowheelBuilder.Doubles)
                .Select(StriderMonowheelBuilder.PrefabPath).ToArray();
            bool IsScout(NpcGroupMemberSpec m) => wheels.Contains(AssetDatabase.GetAssetPath(m.prefab));
            Assert.AreEqual(RosterAuthoring.StriderCityScouts, Count(IsScout), "the scouts the rota sends out");
            Assert.IsTrue(city.members.Where(IsScout).All(m => !m.crew && !m.isLeader && m.role == RosterRole.Scout));
            for (int i = 0; i < wheels.Length; i++)
                Assert.AreEqual(RosterAuthoring.ScoutShare(i, wheels.Length),
                                Count(m => AssetDatabase.GetAssetPath(m.prefab) == wheels[i]), $"{wheels[i]}: its share of the scouts");

            Assert.AreEqual(CityLanes, city.formation.Lanes, "formation shape unchanged");
            Assert.AreEqual(CityRowSpacing, city.formation.RowSpacing, 0.01f, "re-run Wire Strider City");
            Assert.AreEqual(CityLaneSpacing, city.formation.LaneSpacing, 0.01f);

            Assert.IsTrue(city.tasks.All(t => t.targetSite == SiteKind.Ruin || t.targetSite == SiteKind.ScrapField));
            Assert.IsTrue(city.tasks.All(t => Mathf.Approximately(t.travelSpeedMultiplier, StriderCityBuilder.CityTravelMultiplier)));
            Assert.IsTrue(city.tasks.All(t => t.levelGround.Equals(StriderCityBuilder.CityLevelGround)),
                          "every stop holds the city to its level-ground rule (re-run Wire Strider City)");
            Assert.AreEqual(StriderCityBuilder.CityLeaderSpeed, city.travelSpeed, 0.01f, "folded and live speeds agree");

            Assert.IsTrue(city.useStartPosition, "the city starts where a new player can find it, not at a Ruin");
            float fromAnchor = Vector2.Distance(new Vector2(city.startPosition.x, city.startPosition.z), StriderCityStartSite.CityStartAnchor);
            Assert.LessOrEqual(fromAnchor, StriderCityStartSite.CityStartReach + 1f,
                               "where the user stood when they asked for it (re-run Wire Strider City)");
            Assert.AreEqual(RosterAuthoring.StriderCityInitialStay, city.initialStaySeconds, 0.01f,
                            "parked at its start long enough for a new player to walk over (re-run Wire Strider City)");
        }

        private static bool IsElder(NpcGroupMemberSpec m) => AssetDatabase.GetAssetPath(m.prefab) == StriderElderBuilder.PrefabPath;

        [Test]
        public void TheElders_RideLast_AsStandingCrew_OneOrTwoByWeight()
        {
            NpcGroupTemplate city = ReadTemplate(RosterAuthoring.StriderCityTemplateId);
            NpcGroupMemberSpec elder = city.members.Last();
            Assert.IsTrue(IsElder(elder), "the elders are the last spec: a weighted count shifts every later member's draw");
            Assert.AreEqual(1, city.members.Count(IsElder));
            Assert.IsTrue(elder.crew && !elder.isLeader, "elders ride the houses' standing posts");
            CollectionAssert.AreEqual(new[] { 1, 2 }, elder.countWeights.Select(w => w.count).ToArray());
            CollectionAssert.AreEqual(new[] { 0.75f, 0.25f }, elder.countWeights.Select(w => w.weight).ToArray());
            Assert.LessOrEqual(elder.countWeights.Max(w => w.count), RosterAuthoring.StriderCityHouses * StriderCityBuilder.StandingPosts,
                               "never more elders than standing posts: a spare one would walk beside the houses");
        }

        /// FormationModule measures regroupDistance from the leader: a follower whose slot is farther
        /// out than that counts as separated at its own slot and rides for the leader instead. The
        /// column is dealt anew in every world, so any vehicle may draw the farthest slot.
        [Test]
        public void EveryFollower_ReachesTheFarthestSlot_WithinItsRegroupDistance()
        {
            NpcGroupTemplate city = ReadTemplate(RosterAuthoring.StriderCityTemplateId);
            List<NpcGroupMemberSpec> followers = city.members.Where(m => !m.crew && !m.isLeader).ToList();

            Assert.AreEqual(RosterAuthoring.CityFollowers, followers.Sum(m => m.count), "CityFollowers counts every slot the template fills");
            float reach = FormationMath.FarthestSlot(RosterAuthoring.CityFollowers, city.formation);
            foreach (NpcGroupMemberSpec spec in followers)
                Assert.Less(reach, spec.prefab.GetComponent<FormationModule>().RegroupDistance,
                            $"{spec.prefab.name}: rebuild it after a template change");
        }

        /// Every vehicle behind the lead house is a card of the city's deck (ColumnDeal), with the
        /// rules and the footprint WireStriderCity measured from its prefab.
        [Test]
        public void EveryFollower_IsACardOfTheDeck_WithItsRulesAndFootprint()
        {
            NpcGroupTemplate city = ReadTemplate(RosterAuthoring.StriderCityTemplateId);
            NpcGroupMemberSpec leader = city.members.Single(m => m.isLeader);
            Assert.IsFalse(leader.column.shuffled, "the lead house rides first");
            Assert.AreEqual(StriderCityColumn.Card(leader.prefab, shuffled: false), leader.column, "re-run Wire Strider City");

            foreach (NpcGroupMemberSpec spec in city.members.Where(m => !m.crew && !m.isLeader))
            {
                Assert.IsTrue(spec.column.shuffled, $"{spec.prefab.name}: dealt into the column");
                Assert.AreEqual(StriderCityColumn.Card(spec.prefab, shuffled: true), spec.column,
                                $"{spec.prefab.name}: re-run Wire Strider City after a rebuild");
            }

            Assert.IsTrue(city.members.Where(m => m.crew).All(m => !m.column.shuffled), "the crew ride the carriers");
        }

        /// The user, 2026-10-04 and 2026-10-05: not a parade of matching pairs, not sorted by kind, and
        /// "like drawing from a card deck". Whatever seed a world draws, the column it deals keeps every
        /// carrier on the ground the city levels at a stop, no house, crawler, crab or barge beside or
        /// behind its own kind, and every barge (34 m, longer than a row is deep) clear of its neighbours.
        [Test]
        public void EverySeed_DealsAColumnThatKeepsTheRules_AndTheWorldsDiffer()
        {
            NpcGroupTemplate city = ReadTemplate(RosterAuthoring.StriderCityTemplateId);
            NpcGroupMemberSpec leader = city.members.Single(m => m.isLeader);
            List<ColumnCard> deck = city.members.Where(m => m.column.shuffled)
                .SelectMany(m => Enumerable.Repeat(m.column, m.count)).ToList();
            List<int> slots = Enumerable.Range(0, deck.Count).ToList();

            var columns = new HashSet<string>();
            for (int seed = 0; seed < 300; seed++)
            {
                int worldSeed = RosterDraw.StableHash("world" + seed);
                Assert.IsTrue(ColumnDeal.TryDeal(leader.column, deck, slots, city.formation, worldSeed, out int[] order), $"seed {worldSeed}");
                List<PlannedMember> plan = NpcGroupComposition.Resolve(new NpcGroup { Id = city.id, RosterSeed = worldSeed }, city);
                columns.Add(string.Join(",", plan.Skip(1).Take(deck.Count).Select(p => p.Prefab.name)));
            }

            Assert.Greater(columns.Count, 290, "every world deals its own column");
        }
    }

    /// The Striders' war party, as WireWorldSim writes it into the persistent scene: a monowheel convoy.
    public class StriderWarPartyTemplateTests
    {
        private static IEnumerable<GameObject> Monowheels =>
            StriderMonowheelBuilder.Singles.Concat(StriderMonowheelBuilder.Doubles)
                .Select(v => AssetDatabase.LoadAssetAtPath<GameObject>(StriderMonowheelBuilder.PrefabPath(v)));

        [Test]
        public void Folded_ItTravelsAtTheWheelsCruiseSpeed()
        {
            NpcGroupTemplate party = StriderCityTemplateTests.ReadTemplate(RosterAuthoring.StriderWarPartyTemplateId);
            Assert.AreEqual(StriderMonowheelBuilder.CruiseSpeed, party.travelSpeed, 0.01f,
                            "a folded convoy must be where its live wheels would be");
        }

        [Test]
        public void ItsFormation_IsSizedForTheWheels_NotForPeopleOnFoot()
        {
            FormationShape shape = StriderCityTemplateTests.ReadTemplate(RosterAuthoring.StriderWarPartyTemplateId).formation;
            Assert.AreEqual(RosterAuthoring.ConvoyShape.RowSpacing, shape.RowSpacing, 0.01f, "run Wire War Party Templates");
            Assert.AreEqual(RosterAuthoring.ConvoyShape.LaneSpacing, shape.LaneSpacing, 0.01f);

            float slop = shape.LateralJitter + shape.DriftAmplitude;
            float longSlop = shape.LongitudinalJitter + shape.DriftAmplitude;
            int followers = RosterMostRiders() - 1;

            foreach (GameObject wheel in Monowheels)
            {
                Vector2 footprint = Footprint(wheel);
                var formation = wheel.GetComponent<FormationModule>();
                var so = new SerializedObject(formation);
                float tolerance = so.FindProperty("slotTolerance").floatValue;

                Assert.GreaterOrEqual(shape.LaneSpacing - 2f * slop, footprint.x, $"{wheel.name}: side by side, jitter and drift at their worst");
                Assert.GreaterOrEqual(shape.RowSpacing - 2f * longSlop, footprint.y, $"{wheel.name}: nose to tail");
                Assert.Less(2f * tolerance, shape.LaneSpacing - footprint.x, $"{wheel.name}: two neighbours parked off their slots");
                Assert.Less(2f * tolerance, shape.RowSpacing - footprint.y, $"{wheel.name}: two in line parked off their slots");
                Assert.Less(FormationMath.FarthestSlot(followers, shape), formation.RegroupDistance, $"{wheel.name}: a slot it could never hold");
            }
        }

        private static int RosterMostRiders()
        {
            var roster = AssetDatabase.LoadAssetAtPath<FactionRoster>(RosterAuthoring.StriderRosterPath);
            return roster.warPartyTiers.Max(t => t.roles.Sum(r => r.count));
        }

        /// <summary>Width (x) and length (z) of the wheel's colliders, in its own space.</summary>
        private static Vector2 Footprint(GameObject wheel)
        {
            Transform root = wheel.transform;
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (Collider c in wheel.GetComponentsInChildren<Collider>(true))
            {
                Matrix4x4 toRoot = root.worldToLocalMatrix * c.transform.localToWorldMatrix;
                var corners = new List<Vector3>();
                switch (c)
                {
                    case BoxCollider box:
                        for (int i = 0; i < 8; i++)
                            corners.Add(box.center + Vector3.Scale(box.size * 0.5f,
                                new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                        break;
                    case SphereCollider sphere:
                        foreach (Vector3 d in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
                            corners.Add(sphere.center + d * sphere.radius);
                        break;
                    default:
                        Assert.Fail($"{wheel.name}: a {c.GetType().Name} this footprint does not measure");
                        break;
                }

                foreach (Vector3 corner in corners)
                {
                    Vector3 p = toRoot.MultiplyPoint3x4(corner);
                    minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                    minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z);
                }
            }

            return new Vector2(maxX - minX, maxZ - minZ);
        }
    }
}
