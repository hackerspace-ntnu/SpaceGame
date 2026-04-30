using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RepelArtifact : ToolItem
{
    [Header("Catch Zone")]
    [SerializeField] private float catchRadius = 4f;
    [SerializeField] private float shieldForwardOffset = 1.5f;
    [SerializeField, Range(0f, 360f)] private float catchConeAngle = 120f;

    [Header("Repel")]
    [SerializeField] private float repelDelay = 1f;
    [SerializeField, Range(0f, 180f)] private float spreadConeAngle = 30f;

    [Header("Knockback")]
    [SerializeField] private float baseKnockback = 5f;
    [SerializeField] private float perProjectileKnockback = 2f;
    [SerializeField] private ForceMode knockbackForceMode = ForceMode.Impulse;

    [Header("VFX")]
    [SerializeField] private GameObject shieldVfxPrefab;

    private struct CaughtProjectile
    {
        public AgentProjectile projectile;
        public float speed;
    }

    protected override void Use()
    {
        base.Use();
        if (aimProvider == null)
        {
            RefundUse();
            return;
        }

        StartCoroutine(CatchAndRepelRoutine());
    }

    private IEnumerator CatchAndRepelRoutine()
    {
        Transform cameraTransform = GetCameraTransform();
        if (cameraTransform == null)
        {
            RefundUse();
            yield break;
        }

        GameObject vfx = null;
        if (shieldVfxPrefab != null)
        {
            vfx = Instantiate(shieldVfxPrefab);
            UpdateShieldTransform(vfx.transform, cameraTransform);
        }

        List<CaughtProjectile> caught = new List<CaughtProjectile>();
        float halfCatchAngle = catchConeAngle * 0.5f;

        TryCatchProjectiles(cameraTransform, halfCatchAngle, caught);

        float elapsed = 0f;
        while (elapsed < repelDelay)
        {
            if (vfx != null)
                UpdateShieldTransform(vfx.transform, cameraTransform);

            TryCatchProjectiles(cameraTransform, halfCatchAngle, caught);

            elapsed += Time.deltaTime;
            yield return null;
        }

        Vector3 repelOrigin = cameraTransform.position;
        Vector3 repelForward = cameraTransform.forward;

        if (vfx != null)
            Destroy(vfx);

        if (caught.Count == 0)
        {
            RefundUse();
            yield break;
        }

        ReleaseProjectiles(caught, repelForward);
        ApplyKnockback(repelOrigin, repelForward, caught.Count);
    }

    private Transform GetCameraTransform()
    {
        Ray ray = aimProvider.GetAimRay();
        Camera mainCam = Camera.main;
        if (mainCam != null && Vector3.Distance(mainCam.transform.position, ray.origin) < 0.01f)
            return mainCam.transform;

        // Fallback: synthesize a transform-like wrapper using the ray. Camera.main is the
        // expected source since AimProvider uses it; this branch is just a safety net.
        return mainCam != null ? mainCam.transform : null;
    }

    private void UpdateShieldTransform(Transform shield, Transform cam)
    {
        shield.position = cam.position + cam.forward * shieldForwardOffset;
        shield.rotation = Quaternion.LookRotation(cam.forward, cam.up);
    }

    private void TryCatchProjectiles(Transform cam, float halfAngle, List<CaughtProjectile> caught)
    {
        AgentProjectile[] all = Object.FindObjectsByType<AgentProjectile>(FindObjectsSortMode.None);
        Vector3 camPos = cam.position;
        Vector3 camFwd = cam.forward;

        foreach (AgentProjectile p in all)
        {
            if (p == null || p.IsFrozen) continue;

            Vector3 toProj = p.transform.position - camPos;
            float dist = toProj.magnitude;
            if (dist > catchRadius + shieldForwardOffset) continue;

            float angle = Vector3.Angle(camFwd, toProj);
            if (angle > halfAngle) continue;

            Rigidbody prb = p.GetComponent<Rigidbody>();
            float speed = prb != null ? prb.linearVelocity.magnitude : 0f;

            p.Freeze();
            caught.Add(new CaughtProjectile { projectile = p, speed = speed });
        }
    }

    private void ReleaseProjectiles(List<CaughtProjectile> caught, Vector3 baseDirection)
    {
        int count = caught.Count;
        float halfSpread = spreadConeAngle * 0.5f;

        for (int i = 0; i < count; i++)
        {
            CaughtProjectile cp = caught[i];
            if (cp.projectile == null) continue;

            Vector3 dir = SampleConeDirection(baseDirection, halfSpread, i, count);
            cp.projectile.ReleaseRetargeted(dir, cp.speed, owner);
        }
    }

    private Vector3 SampleConeDirection(Vector3 baseDir, float halfAngleDeg, int index, int count)
    {
        if (count <= 1 || halfAngleDeg <= 0f)
            return baseDir;

        // Fibonacci-like uniform spread on a spherical cap.
        float t = (index + 0.5f) / count;
        float cosHalf = Mathf.Cos(halfAngleDeg * Mathf.Deg2Rad);
        float cosTheta = Mathf.Lerp(1f, cosHalf, t);
        float sinTheta = Mathf.Sqrt(Mathf.Max(0f, 1f - cosTheta * cosTheta));
        float phi = index * 2.39996323f; // golden angle in radians

        Vector3 local = new Vector3(Mathf.Cos(phi) * sinTheta, Mathf.Sin(phi) * sinTheta, cosTheta);

        Quaternion toBase = Quaternion.FromToRotation(Vector3.forward, baseDir.normalized);
        return toBase * local;
    }

    private void ApplyKnockback(Vector3 origin, Vector3 forward, int caughtCount)
    {
        float force = baseKnockback + perProjectileKnockback * caughtCount;
        Collider[] hits = Physics.OverlapSphere(origin, catchRadius + shieldForwardOffset);
        HashSet<Rigidbody> applied = new HashSet<Rigidbody>();

        foreach (Collider c in hits)
        {
            Rigidbody r = c.attachedRigidbody;
            if (r == null || applied.Contains(r)) continue;

            // Skip the wielder.
            if (owner != null && r.transform.IsChildOf(owner.transform)) continue;

            Vector3 toTarget = r.worldCenterOfMass - origin;
            float angle = Vector3.Angle(forward, toTarget);
            if (angle > catchConeAngle * 0.5f) continue;

            Vector3 dir = toTarget.sqrMagnitude > 0.0001f ? toTarget.normalized : forward;
            r.AddForce(dir * force, knockbackForceMode);
            applied.Add(r);
        }
    }
}
