// Tests for the message that shows an NPC's attack to everybody who is only watching it.
//
// Same constraint as AgentAuthorityTests and NetMessagingTests: there is no session here, so the
// half of the contract that can be proved is the half that does not need a wire. That turns out to
// be most of it — the ray convention, the kind filter, the subscribe/unsubscribe pairing, the
// silence in single-player, and the rule that the deciding machine never presents an attack twice.
//
// The half that cannot be proved here is a genuine watcher, because AgentAuthority answers
// "I decide" for every entity when there is no NetworkManager, by design. Every handler therefore
// stops at its first guard in EditMode, which is why the kind filter below is tested on the encode
// rather than through OnAgentActed — asserting that a swing draws no bullet here would pass on the
// authority check and prove nothing. Both that and a client actually drawing the swing need the
// two-process run in the multiplayer skill.
using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using SpaceGame.Agents;
using SpaceGame.Core;

namespace SpaceGame.Tests
{
    public class AgentActionBroadcastTests
    {
        // Everything this fixture puts in the scene is named with this prefix, and the sweep in
        // TearDown only touches names that carry it. EditMode tests run inside whatever scene the
        // editor happens to have open, so a tidy-up that matched "(Clone)" would delete the user's
        // own objects.
        private const string FixturePrefix = "SGTestBullet_";

        private readonly List<GameObject> spawned = new();
        private readonly List<ScriptableObject> assets = new();

        private GameObject NewObject(string name = "agent")
        {
            var go = new GameObject(name);
            spawned.Add(go);
            return go;
        }

        [TearDown]
        public void TearDown()
        {
            // Clones made by PresentShot are not in `spawned`, so sweep the scene for them first.
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
                if (root != null && root.name.StartsWith(FixturePrefix)) UnityEngine.Object.DestroyImmediate(root);

            foreach (GameObject go in spawned)
                if (go != null) UnityEngine.Object.DestroyImmediate(go);

            foreach (ScriptableObject asset in assets)
                if (asset != null) UnityEngine.Object.DestroyImmediate(asset);

            spawned.Clear();
            assets.Clear();
            LogAssert.ignoreFailingMessages = false;
        }

        // ─────────── The ray the authority aimed ───────────

        [Test]
        public void TheAimSurvivesTheRoundTrip()
        {
            var origin = new Vector3(12.5f, 3.25f, -40f);
            Vector3 aim = new Vector3(0.4f, -0.2f, 1f).normalized;

            NetArg arg = AgentActionRelay.Describe(AgentAction.Ranged, origin, aim, Quaternion.identity);

            Assert.IsTrue(AgentActionRelay.TryReadRay(in arg, out Vector3 readOrigin, out Vector3 readAim));
            Assert.AreEqual(origin, readOrigin);
            Assert.Less(Vector3.Angle(aim, readAim), 0.1f,
                "P and R are a ray, and a watcher draws the tracer straight down it. An encode that " +
                "quietly inverts or swings an axis is a bullet leaving the barrel sideways, and " +
                "nothing on the deciding machine would ever notice.");
        }

        [Test]
        public void AVerticalShotIsNotFlattenedIntoAForwardOne()
        {
            NetArg up = AgentActionRelay.Describe(AgentAction.Ranged, Vector3.zero, Vector3.up, Quaternion.identity);
            NetArg down = AgentActionRelay.Describe(AgentAction.Ranged, Vector3.zero, Vector3.down, Quaternion.identity);

            Assert.IsTrue(AgentActionRelay.TryReadRay(in up, out _, out Vector3 readUp));
            Assert.IsTrue(AgentActionRelay.TryReadRay(in down, out _, out Vector3 readDown));

            // Quaternion.LookRotation orthonormalizes against its up hint and collapses when the
            // two are parallel, answering identity — which points +Z. A creature shooting at
            // something directly overhead would have had every watcher fire horizontally instead.
            Assert.Less(Vector3.Angle(Vector3.up, readUp), 0.1f);
            Assert.Less(Vector3.Angle(Vector3.down, readDown), 0.1f);
        }

        [Test]
        public void ADegenerateDirectionFallsBackToTheAgentsOwnFacing()
        {
            Quaternion facing = Quaternion.Euler(0f, 137f, 0f);

            NetArg arg = AgentActionRelay.Describe(AgentAction.Melee, Vector3.one, Vector3.zero, facing);

            Assert.IsTrue(arg.HasOrientation,
                "A target standing exactly on top of the agent leaves no direction to encode. " +
                "Leaving R all-zero there would make HasOrientation read the whole message as " +
                "unfilled, and the swing would go unseen rather than merely un-aimed.");
            Assert.IsTrue(AgentActionRelay.TryReadRay(in arg, out _, out Vector3 aim));
            Assert.Less(Vector3.Angle(facing * Vector3.forward, aim), 0.1f);
        }

