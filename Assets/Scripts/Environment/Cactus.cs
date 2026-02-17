using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class Cactus : MonoBehaviour
{
    [SerializeField] private int damagePerTick = 5;
    [SerializeField] private float tickInterval = 1f;

    private readonly Dictionary<HealthComponent, Coroutine> active = new();

    private void Reset()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        var health = other.GetComponentInParent<HealthComponent>();
        if (health == null || !health.Alive || active.ContainsKey(health)) return;

        Coroutine c = StartCoroutine(DamageOverTime(health));
        active.Add(health, c);
    }

    private void OnTriggerExit(Collider other)
    {
        var health = other.GetComponentInParent<HealthComponent>();
        if (health == null) return;

        if (active.TryGetValue(health, out Coroutine c))
        {
            StopCoroutine(c);
            active.Remove(health);
        }
    }

    private IEnumerator DamageOverTime(HealthComponent healthComponent)
    {
        while (healthComponent && healthComponent.Alive)
        {
            healthComponent.Damage(damagePerTick);
            yield return new WaitForSeconds(tickInterval);
        }

        active.Remove(healthComponent);
    }
}