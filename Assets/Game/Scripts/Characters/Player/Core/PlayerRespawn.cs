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

            // The rule of respawn: you come back inside your ship — in versus, your TEAM's ship.
            // The seat resolver answers wherever the ship has been driven or landed, which the
            // world's free-standing spawn point cannot. Only when no ship can take this player —
            // a scene with no hull, a versus team that never resolved — does the old spawn-point
            // and open-ground resolution below run, so a shipless world still stands people up.
            if (ShipRespawn.TryGetPose(gameObject, out Vector3 position, out Quaternion rotation))
            {
                // Placement first, healing second, and the order is load-bearing. Healing raises
                // OnRevive, which is what hands the player their controls back; doing that before
                // the move would give them a frame or two of live control standing on their own
                // corpse.
                NetworkedTeleport.Move(gameObject, position, rotation);
            }
            else if (SpawnManager.Instance != null &&
                     SpawnManager.Instance.TryGetRespawnPosition(transform.position, out position))
            {
                Debug.LogWarning("[Respawn] No ship could take this player — falling back to the " +
                                 "spawn point / open ground resolution.", this);
                NetworkedTeleport.Move(gameObject, position, transform.rotation);
            }
            else
            {
                Debug.LogError("[Respawn] No ship, no valid spawn position — not at a SpawnPoint " +
                               "and not on open ground anywhere near one or near the body, so the " +
                               "player stays down. Is any of the world around them loaded?", this);
                return;
            }

            // ResetToFull rather than Heal(maxHealth): overkill drives currentHealth below zero and
            // Heal caps the restore at the amount passed, so a heavily overkilled player would come
            // back damaged — or still dead. Same reasoning as MatchManager's respawn.
            health.ResetToFull();
        }
    }
}