        [Test]
        public void AMessageWithNoAimIsDroppedRatherThanGuessedAt()
        {
            Assert.IsFalse(AgentActionRelay.TryReadRay(default, out _, out _),
                "A watcher with no ray must draw nothing. Falling back to its own copy of the " +
                "world is exactly the behaviour this message was written to replace.");
        }

        // ─────────── The kind ───────────

        [Test]
        public void TheKindTravelsInA()
        {
            Assert.AreEqual(AgentAction.Melee,
                AgentActionRelay.Describe(AgentAction.Melee, Vector3.zero, Vector3.forward, Quaternion.identity).A);
            Assert.AreEqual(AgentAction.Ranged,
                AgentActionRelay.Describe(AgentAction.Ranged, Vector3.zero, Vector3.forward, Quaternion.identity).A);

            Assert.AreNotEqual(AgentAction.Melee, AgentAction.Ranged,
                "Both combat modules listen on the same channel and tell their message from the " +
                "other one by this field alone.");
        }

        [Test]
        public void BothCombatModulesListenOnTheSameChannel()
        {
            GameObject agent = NewObject("bot with both");
            var melee = agent.AddComponent<CloseCombatModule>();
            var ranged = agent.AddComponent<AgentRangedCombatModule>();

            Enable(melee);
            Enable(ranged);

            Assert.AreEqual(2, HandlerCount(agent, NetMsg.AgentActed),
                "DeathmatchBot and PatrolRobot carry both. Every AgentActed reaches both handlers, " +
                "which is why each one filters on NetArg.A before drawing anything — a sword swing " +
                "must not put a bullet in the air.");
        }

        // ─────────── Single-player ───────────

        [Test]
        public void SinglePlayerPutsNothingOnTheWire()
        {
            GameObject agent = NewObject();
            var probe = agent.AddComponent<Probe>();
            probe.NetOn(NetMsg.AgentActed, probe.Record);

            AgentActionRelay.Broadcast(probe, AgentAction.Melee, Vector3.zero, Vector3.forward);

            Assert.AreEqual(0, probe.Calls,
                "Offline the authority has already drawn the swing itself. A send that fell through " +
                "to a local dispatch would present it a second time, which is the one way this " +
                "change could have altered the solo game.");
        }

        [Test]
        public void BroadcastingAboutNothingIsSafe()
        {
            Assert.DoesNotThrow(() =>
                AgentActionRelay.Broadcast(null, AgentAction.Ranged, Vector3.zero, Vector3.forward));
        }

        // ─────────── Subscriptions ───────────

        [Test]
        public void MeleeSubscribesAndUnsubscribes()
        {
            GameObject agent = NewObject("melee");
            var module = agent.AddComponent<CloseCombatModule>();

            Enable(module);
            Assert.AreEqual(1, HandlerCount(agent, NetMsg.AgentActed));

            Disable(module);
            Assert.AreEqual(0, HandlerCount(agent, NetMsg.AgentActed),
                "NetAuthority switches components off and on as ownership moves. A subscription " +
                "that outlived its disable would present the same swing once per re-enable.");
        }

        [Test]
        public void RangedSubscribesAndUnsubscribes()
        {
            GameObject agent = NewObject("ranged");
            var module = agent.AddComponent<AgentRangedCombatModule>();

            Enable(module);
            Assert.AreEqual(1, HandlerCount(agent, NetMsg.AgentActed));

            Disable(module);
            Assert.AreEqual(0, HandlerCount(agent, NetMsg.AgentActed));
        }

        [Test]
        public void NeitherTurretListensForAgentActed()
        {
            GameObject turret = NewObject("turret");
            var module = turret.AddComponent<TurretModule>();
            Enable(module);

            GameObject launcher = NewObject("rocket launcher");
            var rocket = launcher.AddComponent<RocketLauncherTurret>();
            Enable(rocket);

            Assert.AreEqual(0, HandlerCount(turret, NetMsg.AgentActed));
            Assert.AreEqual(0, HandlerCount(launcher, NetMsg.AgentActed),
                "Both already run their own Update on every machine and already mark the shells " +
                "they did not authorise cosmetic, so every peer draws the arc for itself. Adding " +
                "AgentActed on top without first gating Fire() would put two shells in the air per " +
                "shot on every watcher.");
        }

        // ─────────── The deciding machine must not present twice ───────────

        [Test]
        public void TheDecidingMachineIgnoresItsOwnMeleeBroadcast()
        {
            // Warnings only: no AudioCatalog and no FMOD banks in a test scene. That is the fixture
            // complaining about itself, not about the rule under test.
            LogAssert.ignoreFailingMessages = true;

            GameObject agent = NewObject("melee");
            var module = agent.AddComponent<CloseCombatModule>();
            PlantAuthority(module);

            int presented = 0;
            module.OnAttackEvent += () => presented++;

            // Proves the presentation half is reachable at all before asserting that the handler
            // did not reach it — otherwise a renamed event would make the assertion below pass for
            // the wrong reason.
            Invoke(module, "PresentSwing", Vector3.zero);
            Assert.AreEqual(1, presented, "Fixture cannot present a swing at all.");

            NetArg arg = AgentActionRelay.Describe(AgentAction.Melee, Vector3.zero, Vector3.forward, Quaternion.identity);
            Invoke(module, "OnAgentActed", arg, NetworkManager.ServerClientId);

            Assert.AreEqual(1, presented,
                "Offline and on the host, AgentAuthority answers 'I decide' — and the deciding " +
                "machine drew the swing while making it. Presenting again would double every " +
                "sound and re-trigger the animation mid-swing.");
        }

