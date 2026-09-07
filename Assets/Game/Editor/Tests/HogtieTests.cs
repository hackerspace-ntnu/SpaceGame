// What the tie has to keep being true about itself.
//
// Every failure this file guards against is silent. A pool the struggle cannot dent still ticks
// down and still frees the body — two minutes later, which reads as "the struggle key does
// nothing" rather than as a bug. A claim given back by the wrong hand stands a netted player up
// with a net still on them. A peer that drains a pool of its own frees a captive on one screen
// while every other machine holds them. None of those throws, and none of them is visible on the
// machine that fired.
//
// In Editor/ rather than beside the asmdef'd EditMode tests because these touch Assembly-CSharp
// types, and an asmdef cannot reference Assembly-CSharp.
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Core.Persistence;
using SpaceGame.Gameplay;
using SpaceGame.Gameplay.Ragdoll;
using SpaceGame.Items;
using SpaceGame.Persistence;

namespace SpaceGame.EditorTools
{
    public class HogtieTests
    {
        /// <summary>The step everything below is driven at. A frame of a sixty-hertz session.</summary>
        private const float Frame = 1f / 60f;

        /// <summary>Longer than any tie is allowed to last, so a runaway loop fails rather than hangs.</summary>
        private const float RunawaySeconds = 400f;

        /// <summary>
        /// The authored ceiling, pinned here rather than read off <see cref="HogtieSettings"/>.
        ///
        /// The user asked for two minutes in so many words, so this is a requirement and not a
        /// tunable: reading the field would make the test agree with whatever the field says, which
        /// is exactly the mutation it exists to catch.
        /// </summary>
        private const float AuthoredCeiling = 120f;

        /// <summary>
        /// What a perfect struggle is supposed to buy, and how far off it may land.
        ///
        /// <para>
        /// The band is deliberately narrow enough to exclude the two wrong answers this number has.
        /// Assuming the struggle meter sits at 1 while saturated — it does not; <c>Push</c> clamps
        /// it and it decays between pushes to a time-average of 0.8504 — yields a multiplier of
        /// 1.667 and a real escape of 50.4 s. A multiplier of zero yields the full 120 s. Both fall
        /// outside; the measured 45.8 s sits in the middle. See <see cref="Hogtie"/> for the
        /// derivation in full.
        /// </para>
        /// </summary>
        private const float StruggledEscape = 45f;
        private const float EscapeTolerance = 3f;

        private readonly List<GameObject> spawned = new List<GameObject>();
        private readonly List<Object> assets = new List<Object>();
        private IItemDropService restoreDropService;

        [SetUp]
        public void SetUp() => restoreDropService = GameServices.ItemDropService;

        [TearDown]
        public void TearDown()
        {
            GameServices.ItemDropService = restoreDropService;

            foreach (GameObject go in spawned)
                if (go != null) Object.DestroyImmediate(go);
            spawned.Clear();

            foreach (Object asset in assets)
                if (asset != null) Object.DestroyImmediate(asset);
            assets.Clear();
        }

        // ── Fixtures ─────────────────────────────────────────────────────────

        private GameObject NewObject(string name)
        {
            var go = new GameObject(name);
            spawned.Add(go);
            return go;
        }

