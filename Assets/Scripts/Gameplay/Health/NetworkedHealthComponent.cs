using Unity.Netcode;
using UnityEngine;

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
        }

        if (IsOwner)
        {
            networkHealth.OnValueChanged += ApplyHealth;
        }
    }

    public override void OnDestroy()
    {
        if (health == null) return;

        if (IsServer)
        {
            health.OnDamage -= SyncHealth;
            health.OnHeal -= SyncHealth;
            health.OnDeath -= SyncHealth;
        }

        if (IsOwner)
        {
            networkHealth.OnValueChanged -= ApplyHealth;
        }
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
