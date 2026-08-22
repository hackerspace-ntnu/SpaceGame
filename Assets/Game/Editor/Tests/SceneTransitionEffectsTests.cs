// Which machine plays a scene transition's effects, and how the two halves talk.
//
// The bug this covers has been on both sides of the same line. VolumeTrigger fires from a collider,
// and every player's body exists on every machine, so before it was server-gated a transition ran
// on every peer for every initiator — the host's screen faded to black because somebody else walked
// through a door. Gating it then put the fade on the server for everyone, so a client walking into
// a volume saw nothing at all.
//
// The property under test is therefore "whose eyes", plus the handshake that lets a walk-through
// cutscene on somebody else's machine hold up the teleport without being able to wedge it.
//
// Like NetLatchTests, this leans on the degradation contract: with no NetworkManager — which is what
// an EditMode test, a scene opened from the editor and a torn-down session all look like — every
// send falls through to a local dispatch, so the whole round trip runs here in one frame. Offline
// behaviour staying exactly what it was before any of this was networked is itself one of the
// assertions.
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Core;

namespace SpaceGame.EditorTools
{
    public class SceneTransitionEffectsTests
    {
        private readonly List<Object> spawned = new();

        private GameObject NewObject(string name = "entity")
        {
            var go = new GameObject(name);
            spawned.Add(go);
            return go;
        }

        private T NewAsset<T>() where T : ScriptableObject
        {
            var asset = ScriptableObject.CreateInstance<T>();
            spawned.Add(asset);
            return asset;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in spawned)
                if (o != null) Object.DestroyImmediate(o);

            spawned.Clear();

            // The registry and the lockout map are static and outlive a fixture. Anything left in
            // them would make the next test see a destroyed component under a live id.
            Invoke(typeof(SceneTransition), "ResetStatics");
        }

        // ─────────── Test plumbing ───────────

