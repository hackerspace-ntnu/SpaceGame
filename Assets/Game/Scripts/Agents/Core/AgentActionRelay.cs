// The one place an NPC's attack goes onto the wire, and the one place a watcher takes it off again.
//
// It exists because gating the agent stack to the authority (see AgentAuthority) took the swings
// and the muzzle flashes off every other machine along with the damage. Before that gate peers DID
// draw them — from their own copy of the brain, at their own copy of the target, on their own
// clock — which is precisely the desync the gate closed. So this is not a new feature; it is the
// honest version of something the divergence was providing by accident.
//
// Shared rather than written once per module because melee and ranged put the identical thing on
// the wire: a kind, an origin and a direction. Two copies of that encode would be two places for
// the direction convention to drift, and a tracer drawn along the wrong axis is the sort of bug
// nobody notices until somebody else is watching.
using UnityEngine;
using SpaceGame.Core;

namespace SpaceGame.Agents
{
    /// <summary>
    /// Encode and decode <see cref="NetMsg.AgentActed"/>.
    ///
    /// <para>
    /// Presentation only, in both directions. Nothing here damages, spawns anything the world
    /// records, or writes state a peer can observe — the deciding machine has already done all of
    /// that by the time <see cref="Broadcast"/> is called, and a watcher acting on the message must
    /// never do any of it. That rule is what keeps this from re-opening the bug it was written for:
    /// NetDamage applies a hit on the server and forwards it as a request from a client, so any
    /// path that lets a watcher deal an NPC's damage bills the target once per machine.
    /// </para>
    /// </summary>
    public static class AgentActionRelay
    {
        /// <summary>
        /// Tell every other machine that <paramref name="agent"/> just attacked, and from where.
        ///
        /// <para>
        /// <paramref name="origin"/> and <paramref name="direction"/> are a RAY, not a landing
        /// point — the same convention <see cref="NetMsg.UseItemHold"/> uses, and for the same
        /// reason: a peer that re-derived the shot from its own copy of the world would draw it
        /// leaving from wherever its own divergent brain happened to be pointing.
        /// </para>
        /// </summary>
        public static void Broadcast(Component agent, int action, Vector3 origin, Vector3 direction)
        {
            if (agent == null) return;

            // Server-only, and asked here rather than left to NetRelay to refuse. AgentActed
            // travels outward and only outward — an NPC's decisions are the authority's alone, so
            // the id has no request direction — which means a machine that is not the server has
            // nobody it is permitted to tell. NetRelay would log a warning for every shot instead
            // of dropping it quietly, and an agent whose NetworkObject has been handed to a client
            // (a creature carried on a player-owned vehicle) would produce one per swing.
            //
            // It is also what makes single-player provably untouched rather than incidentally so:
            // offline Network.Server is false, so the send does not merely no-op at the far end —
            // it never happens, and no NetChannel is walked looking for a relay that is not there.
            if (!Network.Server) return;

            NetArg arg = Describe(action, origin, direction, agent.transform.rotation);

            // Others, never All. This machine already drew the attack as part of performing it, and
            // NetRelay's Others filters the sender out of its own broadcast on arrival — so the
            // authority cannot present the same swing twice, on the host or anywhere else.
            agent.NetToOthers(NetMsg.AgentActed, arg);
        }

        /// <summary>
        /// Pack an attack into a <see cref="NetArg"/>. The exact inverse of
        /// <see cref="TryReadRay"/>, and split out from <see cref="Broadcast"/> so the pair can be
        /// tested against each other without a session — a direction convention that quietly
        /// inverts is a tracer leaving the barrel backwards, which nothing else would catch.
        /// </summary>
        public static NetArg Describe(int action, Vector3 origin, Vector3 direction, Quaternion fallback)
        {
            var arg = new NetArg { A = action, P = origin };

            // Falling back to the agent's own facing rather than leaving R zeroed: an all-zero
            // quaternion is not a rotation, and NetArg.HasOrientation would read it as "nobody
            // filled this in" and make the watcher drop the whole message. A degenerate direction
            // is what a melee target standing exactly on top of the agent produces.
            if (direction.sqrMagnitude <= 1e-6f)
            {
                arg.R = fallback;
                return arg;
            }

            Vector3 aim = direction.normalized;

            // Vector3.up is the wrong hint for a shot going straight up or straight down.
            // LookRotation orthonormalizes the forward axis against it, and parallel inputs
            // collapse that basis — Unity answers with identity, which would send every watcher's
            // tracer off along +Z while the authority's went vertically. A creature firing at
            // something directly overhead is not hypothetical in a world that has flyers in it.
            Vector3 upHint = Mathf.Abs(Vector3.Dot(aim, Vector3.up)) > 0.999f
                ? Vector3.forward
                : Vector3.up;

            arg.R = Quaternion.LookRotation(aim, upHint);
            return arg;
        }

        /// <summary>
        /// Unpack the ray an <see cref="NetMsg.AgentActed"/> message carries.
        ///
        /// <para>
        /// False means the message did not carry an aim, and the caller must draw nothing rather
        /// than substitute a local guess — guessing is the exact behaviour this message replaced.
        /// </para>
        /// </summary>
        public static bool TryReadRay(in NetArg arg, out Vector3 origin, out Vector3 direction)
        {
            origin = arg.P;
            direction = Vector3.zero;

            // HasOrientation separates "the authority told me where it was aiming" from a
            // default-constructed struct, whose all-zero R is not an orientation at all.
            if (!arg.HasOrientation) return false;

            direction = arg.R * Vector3.forward;
            return direction.sqrMagnitude > 1e-6f;
        }
    }
}
