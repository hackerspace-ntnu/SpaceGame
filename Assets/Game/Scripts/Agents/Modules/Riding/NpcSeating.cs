// Putting an NPC somewhere it is carried, and giving it back afterwards: the mechanics shared by
// everything that seats NPCs as cargo — a caravan animal's saddle (NpcPassenger) and a sky
// vessel's deck (VesselSeats).
//
// Two halves, and the split matters:
//
//   • The record (an instance, one per seated NPC): what seating switched off, so exactly that much
//     can be switched back on. Seating takes the NPC's FEET — NavMeshAgent, motors, a kinematic
//     body — and tells its brain it is cargo (AgentController.RidesAsPassenger). It does not take
//     the brain, which is how a passenger keeps shooting, and it does not take the colliders, which
//     is how a passenger stays shootable. See NpcPassenger's header for how both of those were once
//     got wrong.
//   • The placement (static): parenting a spawned NetworkObject under the carrier's own
//     NetworkObject with the seat marker's offset folded into its local space, because netcode
//     refuses a bare marker as a parent.
//
// The body goes through CarriedBody like every other carrier's does, so a nomad seated while
// something else also holds it gets back the body it had before the first hold, not a transient one.
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using SpaceGame.Gameplay;

namespace SpaceGame.Agents
{
    public sealed class NpcSeating
    {
        // Recorded rather than re-derived: a rider whose NavMeshAgent was already disabled by
        // something else must not be handed a working one by getting off.
        private readonly List<Behaviour> suppressed = new();

        // Kept separately from the list above: this is a flag on a component that stays ENABLED.
        private readonly List<AgentController> carried = new();

        // The seated NPC, kept for the body claim and so a teardown can tell a corpse from the living.
        private GameObject seated;

        /// <summary>Switch off everything that would make <paramref name="npc"/> move under its own power.</summary>
        public void Suppress(GameObject npc)
        {
            suppressed.Clear();
            carried.Clear();

            foreach (AgentController controller in npc.GetComponentsInChildren<AgentController>(true))
            {
                if (controller == null || controller.RidesAsPassenger) continue;
                controller.RidesAsPassenger = true;
                carried.Add(controller);
            }

            foreach (NavMeshAgent agent in npc.GetComponentsInChildren<NavMeshAgent>(true))
                Disable(agent);

            // Motors drive the body directly and would fight the seat for the transform.
            foreach (MonoBehaviour behaviour in npc.GetComponentsInChildren<MonoBehaviour>(true))
                if (behaviour is IMovementMotor) Disable(behaviour);

            seated = npc;
            CarriedBody.Hold(npc, this);
        }

        private void Disable(Behaviour behaviour)
        {
            if (behaviour == null || !behaviour.enabled) return;
            behaviour.enabled = false;
            suppressed.Add(behaviour);
        }

        /// <summary>
        /// Give back only what <see cref="Suppress"/> took. A dead NPC gets nothing back:
        /// HealthReactionModule has already switched its brain off, and a working NavMeshAgent stands
        /// the corpse up and walks it away.
        /// </summary>
        /// <param name="navMeshReach">How far to look for NavMesh to put a living NPC's agent on.</param>
        public void Restore(GameObject npc, float navMeshReach)
        {
            bool dead = IsDead(npc);
            GiveBack(dead);

            if (seated != null) CarriedBody.Release(seated, this);
            seated = null;

            if (dead) return;

            // A NavMeshAgent switched on away from the mesh is inert and logs nothing. Warping it is
            // what makes the NPC actually able to walk after getting off.
            if (npc.TryGetComponent(out NavMeshAgent agent) && agent.enabled && !agent.isOnNavMesh &&
                NavMesh.SamplePosition(npc.transform.position, out NavMeshHit hit, navMeshReach, NavMesh.AllAreas))
            {
                agent.Warp(hit.position);
            }
        }

        /// <summary>
        /// Let go of an NPC without putting it anywhere, for when its carrier is being torn down (or
        /// the NPC already was) and moving it is not possible. A living NPC gets its feet and its
        /// brain back where it stands; the body claim is dropped, not restored — see
        /// <see cref="CarriedBody.Abandon"/>. Safe on an empty record and on a destroyed NPC.
        /// </summary>
        public void Abandon()
        {
            GiveBack(seated != null && IsDead(seated));
            CarriedBody.Abandon(this);
            seated = null;
        }

