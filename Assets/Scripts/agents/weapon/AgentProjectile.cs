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

    private Rigidbody rb;
    private Collider col;
    private Vector3 frozenVelocity;
    private bool isFrozen;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        col = GetComponent<Collider>();
    }

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
        Invoke(nameof(LifetimeExpired), lifetime);
    }

    private void LifetimeExpired()
    {
        Destroy(gameObject);
    }

    public bool IsFrozen => isFrozen;
    public float FrozenSpeed => frozenVelocity.magnitude;

    public void Freeze()
    {
        if (isFrozen || hasHit) return;
        isFrozen = true;
        if (rb != null)
        {
            frozenVelocity = rb.linearVelocity;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
        }
        if (col != null) col.enabled = false;
        CancelInvoke(nameof(LifetimeExpired));
    }

    public void ReleaseRetargeted(Vector3 direction, float speed, GameObject newShooter)
    {
        if (!isFrozen) return;
        isFrozen = false;

        shooterFaction = newShooter != null ? newShooter.GetComponentInParent<EntityFaction>() : null;
        shooterTransform = newShooter != null ? newShooter.transform : null;
        if (newShooter != null && col != null)
        {
            foreach (Collider shooterCol in newShooter.GetComponentsInChildren<Collider>())
                Physics.IgnoreCollision(col, shooterCol);
        }

        if (col != null) col.enabled = true;
        transform.rotation = Quaternion.LookRotation(direction);
        if (rb != null)
        {
            rb.isKinematic = false;
            rb.linearVelocity = direction * speed;
        }

        Invoke(nameof(LifetimeExpired), lifetime);
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
