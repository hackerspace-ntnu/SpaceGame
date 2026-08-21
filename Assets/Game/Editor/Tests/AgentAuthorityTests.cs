// Tests for the rule that decides which machine an NPC actually runs on.
//
// Same constraint as NetMessagingTests and NetAuthorityAndDamageTests: there is no session here, so
// what can be tested is the shape of the decision rather than a live handshake. That is enough,
// because every bug this covers is a rule that was missing rather than a packet that went astray —
// an agent stack with no authority check anywhere in it, and a dismount handler that never asked
// who was talking. Both are provable without a wire.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;
using SpaceGame.Agents;
using SpaceGame.Core;

namespace SpaceGame.Tests
{
    public class AgentAuthorityTests
    {
        private readonly List<GameObject> spawned = new();

        private GameObject NewObject(string name = "agent")
        {
            var go = new GameObject(name);
            spawned.Add(go);
            return go;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned)
                if (go != null) UnityEngine.Object.DestroyImmediate(go);

            spawned.Clear();
            LogAssert.ignoreFailingMessages = false;
        }

        // ─────────── The cached authority answer ───────────

        [Test]
        public void CachedAuthorityAgreesWithTheUncachedOne()
        {
            GameObject entity = NewObject();
            var authority = new AgentAuthority(entity.transform);

            Assert.AreEqual(Network.Owns(entity.transform), authority.SimulatedHere,
                "AgentAuthority exists only to make Network.Owns cheap enough to ask every frame. " +
                "The moment the two disagree it has stopped being a cache and started being a " +
                "second, quieter rule.");
        }

        [Test]
        public void AnUnnetworkedAgentIsSimulatedEverywhere()
        {
            GameObject entity = NewObject();

            Assert.IsTrue(new AgentAuthority(entity.transform).SimulatedHere,
                "An entity with no NetworkObject has no remote truth to defer to. Refusing to " +
                "simulate it would freeze every creature in a scene opened straight from the editor.");
        }

        [Test]
        public void AuthorityQuestionsAboutNothingAreSafe()
        {
            var authority = new AgentAuthority(null);

            Assert.IsTrue(authority.SimulatedHere);
            Assert.DoesNotThrow(() => authority.Invalidate());
            Assert.IsTrue(authority.SimulatedHere, "Invalidating must re-resolve, not break.");
        }

        [Test]
        public void InvalidatingReResolvesRatherThanLatching()
        {
            GameObject entity = NewObject();
            var authority = new AgentAuthority(entity.transform);

            bool before = authority.SimulatedHere;
            authority.Invalidate();

            Assert.AreEqual(before, authority.SimulatedHere,
                "A reparent invalidates the cached NetworkObject lookup. The answer must come back " +
                "the same when nothing about the entity changed.");
        }

        [Test]
        public void SinglePlayerAgentsStillSimulateLocally()
        {
            GameObject entity = NewObject();
            var controller = entity.AddComponent<AgentController>();
            var targeting = entity.AddComponent<AgentTargeting>();

            // Single-player runs as a host, and offline is what a host looks like before it starts.
            // Both of these gate their per-frame work on this property, so a false here is the whole
            // solo game standing still.
            Assert.IsTrue(controller.SimulatesHere);
            Assert.IsTrue(targeting.SimulatesHere);
        }

        // ─────────── What a watching machine is still allowed to run ───────────

        [Test]
        public void EveryPresentationModuleIsAlsoABehaviourModule()
        {
            foreach (Type type in PresentationModuleTypes())
            {
                Assert.IsTrue(typeof(IBehaviourModule).IsAssignableFrom(type),
                    $"{type.Name} is marked IPresentationModule but is not a behaviour module, so " +
                    "AgentController has no way to tick it and the marker does nothing at all.");
            }
        }

        [Test]
        public void NoPresentationModuleClaimsMovement()
        {
            foreach (Type type in PresentationModuleTypes())
            {
                var host = NewObject(type.Name);
                var module = (IBehaviourModule)host.AddComponent(type);

                Assert.IsFalse(module.ClaimsMovement,
                    $"{type.Name} keeps ticking on machines that do not own the agent. A module " +
                    "that claims movement is steering a body it does not own — and its MoveIntent " +
                    "is discarded there anyway, so the arbitration it wins is a lie.");
            }
        }

        [Test]
        public void ChatterIsMarkedAsPresentation()
        {
            // Not a tautology: it is the one module the split was designed around. The popup is
            // shown to whoever is at THIS machine, so gating it on authority means only the host
            // ever hears a camp talking.
            Assert.IsTrue(typeof(IPresentationModule).IsAssignableFrom(typeof(ChatterModule)));
        }

        private static IEnumerable<Type> PresentationModuleTypes() =>
            typeof(IPresentationModule).Assembly.GetTypes()
                .Where(t => !t.IsAbstract
                            && typeof(MonoBehaviour).IsAssignableFrom(t)
                            && typeof(IPresentationModule).IsAssignableFrom(t));

        // ─────────── Motors that keep going after nobody ticks them ───────────

