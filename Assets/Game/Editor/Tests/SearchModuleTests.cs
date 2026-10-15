// SearchModule starts on AgentTargeting.LostCount, not on a falling edge of HasTarget -- see
// AgentTargeting.LostCount. Chase out-ranks Search and claims every frame a target is held, so the
// one frame a target goes is never a frame Search is ticked on; these tests tick Search only after
// the loss, exactly as AgentController does.
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Audio;

namespace SpaceGame.EditorTools
{
    public class SearchModuleTests
    {
        private static readonly Vector3 Sighting = new Vector3(12f, 0f, 30f);

        private GameObject agent;
        private GameObject quarry;
        private AgentTargeting targeting;
        private SearchModule search;

        [SetUp]
        public void SetUp()
        {
            agent = new GameObject("SearchModuleTestAgent");
            quarry = new GameObject("SearchModuleTestQuarry");
            quarry.transform.position = Sighting;

            targeting = agent.AddComponent<AgentTargeting>();
            search = agent.AddComponent<SearchModule>();

            // Silent: the test is about where the agent goes, not about the audio catalog.
            var so = new SerializedObject(search);
            so.FindProperty("searchId").intValue = (int)SfxId.None;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(agent);
            if (quarry != null)
                Object.DestroyImmediate(quarry);
        }

        private MoveIntent? Tick(float deltaTime = 0.1f) =>
            search.Tick(new AgentContext { Self = agent.transform, Targeting = targeting }, deltaTime);

        // AgentTargeting's own per-frame pass. Edit mode runs no Awake, so the settings it reads are
        // built first; with no EntityFaction it scores nobody, which leaves only the held target.
        private void TickTargeting()
        {
            const System.Reflection.BindingFlags Hidden =
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            typeof(AgentTargeting).GetMethod("EnsureSettings", Hidden).Invoke(targeting, null);
            typeof(AgentTargeting).GetMethod("Update", Hidden).Invoke(targeting, null);
        }

        [Test]
        public void StartsSearchAfterTargetLost()
        {
            targeting.RestoreMemory(quarry.transform, true, Sighting, true, 0f, null);
            Assert.IsTrue(targeting.HasTarget, "precondition: the agent is holding a target");

            targeting.ClearTarget();
            MoveIntent? intent = Tick();

            Assert.IsTrue(intent.HasValue, "the first tick after a loss must start a search");
            Assert.AreEqual(AgentIntentType.MoveToPosition, intent.Value.Type);
            Assert.AreEqual(Sighting, intent.Value.TargetPosition);
            Assert.IsTrue(search.IsSearching);
        }

        [Test]
        public void ALossWithNoMemoryIsNotSearched()
        {
            targeting.RestoreMemory(quarry.transform, true, Vector3.zero, false, 0f, null);
            targeting.ClearTarget();

            Assert.IsNull(Tick(), "nowhere to look: a dead target drops its memory, and so does this");
            Assert.IsFalse(search.IsSearching);
        }

        [Test]
        public void ALossIsSearchedOnceAndNotAgainAfterTheSearchEnds()
        {
            targeting.RestoreMemory(quarry.transform, true, Sighting, true, 0f, null);
            targeting.ClearTarget();

            Assert.IsTrue(Tick().HasValue);
            Assert.IsNull(Tick(60f), "the search runs out");
            Assert.IsNull(Tick(), "the same loss must not start a second search");
        }

        [Test]
        public void ATargetDestroyedWhileHeldIsSearchedFor()
        {
            // Despawned, logged out or streamed away: from the agent's side it vanished where it was
            // last seen. Unity's null must not hide that a target was held.
            targeting.RestoreMemory(quarry.transform, true, Sighting, true, 0f, null);
            Object.DestroyImmediate(quarry);

            TickTargeting();
            MoveIntent? intent = Tick();

            Assert.IsFalse(targeting.HasTarget);
            Assert.IsTrue(intent.HasValue, "a target destroyed while held is a lost target, not a dead one");
            Assert.AreEqual(Sighting, intent.Value.TargetPosition);
        }

        [Test]
        public void ARestoredTargetThatCannotBeHadBackIsSearchedFor()
        {
            // The save held a target that did not resolve on load (logged out, despawned): the memory
            // is kept and the target counts as lost.
            targeting.RestoreMemory(null, true, Sighting, true, 1f, null);

            MoveIntent? intent = Tick();

            Assert.IsTrue(intent.HasValue, "a saved target that is gone after the load is a lost target");
            Assert.AreEqual(Sighting, intent.Value.TargetPosition);
        }

        [Test]
        public void ARestoredMemoryWithoutATargetIsNotALoss()
        {
            targeting.RestoreMemory(null, false, Sighting, true, 1f, null);

            Assert.IsNull(Tick());
        }

        [Test]
        public void ReacquiringTheTargetAbortsTheSearch()
        {
            targeting.RestoreMemory(quarry.transform, true, Sighting, true, 0f, null);
            targeting.ClearTarget();
            Assert.IsTrue(Tick().HasValue);

            targeting.RestoreMemory(quarry.transform, true, Sighting, true, 0f, null);

            Assert.IsNull(Tick());
            Assert.IsFalse(search.IsSearching);
        }
    }
}
