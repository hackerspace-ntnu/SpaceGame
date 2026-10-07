using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using SpaceGame.Teleporting;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Places an object at a saved position without the movement systems undoing it.
    ///
    /// Assigning <c>transform.position</c> is not enough for anything the player or an agent drives.
    /// A CharacterController caches its own internal position and writes it back over the transform
    /// on its next Move, so the object snaps home within a frame. A NavMeshAgent does the same from
    /// its own agent position, and additionally has to be told to re-find the mesh — an agent moved
    /// off its current polygon simply stops. A Rigidbody keeps whatever momentum it had, so a body
    /// restored mid-air carries the fall it was in when the save was written.
    ///
    /// Every one of those is a real way a load "worked" and then quietly put the player back where
    /// they started.
    ///
    /// The Rigidbody is the one that had to be learned twice. Physics.autoSyncTransforms is false,
    /// so writing transform.position does not reach the body PhysX simulates: it keeps the pose it
    /// last stepped and restores it, and an interpolated body does it a frame sooner because
    /// interpolation drives the transform from that pose every frame. UnderTerrainGuard resyncs its
    /// bodies by hand for exactly this reason; this path did not, which is why a respawn resolved
    /// the spawn point inside the ship, wrote it, and left the player standing on their own grave.
    ///
    /// Despite the name this is not only the save system's. It is the ONE function in this project
    /// that moves an object instantly, and every feature that teleports anything — the portal gun,
    /// respawn, interior transitions, the chat command, the under-terrain failsafe — already goes
    /// through it. That is what makes <see cref="ITeleportAware"/> worth having and why it is
    /// raised from here rather than from any of them: a system with world-space state declares its
    /// own need once, and then EVERY way of being teleported satisfies it.
    /// </summary>
    public static class SaveTeleport
    {
        /// <param name="zeroVelocity">
        /// Whether to stop the body on arrival. True for placement — a body teleported while
        /// carrying the velocity it had at its old position keeps travelling in a direction that no
        /// longer means anything. False when a <see cref="RigidbodySaveable"/> on the same object is
        /// about to restore the real velocity, since the two savers run in component order and
        /// neither may depend on winning that race.
        /// </param>
        public static void Move(GameObject target, Vector3 position, Quaternion rotation, bool zeroVelocity = true)
        {
            if (target == null) return;

            // Read before anything writes: the transfer handed to listeners is derived from where
            // the object actually was and where it actually ended up, which is the only description
            // of the move that cannot disagree with the move.
            Transform self = target.transform;
            Vector3 from = self.position;
            Quaternion fromRotation = self.rotation;

            var controller = target.GetComponent<CharacterController>();
            var agent = target.GetComponent<NavMeshAgent>();

            bool controllerWasEnabled = controller != null && controller.enabled;
            if (controllerWasEnabled) controller.enabled = false;

            // Warp, not transform assignment: it moves the agent's own position too, which is
            // what the agent actually navigates from.
            //
            // Its RETURN VALUE is the whole story, and it used to be discarded. Warp refuses a
            // destination it cannot map onto a navigation polygon, and refuses it in silence — no
            // error, no exception, and the agent left standing exactly where it was. Asking
            // `agent.isOnNavMesh` afterwards does not catch it either: the agent is still on the
            // mesh it never left, so the question meant as "did the warp work" answers yes
            // precisely when it did not.
            //
            // That is not an exotic case. A portal cut into a wall puts its far side at whatever
            // height it was shot at, and the navigation mesh is on the floor — so every creature
            // that walked into an aperture was rotated to face out of the exit and then left
            // standing where it started, which on screen is a creature ignoring the portal
            // entirely. DeferredNavMeshWarp already said this in as many words about its own
            // retry; the two callers of Warp did not.
            bool placed = agent != null && agent.enabled && agent.isOnNavMesh && agent.Warp(position);

            if (placed)
            {
                target.transform.rotation = rotation;
            }
            else
            {
                target.transform.SetPositionAndRotation(position, rotation);

                // Reached either because the agent is not on a mesh right now, or because the warp
                // was refused. Both want the same thing: put the GameObject where it belongs, and
                // keep trying to bring the agent's own position after it.
                //
                // An agent that is not on a mesh right now is the ordinary case during chunk
                // hydration, not an exceptional one: this runs from WorldStreamer.OnChunkLoaded,
                // and a chunk's NavMesh is frequently not available yet. The transform write above
                // is a real placement, but the AGENT's own position is still wherever it was, so
                // the first thing the agent does is stop or walk back — which is the "load worked
                // and then quietly put it back where it started" failure this class exists to
                // prevent, arriving through the one branch that did not handle it.
                //
                // So the warp is retried until the mesh shows up, rather than skipped in silence.
                if (agent != null && agent.enabled)
                    DeferredNavMeshWarp.Schedule(agent, position, rotation);
            }

            if (controllerWasEnabled) controller.enabled = true;

            PlaceBodies(target, zeroVelocity);
            Announce(target, from, fromRotation);
        }

        /// <summary>
        /// Tell everything under <paramref name="target"/> that keeps world-space state where that
        /// state has to move to. See <see cref="ITeleportAware"/>.
        ///
        /// Last, and deliberately so: by the time this runs the transform is final and every body
        /// under it has been resynced, so a listener that reads its own transform reads the truth.
        /// </summary>
        private static void Announce(GameObject target, Vector3 from, Quaternion fromRotation)
        {
            Transform self = target.transform;
            Vector3 to = self.position;
            Quaternion toRotation = self.rotation;

            // A resync is not a teleport. NetAuthority and NetworkPlayerController call Move with
            // the pose the object already has, purely to push it into PhysX, and they do it on
            // ownership changes rather than rarely. Waking every listener to rebase its world state
            // by an identity transform would be pure cost, and the float noise in "identity" is
            // exactly the kind of thing that walks a foothold a millimetre at a time.
            if ((to - from).sqrMagnitude < StillThere * StillThere &&
                Quaternion.Angle(fromRotation, toRotation) < StillFacing)
                return;

            // A fresh list rather than a shared scratch buffer: a listener is entitled to teleport
            // something else — a mount carrying a rider is the obvious case — and a static buffer
            // turns that into a silently truncated notification list two levels up. Teleports are
            // rare events, not a per-frame path, so the allocation buys real safety for nothing.
            var listeners = new List<ITeleportAware>();
            target.GetComponentsInChildren(true, listeners);
            if (listeners.Count == 0) return;

            var move = new TeleportMove(from, fromRotation, to, toRotation);
            for (int i = 0; i < listeners.Count; i++) listeners[i].OnTeleported(in move);
        }

        /// <summary>Below this much movement, and this much turn, a Move is a resync rather than a teleport.</summary>
        private const float StillThere = 0.0001f;
        private const float StillFacing = 0.01f;

        /// <summary>
        /// Puts every Rigidbody under <paramref name="target"/> where its transform now is.
        ///
        /// Every body, not just the root's own: moving a transform moves its children, but PhysX
        /// holds each body's pose independently. On a single-body prefab resyncing only the root
        /// costs nothing; on an articulated one — a walker's legs, a rover's bogies and wheels —
        /// it drags the chassis away from parts that stayed behind and pulls the joints apart.
        /// Empty is a normal case: the DuneFoil has no Rigidbody anywhere.
        /// </summary>
        private static void PlaceBodies(GameObject target, bool zeroVelocity)
        {
            foreach (Rigidbody body in target.GetComponentsInChildren<Rigidbody>(true))
            {
                if (body == null) continue;

                // Interpolation is switched off across the write and put back after it. An
                // interpolated body blends the transform from the poses it has already simulated,
                // so leaving it on means the frame after a teleport is spent travelling back toward
                // where the body came from. Restoring the setting matters as much as clearing it —
                // a body left on None never smooths again.
                RigidbodyInterpolation interpolation = body.interpolation;
                if (interpolation != RigidbodyInterpolation.None)
                    body.interpolation = RigidbodyInterpolation.None;

                body.position = body.transform.position;
                body.rotation = body.transform.rotation;

                if (interpolation != RigidbodyInterpolation.None)
                    body.interpolation = interpolation;

                if (zeroVelocity && !body.isKinematic)
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }
            }
        }
    }
}
