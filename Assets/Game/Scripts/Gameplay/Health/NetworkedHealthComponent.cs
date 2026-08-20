// Replicates one entity's health, and receives the damage requests that change it.
//
// Both halves live here because they are the same statement: the server owns this health value.
// It publishes it outward, and it is the only machine that may be asked to change it.
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Core;

namespace SpaceGame.Gameplay
{
    [RequireComponent(typeof(HealthComponent))]
    public class NetworkedHealthComponent : NetworkBehaviour
    {
        private HealthComponent health;

        // Everyone, not Owner. A server-owned entity — which is every AI, creature and vehicle in
        // the game — has no owning client, so Owner permission published its health to nobody and
        // every client kept showing a monster at full health while the server watched it die.
        private readonly NetworkVariable<int> networkHealth = new(
            100, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private void Awake() => health = GetComponent<HealthComponent>();

        private void OnEnable() => this.NetOn(NetMsg.Damage, OnDamageRequested);

        private void OnDisable() => this.NetOff(NetMsg.Damage, OnDamageRequested);

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                networkHealth.Value = health.GetHealth;

                health.OnDamage += SyncHealth;
                health.OnHeal += SyncHealth;
                health.OnDeath += SyncHealth;
                health.OnRestored += SyncHealth;
            }
            else
            {
                // Late joiners and newly streamed-in entities arrive with the current value
                // already in the variable and no change event coming, so read it once on spawn.
                networkHealth.OnValueChanged += ApplyHealth;
                ApplyHealth(health.GetHealth, networkHealth.Value);
            }
        }

        public override void OnDestroy()
        {
            // Only the unsubscribes are conditional. The early `return` used to skip
            // base.OnDestroy() as well, and NetworkBehaviour.OnDestroy is what disposes this
            // behaviour's NetworkVariables and drops it from its NetworkObject's
            // ChildNetworkBehaviours list -- so every destroy leaked both.
            if (health != null)
            {
                if (IsServer)
                {
                    health.OnDamage -= SyncHealth;
                    health.OnHeal -= SyncHealth;
                    health.OnDeath -= SyncHealth;
                    health.OnRestored -= SyncHealth;
                }
                else
                {
                    networkHealth.OnValueChanged -= ApplyHealth;
                }
            }

            base.OnDestroy();
        }

        /// <summary>
        /// A client asking the server to hurt this entity — see <see cref="NetDamage"/>. Ignored
        /// anywhere but the server, where the message cannot arrive in the first place; the guard
        /// is for the offline path, on which the message is dispatched locally.
        /// </summary>
        private void OnDamageRequested(in NetArg arg, ulong sender)
        {
            if (!Network.Simulates(this) || health == null || arg.A <= 0) return;

            GameObject source = arg.Resolve();
            health.Damage(arg.A, source != null ? source.transform : null);
        }

        private void SyncHealth(int _) => SyncHealth();

        private void SyncHealth() => networkHealth.Value = health.GetHealth;

        /// <summary>
        /// Assigns the server's value rather than replaying the difference.
        ///
        /// The old version computed a delta and called Damage/Heal with it, which meant a client
        /// that missed one update stayed wrong forever, a heal past max silently clamped away part
        /// of the correction, and every replicated hit fired the local damage flash a second time.
        /// RestoreHealth exists precisely for "this value is now the truth".
        /// </summary>
        private void ApplyHealth(int previous, int current)
        {
            if (health != null) health.RestoreHealth(current);
        }
    }
}
