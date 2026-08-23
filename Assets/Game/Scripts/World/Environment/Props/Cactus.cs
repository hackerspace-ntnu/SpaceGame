using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Gameplay;

namespace SpaceGame.World
{
    /// <summary>
    /// Sand-country scenery that hurts to stand in.
    ///
    /// <para>
    /// <b>Only one machine may bill a victim.</b> A cactus is a scene prop, so every machine in the
    /// session has its own copy of it, and every machine's copy sees every player walk into it —
    /// remote replicas included, since they carry colliders and are moved by their NetworkTransform.
    /// Ungated, each of those copies called <see cref="NetDamage"/>, so a player standing in a
    /// cactus in a four-player session lost four times the authored damage per tick, and the sting
    /// got worse every time somebody joined.
    /// </para>
    /// </summary>
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
                // Asked of the VICTIM, not of this cactus. Network.Simulates answers about the
                // object it is handed, and scenery has no NetworkObject of its own — asked here it
                // would say "yes, you simulate it" on every machine at once, which is the multiplied
                // sting this guard exists to stop. The victim knows the real answer: the server for
                // a networked player or creature, and every machine for a purely local one, which
                // is right because each of those has its own unshared copy to hurt.
                //
                // The whole loop keeps running either way. It is what notices the victim dying or
                // leaving, and stopping it on a peer would leave a stale entry in `active` that
                // OnTriggerExit still has to clear.
                if (Network.Simulates(healthComponent))
                    NetDamage.Apply(healthComponent.gameObject, damagePerTick);

                yield return new WaitForSeconds(tickInterval);
            }

            active.Remove(healthComponent);
        }
    }
}