        private void GiveBack(bool dead)
        {
            if (!dead)
            {
                foreach (Behaviour behaviour in suppressed)
                    if (behaviour != null) behaviour.enabled = true;

                foreach (AgentController controller in carried)
                    if (controller != null) controller.RidesAsPassenger = false;
            }

            suppressed.Clear();
            carried.Clear();
        }

        private static bool IsDead(GameObject npc) => npc.TryGetComponent(out HealthComponent health) && !health.Alive;

        /// <summary>
        /// Parent <paramref name="npc"/> to <paramref name="seat"/>, offset by
        /// <paramref name="offset"/>/<paramref name="euler"/> in the seat's space.
        ///
        /// <para>
        /// Spawned on both sides: parented to <paramref name="carrier"/> — the only legal parent —
        /// through netcode, so the arrangement replicates, with the marker's offset folded into the
        /// carrier's local space. Otherwise netcode's parenting rules are in the way rather than
        /// protecting anything: an unspawned NetworkObject refuses a reparent and silently puts the
        /// parent back, which left riders standing in the air where their mount was born.
        /// </para>
        /// </summary>
        public static void Attach(Transform npc, NetworkObject carrier, Transform seat, Vector3 offset, Vector3 euler)
        {
            NetworkObject npcNetObj = npc.GetComponent<NetworkObject>();

            if (npcNetObj != null && npcNetObj.IsSpawned &&
                carrier != null && carrier.IsSpawned &&
                npcNetObj.TrySetParent(carrier, worldPositionStays: true))
            {
                (Vector3 position, Quaternion rotation) = SeatPoseIn(carrier.transform, seat, offset, euler);
                npc.SetLocalPositionAndRotation(position, rotation);
                return;
            }

            if (npcNetObj != null) npcNetObj.AutoObjectParentSync = false;

            npc.SetParent(seat, worldPositionStays: false);
            npc.SetLocalPositionAndRotation(offset, Quaternion.Euler(euler));
        }

        /// <summary>Mirror of <see cref="Attach"/>: a spawned NetworkObject leaves through netcode so everyone sees it.</summary>
        public static void Detach(Transform npc)
        {
            NetworkObject npcNetObj = npc.GetComponent<NetworkObject>();
            if (npcNetObj != null && npcNetObj.IsSpawned && npcNetObj.TryRemoveParent(true))
                return;

            npc.SetParent(null, worldPositionStays: true);
        }

        /// <summary>
        /// Where a seated NPC sits: <paramref name="offset"/> from <paramref name="seat"/>, read in
        /// <paramref name="space"/> — the carrier's root for the netcode path, world space (null) for
        /// everything else. The two answers describe the same point; getting the fold wrong is how a
        /// rider floats above the saddle on every machine but one.
        /// </summary>
        public static (Vector3 position, Quaternion rotation) SeatPoseIn(
            Transform space, Transform seat, Vector3 offset, Vector3 euler)
        {
            Vector3 position = seat.TransformPoint(offset);
            Quaternion rotation = seat.rotation * Quaternion.Euler(euler);

            return space == null
                ? (position, rotation)
                : (space.InverseTransformPoint(position), Quaternion.Inverse(space.rotation) * rotation);
        }

        /// <summary>
        /// Every NPC seated under <paramref name="carrier"/>, as seen by a machine that was told
        /// nothing: an <see cref="AgentController"/> below the carrier that is not the carrier's own.
        /// A player has no AgentController, which keeps this from adopting somebody a MountModule
        /// is already posing.
        /// </summary>
        public static void CollectSeatedNpcs(Transform carrier, List<Transform> into)
        {
            into.Clear();
            foreach (AgentController controller in carrier.GetComponentsInChildren<AgentController>(true))
            {
                if (controller == null || controller.gameObject == carrier.gameObject) continue;
                if (!into.Contains(controller.transform)) into.Add(controller.transform);
            }
        }
    }
}