        [Test]
        public void TheNavMeshMotorCanBeSwitchedOffAndBackOn()
        {
            (NavMeshAgentMotor motor, NavMeshAgent agent) = NewNavMeshMotor();
            agent.enabled = true;

            motor.SuspendSelfDrive();
            Assert.IsFalse(agent.enabled,
                "An enabled NavMeshAgent writes transform.position from its own internal position " +
                "every frame, so a watching machine that merely stops deciding stops tracking the " +
                "server entirely.");

            motor.ResumeSelfDrive();
            Assert.IsTrue(agent.enabled);
        }

        [Test]
        public void ResumingDoesNotSwitchOnAnAgentThatWasAlreadyParked()
        {
            (NavMeshAgentMotor motor, NavMeshAgent agent) = NewNavMeshMotor();

            // The state NavMeshAgentMotor.Awake leaves an agent in when it wakes somewhere with no
            // NavMesh under it. Resuming it would drop the creature through the world.
            agent.enabled = false;

            motor.SuspendSelfDrive();
            motor.ResumeSelfDrive();

            Assert.IsFalse(agent.enabled);
        }

        [Test]
        public void SuspendAndResumeAreIdempotent()
        {
            (NavMeshAgentMotor motor, NavMeshAgent agent) = NewNavMeshMotor();
            agent.enabled = true;

            // Ownership can change on the same frame as a spawn, so both of these are reachable
            // twice in a row — and a second Suspend that recorded "was disabled" would strand the
            // agent switched off for the rest of the session.
            motor.SuspendSelfDrive();
            motor.SuspendSelfDrive();
            motor.ResumeSelfDrive();
            motor.ResumeSelfDrive();

            Assert.IsTrue(agent.enabled);
        }

        private (NavMeshAgentMotor, NavMeshAgent) NewNavMeshMotor()
        {
            GameObject entity = NewObject("navmesh agent");

            // An EditMode scene has no baked NavMesh, so enabling an agent in it complains. The
            // complaint is about the fixture, not about the rule under test — which is purely
            // "does Suspend record what it switched off".
            LogAssert.ignoreFailingMessages = true;

            // AddComponent does not run Awake outside play mode, so the motor's own reference to
            // the agent has to be planted by hand — the same field Awake resolves.
            var agent = entity.AddComponent<NavMeshAgent>();
            var motor = entity.AddComponent<NavMeshAgentMotor>();

            FieldInfo field = typeof(NavMeshAgentMotor)
                .GetField("agent", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "NavMeshAgentMotor.agent was renamed; this test plants it directly.");
            field.SetValue(motor, agent);

            return (motor, agent);
        }

        // ─────────── Who may throw a rider off ───────────

        private const ulong Server = NetworkManager.ServerClientId;
        private const ulong Rider = 7;
        private const ulong Stranger = 9;

        [Test]
        public void ARiderMayDismountThemselves()
        {
            Assert.IsTrue(MountNetworkSync.IsDismountAllowed(Rider, Server, Rider));
        }

        [Test]
        public void NobodyElseMayDismountARider()
        {
            Assert.IsFalse(MountNetworkSync.IsDismountAllowed(Stranger, Server, Rider),
                "Dismount arrives on the MOUNT's channel and every client knows every mount's id, " +
                "so an unchecked handler lets anyone throw anyone off from anywhere on the map.");
        }

        [Test]
        public void TheServerMayAlwaysDismount()
        {
            Assert.IsTrue(MountNetworkSync.IsDismountAllowed(Server, Server, Rider),
                "Deaths, teardowns and restores all dismount riders with nobody having asked.");
            Assert.IsTrue(MountNetworkSync.IsDismountAllowed(Server, Server, null),
                "Offline every send is attributed to the server id, so single-player takes this path.");
        }

        [Test]
        public void AnUnidentifiableRiderIsNotTrappedInTheSeat()
        {
            Assert.IsTrue(MountNetworkSync.IsDismountAllowed(Stranger, Server, null),
                "No rider identity means there is nothing to compare against. Refusing there would " +
                "leave an unnetworked or restored rider unable to get off at all, which is a worse " +
                "bug than the one the check exists to prevent.");
        }

        // ─────────── Animation on a machine that is only watching ───────────

        [Test]
        public void ReplicatedMotionBecomesAVelocity()
        {
            Vector3 measured = AgentAnimatorDriver.MeasureVelocity(new Vector3(0f, 0f, 0.1f), 0.05f);

            Assert.AreEqual(2f, measured.z, 1e-4f,
                "This is the only velocity a watching machine has: the motor is parked and reports " +
                "zero, which is what made remote creatures slide with still feet.");
        }

        [Test]
        public void APlacementIsNotAStride()
        {
            Vector3 measured = AgentAnimatorDriver.MeasureVelocity(new Vector3(0f, 0f, 400f), 0.02f);

            Assert.AreEqual(Vector3.zero, measured,
                "A NetworkTransform teleport, a respawn or a chunk streaming in under the agent " +
                "would otherwise flash a full sprint for one frame.");
        }

        [Test]
        public void AFrameWithNoTimeInItMeasuresNothing()
        {
            Assert.AreEqual(Vector3.zero, AgentAnimatorDriver.MeasureVelocity(Vector3.forward, 0f));
        }
    }
}
