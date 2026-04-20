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

    public void Init(int damageAmount, Action<bool, Vector3> resultCallback, GameObject shooter = null)
    {
        damage = damageAmount;
        onResult = resultCallback;
        shooterFaction = shooter != null ? shooter.GetComponentInParent<EntityFaction>() : null;

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

        if (impactVfxPrefab != null)
            Instantiate(impactVfxPrefab, hitPos, Quaternion.LookRotation(collision.contacts[0].normal));

        // Ignore allied targets (friendly fire off).
        if (shooterFaction != null && shooterFaction.IsHostileTo(collision.transform) == false)
        {
            hasHit = false; // allow passing through allies
            return;
        }

        IDamageable damageable = collision.gameObject.GetComponentInParent<IDamageable>();
        if (damageable != null && damageable.Alive)
        {
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