        /// <summary>
        /// Unity does not run Awake or OnEnable for a component added outside play mode, and the
        /// registry is built in one while the message handlers are subscribed in the other.
        /// </summary>
        private static void Life(Component target, string method) =>
            target.GetType()
                  .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)
                  ?.Invoke(target, null);

        private static void Invoke(System.Type type, string method) =>
            type.GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, null);

        private static void Set<T>(T target, string name, object value) =>
            typeof(T).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                     .SetValue(target, value);

        /// <summary>Spelled out rather than read off the enum, so the audience rule cannot drift silently.</summary>
        private static string AudienceFor(GameObject initiator) =>
            typeof(SceneTransition)
                .GetMethod("AudienceFor", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { initiator })
                .ToString();

        /// <summary>Drives an IEnumerator to completion the way a coroutine host would, minus the frames.</summary>
        private static void Drain(IEnumerator routine, int maxSteps = 64)
        {
            int steps = 0;
            while (routine.MoveNext())
            {
                if (++steps > maxSteps) Assert.Fail("Routine never finished — it is waiting on something.");
            }
        }

        /// <summary>An effect that records what it was asked to do and nothing else.</summary>
        private class ProbeEffect : SceneTransitionEffect
        {
            public int Begins;
            public GameObject SawInitiator;
            public readonly ProbeHandle Handle = new();

            public override TransitionChannel Channel => TransitionChannel.Custom;

            public override EffectHandle Begin(SceneTransition host)
            {
                Begins++;
                SawInitiator = host != null ? host.LastInitiator : null;
                return Handle;
            }
        }

        private class ProbeHandle : EffectHandle
        {
            public int Ends;

            public override void End() => Ends++;

            public override IEnumerator AwaitCompletion() { yield break; }
        }

        private SceneTransition NewTransition(string name, params SceneTransitionEffect[] effects)
        {
            SceneTransition transition = BuildTransition(name, effects);
            Life(transition, "OnEnable");
            return transition;
        }

        /// <summary>
        /// A transition that has not been enabled yet, so a caller can finish placing it first.
        ///
        /// The split matters because <c>OnEnable</c> is what registers the transition under its id,
        /// and the id is derived from where the object sits. Enabling one at the scene root and
        /// moving it afterwards registers it under a path it does not occupy.
        /// </summary>
        private SceneTransition BuildTransition(string name, params SceneTransitionEffect[] effects)
        {
            var transition = NewObject(name).AddComponent<SceneTransition>();
            Set(transition, "effects", effects);
            return transition;
        }

        /// <summary>
        /// A transition parented under <paramref name="parent"/>, with its id computed from where
        /// it ended up. The id is cached on first use, so a transition that is reparented after
        /// OnEnable has to be asked again — a real scene never does this, only these tests do.
        /// </summary>
        private SceneTransition PlacedTransition(string name, GameObject parent)
        {
            // Parent BEFORE enabling.
            //
            // This used to enable at the scene root and reparent afterwards, which registered the
            // transition under a root path it was about to leave. With two identically named doors
            // that is an outright collision: the first is created at root/door[0] and moved away,
            // so the second is ALSO created at root/door[0] and OnEnable finds the first already
            // registered there — an error about a hash clash between two objects whose real,
            // post-parenting ids differ perfectly well. The id computation was never at fault; the
            // fixture was asking for it a step too early.
            SceneTransition transition = BuildTransition(name);
            transition.transform.SetParent(parent.transform);
            Life(transition, "OnEnable");
            return transition;
        }

        private SceneTransitionViewer NewViewer(GameObject player)
        {
            SceneTransitionViewer viewer = SceneTransitionViewer.Ensure(player);
            Life(viewer, "OnEnable");
            return viewer;
        }

        // ─────────── The protocol ───────────

        [Test]
        public void TheIdsAreTheOnesTheProtocolWasWrittenAgainst()
        {
            // Ids travel between builds. If these move, a shipped client hands a scene transition's
            // phase to whatever handler now owns 70, and the failure is a screen that never fades
            // back up.
            Assert.AreEqual(70, NetMsg.SceneEffects);
            Assert.AreEqual(71, NetMsg.SceneEffectsDone);

            Assert.AreEqual(0, SceneEffectPhase.Out);
            Assert.AreEqual(1, SceneEffectPhase.In);
        }

        // ─────────── Whose eyes ───────────

        [Test]
        public void OfflineTheEffectsAlwaysBelongToThisMachine()
        {
            // The whole point of the split is that it is invisible in single-player. With no
            // NetworkManager there is one machine, one screen and one set of eyes, so every
            // initiator — player or AI — is answered the same way the pre-netcode code did.
            Assert.AreEqual("ThisMachine", AudienceFor(NewObject("player")));
            Assert.AreEqual("ThisMachine", AudienceFor(NewObject("wandering-agent")));
        }

        [Test]
        public void NobodyIsWatchingANullInitiator()
        {
            Assert.AreEqual("Nobody", AudienceFor(null));
        }

        // ─────────── Naming a transition across machines ───────────

        [Test]
        public void TheIdDependsOnThePathAndNotOnTheInstance()
        {
            // This is the property that makes the message useful at all: the server names a
            // transition and the initiator's own machine has to find ITS copy of the same door.
            // GetInstanceID cannot do it — it is a handle into one process.
            // Both doors are built at the same spot in the same hierarchy — one after the other,
            // the way two machines each deserialize the same scene file.
            GameObject room = NewObject("room");

            var first = PlacedTransition("cave-mouth", room);
            int id = first.TransitionId;

            GameObject firstGo = first.gameObject;
            spawned.Remove(firstGo);
            Object.DestroyImmediate(firstGo);

            var rebuilt = PlacedTransition("cave-mouth", room);

            Assert.AreEqual(id, rebuilt.TransitionId);
            Assert.AreNotEqual(rebuilt.GetInstanceID(), rebuilt.TransitionId,
                "The id is the instance id, so it cannot survive the trip to another machine.");
        }

        [Test]
        public void TwoDoorsWithDifferentPathsGetDifferentIds()
        {
            var cave = NewTransition("cave-mouth");
            var hatch = NewTransition("ship-hatch");

            Assert.AreNotEqual(cave.TransitionId, hatch.TransitionId);
        }

        [Test]
        public void TwoIdenticallyNamedDoorsUnderOneParentStillDiffer()
        {
            // The normal result of duplicating a prefab instance in a scene. Without the sibling
            // index in the path, one door's fade would play for the other one's transition.
            GameObject corridor = NewObject("corridor");

            var left = PlacedTransition("door", corridor);
            var right = PlacedTransition("door", corridor);

            Assert.AreNotEqual(left.TransitionId, right.TransitionId);
        }

        [Test]
        public void AnEnabledTransitionCanBeFoundByIdAndADisabledOneCannot()
        {
            var transition = NewTransition("cave-mouth");
            int id = transition.TransitionId;

            Assert.AreSame(transition, SceneTransition.FindById(id));

            Life(transition, "OnDisable");
            Assert.IsNull(SceneTransition.FindById(id),
                "A door that has streamed out still answers to its id, so a client would play the " +
                "effects of a scene it no longer has.");
        }

        // ─────────── Playing the effects, wherever that happens ───────────

        [Test]
        public void BeginEffectsNamesTheInitiatorBeforeTheFirstEffectReadsIt()
        {
            // WalkThroughCutsceneEffect resolves its cutscene subject from LastInitiator inside
            // Begin. On a remote owner's machine nothing else has filled that in, so if it is set
            // afterwards the cutscene runs for whoever the previous transition was about.
            var effect = NewAsset<ProbeEffect>();
            var transition = NewTransition("cave-mouth", effect);
            GameObject player = NewObject("player");

            transition.BeginEffects(player);

            Assert.AreEqual(1, effect.Begins);
            Assert.AreSame(player, effect.SawInitiator);
            Assert.AreSame(player, transition.LastInitiator);
        }

        [Test]
        public void EndEffectsEndsEveryHandleAndForgetsTheInitiator()
        {
            var effect = NewAsset<ProbeEffect>();
            var transition = NewTransition("cave-mouth", effect);

            List<EffectHandle> handles = transition.BeginEffects(NewObject("player"));
            Drain(transition.EndEffects(handles));

            Assert.AreEqual(1, effect.Handle.Ends);
            Assert.IsNull(transition.LastInitiator);
        }

        [Test]
        public void ANullEffectSlotDoesNotStopTheOthers()
        {
            // Inspector arrays grow with empty elements, and a transition that throws here would
            // leave the screen faded down with no handle to end it.
            var effect = NewAsset<ProbeEffect>();
            var transition = NewTransition("cave-mouth", null, effect);

            List<EffectHandle> handles = transition.BeginEffects(NewObject("player"));

            Assert.AreEqual(1, handles.Count);
            Assert.AreEqual(1, effect.Begins);
        }

        [Test]
        public void EndEffectsToleratesHavingNothingToEnd()
        {
            // The AI-initiator path never begins anything, and the in phase must not care.
            var transition = NewTransition("cave-mouth");
            Assert.DoesNotThrow(() => Drain(transition.EndEffects(null)));
        }

        // ─────────── The ack the server blocks on ───────────

        [Test]
        public void TheOwnersDoneMessageReleasesExactlyOneWait()
        {
            GameObject player = NewObject("player");
            SceneTransitionViewer viewer = NewViewer(player);

            const int TransitionId = 4242;

            Assert.IsFalse(viewer.TakeOutPhaseAck(TransitionId),
                "The server was released before anybody said anything.");

            // Offline every direction collapses to a local dispatch, so this is what the owner's
            // NetToServer looks like when it arrives on the server's copy of that player.
            viewer.NetToServer(NetMsg.SceneEffectsDone, new NetArg { B = TransitionId });

            Assert.IsTrue(viewer.TakeOutPhaseAck(TransitionId));
            Assert.IsFalse(viewer.TakeOutPhaseAck(TransitionId),
                "The ack was consumable twice, so a stale one can release a later transition.");
        }

        [Test]
        public void AnAckForAnotherTransitionDoesNotRelease()
        {
            GameObject player = NewObject("player");
            SceneTransitionViewer viewer = NewViewer(player);

            viewer.NetToServer(NetMsg.SceneEffectsDone, new NetArg { B = 1111 });

            Assert.IsFalse(viewer.TakeOutPhaseAck(2222));
        }

        [Test]
        public void TheViewerLandsOnTheEntityRootAndOnlyOnce()
        {
            // The message is addressed to the player's entity, so a viewer parked on a child would
            // register its handlers on a channel nothing is sent to.
            GameObject player = NewObject("player");
            GameObject chest = NewObject("chest");
            chest.transform.SetParent(player.transform);

            SceneTransitionViewer fromRoot = SceneTransitionViewer.Ensure(player);
            SceneTransitionViewer fromChild = SceneTransitionViewer.Ensure(chest);

            Assert.AreSame(fromRoot, fromChild);
            Assert.AreSame(player, fromRoot.gameObject);
        }
    }
}
