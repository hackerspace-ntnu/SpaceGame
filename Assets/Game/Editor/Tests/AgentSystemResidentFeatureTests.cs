// The five general agent-system changes settlement residents rest on (AgentSystem.md, A1–A5):
// the jostle ladder, the facing rule, presentation on watchers, holding goals, and Offstage.
//
// Pure helpers first, because the ladder and the hold are both about SEQUENCES — a third shove, a
// push one metre past the spot — that are trivial as arithmetic and miserable through a scene.
// The component tests at the end only check that the module wires those rules up.
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Core;

namespace SpaceGame.EditorTools
{
    public class AgentSystemResidentFeatureTests
    {
        private readonly List<GameObject> spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned)
                if (go != null) Object.DestroyImmediate(go);
            spawned.Clear();
        }

        private GameObject NewObject(string name)
        {
            var go = new GameObject(name);
            spawned.Add(go);
            return go;
        }

        // ── A1: the jostle ladder ───────────────────────────────────────────────────

        private static AggressionBand Climb(int jostlesToFight, int jostles)
        {
            AggressionBand band = AggressionBand.Calm;
            for (int i = 0; i < jostles; i++)
                band = AggressionMath.LadderStep(band, jostlesToFight);
            return band;
        }

        [Test]
        public void ThreeJostleLadderIsWatchItThenLastWarningThenAFight()
        {
            Assert.AreEqual(AggressionBand.Wary, Climb(3, 1));
            Assert.AreEqual(AggressionBand.Drawn, Climb(3, 2));
            Assert.AreEqual(AggressionBand.Grudge, Climb(3, 3));
        }

        [Test]
        public void TwoJostleLadderSkipsWary()
        {
            Assert.AreEqual(AggressionBand.Drawn, Climb(2, 1));
            Assert.AreEqual(AggressionBand.Grudge, Climb(2, 2));
            Assert.AreEqual(AggressionBand.Drawn, AggressionMath.LadderStep(AggressionBand.Wary, 2),
                "an agent already Wary (trespass, say) still has two jostles to go on a 2-ladder");
        }

        [Test]
        public void OneJostleLadderFightsOnTheFirstJostle()
        {
            Assert.AreEqual(AggressionBand.Grudge, Climb(1, 1));
        }

        [Test]
        public void NoLadderEscalatesOneBandAtATime()
        {
            Assert.AreEqual(AggressionBand.Wary, AggressionMath.LadderStep(AggressionBand.Calm, 0));
            Assert.AreEqual(AggressionBand.Grudge, AggressionMath.LadderStep(AggressionBand.Drawn, 0));
        }

        [Test]
        public void AJostleIsWorthNothingOnTheMeter()
        {
            Assert.AreEqual(0f, AggressionMath.Gain(AggressionInput.Jostle, 1f, AggressionSettings.Default),
                "a shove is counted on the ladder, never weighed");
        }

        [Test]
        public void OnlyAFightAnAllyStartedGoesUnannounced()
        {
            Assert.IsFalse(ProvocationModule.Announces(AggressionInput.AllyHurt), "one alert wakes one camp, not the map");
            Assert.IsTrue(ProvocationModule.Announces(AggressionInput.Hit));
            Assert.IsTrue(ProvocationModule.Announces(AggressionInput.Jostle), "a fight is a fight");
        }

        [Test]
        public void EachBandHoldsSettleSecondsThenCoolsOneStep()
        {
            Assert.AreEqual(AggressionBand.Drawn, AggressionMath.Settle(AggressionBand.Drawn, 9.9f, 10f));
            Assert.AreEqual(AggressionBand.Wary, AggressionMath.Settle(AggressionBand.Drawn, 10f, 10f));
            Assert.AreEqual(AggressionBand.Calm, AggressionMath.Settle(AggressionBand.Wary, 10f, 10f));
            Assert.AreEqual(AggressionBand.Calm, AggressionMath.Settle(AggressionBand.Calm, 99f, 10f));
        }

        [Test]
        public void ABandsFloorReadsAsThatBandForEveryTemperament()
        {
            foreach (float attackAt in new[] { AggressionMath.Max, 60f, 1f })
            foreach (AggressionBand band in new[] { AggressionBand.Calm, AggressionBand.Wary, AggressionBand.Drawn, AggressionBand.Grudge })
            {
                Assert.AreEqual(band, AggressionMath.BandFor(AggressionMath.FloorOf(band, attackAt), attackAt),
                    $"{band} at attackAt {attackAt}");
            }
        }

        [Test]
        public void TheDefaultTemperamentHasNoLadderSoExistingPrefabsAreUnchanged()
        {
            AggressionSettings settings = AggressionSettings.Default;

            Assert.AreEqual(0, settings.jostlesToFight);
            Assert.AreEqual(0f, settings.settleSeconds);
            Assert.AreEqual(AggressionBand.Grudge,
                AggressionMath.BandFor(AggressionMath.Apply(0f, AggressionMath.Gain(AggressionInput.Hit, 0.09f, settings))),
                "the meter still makes a real hit an instant fight");
        }

        private ProvocationModule NewProvocation(int jostlesToFight, float settleSeconds)
        {
            ProvocationModule provocation = NewObject("villager").AddComponent<ProvocationModule>();
            AggressionSettings settings = AggressionSettings.Default;
            settings.jostlesToFight = jostlesToFight;
            settings.settleSeconds = settleSeconds;
            provocation.Settings = settings;
            return provocation;
        }

        [Test]
        public void JostlesClimbTheLadderAndRecordTheCause()
        {
            ProvocationModule provocation = NewProvocation(3, 20f);

            provocation.Jostled(null);
            Assert.AreEqual(AggressionBand.Wary, provocation.Band);
            provocation.Jostled(null);
            Assert.AreEqual(AggressionBand.Drawn, provocation.Band);
            Assert.AreEqual(AggressionInput.Jostle, provocation.LastCause);

            // Nobody viable to blame: the top of the meter, waiting — not a fight with nobody.
            provocation.Jostled(null);
            Assert.AreEqual(AggressionBand.Grudge, provocation.Band);
            Assert.IsFalse(provocation.IsProvoked);
        }

        [TestCase(2)]
        [TestCase(3)]
        public void TheLastJostleOnTheLadderIsTheFight(int jostlesToFight)
        {
            ProvocationModule provocation = NewProvocation(jostlesToFight, 20f);
            Transform shover = NewObject("player").transform;

            for (int i = 1; i < jostlesToFight; i++)
            {
                provocation.Jostled(shover);
                Assert.IsFalse(provocation.IsProvoked, $"jostle {i} of {jostlesToFight} is a warning");
            }

            provocation.Jostled(shover);
            Assert.IsTrue(provocation.IsProvoked);
            Assert.AreSame(shover, provocation.Aggressor);
        }

        [Test]
        public void AnAgentWithNoLadderShrugsOffJostles()
        {
            ProvocationModule provocation = NewProvocation(0, 0f);
            Transform shover = NewObject("player").transform;

            for (int i = 0; i < 5; i++)
                provocation.Jostled(shover);

            Assert.AreEqual(AggressionBand.Calm, provocation.Band);
            Assert.AreEqual(0f, provocation.Aggression);
            Assert.IsNull(provocation.Provoker, "no ladder, nothing to count");
        }

        [Test]
        public void RaisingToGrudgeFightsEvenFromAMeterWaitingAtTheTop()
        {
            ProvocationModule provocation = NewProvocation(1, 20f);
            provocation.Jostled(null);
            Assert.AreEqual(AggressionBand.Grudge, provocation.Band);
            Assert.IsFalse(provocation.IsProvoked, "nobody to blame yet");

            Transform attacker = NewObject("player").transform;
            provocation.Raise(AggressionBand.Grudge, attacker, AggressionInput.Hit);

            Assert.IsTrue(provocation.IsProvoked, "the first viable somebody gets the fight");
            Assert.AreSame(attacker, provocation.Aggressor);
        }

        [Test]
        public void ClosingSpeedIsTheApproachAlongTheLineToTheAgent()
        {
            Vector3 agent = Vector3.zero;

            Assert.AreEqual(2f, JostleSensor.ClosingSpeed(new Vector3(0f, 0f, -1f), new Vector3(0f, 0f, -0.8f), agent, 0.1f), 1e-4f);
            Assert.AreEqual(-2f, JostleSensor.ClosingSpeed(new Vector3(0f, 0f, -0.8f), new Vector3(0f, 0f, -1f), agent, 0.1f), 1e-4f,
                "backing off");
            Assert.AreEqual(0f, JostleSensor.ClosingSpeed(new Vector3(1f, 0f, 0f), new Vector3(1f, 0f, 0.5f), agent, 0.1f), 1e-4f,
                "sidling past");
            Assert.AreEqual(0f, JostleSensor.ClosingSpeed(new Vector3(0f, 0f, -1f), new Vector3(0f, 3f, -1f), agent, 0.1f), 1e-4f,
                "a jump is not a shove");
        }

        [Test]
        public void AShoveCountsOncePerCooldownAndOnlyWhenTouchingAndPushing()
        {
            Assert.IsTrue(JostleSensor.IsJostle(true, 3f, float.PositiveInfinity, 1f, 1.5f), "first contact");
            Assert.IsFalse(JostleSensor.IsJostle(true, 3f, 1f, 1f, 1.5f), "still the same shove");
            Assert.IsTrue(JostleSensor.IsJostle(true, 3f, 1.5f, 1f, 1.5f), "pushing on past the cooldown");
            Assert.IsFalse(JostleSensor.IsJostle(true, 0.5f, 10f, 1f, 1.5f), "standing close is not pushing");
            Assert.IsFalse(JostleSensor.IsJostle(false, 3f, 10f, 1f, 1.5f), "running past is not touching");
        }

        [Test]
        public void RaiseNeverLowersTheBand()
        {
            ProvocationModule provocation = NewProvocation(2, 20f);

            provocation.Jostled(null);
            provocation.Raise(AggressionBand.Wary, null, AggressionInput.Trespass);

            Assert.AreEqual(AggressionBand.Drawn, provocation.Band);
            Assert.AreEqual(AggressionInput.Trespass, provocation.LastCause);
        }

        [Test]
        public void ALadderAlwaysSettlesLongEnoughForTheNextJostle()
        {
            ProvocationModule provocation = NewProvocation(2, 0f);

            Assert.AreEqual(AggressionMath.LadderMinSettleSeconds, provocation.Settings.settleSeconds);
        }

        // ── A2: the facing rule ─────────────────────────────────────────────────────

        [Test]
        public void AFacingModuleNeverOverridesAHigherMovementWinner()
        {
            Assert.IsFalse(AgentController.FacingApplies(ModulePriority.RangedAttack, ModulePriority.Scripted, false),
                "a telegraph or an aim must not turn an NPC away from the player it is talking to");
            Assert.IsFalse(AgentController.FacingApplies(ModulePriority.Reactive, ModulePriority.Reactive, false),
                "a tie is not 'above'");
        }

        [Test]
        public void AFacingModuleOverridesALowerWinnerItselfOrNoWinner()
        {
            Assert.IsTrue(AgentController.FacingApplies(ModulePriority.RangedAttack, ModulePriority.Fallback + 1, false));
            Assert.IsTrue(AgentController.FacingApplies(ModulePriority.RangedAttack, ModulePriority.RangedAttack, true),
                "the winner aiming its own move");
            Assert.IsTrue(AgentController.FacingApplies(ModulePriority.Ambient, null, false));
        }

        // ── A3: presentation on watching machines ───────────────────────────────────

        [Test]
        public void TheBrainIsNotASimulationDriverSoPresentationTicksOnClients()
        {
            GameObject entity = NewObject("agent");
            AgentController controller = entity.AddComponent<AgentController>();

            Assert.IsFalse(SimulationDrivers.Discover(entity).Contains(controller));
        }

        // ── A4: holding goals ───────────────────────────────────────────────────────

        [Test]
        public void AHoldingGoalArrivesWithSlackAndAnOrdinaryOneDoesNot()
        {
            Assert.AreEqual(2f, AgentGoal.ArrivalRadius(2f, false));
            Assert.AreEqual(2f + AgentGoal.HoldArriveSlack, AgentGoal.ArrivalRadius(2f, true));
        }

        [Test]
        public void AHeldPositionLetsGoOnlyBeyondTheReleaseMargin()
        {
            const float radius = 1f, release = 2f;

            Assert.IsFalse(GoalTravelModule.HoldsPosition(false, 2f, radius, release), "not there yet");
            Assert.IsTrue(GoalTravelModule.HoldsPosition(false, 1.5f, radius, release), "arrived, with slack");
            Assert.IsTrue(GoalTravelModule.HoldsPosition(true, 2.9f, radius, release), "shoved, still holding");
            Assert.IsFalse(GoalTravelModule.HoldsPosition(true, 3.1f, radius, release), "pushed off: walk back");
        }

        [Test]
        public void HoldAndFacePointAreSetClearedAndRestored()
        {
            AgentGoal goal = NewObject("guard").AddComponent<AgentGoal>();
            var face = new Vector3(1f, 0f, 5f);

            Assert.IsTrue(goal.Set(Vector3.zero, 1f, "gate", true, face));
            Assert.IsTrue(goal.HoldOnArrival);
            Assert.AreEqual(face, goal.FacePoint);

            goal.Clear();
            Assert.IsFalse(goal.HoldOnArrival);
            Assert.IsNull(goal.FacePoint);

            goal.RestoreGoal(true, Vector3.zero, 1f, "gate", null, 1f, true, face);
            Assert.IsTrue(goal.HoldOnArrival);
            Assert.AreEqual(face, goal.FacePoint);

            goal.Set(Vector3.one, 1f, "wander off");
            Assert.IsFalse(goal.HoldOnArrival, "an ordinary Set is never a held one");
        }

        // ── A5: Offstage ────────────────────────────────────────────────────────────

        [Test]
        public void OffstageIsAFlagAndNeverTheEnabledBit()
        {
            AgentController controller = NewObject("extra").AddComponent<AgentController>();

            controller.Offstage = true;
            Assert.IsTrue(controller.Offstage);
            Assert.IsTrue(controller.enabled, "enabled belongs to death and the save system");

            controller.Offstage = false;
            Assert.IsFalse(controller.Offstage);
            Assert.IsTrue(controller.enabled);
        }
    }
}
