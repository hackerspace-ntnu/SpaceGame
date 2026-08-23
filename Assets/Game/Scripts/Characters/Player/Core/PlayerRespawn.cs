// Puts one dead player back on their feet.
//
// Respawn used to mean "despawn this NetworkObject and spawn a fresh one". That is what made it a
// multiplayer bug rather than a feature: the despawn ran PlayerSaveSync.OnNetworkDespawn, which
// captures the body's state into that profile's save record — health included — and the replacement
// body's OnNetworkSpawn claimed the same profile and restored it. The corpse's zero health and its
// death position went out through the save system and came straight back in, so a respawn produced
// a dead player standing at their own grave. It cost the host too, since single-player runs as one.
//
// So respawn is modelled here as what it actually is: a state change on a player who already exists.
// Nothing is destroyed, nothing is re-created, and the save record is never consulted.
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Gameplay;

namespace SpaceGame.Characters
{
    /// <summary>
    /// The player's own respawn. Lives on the player because a respawn has exactly one subject, and
    /// addressing the request to that player's channel is what lets the server tell two dead players
    /// apart without anyone naming a client id.
    ///
    /// Split of responsibility, which is the reason this is not one method somewhere:
    ///   • the SERVER decides a respawn may happen and where, and heals — health is server-owned
    ///     state and <see cref="NetworkedHealthComponent"/> publishes the result;
    ///   • the OWNER performs the move, because the player's NetworkTransform is owner-authoritative
    ///     (AuthorityMode: Owner). A server-side write to a remote player's transform is overwritten
    ///     by that owner's next state update, which is exactly how a teleport silently does nothing.
    ///     MatchManager.MoveTo already learned this the hard way; this mirrors it.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(HealthComponent))]
    public class PlayerRespawn : MonoBehaviour
    {
        private HealthComponent health;

        private void Awake() => health = GetComponent<HealthComponent>();

        private void OnEnable() => this.NetOn(NetMsg.Respawn, OnRespawnRequested);

        private void OnDisable() => this.NetOff(NetMsg.Respawn, OnRespawnRequested);

        /// <summary>
        /// Ask to come back. Safe to call from any machine and offline — the messaging layer runs
        /// the handler locally when there is no wire, which is the single-player behaviour.
        /// </summary>
        public void Request()
        {
            if (health != null && health.Alive) return;

            this.NetToServer(NetMsg.Respawn);
        }

        /// <summary>
        /// Server side: the only machine allowed to say a respawn happened.
        ///
        /// The sender is not checked against this player. It cannot be spoofed into hurting anyone:
        /// the message arrives on THIS player's channel, so the worst a malicious client can do is
        /// resurrect a teammate who is already asking to be resurrected.
        /// </summary>
        private void OnRespawnRequested(in NetArg arg, ulong sender)
        {
            if (!Network.Simulates(this)) return;

            // Already alive means a duplicate request — two clicks, or a click that crossed a
            // MatchManager respawn. Healing again would be harmless; moving them would not.
            if (health == null || health.Alive) return;

            // Their own position is passed as the last-resort anchor. A spawn point can refuse —
            // the bay is full of geometry, the ship has been driven somewhere awkward, the chunk
            // under it has not loaded — and a refusal used to end the respawn, leaving the player
            // face down with nothing left to press. It does not any more: SpawnManager falls back
            // to open ground outside, near the spawn point if it can and near the corpse if it
            // cannot, since a dead player is by definition standing on ground that exists.
            if (SpawnManager.Instance == null ||
                !SpawnManager.Instance.TryGetRespawnPosition(transform.position, out Vector3 position))
            {
                Debug.LogError("[Respawn] No valid spawn position — not at a SpawnPoint and not on " +
                               "open ground anywhere near one or near the body, so the player stays " +
                               "down. Is any of the world around them loaded?", this);
                return;
            }

            // Placement first, healing second, and the order is load-bearing. Healing raises
            // OnRevive, which is what hands the player their controls back; doing that before the
            // move would give them a frame or two of live control standing on their own corpse.
            NetworkedTeleport.Move(gameObject, position, transform.rotation);

            // ResetToFull rather than Heal(maxHealth): overkill drives currentHealth below zero and
            // Heal caps the restore at the amount passed, so a heavily overkilled player would come
            // back damaged — or still dead. Same reasoning as MatchManager's respawn.
            health.ResetToFull();
        }
    }
}
