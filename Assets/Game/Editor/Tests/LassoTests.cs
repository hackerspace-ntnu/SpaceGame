// What the lasso has to keep being true about itself.
//
// The item this pins replaced three things that were each wrong in a way nothing failed on. The
// rope sagged by an amount scaled to its SPAN, so a rope pulled bar-tight hung as limply as a
// slack one. The catch switched the creature's NavMeshAgent off and switched it back on
// unconditionally, so a creature whose agent had been parked came back enabled and walked off a
// world that was not loaded yet. And the press threw immediately, so the wind-up that is the whole
// gesture of a lasso could not exist.
//
// None of those produce an error, a warning, or a red frame. They produce a rope that looks wrong
// and an animal that looks dead, which is exactly the class of bug a test has to hold.
//
// In Editor/ rather than beside the other EditMode tests because these touch Assembly-CSharp
// types, and an asmdef cannot reference Assembly-CSharp.
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Characters;
using SpaceGame.Core;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    public class LassoTests
    {
        private const int ThrowVerb = 1;

        private static readonly Vector3 End = new Vector3(10f, 0f, 0f);

        /// <summary>Mirrors LassoArtifact's own constant. See <see cref="ThrownRopeTrailsInACurve"/>.</summary>
        private const float FlightSlack = 1.2f;

        private readonly List<GameObject> spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned)
                if (go != null) Object.DestroyImmediate(go);

            spawned.Clear();
        }

        // ── LassoRope ──────────────────────────────────────────────────────────

        [Test]
        public void RopeUnderTensionIsStraight()
        {
            // A rope pulled to its full length must not sag. The rope this replaces scaled its
            // droop to SPAN, so a taut 40 m rope hung exactly as limply as a slack one — the same
            // bug GrappleRope was written to fix on the hook, still present on the lasso.
            LassoRope rope = NewRope(out LineRenderer line);
            rope.Show(Vector3.zero, End);

            // Rope length == the gap: zero slack.
            for (int i = 0; i < 120; i++)
                rope.Simulate(Vector3.zero, End, 10f, 1f / 60f);

            Assert.Less(MaxDeviationFromChord(line), 0.15f,
                "a rope at zero slack sagged away from the straight line between its ends");
        }

        [Test]
        public void RopeWithSlackSags()
        {
            // The other half of the same claim: slack is the ONLY thing sag may come from, so a
            // rope with plenty of it must visibly droop over the same span. Without this test the
            // one above passes trivially on a rope that is always drawn straight.
            LassoRope rope = NewRope(out LineRenderer line);
            rope.Show(Vector3.zero, End);

            for (int i = 0; i < 120; i++)
                rope.Simulate(Vector3.zero, End, 16f, 1f / 60f);

            Assert.Greater(MaxDeviationFromChord(line), 1f,
                "a rope with 6 m of slack over a 10 m span hung straight");
        }

        [Test]
        public void RopeStaysSmoothWhileBeingThrown()
        {
            // The "stairs" bug, and it has to be tested against the THROW specifically. A chain of
            // distance constraints has nothing to say about the ANGLE at a node, so a concertina —
            // one node up, the next down — satisfies every constraint exactly and the solver has
            // no reason to undo it.
            //
            // A rope left hanging does not show this: gravity is a consistent downward bias and it
            // organises the chain into a smooth catenary all by itself. What breaks it is an end
            // moving faster than gravity can sort out, which is exactly a lasso in flight. An
            // earlier version of this test hung a slack rope and passed with the fix switched
            // OFF — it proved nothing.
            //
            // Measured as the turn angle at each node, which separates the two cases cleanly: a
            // smooth curve turns a few degrees per node, a one-node zigzag turns nearly 180.
            LassoRope rope = NewRope(out LineRenderer line);

            Vector3 start = Vector3.zero;
            Vector3 head = start;
            Vector3 velocity = Vector3.forward * 30f + Vector3.up * 8f;

            rope.Show(start, start + Vector3.forward * 0.5f);

            float worst = 0f;
            const float dt = 1f / 60f;

            for (int i = 0; i < 60; i++)
            {
                velocity += Vector3.down * (18f * dt);
                head += velocity * dt;

                rope.Simulate(start, head, Vector3.Distance(start, head) * FlightSlack, dt);
                worst = Mathf.Max(worst, MeanTurnAngle(line));
            }

            // 8 degrees: measured at 4.6 with bend resistance on and 11.2 with it off, so the
            // threshold sits between two known states rather than being picked out of the air.
            Assert.Less(worst, 8f, "the rope folded into a zigzag while it was being thrown");
        }

        [Test]
        public void ThrownRopeTrailsInACurve()
        {
            // The other half of the throw, and the one that got traded away fixing the zigzag.
            //
            // The HEAD has always flown a ballistic arc — that was never the problem. The problem
            // was the rope drawn between the hand and it, which at 2% slack sat inside LassoRope's
            // straightening band and got snapped onto the chord every substep. A perfect arc with
            // a ruler drawn across it, which is what "it is a straight line" meant.
            //
            // So this asserts the rope departs from the straight line between its own two ends. It
            // is deliberately paired with RopeStaysSmoothWhileBeingThrown: one says the cable must
            // curve, the other says the curve must not be a concertina, and it is easy to satisfy
            // either alone by breaking the other.
            LassoRope rope = NewRope(out LineRenderer line);

            Vector3 start = Vector3.zero;
            Vector3 head = start;
            Vector3 velocity = Vector3.forward * 30f + Vector3.up * 8f;

            rope.Show(start, start + Vector3.forward * 0.5f);

            const float dt = 1f / 60f;

            for (int i = 0; i < 60; i++)
            {
                velocity += Vector3.down * (18f * dt);
                head += velocity * dt;
                rope.Simulate(start, head, Vector3.Distance(start, head) * FlightSlack, dt);
            }

            Assert.Greater(MaxDeviationFromChord(line), 0.5f,
                "the thrown rope was drawn as a straight line between the hand and the loop");
        }

        [Test]
        public void RopeStaysSmoothWhileCoilingBack()
        {
            // The worst case of the three, and the one a player sees most often, because every
            // miss ends with it: the rope's rest length collapsing toward zero while both ends
            // come together. All the length it is losing has to go somewhere.
            //
            // Measured at 18.1 degrees with bend resistance on and 38.1 with it off.
            LassoRope rope = NewRope(out LineRenderer line);

            Vector3 start = Vector3.zero;
            Vector3 end = new Vector3(0f, 0f, 20f);
            rope.Show(start, end);

            float worst = 0f;

            for (int i = 0; i < 60; i++)
            {
                float t = i / 59f;
                rope.Simulate(start, Vector3.Lerp(end, start, t), 20f * (1f - t) * 1.06f, 1f / 60f);
                worst = Mathf.Max(worst, MeanTurnAngle(line));
            }

            Assert.Less(worst, 24f, "the rope concertina'd as it coiled back into the hand");
        }

        [Test]
        public void RopeSettlesTheSameAtAnyFrameRate()
        {
            // The cable substeps at a fixed timestep. Without that the shape depends on frame
            // rate, and the difference does not show up in a screenshot — it shows up as two
            // players looking at the same rope and seeing two different ropes.
            LassoRope fast = NewRope(out LineRenderer fastLine);
            LassoRope slow = NewRope(out LineRenderer slowLine);

            fast.Show(Vector3.zero, End);
            slow.Show(Vector3.zero, End);

            for (int i = 0; i < 240; i++) fast.Simulate(Vector3.zero, End, 14f, 1f / 120f);
            for (int i = 0; i < 60; i++) slow.Simulate(Vector3.zero, End, 14f, 1f / 30f);

            Assert.Less(Vector3.Distance(Midpoint(fastLine), Midpoint(slowLine)), 0.5f,
                "the cable settled to a different shape at 30 fps than at 120 fps");
        }

        // ── LassoLoop ──────────────────────────────────────────────────────────

        [Test]
        public void ChargingWidensTheLoop()
        {
            // The twirl has to SHOW its charge, or the wind-up is a delay bolted onto the front of
            // the same instant throw rather than a gesture the player is performing.
            LassoLoop loop = NewLoop(out LineRenderer line);
            loop.Show();

            loop.Twirl(Vector3.zero, Vector3.up, charge: 0f, deltaTime: 0.1f);
            float cold = MeanRadius(line, Vector3.zero);

            loop.Twirl(Vector3.zero, Vector3.up, charge: 1f, deltaTime: 0.1f);
            float hot = MeanRadius(line, Vector3.zero);

            Assert.Greater(hot, cold * 1.5f,
                "a fully wound loop was not visibly wider than a cold one");
        }

        [Test]
        public void CinchClosesTheLoopOntoTheTarget()
        {
            // The most legible frame the item has, and the one it never had: an open throw loop
            // shutting onto a neck. Without it a catch is a line that stops moving.
            LassoLoop loop = NewLoop(out LineRenderer line);
            loop.Show();

            loop.Fly(Vector3.zero, Vector3.forward, charge: 1f, deltaTime: 0.1f);
            float open = MeanRadius(line, Vector3.zero);

            loop.BeginCinch();
            for (int i = 0; i < 60; i++) loop.Ride(Vector3.zero, Vector3.back, 1f / 60f);

            Assert.Less(MeanRadius(line, Vector3.zero), open * 0.8f, "the loop never cinched shut");
        }

        // ── LassoTether ────────────────────────────────────────────────────────

        [Test]
        public void ReleaseHandsTheCreatureItsLegsBack()
        {
            // The bug this whole component exists for. The old catch did `agent.enabled = false`
            // straight from the item, and the old release did `agent.enabled = true` — so a
            // creature whose agent was PARKED (Awake parks one that wakes before a NavMesh exists
            // beneath it) came back switched on and dropped through the world. SuspendSelfDrive
            // records the resting state instead of assuming it.
            GameObject creature = NewGameObject("creature");
            FakeMotor motor = creature.AddComponent<FakeMotor>();
            creature.AddComponent<AgentController>();
            Transform anchor = NewGameObject("anchor").transform;

            LassoTether tether = LassoTether.Ensure(creature);
            tether.Bind(anchor, 8f, new LassoStruggle());

            Assert.IsTrue(motor.Suspended, "the tether never took the creature's legs");

            tether.Release();
            Assert.IsFalse(motor.Suspended, "the creature never got its own legs back");
        }

        [Test]
        public void ReleaseIsIdempotent()
        {
            // Release is reached from the press, from unequip, from the item's OnDestroy and from
            // this component's own, and more than one of those fires for a single rope coming off.
            GameObject creature = NewGameObject("creature");
            Transform anchor = NewGameObject("anchor").transform;

            LassoTether tether = LassoTether.Ensure(creature);
            tether.Bind(anchor, 8f, new LassoStruggle());

            tether.Release();
            Assert.DoesNotThrow(() => tether.Release());
        }

        [Test]
        public void StruggleDecaysToNothing()
        {
            // An animal that fights forever is not caught, it is a tug of war with no result. The
            // budget is what makes roping something a thing that COMPLETES.
            //
            // AdvanceStruggle rather than waiting: EditMode runs with Time.time at 0, which is the
            // trap that makes every MountModule test fail, so nothing under test may key off it.
            GameObject creature = NewGameObject("creature");
            Transform anchor = NewGameObject("anchor").transform;

            LassoTether tether = LassoTether.Ensure(creature);
            tether.Bind(anchor, 8f, new LassoStruggle());

            Assert.Greater(tether.StruggleFraction, 0.9f, "a freshly caught creature did not fight");

            tether.AdvanceStruggle(30f);
            Assert.Less(tether.StruggleFraction, 0.05f, "the creature never tired");
        }

        [Test]
        public void MassIsEstimatedFromTheBodyWhenThereIsNoRigidbody()
        {
            // Dallying needs a mass for every creature, and most creatures here are NavMesh agents
            // with no Rigidbody at all. One shared default would have an ant drag the player
            // exactly as hard as a six-legged habitat, which is dallying with the point removed.
            LassoTether ant = LassoTether.Ensure(CreatureWithBounds(new Vector3(0.4f, 0.3f, 0.6f)));
            LassoTether beast = LassoTether.Ensure(CreatureWithBounds(new Vector3(1.6f, 2.2f, 3.4f)));

            Transform anchor = NewGameObject("anchor").transform;
            ant.Bind(anchor, 8f, new LassoStruggle());
            beast.Bind(anchor, 8f, new LassoStruggle());

            Assert.Less(ant.Mass, beast.Mass * 0.2f,
                "an ant and a beast were given comparable weight on the rope");
        }

        // ── Dallying ───────────────────────────────────────────────────────────

        [Test]
        public void TheHeavierEndOfTheRopeWins()
        {
            // One rope, two ends, and the lighter one loses. Without this a 600 kg beast is reeled
            // in exactly as easily as a crate and "dallying" is a word for nothing.
            //
            // A static function precisely so it can be checked without a scene: it is the one
            // number the two ends of the rope have to agree on, and they are computed on two
            // different machines rather than sent.
            float light = LassoArtifact.PlayerPullShare(targetMass: 20f, playerMass: 80f);
            float heavy = LassoArtifact.PlayerPullShare(targetMass: 600f, playerMass: 80f);

            Assert.Less(light, 0.3f, "a 20 kg critter dragged the player around");
            Assert.Greater(heavy, 0.8f, "a 600 kg beast was reeled in like a sack");
        }

        // ── Gestures ───────────────────────────────────────────────────────────

        [Test]
        public void PressStartsTheTwirlRatherThanTheThrow()
        {
            // The whole wind-up depends on this. If the press still throws, there is no gesture.
            LassoArtifact lasso = NewLasso(out GameObject player);

            lasso.PlayUse(player, new NetArg { B = ThrowVerb });

            Assert.IsTrue(Field<bool>(lasso, "_isTwirling"), "the press did not start a twirl");
            Assert.IsFalse(Field<bool>(lasso, "_isThrowing"), "the press threw immediately");
        }

        [Test]
        public void ReleaseThrowsAlongTheAimInTheMessage()
        {
            // The aim is the one thing a peer cannot derive, and it has to ride the RELEASE tick
            // because that is when the throw happens. A peer falling back on its own camera throws
            // along the host's crosshair.
            LassoArtifact lasso = NewLasso(out GameObject player);

            lasso.PlayUse(player, new NetArg { B = ThrowVerb });
            lasso.PlayHold(player, AimedAt(new Vector3(0f, 0f, 12f)), active: false);

            Assert.IsFalse(Field<bool>(lasso, "_isTwirling"), "the twirl outlived the release");
            Assert.IsTrue(Field<bool>(lasso, "_isThrowing"), "the release did not throw");
        }

        [Test]
        public void ReleaseWithoutAnAimCancelsInsteadOfThrowing()
        {
            // EquipmentController.EndHold(send: false) — unequip, disable, death — delivers a
            // default NetArg. Treating that as a throw would fling a rope along whatever direction
            // an all-zero quaternion decodes to, every time the player scrolled the hotbar
            // mid-wind-up.
            LassoArtifact lasso = NewLasso(out GameObject player);

            lasso.PlayUse(player, new NetArg { B = ThrowVerb });
            lasso.PlayHold(player, default, active: false);

            Assert.IsFalse(Field<bool>(lasso, "_isThrowing"), "an unequip mid-twirl threw the rope");
            Assert.IsFalse(Field<bool>(lasso, "_isTwirling"), "the twirl was left running");
        }

        // ── Fixture ────────────────────────────────────────────────────────────

        /// <summary>
        /// A motor that does nothing but remember whether its legs were taken. Stands in for
        /// NavMeshAgentMotor, which needs a NavMesh under it to be worth constructing.
        /// </summary>
        private class FakeMotor : MonoBehaviour, IMovementMotor, ISelfDrivingMotor
        {
            public bool Suspended { get; private set; }

            public Vector3 Velocity => Vector3.zero;
            public bool IsImmobile => true;
            public bool HasReachedDestination => true;
            public Vector3? CurrentDestination => null;

            public void Tick(in MoveIntent intent, float deltaTime) { }
            public void ForceStop() { }
            public void NudgeDestination(Vector3 offset) { }
            public void SuggestDestination(Vector3 position) { }

            public void SuspendSelfDrive() => Suspended = true;
            public void ResumeSelfDrive() => Suspended = false;
        }

        private GameObject NewGameObject(string name, params System.Type[] components)
        {
            var go = new GameObject(name, components);
            spawned.Add(go);
            return go;
        }

        private GameObject CreatureWithBounds(Vector3 size)
        {
            GameObject creature = NewGameObject("creature");
            var box = creature.AddComponent<BoxCollider>();
            box.size = size;
            return creature;
        }

        private LassoRope NewRope(out LineRenderer line)
        {
            line = NewGameObject("rope", typeof(LineRenderer)).GetComponent<LineRenderer>();

            var rope = new LassoRope();
            rope.Bind(line);
            return rope;
        }

        private LassoLoop NewLoop(out LineRenderer line)
        {
            line = NewGameObject("loop", typeof(LineRenderer)).GetComponent<LineRenderer>();

            var loop = new LassoLoop();
            loop.Bind(line);
            return loop;
        }

        /// <summary>The same shape GrappleUseFlowTests builds: a body, a camera, an aim.</summary>
        private LassoArtifact NewLasso(out GameObject player)
        {
            player = NewGameObject("player", typeof(Rigidbody), typeof(AimProvider));

            GameObject cam = NewGameObject("cam", typeof(Camera));
            cam.transform.SetParent(player.transform, false);
            cam.transform.rotation = Quaternion.LookRotation(Vector3.forward);

            typeof(AimProvider)
                .GetField("playerCamera", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(player.GetComponent<AimProvider>(), cam.GetComponent<Camera>());

            // The item lives on its own object, as the equipped prefab does.
            GameObject item = NewGameObject("lasso", typeof(LineRenderer));
            return item.AddComponent<LassoArtifact>();
        }

        private static NetArg AimedAt(Vector3 point) =>
            new NetArg { P = point, R = Quaternion.LookRotation(point.normalized) };

        private static T Field<T>(object target, string name) =>
            (T)target.GetType()
                .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(target);

        /// <summary>The largest perpendicular distance from the straight line joining the two ends.</summary>
        private static float MaxDeviationFromChord(LineRenderer line)
        {
            int count = line.positionCount;
            if (count < 3) return 0f;

            Vector3 a = line.GetPosition(0);
            Vector3 b = line.GetPosition(count - 1);
            Vector3 axis = b - a;
            float span = axis.magnitude;
            if (span < 0.001f) return 0f;

            Vector3 dir = axis / span;
            float worst = 0f;

            for (int i = 1; i < count - 1; i++)
            {
                Vector3 offset = line.GetPosition(i) - a;
                float along = Vector3.Dot(offset, dir);
                worst = Mathf.Max(worst, (offset - dir * along).magnitude);
            }

            return worst;
        }

        private static Vector3 Midpoint(LineRenderer line) => line.GetPosition(line.positionCount / 2);

        /// <summary>
        /// Average degrees the line turns at each interior point. Near zero for a smooth curve,
        /// near 180 for a one-node-wide concertina.
        /// </summary>
        private static float MeanTurnAngle(LineRenderer line)
        {
            int count = line.positionCount;
            if (count < 3) return 0f;

            float total = 0f;
            int measured = 0;

            for (int i = 1; i < count - 1; i++)
            {
                Vector3 incoming = line.GetPosition(i) - line.GetPosition(i - 1);
                Vector3 outgoing = line.GetPosition(i + 1) - line.GetPosition(i);

                if (incoming.sqrMagnitude < 1e-8f || outgoing.sqrMagnitude < 1e-8f) continue;

                total += Vector3.Angle(incoming, outgoing);
                measured++;
            }

            return measured == 0 ? 0f : total / measured;
        }

        private static float MeanRadius(LineRenderer line, Vector3 centre)
        {
            float total = 0f;
            for (int i = 0; i < line.positionCount; i++)
                total += Vector3.Distance(line.GetPosition(i), centre);

            return line.positionCount == 0 ? 0f : total / line.positionCount;
        }
    }
}