        /// <summary>
        /// A component with its <c>Awake</c> actually raised.
        ///
        /// <c>AddComponent</c> does not raise Awake outside play mode, and <c>PlayerRagdoll</c>
        /// caches its <c>RagdollRig</c> there. Left unwoken, every hold below throws a
        /// NullReferenceException on the first line that touches the rig, and a test written to
        /// expect a refusal would pass on the exception rather than on the refusal it names. Copied
        /// in shape from <c>NetGunTests</c>, which needs it for the same reason.
        /// </summary>
        private static T Woken<T>(GameObject on) where T : MonoBehaviour
        {
            T component = on.AddComponent<T>();

            System.Reflection.MethodInfo awake = typeof(T).GetMethod(
                "Awake",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

            Assert.IsNotNull(awake,
                $"{typeof(T).Name} no longer has an Awake to raise. This helper would silently " +
                "hand back a half-built component and every test using it would be measuring that.");

            awake.Invoke(component, null);
            return component;
        }

        /// <summary>
        /// A body <c>RagdollRig</c> can really build a skeleton out of, without a skinned mesh.
        ///
        /// Five equal rigid parts clear both the weight floor and the four-bone minimum, so
        /// <c>GoLimp</c> keeps bones and <c>IsLimp</c> goes true — the difference between a test of
        /// a hold that took and a test of a hold that could never have taken. The primitives' own
        /// colliders go, because <c>BuildBone</c> adds a box around each part's mesh.
        /// </summary>
        private GameObject NewRagdollBody(string name)
        {
            GameObject root = NewObject(name);

            for (int i = 0; i < 5; i++)
            {
                GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
                part.name = $"Part{i}";
                part.transform.SetParent(root.transform);
                part.transform.localPosition = new Vector3(0f, i * 0.5f, 0f);

                Object.DestroyImmediate(part.GetComponent<Collider>());
            }

            return root;
        }

        /// <summary>An upright player, tie-able by nothing yet.</summary>
        private GameObject NewStandingPlayer()
        {
            GameObject player = NewRagdollBody("Player");
            Woken<PlayerRagdoll>(player);
            return player;
        }

        /// <summary>
        /// A player already flat on the ground, with NOTHING holding them there.
        ///
        /// <para>
        /// The repulsor-blast shape, and the reason it is the default fixture: it is the case
        /// <c>CanTie</c> is deliberately broader than "netted" for, and it is the only one in which
        /// <c>RagdollRig.BudgetExempt</c> starts false — which is what makes that flag readable
        /// below as "the tie has hold of this body" rather than as "something does".
        /// </para>
        /// </summary>
        private GameObject NewDownedPlayer(out RagdollRig rig)
        {
            GameObject player = NewStandingPlayer();
            rig = player.GetComponent<RagdollRig>();

            rig.GoLimp(Vector3.zero, settled: true, drives: false);

            Assert.IsTrue(rig.IsLimp,
                "The fixture never went limp, so every tie below would be refused for the wrong " +
                "reason and the refusals would all pass.");
            Assert.IsFalse(rig.BudgetExempt,
                "The fixture is already exempt from RagdollBudget, so BudgetExempt cannot be read " +
                "as the tie's own mark and every hold assertion below is meaningless.");

            return player;
        }

        private Hogtie NewTie(GameObject body, InventoryItem rope = null, bool authority = true)
        {
            Hogtie tie = Hogtie.Ensure(body);

            Assert.IsTrue(tie.Bind(new HogtieSettings(), rope, authority),
                "The tie was refused, so nothing below is measuring a tie.");

            return tie;
        }

        /// <summary>
        /// Run a tie forward until it lets go, and say how long that took.
        ///
        /// <paramref name="struggling"/> presses jump on every frame, which the send meter throttles
        /// to the authored cap — so this is a PERFECT struggle and not a superhuman one. That
        /// throttle is the thing being relied on: an implementation that let every frame's press
        /// through would finish this loop in a couple of seconds.
        /// </summary>
        private static float SecondsUntilFree(Hogtie tie, bool struggling)
        {
            float elapsed = 0f;

            while (tie.IsBound && elapsed < RunawaySeconds)
            {
                tie.Step(Frame, jumpPressed: struggling, move: Vector2.zero);
                elapsed += Frame;
            }

            Assert.Less(elapsed, RunawaySeconds,
                "The tie never ended. Its pool is not being drained at all, so a tied body stays " +
                "down for the rest of the session.");

            return elapsed;
        }

        private InventoryItem NewRopeItem()
        {
            var item = ScriptableObject.CreateInstance<InventoryItem>();
            item.name = "Leash";
            item.ID = "test.leash";
            assets.Add(item);
            return item;
        }

        /// <summary>Records what was dropped and where, so the rope's return is observable.</summary>
        private sealed class DropSpy : IItemDropService
        {
            public readonly List<InventoryItem> Dropped = new List<InventoryItem>();
            public Transform LastOrigin;

            public void DropItem(Transform origin, InventoryItem item, ItemState state = null)
            {
                Dropped.Add(item);
                LastOrigin = origin;
            }
        }

        // ── The two durations ────────────────────────────────────────────────

        [Test]
        public void AnUntouchedTieLastsTheAuthoredTwoMinutes()
        {
            // The user's own number, and the only part of this feature they stated as a figure.
            // Nothing else here would notice it being wrong: a tie that lasts thirty seconds looks
            // exactly like one that lasts two minutes until somebody times it.
            GameObject player = NewDownedPlayer(out _);
            Hogtie tie = NewTie(player);

            float held = SecondsUntilFree(tie, struggling: false);

            Assert.That(held, Is.EqualTo(AuthoredCeiling).Within(1f),
                $"A tied body nobody touched was held for {held:F1} s against the two minutes " +
                "the user asked for. Either the pool was reset to something other than " +
                "HoldSeconds, or something is draining it that should not be — SnareIntegrity's " +
                "idle rot is a quarter-speed floor, so a tie billed at zero load would last four " +
                "times too long rather than not at all.");
        }

        [Test]
        public void APerfectStruggleBreaksATieInAboutFortyFiveSeconds()
        {
            // "Struggle out, but far slower than a net" is the user's choice, and this is the half
            // of it that a number can hold. The other half — that it really is slower than a net —
            // is the test below.
            GameObject player = NewDownedPlayer(out _);
            Hogtie tie = NewTie(player);

            float held = SecondsUntilFree(tie, struggling: true);

            Assert.That(held, Is.EqualTo(StruggledEscape).Within(EscapeTolerance),
                $"A body struggling flat out got free in {held:F1} s against a target of " +
                $"{StruggledEscape:F0}. 50.4 s means the multiplier was derived assuming the " +
                "struggle meter sits at 1 while saturated — it does not, it averages 0.8504. " +
                "120 s means the struggle is not being billed to the pool at all. Anything much " +
                "under 40 means the send throttle is not throttling, so mashing beats holding.");
        }

        [Test]
        public void AStruggledTieOutlastsAStruggledNetSeveralTimesOver()
        {
            // The comparison the user actually asked for, measured rather than asserted in a
            // comment. It is not implied by the test above: a 45 s escape is only "far slower than
            // a net" while the net's own escape stays where it is, and the two share both the
            // integrity pool and the meter — so a change to either moves both, and a fixed number
            // here would go on passing while the gap it names closed.
            GameObject tied = NewDownedPlayer(out _);
            float tieEscape = SecondsUntilFree(NewTie(tied), struggling: true);

            float netEscape = SecondsUntilNetTears(new SnareStruggle());

            Assert.Greater(tieEscape, netEscape * 3f,
                $"A tie struggled out in {tieEscape:F1} s against a net's {netEscape:F1} s. The " +
                "user chose 'far slower than a net'; under three times is not that.");
        }

        /// <summary>
        /// The net's own escape, computed from the net's own authored settings and the same two
        /// classes the tie uses.
        ///
        /// <para>
        /// Reproduced here rather than driven through a whole <c>SnareCatch</c> — which needs a
        /// lattice, a drape, a mesh and a landing — because the only part being compared is the
        /// arithmetic both restraints share: one captive's worth of mass plus the multiplier times
        /// the meter, against a pool of the authored seconds. That is exactly what
        /// <c>SnareCatch.StrugglingMass</c> feeds <c>SnareIntegrity.Drain</c> for a single player.
        /// </para>
        /// </summary>
        private static float SecondsUntilNetTears(SnareStruggle net)
        {
            var pool = new SnareIntegrity();
            pool.Reset(net.HoldSeconds);

            var send = new SnareStruggleMeter(net.MaxUsefulStruggleRate, net.StruggleDecaySeconds);
            var authority = new SnareStruggleMeter(net.MaxUsefulStruggleRate, net.StruggleDecaySeconds);

            float elapsed = 0f;

            while (!pool.IsSpent && elapsed < RunawaySeconds)
            {
                send.Advance(Frame);
                if (send.Push()) authority.Push();

                authority.Advance(Frame);
                pool.Drain(SnareIntegrity.ReferenceLoad
                           * (1f + net.StruggleMultiplier * authority.Level), Frame);

                elapsed += Frame;
            }

            return elapsed;
        }

        // ── Who may be tied ──────────────────────────────────────────────────

        [Test]
        public void ATieRefusesABodyThatIsStillOnItsFeet()
        {
            // The rule that stops the leash being a one-click two-minute stun. A tie is a
            // follow-up: something else has to put them down first, which costs the tier a second
            // action and gives the target a window in which the first one can be answered.
            GameObject player = NewStandingPlayer();

            Assert.IsFalse(Hogtie.CanTie(player),
                "A player standing up was reported as tieable.");

            Hogtie tie = Hogtie.Ensure(player);

            Assert.IsFalse(tie.Bind(new HogtieSettings(), null, authority: true),
                "The tie was refused by CanTie and Bind took it anyway, so the gate is decorative.");
            Assert.IsFalse(tie.IsBound);

            // The control, on the same fixture. Without it a CanTie that answers false for
            // everything would pass the assertions above and ship a leash that can tie nobody.
            player.GetComponent<RagdollRig>().GoLimp(Vector3.zero, settled: true, drives: false);

            Assert.IsTrue(Hogtie.CanTie(player),
                "The same player could not be tied lying down either, so the refusal above was " +
                "not about being on their feet.");
            Assert.IsTrue(tie.Bind(new HogtieSettings(), null, authority: true));
        }

        [Test]
        public void ATieAcceptsABodyMerelyKnockedFlatRatherThanHeld()
        {
            // CanTie is deliberately broader than "netted". A player knocked flat by a repulsor
            // blast recovers on their own timer and has no captor at all — nothing holds a claim on
            // them — so a gate written against the claim set alone would refuse them, and the blast
            // and the leash would feel like unrelated systems.
            GameObject player = NewDownedPlayer(out RagdollRig rig);
            PlayerRagdoll ragdoll = player.GetComponent<PlayerRagdoll>();

            Assert.IsTrue(rig.IsLimp && !rig.BudgetExempt,
                "This fixture is being held by something after all, so it is not the blast case.");
            Assert.IsTrue(ragdoll.IsHeldOrDown,
                "A limp body with no captor reports as neither held nor down, so CanTie has " +
                "nothing to read.");

            Assert.IsTrue(Hogtie.CanTie(player),
                "A player knocked flat could not be tied. Only netted bodies are tieable, which " +
                "is the narrow rule this is written against.");
        }

        [Test]
        public void ATieRefusesABodyThatIsAlreadyTied()
        {
            // A second rope spent on somebody already tied is a rope lost for nothing — and worse,
            // a second Bind that took would overwrite the first tie's pool and hand the target a
            // fresh two minutes as a reward for being tied twice.
            GameObject player = NewDownedPlayer(out _);
            Hogtie tie = NewTie(player);

            Assert.IsFalse(Hogtie.CanTie(player), "An already-tied body was reported as tieable.");
            Assert.IsFalse(tie.Bind(new HogtieSettings(), null, authority: true),
                "A second tie was accepted over the first.");

            Assert.IsTrue(tie.IsBound, "The refused second tie let go of the first one's hold.");
        }

        [Test]
        public void ATieRefusesABodyWithNoRagdollAtAll()
        {
            // Every player prefab carries a PlayerRagdoll, so this is the case where that wiring
            // has been lost. A tie that quietly held such a body would be a wiring bug presenting
            // as a balance one, days later, as leashes that vanish having done nothing.
            GameObject nobody = NewObject("Nobody");

            Assert.IsFalse(Hogtie.CanTie(nobody));
            Assert.IsFalse(Hogtie.Ensure(nobody).Bind(new HogtieSettings(), null, authority: true));
        }

        [Test]
        public void ATieAddressesTheBodyAndNotTheBoneItWasAimedAt()
        {
            // The trap this feature is built over. A tie may only land on a body that is ALREADY
            // limp — and a limp body is a built ragdoll, whose every bone carries a Rigidbody and a
            // BoxCollider of its own. The leash's ordinary anchor path resolves a hit through
            // GetComponentInParent<Rigidbody>(), which for a downed player is their forearm: the
            // tie would be added to a bone, addressed to an object with no NetworkObject, and hold
            // nothing at all while reporting success.
            GameObject player = NewDownedPlayer(out _);

            Collider bone = player.GetComponentInChildren<Collider>();
            Assert.IsNotNull(bone,
                "The ragdoll built no colliders, so this test is not aiming at a bone and would " +
                "pass against a resolver that only ever answers with the object it was given.");
            Assert.AreNotSame(player, bone.gameObject,
                "The collider found is the root itself, so the resolution under test is trivial.");

            Assert.AreSame(player, Hogtie.BodyOf(bone),
                "A hit on a ragdoll bone resolved to the bone rather than to the body. The tie " +
                "would be added to a forearm.");
        }

        // ── Living beside a net ──────────────────────────────────────────────

        [Test]
        public void ANetTearingOutFromUnderATieLeavesTheBodyDown()
        {
            // The claim set is what makes this work, and it is the reason nothing in Hogtie tracks
            // the net and nothing in SnaredBody tracks the tie. Verified rather than assumed: a
            // hold that were one flag instead of a set would have the net stand a tied player up
            // on its way out, and the tier would never know why their rope did nothing.
            GameObject player = NewDownedPlayer(out RagdollRig rig);

            GameObject net = NewObject("Net");
            SnaredBody netted = SnaredBody.Ensure(player);
            Assert.IsTrue(netted.Bind(net.transform, new SnareStruggle()),
                "The net refused this body, so the two holds under test never coexisted.");

            Hogtie tie = NewTie(player);

            Assert.IsTrue(rig.BudgetExempt, "Neither hold registered.");

            netted.Release(net.transform);

            Assert.IsTrue(tie.IsBound, "The net rotting off cleared the tie's own record of itself.");
            Assert.IsTrue(rig.BudgetExempt,
                "The net rotted and stood up a body that is still tied. The tie has another " +
                "minute and a half to run and nothing left holding the body down.");

            // And the last claim out really does end it, rather than the body being stuck down for
            // good once two things ever held it.
            tie.Untie();

            Assert.IsFalse(rig.BudgetExempt,
                "Every claim has been given back and the body is still being held down.");
        }

        [Test]
        public void ATieCutOffANettedBodyLeavesTheNetHoldingIt()
        {
            // The same rule reached from the other end, because the two are not symmetrical in the
            // code: the net releases through SnaredBody.Release and the tie through Hogtie.Untie,
            // and only one of them can be wrong at a time.
            GameObject player = NewDownedPlayer(out RagdollRig rig);

            GameObject net = NewObject("Net");
            SnaredBody netted = SnaredBody.Ensure(player);
            Assert.IsTrue(netted.Bind(net.transform, new SnareStruggle()));

            Hogtie tie = NewTie(player);
            tie.Untie();

            Assert.IsFalse(tie.IsBound);
            Assert.IsTrue(netted.IsBound, "Cutting the ropes cleared the net's record of itself.");
            Assert.IsTrue(rig.BudgetExempt,
                "Somebody cut the ropes and the netted body got up out of the net as well.");
        }

        // ── Ending it ────────────────────────────────────────────────────────

        [Test]
        public void AnUntieLetsGoAtOnceRatherThanOnTheTimer()
        {
            // "Another player can untie you" is the other half of the user's answer. It has to be
            // instant: a rescue that takes effect a minute later is not a rescue, and there is
            // nothing on screen to say it is coming.
            GameObject player = NewDownedPlayer(out RagdollRig rig);
            Hogtie tie = NewTie(player);

            Assert.IsTrue(rig.BudgetExempt);
            Assert.That(tie.HoldFraction, Is.GreaterThan(0.99f),
                "The pool is already part spent before a single frame has run.");

            tie.Untie();

            Assert.IsFalse(tie.IsBound, "The untie did not take.");
            Assert.IsFalse(rig.BudgetExempt,
                "The ropes came off and the body is still being held down, so the rescued player " +
                "still cannot move.");

            // Twice is a no-op, because every route out funnels through Untie and the authority's
            // own broadcast re-enters it on the host.
            tie.Untie();
            Assert.IsFalse(tie.IsBound);
        }

        [Test]
        public void ATiedBodyThatDiesIsUntiedAtOnce()
        {
            // A corpse is not a captive. The ragdoll adapters already drop every claim on death —
            // that is what stops a permanently un-evictable RagdollBudget slot — but nothing there
            // knows about the ropes, so without a hook of its own the tie outlives the player:
            // the pool goes on draining, their machine goes on reading a dead player's keys, and
            // the rope does not come back for another two minutes.
            GameObject player = NewRagdollBody("Player");

            // Added BEFORE the adapter wakes, because PlayerRagdoll caches it in Awake. (Its own
            // OnDeath subscription is made in OnEnable, which AddComponent does not raise outside
            // play mode — so what is measured below is Hogtie's hook and only Hogtie's, which is
            // the one under test.)
            var health = player.AddComponent<HealthComponent>();
            Woken<PlayerRagdoll>(player);

            RagdollRig rig = player.GetComponent<RagdollRig>();
            rig.GoLimp(Vector3.zero, settled: true, drives: false);

            var spy = new DropSpy();
            GameServices.ItemDropService = spy;

            InventoryItem rope = NewRopeItem();
            Hogtie tie = NewTie(player, rope);

            health.Damage(health.GetMaxHealth);

            Assert.IsFalse(health.Alive, "The fixture never died, so nothing below was triggered.");
            Assert.IsFalse(tie.IsBound, "The body died and the ropes stayed on the corpse.");
            Assert.AreEqual(1, spy.Dropped.Count,
                "The rope did not come back when its captive died, so it is gone from the world " +
                "for good.");
        }

        [Test]
        public void TheRopeComesBackAtTheBodyExactlyOnce()
        {
            // The leash is consumed by the tie, so if the tie ends without giving it back the item
            // is destroyed by using it — which no other artifact in this project does. Exactly
            // once, because the authority both drops the rope and broadcasts the end, and on a host
            // that broadcast re-enters Untie inline.
            GameObject player = NewDownedPlayer(out _);

            var spy = new DropSpy();
            GameServices.ItemDropService = spy;

            InventoryItem rope = NewRopeItem();
            Hogtie tie = NewTie(player, rope);

            SecondsUntilFree(tie, struggling: true);

            Assert.AreEqual(1, spy.Dropped.Count,
                $"The rope was put back into the world {spy.Dropped.Count} times.");
            Assert.AreSame(rope, spy.Dropped[0], "Something other than the leash was dropped.");
            Assert.AreSame(player.transform, spy.LastOrigin,
                "The rope came back somewhere other than at the body it was tied round.");
        }

        [Test]
        public void APeerNeverDrainsAPoolOfItsOwn()
        {
            // The authority split, and the reason it cannot be skipped. Every machine holds the
            // body — a peer that did not would watch a tied player walk about — but a peer that
            // also ran the pool would reach zero on its own schedule and free the captive on one
            // screen while every other machine still holds them. It waits to be told.
            GameObject player = NewDownedPlayer(out _);
            Hogtie tie = Hogtie.Ensure(player);

            Assert.IsTrue(tie.Bind(new HogtieSettings(), null, authority: false),
                "A peer refused to hold the body at all, which is the opposite failure.");

            for (float t = 0f; t < AuthoredCeiling * 2f; t += Frame)
                tie.Step(Frame, jumpPressed: true, move: Vector2.zero);

            Assert.IsTrue(tie.IsBound,
                "A peer ran the pool down by itself. Four minutes of struggling — twice the " +
                "ceiling — ended a tie on a machine that is not entitled to end anything.");
            Assert.That(tie.HoldFraction, Is.EqualTo(1f).Within(1e-4f),
                "The peer's pool moved at all, so it is being spent and merely has not run out.");
        }

        // ── What must never be written down ──────────────────────────────────

        [Test]
        public void ATieIsNeverPersisted()
        {
            // Deliberate, and the failure it avoids is the worst kind this project has: a quit-time
            // autosave that captured a tie reloads a world in which a player cannot move, with
            // nothing in the log to say why. Untying by loading is a far better failure.
            //
            // Asserted against the discovery mechanism rather than against the intent, because the
            // mechanism is the whole of it: SaveableEntity.CollectSavers is a GetComponents<
            // ISaveable>() walk, so a Hogtie that grew an ISaveable would be captured the moment it
            // did, with no other change anywhere.
            Assert.IsFalse(typeof(ISaveable).IsAssignableFrom(typeof(Hogtie)),
                "Hogtie implements ISaveable, so a tie now reaches the save file and a loaded " +
                "world can contain a player who cannot move.");

            GameObject player = NewDownedPlayer(out _);
            NewTie(player);

            var savers = new List<ISaveable>();
            SaveableEntity.CollectSavers(player.transform, savers);

            Assert.IsEmpty(savers,
                "A tied body has savers on it. Whatever they are, they were put there by the tie.");
        }

        // ── The struggle itself ──────────────────────────────────────────────

        [Test]
        public void AJumpCountsAsAStruggleAgainstTheRopes()
        {
            // The shared reader in its simplest form. The net's own tests pin the reversal angles;
            // this pins that a tie is wired to the same reader at all, which a tie that read
            // nothing would fail while every duration test above still passed — slowly.
            GameObject player = NewDownedPlayer(out _);
            Hogtie tie = NewTie(player);

            Assert.That(tie.StruggleLevel, Is.EqualTo(0f).Within(1e-4f),
                "A body that has done nothing is already struggling.");

            tie.Step(Frame, jumpPressed: true, move: Vector2.zero);

            Assert.That(tie.StruggleLevel, Is.GreaterThan(0f),
                "The tied body threw itself about and the ropes never noticed.");
        }

        [Test]
        public void ThrowingYourselfTheOtherWayCountsAndSteeringDoesNot()
        {
            // The reader was extracted from SnaredBody so a tie and a net could not disagree about
            // what a struggle is. This is the tie's end of that: without it the extraction could
            // regress to "any movement counts" here while the net's own tests went on passing.
            GameObject player = NewDownedPlayer(out _);
            Hogtie tie = NewTie(player);

            tie.Step(Frame, jumpPressed: false, move: Vector2.right);

            Assert.That(tie.StruggleLevel, Is.EqualTo(0f).Within(1e-4f),
                "The first push of a direction counted as a reversal of nothing.");

            // 90 degrees round. Steering, not fighting.
            tie.Step(Frame, jumpPressed: false, move: Vector2.up);

            Assert.That(tie.StruggleLevel, Is.EqualTo(0f).Within(1e-4f),
                "Merely steering while tied is draining the ropes, so the escape is a matter of " +
                "leaning on a key.");

            // 180 degrees round from that. A reversal by any authored angle.
            tie.Step(Frame, jumpPressed: false, move: Vector2.down);

            Assert.That(tie.StruggleLevel, Is.GreaterThan(0f),
                "The body threw itself the opposite way and it counted for nothing.");
        }
    }
}
