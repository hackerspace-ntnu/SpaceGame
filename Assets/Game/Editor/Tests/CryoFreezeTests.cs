using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using SpaceGame.Agents;
using SpaceGame.Gameplay;
using SpaceGame.Gameplay.Status;
using SpaceGame.Gameplay.Surface;
using SpaceGame.Items;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTests
{
    /// <summary>
    /// A freeze holds every body, holds it for ten seconds, and does nothing else to it.
    ///
    /// <para>
    /// The defects these exist for are all one shape: something about being frozen was authored per
    /// prefab rather than derived from the condition, so the creatures somebody had remembered to
    /// tick a box on froze and the rest walked out of the plume unharmed. The rules are now the
    /// condition's own — <see cref="StatusBehaviour.Suppresses"/> and
    /// <see cref="StatusReceiver.EnsureOnBody"/> — and these hold them there.
    /// </para>
    /// </summary>
    public class CryoFreezeTests
    {
        private readonly List<GameObject> spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned)
                if (go != null) Object.DestroyImmediate(go);

            spawned.Clear();
        }

        // ── What can be frozen ─────────────────────────────────────────────────

        /// <summary>
        /// The defect: the plume only froze bodies that already carried a receiver, so a creature
        /// whose prefab had no StatusReactionModule on it could be sprayed all day.
        /// </summary>
        [Test]
        public void AnythingAliveOrLooseTakesTheCold()
        {
            GameObject creature = New("creature", typeof(HealthComponent));
            GameObject crate = New("crate", typeof(Rigidbody));
            GameObject dune = New("dune");

            Assert.IsNotNull(StatusReceiver.EnsureOnBody(creature),
                             "A body with health could not be given a condition, so nothing the " +
                             "cryo sprayer or the flamethrower hits would freeze or burn.");

            Assert.IsNotNull(StatusReceiver.EnsureOnBody(crate),
                             "A loose Rigidbody prop could not be given a condition.");

            Assert.IsNull(StatusReceiver.EnsureOnBody(dune),
                          "World geometry took a receiver. A sweep across a dune now freezes the " +
                          "whole terrain chunk as one body.");
        }

        /// <summary>
        /// One coat of ice per body, not one per limb: a creature's colliders are children, and the
        /// condition has to land on the object whose relay the status message arrives on.
        /// </summary>
        [Test]
        public void ALimbResolvesToTheBodyItBelongsTo()
        {
            GameObject creature = New("creature", typeof(HealthComponent));
            GameObject leg = New("leg", typeof(BoxCollider));
            leg.transform.SetParent(creature.transform);

            StatusReceiver first = StatusReceiver.EnsureOnBody(leg);
            StatusReceiver second = StatusReceiver.EnsureOnBody(creature);

            Assert.AreSame(first, second,
                           "A limb and its body got separate receivers, so a creature with a " +
                           "collider per leg wears one coat of ice per leg.");
        }

        // ── What being frozen means ────────────────────────────────────────────

        /// <summary>
        /// Helplessness is the KIND's answer. Authored per creature, it is a row somebody forgets.
        /// </summary>
        [Test]
        public void FrozenAndFoamedSuppressTheBodyAndNothingElseDoes()
        {
            Assert.IsTrue(new FrozenStatus().Suppresses, "A frozen body was left able to act.");
            Assert.IsTrue(new FoamedStatus().Suppresses, "A foamed body was left able to act.");

            Assert.IsFalse(new BurningStatus().Suppresses,
                           "Burning stopped the body acting. A creature that catches fire must " +
                           "run, not stand still and take it.");
            Assert.IsFalse(new SlickStatus().Suppresses, "Slick stopped the body acting.");
            Assert.IsFalse(new InflatedStatus().Suppresses, "Inflated stopped the body acting.");
        }

        [Test]
        public void AFreezeLastsTenSeconds()
        {
            Assert.AreEqual(10f, new FrozenStatus().DefaultSeconds, 0.001f,
                            "A freeze is ten seconds. Anything else and the sprayer and the " +
                            "condition disagree about what the item is worth.");
        }

        /// <summary>
        /// The freeze must not be an execution. It watched for damage and killed the body outright
        /// above a threshold; a victim who can see it coming for a second and a half and can do
        /// nothing about it once it lands is exactly the case that must not be lethal
        /// (GDC-L1-MP-0002).
        /// </summary>
        [Test]
        public void AFreezeNeitherDamagesNorKills()
        {
            Assert.IsFalse(new FrozenStatus() is DamageWatchingStatus,
                           "Frozen watches damage again. A hit on a frozen body now kills it, " +
                           "which is the shatter this deliberately removed.");

            GameObject body = New("body", typeof(HealthComponent), typeof(StatusReceiver));
            var health = body.GetComponent<HealthComponent>();
            var status = body.GetComponent<StatusReceiver>();

            status.Apply(StatusKind.Frozen);
            Assert.IsTrue(status.Has(StatusKind.Frozen),
                          "The freeze did not take offline, where every machine decides.");
            Assert.IsTrue(status.Suppressed, "A frozen body did not read as suppressed.");

            health.Damage(9999);

            Assert.IsTrue(health.Alive || health.GetHealth <= 0,
                          "A frozen body survived or died by the ordinary damage path, which is " +
                          "the only path allowed to kill it.");
            Assert.IsFalse(status.SecondsLeft(StatusKind.Frozen) > 10f,
                           "Being hit extended the freeze.");
        }

        // ── What a frozen creature does ────────────────────────────────────────

        /// <summary>
        /// The defect this exists for: an agent with no <c>StatusReactionModule</c> authored on it
        /// kept walking while frozen, because the suppression lived in that module alone.
        /// </summary>
        [Test]
        public void AFrozenAgentIsToldToStandStillEvenWithNoReactionModule()
        {
            GameObject agent = New("agent", typeof(RecordingMotor), typeof(AgentController));
            var motor = agent.GetComponent<RecordingMotor>();
            var controller = agent.GetComponent<AgentController>();

            Invoke(controller, "Awake");

            var status = agent.GetComponent<StatusReceiver>();
            Assert.IsNotNull(status,
                             "An agent came up with no StatusReceiver, so nothing that lands a " +
                             "condition on it would ever reach it.");

            status.Apply(StatusKind.Frozen);
            Invoke(controller, "Update");

            Assert.AreEqual(1, motor.Ticks, "The motor was not ticked while the agent was frozen.");
            Assert.AreEqual(AgentIntentType.Idle, motor.LastIntent.Type,
                            "A frozen agent was still handed a movement intent. It walks out of " +
                            "the ice.");
        }

        // ── What the cold does to the ground ───────────────────────────────────

        /// <summary>
        /// Frost and ice leave the same grip. Two numbers were one number too many the day the
        /// sprayer started laying both of them: a player who has learnt what frozen ground does to
        /// them should not have to learn it twice because one patch happened to land on water
        /// (GDC-L1-SYS-0006).
        /// </summary>
        [Test]
        public void FrostAndIceLeaveTheSameGrip()
        {
            float frost = new SlickCoat().Grip;
            float ice = new IceCoat().Grip;

            Assert.AreEqual(ice, frost, 0.001f,
                            $"Frost leaves {frost} grip and ice leaves {ice}. The same plume made " +
                            "both, so the difference between them is what is THERE, not how much " +
                            "of it a foot can use.");

            Assert.Less(frost, 0.1f,
                        "Frost is no longer frictionless. The whole of what the sprayer does to " +
                        "ground it cannot freeze is take the grip out of it.");
        }

        /// <summary>
        /// The film on a body and the film on the ground report the same figure. They are asked
        /// through one interface precisely so that a mover never learns which of the two it is
        /// standing in, and two different numbers would make that a lie.
        /// </summary>
        [Test]
        public void TheFilmOnABodyMatchesTheFilmOnTheGround()
        {
            var body = new SlickStatus();
            GameObject slicked = New("slicked", typeof(StatusReceiver));

            body.OnApplied(slicked.GetComponent<StatusReceiver>());

            try
            {
                Assert.AreEqual(new SlickCoat().Grip, body.GripFor(slicked, Vector3.zero), 0.001f,
                                "A slicked body and slicked ground disagree about how much grip " +
                                "is left, so the same spray does two different things depending " +
                                "on whether it caught the creature or the sand under it.");
            }
            finally
            {
                body.OnCleared(slicked.GetComponent<StatusReceiver>());
            }
        }

        /// <summary>
        /// Frost goes on anything; ice needs something that can freeze. That split is what stops
        /// the sprayer building a bridge over dry sand while still leaving every surface it touches
        /// slippery.
        /// </summary>
        [Test]
        public void FrostTakesAnySurfaceAndIceDoesNot()
        {
            Assert.IsTrue(new SlickCoat().CanCoat(Vector3.zero, null, out _),
                          "Frost refused a surface. It is the coat with no conditions on it — the " +
                          "one that makes 'everything the plume touches is slippery' true.");

            Assert.IsFalse(new IceCoat().CanCoat(Vector3.zero, null, out _),
                           "Ice was laid on nothing at all. A sheet over dry sand is a bridge " +
                           "over nothing and a rule the player cannot see either way.");
        }

        /// <summary>
        /// Frost expires and is never written to a save; ice is the one kind that outlives the
        /// session. Getting this backwards fills a save file with twenty-second patches.
        /// </summary>
        [Test]
        public void FrostExpiresAndIsNotSaved()
        {
            var frost = new SlickCoat();

            Assert.Greater(frost.DefaultSeconds, 0f, "Frost never expires.");
            Assert.IsFalse(frost.Saved, "Frost is written to the save. It lasts twenty seconds.");
            Assert.IsTrue(new IceCoat().Saved, "Ice is no longer saved. A frozen pool is a bridge " +
                          "somebody built and it has to come back.");
        }

        // ── What actually ships ────────────────────────────────────────────────

        /// <summary>
        /// The defect this exists for: the sprayer froze at five metres in the game while its own
        /// source said nine. Serialized fields are stored ON THE PREFAB, so editing a default in
        /// the class changes nothing that ships — and nothing says so.
        /// </summary>
        [Test]
        public void ThePrefabCarriesTheReachAndTheFreezeTimeTheCodeDeclares()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SprayerPrefab);
            Assert.IsNotNull(prefab, $"No cryo sprayer prefab at {SprayerPrefab}.");

            var shipped = prefab.GetComponent<CryoSprayerArtifact>();
            Assert.IsNotNull(shipped, "The cryo sprayer prefab has no CryoSprayerArtifact on it.");

            var authored = new SerializedObject(shipped);
            var defaults = new SerializedObject(
                New("defaults", typeof(CryoSprayerArtifact)).GetComponent<CryoSprayerArtifact>());

            AssertMatches(authored, defaults, "range",
                          "The gun freezes at a different distance than its code says. Whichever " +
                          "of the two is right, the prefab is the one the player holds.");

            AssertMatches(authored, defaults, "freezeSeconds",
                          "The gun takes a different time to freeze a body than its code says.");

            AssertMatches(authored, defaults, "coneHalfAngle",
                          "The gun freezes a different cone than its code says, so the plume the " +
                          "player watches and the cold it carries are two different shapes.");
        }

        /// <summary>
        /// The defect: the plume froze only what the crosshair was exactly on, so vapour visibly
        /// washing over a creature did nothing to it and the gun read as broken.
        /// </summary>
        [Test]
        public void TheConeThatFreezesIsTheConeThatIsDrawn()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SprayerPrefab);
            Assert.IsNotNull(prefab, $"No cryo sprayer prefab at {SprayerPrefab}.");

            var shipped = prefab.GetComponent<CryoSprayerArtifact>();
            var drawn = prefab.GetComponentInChildren<CryoSprayerNozzle>(true);
            Assert.IsNotNull(drawn, "The cryo sprayer prefab has no nozzle, so nothing draws the plume.");

            float freezes = new SerializedObject(shipped).FindProperty("coneHalfAngle").floatValue;

            Assert.AreEqual(drawn.OpenConeDegrees, freezes, 0.5f,
                            $"The plume is drawn at {drawn.OpenConeDegrees}° and freezes at " +
                            $"{freezes}°. Vapour a player can see washing over a creature has to " +
                            "freeze it.");
        }

        /// <summary>
        /// Aiming has to keep paying, or an eighteen-metre cone nine metres across at its far end
        /// makes the sprayer a longer-ranged flamethrower (GDC-L1-BAL-0004).
        /// </summary>
        [Test]
        public void TheColdFallsOffAcrossTheCone()
        {
            Assert.AreEqual(1f, ConeSweep.Falloff(0f, 15f, 0.35f), 0.001f,
                            "A body on the crosshair no longer freezes at the authored rate.");

            Assert.AreEqual(0.35f, ConeSweep.Falloff(15f, 15f, 0.35f), 0.001f,
                            "A body at the rim of the cone does not take the authored edge rate.");

            Assert.AreEqual(0.35f, ConeSweep.Falloff(40f, 15f, 0.35f), 0.001f,
                            "The falloff runs past the rim, so a body outside the cone would " +
                            "still be scaled rather than skipped.");

            Assert.Greater(ConeSweep.Falloff(5f, 15f, 0.35f), ConeSweep.Falloff(12f, 15f, 0.35f),
                           "The cold does not fall off with angle, so aiming buys nothing.");
        }

        private static void AssertMatches(SerializedObject shipped, SerializedObject defaults,
                                          string field, string why)
        {
            SerializedProperty a = shipped.FindProperty(field);
            SerializedProperty b = defaults.FindProperty(field);

            Assert.IsNotNull(a, $"No '{field}' on the shipped sprayer.");
            Assert.IsNotNull(b, $"No '{field}' on a fresh CryoSprayerArtifact.");

            Assert.AreEqual(b.floatValue, a.floatValue, 0.001f,
                            $"Prefab '{field}' is {a.floatValue}, the class default is " +
                            $"{b.floatValue}. {why}");
        }

        // ── Fixture ────────────────────────────────────────────────────────────

        private const string SprayerPrefab =
            "Assets/Game/Prefabs/Items/Artifacts/Gadgets/CryoSprayer.prefab";

        /// <summary>
        /// A motor that remembers what it was last told. Stands in for NavMeshAgentMotor, which
        /// needs a NavMesh under it to be worth constructing.
        /// </summary>
        private class RecordingMotor : MonoBehaviour, IMovementMotor
        {
            public int Ticks { get; private set; }
            public MoveIntent LastIntent { get; private set; }

            public Vector3 Velocity => Vector3.zero;
            public float TopSpeed => 0f;
            public bool IsImmobile => true;
            public bool HasReachedDestination => true;
            public Vector3? CurrentDestination => null;

            public void Tick(in MoveIntent intent, float deltaTime)
            {
                Ticks++;
                LastIntent = intent;
            }

            public void ForceStop() { }
            public void NudgeDestination(Vector3 offset) { }
            public void SuggestDestination(Vector3 position) { }
        }

        private GameObject New(string name, params System.Type[] components)
        {
            var go = new GameObject(name, components);
            spawned.Add(go);
            return go;
        }

        /// <summary>
        /// Unity raises no Awake or Update for a component added outside play mode, so the two are
        /// called by hand — the same way the rest of these EditMode tests drive a MonoBehaviour.
        /// </summary>
        private static void Invoke(Component target, string method)
        {
            MethodInfo info = target.GetType().GetMethod(
                method, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);

            Assert.IsNotNull(info, $"No '{method}' on {target.GetType().Name}.");
            info.Invoke(target, null);
        }
    }
}
