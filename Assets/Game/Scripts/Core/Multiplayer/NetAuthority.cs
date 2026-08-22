// Makes one entity server-simulated.
//
// Without this every client runs its own private copy of every AI, creature and vehicle: they walk
// different paths, shoot at different targets, and damage applied on one machine is invisible on
// the others. The fix is not to replicate the decisions — it is to stop making them anywhere but
// the server, and let the NetworkTransform/NetworkAnimator already on the prefab carry the result.
//
// What gets switched off is only the layer that DRIVES motion. Animators, renderers, colliders,
// audio and every visual component stay on, because a remote copy still has to look right while
// something else moves it.
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using SpaceGame.Agents;
using SpaceGame.Locomotion;

namespace SpaceGame.Core
{
    [DisallowMultipleComponent]
    public sealed class NetAuthority : NetworkBehaviour
    {
        // Behaviour, not MonoBehaviour: NavMeshAgent is a built-in Behaviour and is exactly the
        // kind of thing that must be switched off on a remote copy, so a MonoBehaviour list would
        // silently drop it the moment anyone filled the list in.
        [Tooltip("The behaviours that drive this entity's simulation. Disabled on machines that do " +
                 "not own it. Leave empty and they are discovered automatically — use the context " +
                 "menu to fill the list in, then edit it.\n\n" +
                 "Worth editing for procedurally animated rigs: a legged walker's locomotion both " +
                 "moves the body AND solves the legs, so switching it off leaves a remote copy " +
                 "sliding with still feet. Take it out of this list and the legs keep solving " +
                 "against the replicated body position, which is what you want to see.")]
        [SerializeField] private List<Behaviour> simulationDrivers = new();

        [Tooltip("Make the Rigidbody kinematic on remote machines, so the replicated transform is " +
                 "not fought by local physics. Turn off for entities whose motion is meant to be " +
                 "predicted locally.")]
        [SerializeField] private bool freezePhysicsOnRemote = true;

        private readonly List<Behaviour> disabled = new();
        private bool wasKinematic;
        private Rigidbody body;

        /// <summary>
        /// True when this machine is the one deciding what this entity does.
        ///
        /// Ownership, not server-ness. An AI is owned by the server, so the server drives it and
        /// every client watches — but a mount is handed to whoever climbs on, and that rider has
        /// to be able to steer the thing they are sitting on. One rule covers both, and
        /// <see cref="Network.Owns"/> answers true offline and for anything unnetworked.
        ///
        /// Deliberately NOT called HasAuthority: NetworkBehaviour already has a property by that
        /// name, and shadowing it means the answer you get depends on the static type of the
        /// reference you happened to be holding. This one also answers for un-networked and
        /// offline entities, which the base property does not.
        /// </summary>
        public bool IsSimulatedHere => Network.Owns(this);

        // Offline never fires OnNetworkSpawn, and an entity that is only ever single-player must
        // still end up in the "I simulate this" state rather than half-configured.
        private void Start() => Refresh();

        public override void OnNetworkSpawn() => Refresh();

        // Ownership moves while the entity is alive — every time somebody mounts or dismounts. The
        // drivers have to follow it, or the new rider steers a body that is switched off and the
        // previous one keeps simulating a body that is no longer theirs.
        public override void OnGainedOwnership() => Refresh();

        public override void OnLostOwnership() => Refresh();

        // Handing the entity back to local simulation on despawn is what keeps a client that loses
        // the session from being left with a frozen, un-drivable world.
        public override void OnNetworkDespawn() => Restore();

        /// <summary>
        /// Put the entity into whatever state this machine's authority calls for.
        ///
        /// Idempotent, and it has to be: Start and OnNetworkSpawn race — an instantiated-then-
        /// spawned prefab runs Start first, when the object is not spawned yet and therefore looks
        /// unowned-and-therefore-ours — and ownership changes again every time somebody mounts.
        /// An earlier version guarded with a ran-once flag, which meant the Start that arrived
        /// first won and every client happily kept simulating its own private copy.
        /// </summary>
        private void Refresh()
        {
            // Always undo first. Suppress records what it switched off so it can put it back;
            // running it twice would record the suppressed state as the original.
            Restore();
            if (IsSimulatedHere) return;

            // Before the drivers, because a kinematic posing layer is not one and must not be
            // treated as one. A legged machine's locomotion both MOVES the body and SOLVES the
            // legs: switch it off and the remote copy slides along with still feet, leave it on and
            // it overwrites the replicated pose in LateUpdate every frame — so the remote copy
            // never moves at all while the real machine walks away. That second failure is what
            // made a mounted ostrich vanish out from under its rider on every other machine.
            // Neither switch is right; it has to follow the wire instead.
            SetExternallyPosed(true);

            foreach (Behaviour driver in ResolveDrivers())
            {
                if (driver == null || !driver.enabled) continue;
                driver.enabled = false;
                disabled.Add(driver);
            }

            if (!freezePhysicsOnRemote) return;

            body = GetComponent<Rigidbody>();
            if (body == null) return;

            wasKinematic = body.isKinematic;
            body.isKinematic = true;
        }

