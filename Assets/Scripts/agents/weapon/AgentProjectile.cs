// Lightweight projectile fired by AgentRangedCombatModule.
// Deals damage on first collision with an IDamageable and reports hit/miss back via callback.
// Destroy after lifetime even if nothing is hit.
using System;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class AgentProjectile : MonoBehaviour
{
    [SerializeField] private float lifetime = 4f;
    [SerializeField] private GameObject impactVfxPrefab;

    private int damage;
    private Action<bool, Vector3> onResult; // (hitDamageable, hitPosition)
    private bool hasHit;
    private EntityFaction shooterFaction;
    private Transform shooterTransform;

    public void Init(int damageAmount, Action<bool, Vector3> resultCallback, GameObject shooter = null)
    {
        damage = damageAmount;
        onResult = resultCallback;
        shooterFaction = shooter != null ? shooter.GetComponentInParent<EntityFaction>() : null;
        shooterTransform = shooter != null ? shooter.transform : null;

        if (shooter != null)
        {
            foreach (Collider shooterCol in shooter.GetComponentsInChildren<Collider>())
                Physics.IgnoreCollision(GetComponent<Collider>(), shooterCol);
        }
    }

    private void Start()
    {
        Destroy(gameObject, lifetime);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (hasHit)
            return;

        hasHit = true;
        Vector3 hitPos = collision.contacts.Length > 0 ? collision.contacts[0].point : transform.position;
        Vector3 hitNormal = collision.contacts.Length > 0 ? collision.contacts[0].normal : -transform.forward;

        if (impactVfxPrefab != null)
            Instantiate(impactVfxPrefab, hitPos, Quaternion.LookRotation(hitNormal));

        IDamageable damageable = collision.gameObject.GetComponentInParent<IDamageable>();
        EntityFaction hitFaction = collision.transform.GetComponentInParent<EntityFaction>();

        // Friendly fire off: pass through explicitly allied damageables only.
        // Unfactioned damageables are valid targets because targeting modules allow them.
        if (damageable != null && shooterFaction != null && hitFaction != null && shooterFaction.IsAlliedWith(hitFaction))
        {
            hasHit = false;
            return;
        }

        if (damageable != null && damageable.Alive)
        {
            if (damageable is HealthComponent hc)
                hc.Damage(damage, shooterTransform);
            else
                damageable.Damage(damage);
            onResult?.Invoke(true, hitPos);
        }
        else
        {
            onResult?.Invoke(false, hitPos);
        }

        Destroy(gameObject);
    }
}
