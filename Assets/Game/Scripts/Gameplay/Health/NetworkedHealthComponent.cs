using Unity.Netcode;
using UnityEngine;

namespace SpaceGame.Gameplay
{
    [RequireComponent(typeof(HealthComponent))]
    public class NetworkedHealthComponent : NetworkBehaviour
    {
        private HealthComponent health;
    
        private NetworkVariable<int> networkHealth = new (100, NetworkVariableReadPermission.Owner);

        public override void OnNetworkSpawn()
        {
            health = GetComponent<HealthComponent>();

            if (IsServer)
            {
                networkHealth.Value = health.GetHealth;
            
                health.OnDamage += SyncHealth;
                health.OnHeal += SyncHealth;
                health.OnDeath += SyncHealth;
                health.OnRestored += SyncHealth;
            }

            if (IsOwner)
            {
                networkHealth.OnValueChanged += ApplyHealth;
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

                if (IsOwner)
                {
                    networkHealth.OnValueChanged -= ApplyHealth;
                }
            }

            base.OnDestroy();
        }
    
        private void SyncHealth(int _)
        {
            networkHealth.Value = health.GetHealth;
        }

        private void SyncHealth()
        {
            networkHealth.Value = health.GetHealth;
        }
    
        private void ApplyHealth(int oldValue, int newValue)
        {
            int delta = newValue - health.GetHealth;

            if (delta < 0)
                health.Damage(-delta);
            else if (delta > 0)
                health.Heal(delta);
        }
    }
}