        private void Restore()
        {
            foreach (Behaviour driver in disabled)
                if (driver != null) driver.enabled = true;

            disabled.Clear();

            // Unconditional rather than remembered, because unlike the drivers there is nothing to
            // remember: owning your own transform is the only state a posing layer is ever authored
            // in, and this is the sole thing that ever moves it off that. Handing it back on despawn
            // is what stops a client who loses the session being left with a frozen world.
            SetExternallyPosed(false);

            if (body != null) body.isKinematic = wasKinematic;
            body = null;
        }

        /// <summary>
        /// Tell every kinematic posing layer on this entity whether it still owns its transform.
        ///
        /// Filtered by <see cref="BelongsTo"/> for the same reason the drivers are: a rider is
        /// parented INTO their mount while mounted, so an unfiltered sweep would reach down into
        /// the player sitting on the saddle.
        /// </summary>
        private void SetExternallyPosed(bool value)
        {
            foreach (MonoBehaviour behaviour in GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour is not IExternallyPosed posed) continue;
                if (!BelongsTo(gameObject, behaviour)) continue;

                posed.ExternallyPosed = value;
            }
        }

        /// <summary>
        /// The serialized list when there is one, otherwise whatever discovery finds now.
        ///
        /// Discovery at runtime rather than a hard requirement to fill the list, so adding this
        /// component to a prefab is the only step — an entity that half-simulates because someone
        /// forgot to press a button is the failure this avoids.
        /// </summary>
        private IEnumerable<Behaviour> ResolveDrivers()
        {
            if (simulationDrivers.Count > 0)
            {
                foreach (Behaviour driver in simulationDrivers)
                    yield return driver;
                yield break;
            }

            foreach (Behaviour driver in Discover(gameObject))
                yield return driver;
        }

        /// <summary>
        /// Everything on <paramref name="root"/> that moves it of its own accord: the agent brain,
        /// its motor, and the NavMeshAgent underneath both.
        ///
        /// Matched by interface and base type, never by name, so a new motor or a renamed
        /// controller is covered the day it is written.
        /// </summary>
        public static List<Behaviour> Discover(GameObject root)
        {
            var found = new List<Behaviour>();
            if (root == null) return found;

            foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null || !BelongsTo(root, behaviour)) continue;

                if (behaviour is AgentController or IMovementMotor)
                    found.Add(behaviour);
            }

            foreach (NavMeshAgent agent in root.GetComponentsInChildren<NavMeshAgent>(true))
                if (BelongsTo(root, agent)) found.Add(agent);

            return found;
        }

        /// <summary>
        /// Is <paramref name="candidate"/> part of <paramref name="root"/>'s own entity, rather
        /// than a separate one that happens to be parented under it?
        ///
        /// A rider is parented into their mount's hierarchy while mounted, so without this a
        /// mount's remote copy would reach down and switch off the player sitting on it — and,
        /// worse, switch them back on again when the rider dismounted, handing a remote player's
        /// controls to the wrong machine. The boundary between two entities is a NetworkObject.
        /// </summary>
        private static bool BelongsTo(GameObject root, Component candidate)
        {
            NetworkObject owner = candidate.GetComponentInParent<NetworkObject>();
            NetworkObject mine = root.GetComponentInParent<NetworkObject>();

            // Neither is networked: the old whole-hierarchy behaviour, which is right for a plain
            // prefab whose parts are all one thing.
            return owner == null || mine == null || owner == mine;
        }

#if UNITY_EDITOR
        [ContextMenu("Refresh simulation drivers")]
        private void RefreshDrivers()
        {
            simulationDrivers.Clear();
            simulationDrivers.AddRange(Discover(gameObject));
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}
