// The cached answer to "may this machine run this agent's decisions".
//
// Network.Simulates and Network.Owns already answer that question, and they are the right answer
// everywhere it is asked once: a use, a hit, a spawn. They are the wrong answer for an agent,
// because they call GetComponentInParent<NetworkObject>() on every invocation and an agent asks
// once per frame — times the brain, the targeting and the motor, times every creature in a
// streamed world. That is a managed→native hierarchy walk per agent per frame to re-derive a
// component reference that has not moved since Awake.
//
// So this caches the NetworkObject, never the boolean. IsSpawned and IsOwner are plain property
// reads, and reading them live is what makes the answer immune to going stale: ownership moves
// every time somebody mounts or dismounts, and an entity can be spawned and despawned under us
// without any event this class would have to subscribe to. The only thing that can invalidate the
// cached reference is REPARENTING — a creature carried onto a walker's deck acquires a different
// NetworkObject above it — and that is what Invalidate is for.
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Core;

namespace SpaceGame.Agents
{
    /// <summary>
    /// One agent component's cached view of whether this machine drives the entity it is on.
    ///
    /// Hold one per component in a field, built in Awake. It is deliberately not a static helper
    /// with a lookup table: the cache belongs to the component whose frames it saves, and dies
    /// with it.
    /// </summary>
    public sealed class AgentAuthority
    {
        private readonly Component entity;

        private NetworkObject netObj;
        private bool resolved;

        public AgentAuthority(Component entity)
        {
            this.entity = entity;
        }

        /// <summary>
        /// Is this machine the one deciding what the entity does?
        ///
        /// <para>
        /// Ownership rather than server-ness, and the distinction is load-bearing. An AI is owned
        /// by the server, so the server decides and every client watches. A mount is handed to
        /// whoever climbs on, and that rider's SteerModule has to be able to drive it — gating the
        /// module stack on <see cref="Network.Server"/> instead would mean a client could mount a
        /// walker and then not be able to steer it. This is the same rule
        /// <see cref="NetAuthority.IsSimulatedHere"/> applies, deliberately, so an entity carrying
        /// both cannot end up half-simulated on two different machines.
        /// </para>
        /// <para>
        /// True offline, and true for an entity with no NetworkObject: an unnetworked thing has no
        /// remote truth to defer to, so every machine running its own copy is the best available
        /// answer and refusing would freeze it solid. That is <see cref="Network.Owns"/>'s
        /// contract and this must keep matching it — AgentAuthorityTests asserts the two agree.
        /// </para>
        /// </summary>
        public bool SimulatedHere
        {
            get
            {
                // Checked before the cache is even consulted, so single-player never walks a
                // hierarchy at all. It also means an agent that existed before the session started
                // resolves on the first frame after it starts rather than caching a pre-session
                // answer forever.
                if (!Network.IsNetworked) return true;

                if (!resolved) Resolve();

                // Fake-null covers a NetworkObject destroyed under us — a torn-down session leaves
                // the agent to run itself, which is what single-player looks like.
                if (netObj == null || !netObj.IsSpawned) return true;

                return netObj.IsOwner;
            }
        }

        /// <summary>
        /// Forget which NetworkObject this entity sits under and look again on the next question.
        ///
        /// Call it from OnTransformParentChanged. Nothing else moves an entity between
        /// NetworkObjects, and a miss is cached like a hit: an agent whose prefab has no
        /// NetworkObject does not grow one at runtime, and re-searching for it every frame would
        /// reintroduce exactly the cost this class exists to remove.
        /// </summary>
        public void Invalidate()
        {
            resolved = false;
            netObj = null;
        }

        private void Resolve()
        {
            netObj = entity != null ? entity.GetComponentInParent<NetworkObject>() : null;
            resolved = true;
        }
    }
}
