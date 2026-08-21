// The planner is shared between the live module and the headless simulator, which is the point of
// it being static and pure — two implementations of "pick the next job" would drift, and the drift
// would only show as a caravan that behaves differently in the ten seconds after it spawns.
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.World;

namespace SpaceGame.EditorTools
{
    public class NpcTaskPlannerTests
    {
        [SetUp]
        public void Setup() => WorldSiteRegistry.Clear();

        [TearDown]
        public void TearDown() => WorldSiteRegistry.Clear();

        private static NpcTask Task(string label, SiteKind kind, float weight = 1f, float radius = 500f) =>
            new NpcTask
            {
                label = label,
                targetSite = kind,
                weight = weight,
                searchRadius = radius,
                arriveRadius = 6f,
                dwellSeconds = new Vector2(10f, 20f),
            };

        [Test]
        public void ZeroWeightTasksAreNeverPicked()
        {
            var tasks = new[]
            {
                Task("disabled", SiteKind.Ruin, weight: 0f),
                Task("active", SiteKind.ScrapField, weight: 1f),
            };

            for (int i = 0; i < 50; i++)
                Assert.AreEqual(1, NpcTaskPlanner.PickTask(tasks, avoidIndex: -1));
        }

        [Test]
        public void TheCurrentTaskIsAvoidedWhenThereIsAnAlternative()
        {
            // Otherwise a scavenger re-picks scavenging every time and never visits anywhere else,
            // which makes a multi-task NPC indistinguishable from a single-task one.
            var tasks = new[] { Task("a", SiteKind.Ruin), Task("b", SiteKind.ScrapField) };

            for (int i = 0; i < 50; i++)
                Assert.AreEqual(1, NpcTaskPlanner.PickTask(tasks, avoidIndex: 0));
        }

        [Test]
        public void ASingleTaskIsRepeatedRatherThanRefused()
        {
            // Avoidance is a preference, not a rule. An NPC with one job must keep doing it — the
            // alternative is an NPC that does nothing at all, which is far worse than a repetitive one.
            var tasks = new[] { Task("only", SiteKind.TradePost) };

            Assert.AreEqual(0, NpcTaskPlanner.PickTask(tasks, avoidIndex: 0));
        }

        [Test]
        public void NoTasksMeansNoPick()
        {
            Assert.AreEqual(-1, NpcTaskPlanner.PickTask(null));
            Assert.AreEqual(-1, NpcTaskPlanner.PickTask(new NpcTask[0]));
        }

        [Test]
        public void ARegisteredSiteIsPreferredOverRoaming()
        {
            WorldSiteRegistry.Register(SiteKind.ScrapField, new Vector3(120f, 0f, 0f), 9f, "Tipping Ground");

            NpcTask task = Task("scavenging", SiteKind.ScrapField);

            Assert.IsTrue(NpcTaskPlanner.ResolveDestination(task, Vector3.zero, null,
                out Vector3 destination, out float radius, out string siteId, out string siteName));

            Assert.AreEqual(new Vector3(120f, 0f, 0f), destination);
            Assert.AreEqual(9f, radius, 0.001f, "the site's own radius should govern arrival");
            Assert.IsNotEmpty(siteId);
            Assert.AreEqual("Tipping Ground", siteName);
        }

        [Test]
        public void SitesOutOfSearchRangeAreNotUsed()
        {
            WorldSiteRegistry.Register(SiteKind.Ruin, new Vector3(5000f, 0f, 0f), 9f, "distant");

            NpcTask task = Task("ruin-seeking", SiteKind.Ruin, radius: 300f);

            NpcTaskPlanner.ResolveDestination(task, Vector3.zero, null, out _, out _,
                                              out string siteId, out _);

            Assert.IsEmpty(siteId, "a site beyond the search radius must not be chosen");
        }

        [Test]
        public void TheRoamFallbackReachesOutRatherThanShuffling()
        {
            // Uniform sampling of a disc bunches points near the middle, which for a "roam far"
            // fallback produces NPCs that shuffle a few metres and stop — the exact behaviour the
            // fallback exists to avoid.
            const float radius = 900f;

            for (int i = 0; i < 100; i++)
            {
                Vector3 point = NpcTaskPlanner.RoamPointUnsampled(Vector3.zero, radius, null);
                float distance = new Vector2(point.x, point.z).magnitude;

                Assert.GreaterOrEqual(distance, radius * 0.33f - 0.01f,
                    "roam points must be genuinely far out, not clustered around the origin");
                Assert.LessOrEqual(distance, radius + 0.01f);
                Assert.AreEqual(0f, point.y, 0.001f);
            }
        }

        [Test]
        public void RoamPointsAreDeterministicForAGivenSeed()
        {
            // The simulator needs to be able to reproduce a decision — the same group ticked twice
            // from the same state must not wander off in two directions.
            Vector3 first = NpcTaskPlanner.RoamPointUnsampled(Vector3.zero, 500f, new System.Random(7));
            Vector3 second = NpcTaskPlanner.RoamPointUnsampled(Vector3.zero, 500f, new System.Random(7));

            Assert.AreEqual(first, second);
        }
    }
}