        [Test]
        public void TheDecidingMachineIgnoresItsOwnRangedBroadcast()
        {
            // Warnings only: the fixture has no AudioCatalog and no FMOD banks, which is the test
            // environment complaining about itself rather than about the rule under test.
            LogAssert.ignoreFailingMessages = true;

            (AgentRangedCombatModule module, string cloneName) = NewRangedModule();

            // Proves the fixture can actually fire before asserting that it did not — otherwise a
            // null weapon or a null prefab would make the assertion below pass for the wrong reason.
            Invoke(module, "PresentShot", Vector3.zero, Vector3.zero, Vector3.forward, true);
            Assert.AreEqual(1, CloneCount(cloneName), "Fixture is not wired to fire at all.");

            NetArg arg = AgentActionRelay.Describe(AgentAction.Ranged, Vector3.zero, Vector3.forward, Quaternion.identity);
            Invoke(module, "OnAgentActed", arg, NetworkManager.ServerClientId);

            Assert.AreEqual(1, CloneCount(cloneName),
                "The deciding machine already put this bullet in the air. A second one from its " +
                "own broadcast is two tracers per shot — and only Cosmetic keeps it from also " +
                "being two hits.");
        }

        // ─────────── Fixture ───────────

        /// <summary>
        /// A ranged module wired well enough to fire, with its authority resolved by hand.
        ///
        /// AddComponent does not run Awake outside play mode, so every field Awake would have
        /// resolved has to be planted — including the AgentAuthority, or the handler's guard would
        /// short-circuit on null and the test would pass without ever exercising the rule.
        /// </summary>
        private (AgentRangedCombatModule, string) NewRangedModule()
        {
            GameObject agent = NewObject("ranged");
            var module = agent.AddComponent<AgentRangedCombatModule>();
            PlantAuthority(module);

            GameObject bullet = NewObject(FixturePrefix + Guid.NewGuid().ToString("N"));

            var definition = ScriptableObject.CreateInstance<AgentWeaponDefinition>();
            definition.projectilePrefab = bullet;
            definition.fireId = SpaceGame.Audio.SfxId.None;   // keeps the audio catalog out of it
            assets.Add(definition);

            Plant(module, "weapon", definition);

            return (module, bullet.name + "(Clone)");
        }

        private static void PlantAuthority(Component module) =>
            Plant(module, "authority", new AgentAuthority(module));

        private static void Plant(Component target, string fieldName, object value)
        {
            FieldInfo field = target.GetType()
                .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNotNull(field, $"{target.GetType().Name}.{fieldName} was renamed; this test plants it directly.");
            field.SetValue(target, value);
        }

        private static void Invoke(Component target, string methodName, params object[] args)
        {
            MethodInfo method = target.GetType()
                .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNotNull(method, $"{target.GetType().Name}.{methodName} was renamed or removed.");
            method.Invoke(target, args);
        }

        // Unity does not run OnEnable/OnDisable on AddComponent outside play mode, and the
        // subscription pairing is precisely what these tests are about.
        private static void Enable(Component module) => TryInvoke(module, "OnEnable");
        private static void Disable(Component module) => TryInvoke(module, "OnDisable");

        private static void TryInvoke(Component target, string methodName)
        {
            MethodInfo method = target.GetType()
                .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);

            method?.Invoke(target, Array.Empty<object>());
        }

        /// <summary>How many handlers the entity's channel holds for one message id.</summary>
        private static int HandlerCount(GameObject entity, ushort id)
        {
            NetChannel channel = NetChannel.Find(entity.transform);
            if (channel == null) return 0;

            FieldInfo field = typeof(NetChannel)
                .GetField("handlers", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "NetChannel.handlers was renamed; this test reads it directly.");

            var table = (Dictionary<ushort, List<NetHandler>>)field.GetValue(channel);
            return table.TryGetValue(id, out List<NetHandler> list) ? list.Count : 0;
        }

        private static int CloneCount(string cloneName)
        {
            int count = 0;
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
                if (root != null && root.name == cloneName) count++;

            return count;
        }

        /// <summary>A stand-in for anything else that might be listening on the agent's channel.</summary>
        private class Probe : MonoBehaviour
        {
            public int Calls;

            public void Record(in NetArg arg, ulong sender) => Calls++;
        }
    }
}
