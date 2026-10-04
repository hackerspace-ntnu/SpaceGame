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
        private const float CitySpacing = 30f;

        /// <summary>The template <paramref name="id"/> in the persistent scene, opened (and closed again) if needed.</summary>
        internal static NpcGroupTemplate ReadTemplate(string id)
        {
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool opened = !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                NpcWorldSim sim = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<NpcWorldSim>(true)).Single();
                var templates = (NpcGroupTemplate[])typeof(NpcWorldSim)
                    .GetField("templates", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(sim);
                return templates.Single(t => t.id == id);
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }

        [Test]
        public void TheCity_IsItsHousesTwoWorkersTwoCrabsEightScoutsAndSixCrewPerHouse()
        {
            NpcGroupTemplate city = ReadTemplate(RosterAuthoring.StriderCityTemplateId);

            Assert.IsFalse(city.runtimeOnly, "the city is seeded at startup");
            Assert.AreEqual(RosterAuthoring.StriderFactionPath, AssetDatabase.GetAssetPath(city.tribe));

            int Count(System.Func<NpcGroupMemberSpec, bool> which) => city.members.Where(which).Sum(m => m.count);
            bool IsCrab(NpcGroupMemberSpec m) => AssetDatabase.GetAssetPath(m.prefab) == StriderCrabOutriderBuilder.PrefabPath;
            Assert.AreEqual(RosterAuthoring.StriderCityHouses, Count(m => AssetDatabase.GetAssetPath(m.prefab) == StriderCityBuilder.HabitatPath));
            Assert.AreEqual(1, city.members.Count(m => m.isLeader));
            Assert.AreEqual(StriderCityBuilder.HabitatPath, AssetDatabase.GetAssetPath(city.members[0].prefab), "carriers before crew");
            Assert.AreEqual(2, Count(m => AssetDatabase.GetAssetPath(m.prefab) == DesertCrawlerBuilder.PrefabPath));
            // A fixed prefab: the roster's Rider role is the monowheels now, and the crabs stay the flank escort.
            Assert.AreEqual(2, Count(m => IsCrab(m) && !m.crew));
            Assert.IsFalse(city.members.Any(m => m.prefab == null && !m.crew), "only crew are drawn from the roster");
            Assert.AreEqual(RosterAuthoring.StriderCityHouses * StriderCityBuilder.CrewPosts, Count(m => m.crew));

            string[] singles = StriderMonowheelBuilder.Singles.Select(StriderMonowheelBuilder.PrefabPath).ToArray();
            bool IsScout(NpcGroupMemberSpec m) => singles.Contains(AssetDatabase.GetAssetPath(m.prefab));
            Assert.AreEqual(RosterAuthoring.StriderCityScouts, Count(IsScout), "the scouts the rota sends out");
            Assert.IsTrue(city.members.Where(IsScout).All(m => !m.crew && !m.isLeader && m.role == RosterRole.Scout));
            CollectionAssert.AreEquivalent(singles, city.members.Where(IsScout).Select(m => AssetDatabase.GetAssetPath(m.prefab)),
                                           "one entry per single");
            int firstScout = System.Array.FindIndex(city.members, IsScout);
            int lastCarrier = System.Array.FindLastIndex(city.members, m =>
                AssetDatabase.GetAssetPath(m.prefab) == StriderCityBuilder.HabitatPath
                || AssetDatabase.GetAssetPath(m.prefab) == DesertCrawlerBuilder.PrefabPath);
            Assert.Greater(firstScout, lastCarrier, "scouts after the houses and crawlers");
            Assert.Less(System.Array.FindIndex(city.members, IsCrab), firstScout, "the crabs flank beside the crawlers, not at the tail");

            Assert.AreEqual(CityLanes, city.formation.Lanes, "formation shape unchanged");
            Assert.AreEqual(CitySpacing, city.formation.RowSpacing, 0.01f);
            Assert.AreEqual(CitySpacing, city.formation.LaneSpacing, 0.01f);

            Assert.IsTrue(city.tasks.All(t => t.targetSite == SiteKind.Ruin || t.targetSite == SiteKind.ScrapField));
            Assert.IsTrue(city.tasks.All(t => Mathf.Approximately(t.travelSpeedMultiplier, StriderCityBuilder.CityTravelMultiplier)));
            Assert.IsTrue(city.tasks.All(t => t.levelGround.Equals(StriderCityBuilder.CityLevelGround)),
                          "every stop holds the city to its level-ground rule (re-run Wire Strider City)");
            Assert.AreEqual(StriderCityBuilder.CityLeaderSpeed, city.travelSpeed, 0.01f, "folded and live speeds agree");

            Assert.IsTrue(city.useStartPosition, "the city starts in the middle of the world, not at a Ruin");
            Vector3 centre = StriderCityStartSite.MapCentre(SpaceGame.World.NavMeshTools.WorldNavMeshBaker.LoadConfig());
            float fromCentre = Vector2.Distance(new Vector2(city.startPosition.x, city.startPosition.z), new Vector2(centre.x, centre.z));
            Assert.LessOrEqual(fromCentre, StriderCityStartSite.CityStartDistance + StriderCityStartSite.CityStartBand,
                               "the city starts near the middle of the map (re-run Wire Strider City)");
        }

        /// FormationModule measures regroupDistance from the leader: a follower whose slot is farther
        /// out than that counts as separated at its own slot and rides for the leader instead.
        [Test]
        public void EveryFollower_ReachesItsSlot_WithinItsRegroupDistance()
        {
            NpcGroupTemplate city = ReadTemplate(RosterAuthoring.StriderCityTemplateId);

            // Followers take slots in spawn order, which is template order; the leader holds none, and
            // the crew ride the houses.
            int followerIndex = 0;
            foreach (NpcGroupMemberSpec spec in city.members.Where(m => !m.crew))
            {
                float regroup = spec.prefab.GetComponent<FormationModule>().RegroupDistance;
                for (int i = spec.isLeader ? 1 : 0; i < spec.count; i++, followerIndex++)
                {
                    float reach = FormationMath.FarthestSlot(followerIndex + 1, city.formation);
                    Assert.Less(reach, regroup, $"{spec.prefab.name} in slot {followerIndex}: rebuild it after a template change");
                }
            }

            Assert.AreEqual(RosterAuthoring.CityFollowers, followerIndex, "CityFollowers counts every slot the template fills");
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
