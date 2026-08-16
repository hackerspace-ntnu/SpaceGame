using System.Collections;
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Characters;
using SpaceGame.Core;

namespace SpaceGame.Items
{
    /// <summary>
    /// Grappling hook artifact — extends ToolItem.
    ///
    /// First Use()  → animates rope shooting toward target, then starts pulling the player.
    /// Second Use() → releases the hook (or it auto-releases on arrival).
    ///
    /// The per-frame pull runs as a coroutine on this MonoBehaviour so ToolItem's
    /// fire-and-forget Use() stays clean.
    /// </summary>
    public class GrapplingHookArtifact : ToolItem
    {
        /// <summary>
        /// Owner-run: the swing IS the item. A round trip through the server would sit inside
        /// every jump. GrappleNetworkSync replicates the anchor so peers see the rope.
        /// </summary>
        public override UseAuthority Authority => UseAuthority.Owner;

        [Header("Firing")]
        [SerializeField] private float maxRange = 60f;
        [SerializeField] private LayerMask hookableLayers = ~0;
        [SerializeField] private float shootSpeed = 33f;   // rope-extend speed (lasso 30 × 1.1)

        [Header("Pull")]
        [SerializeField] private float reelSpeed = 26f;    // rope shortens this many units/sec (was 20, now 1.3×)
        [SerializeField] private float arrivalDistance = 2.5f;
        [SerializeField] private float arrivalBoost = 14f;

        [Header("Rope Visual")]
        [SerializeField] private LineRenderer lineRenderer;
        [SerializeField] private Transform muzzle;      // optional gun-tip transform
        [SerializeField] private int ropeSegments = 12;
        [SerializeField] private float ropeGravity = 4f;

        // ── Runtime state ──────────────────────────────────────────────────────
        private bool _isGrappling;
        private bool _isShooting;
        private Vector3 _hookPoint;
        private float _ropeLength;
        private float _shootHeadProgress;  // 0→1 during shoot animation
        private Coroutine _pullCoroutine;

        // ── ToolItem override ──────────────────────────────────────────────────

        protected override void Use()
        {
            base.Use(); // sets aimProvider

            if (_isGrappling)
            {
                StopGrapple();
                return;
            }

            if (_isShooting) return;

            RaycastHit? hit = aimProvider.GetRayCast(maxRange);
            if (hit == null) return;

            // Respect hookable layer mask
            if ((hookableLayers.value & (1 << hit.Value.collider.gameObject.layer)) == 0)
                return;

            _hookPoint = hit.Value.point;
            _ropeLength = Vector3.Distance(owner.transform.position, _hookPoint);

            owner.GetComponent<PlayerMovement>()?.DisableGroundSnap(999f);

            EnableRope();

            // Tell the other players where the rope landed. The pull itself stays local — this is
            // client-predicted, so the swing never waits on a round trip.
            NetworkSync?.ReportAttached(_hookPoint);

            _pullCoroutine = StartCoroutine(ShootThenPullRoutine());
        }

        // ── Shoot animation → pull coroutine ──────────────────────────────────
        //
        // Animates the rope extending from muzzle to hook point (headProgress 0→1),
        // then hands off to PullRoutine for the pendulum grapple.

        private IEnumerator ShootThenPullRoutine()
        {
            _isShooting = true;
            _isGrappling = false;

            var animator = owner.GetComponentInChildren<Animator>();
            if (animator) animator.SetTrigger("ShootRifle");

            float distance = Vector3.Distance(GetRopeStart(), _hookPoint);
            float duration = distance / Mathf.Max(shootSpeed, 0.1f);
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                _shootHeadProgress = Mathf.Clamp01(elapsed / duration);
                UpdateRopeWithProgress(_shootHeadProgress, GetRopeStart());
                yield return null;
            }

            _isShooting = false;
            _isGrappling = true;
            _shootHeadProgress = 1f;

            yield return StartCoroutine(PullRoutine());
        }

        // ── Pull coroutine ─────────────────────────────────────────────────────
        //
        // Pendulum constraint: shorten the rope each frame, then enforce it as a
        // hard inextensible constraint. Gravity acts freely — the player swings in
        // an arc rather than flying straight at the hook point.

        private IEnumerator PullRoutine()
        {
            var rb = owner.GetComponent<Rigidbody>();

            // Only the player who owns this body may drive it. On a remote peer the swing arrives
            // through that player's NetworkTransform instead; running the constraint here too would
            // have two authorities writing the same Rigidbody.
            if (!OwnsMovement())
                yield break;

            while (_isGrappling && rb != null)
            {
                // Shorten rope over time
                _ropeLength = Mathf.Max(arrivalDistance, _ropeLength - reelSpeed * Time.deltaTime);

                Vector3 toHook = _hookPoint - rb.position;
                float dist = toHook.magnitude;
                Vector3 radial = dist > 0.001f ? toHook / dist : Vector3.up;

                // Hard constraint: if player is beyond rope length, cancel outward velocity
                // and snap back to the rope surface so they swing rather than drift away
                if (dist > _ropeLength)
                {
                    float radialVel = Vector3.Dot(rb.linearVelocity, -radial); // velocity away from hook
                    if (radialVel > 0f)
                        rb.linearVelocity += radial * radialVel;               // cancel the outward component

                    rb.position = _hookPoint - radial * _ropeLength;
                }

                // Arrival — release and apply momentum boost
                if (_ropeLength <= arrivalDistance)
                {
                    rb.linearVelocity += radial * arrivalBoost;
                    StopGrapple();
                    yield break;
                }

                UpdateRopeWithProgress(1f, rb.position);
                yield return null;
            }
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private void StopGrapple()
        {
            bool wasOut = _isGrappling || _isShooting;

            _isGrappling = false;
            _isShooting = false;

            if (_pullCoroutine != null)
            {
                StopCoroutine(_pullCoroutine);
                _pullCoroutine = null;
            }

            DisableRope();
            owner.GetComponent<PlayerMovement>()?.DisableGroundSnap(0.15f);

            if (wasOut)
                NetworkSync?.ReportReleased();
        }

        /// <summary>
        /// The server rejected this anchor (out of range). Drop the rope without re-reporting the
        /// release — the sync already knows, and it owns the anchor state that triggered this.
        /// </summary>
        public void CancelFromNetwork()
        {
            _isGrappling = false;
            _isShooting = false;

            if (_pullCoroutine != null)
            {
                StopCoroutine(_pullCoroutine);
                _pullCoroutine = null;
            }

            DisableRope();
            if (owner != null)
                owner.GetComponent<PlayerMovement>()?.DisableGroundSnap(0.15f);
        }

        // Resolved from the player rather than this item: the held gun has no NetworkObject, so the
        // RPC channel lives on the body holding it.
        private GrappleNetworkSync NetworkSync
        {
            get
            {
                if (_networkSync == null && owner != null)
                    _networkSync = owner.GetComponentInChildren<GrappleNetworkSync>(true);
                return _networkSync;
            }
        }

        private GrappleNetworkSync _networkSync;

        /// <summary>
        /// True when this machine is allowed to move the grappling player's Rigidbody: offline, or
        /// networked and this is the owning client.
        /// </summary>
        private bool OwnsMovement()
        {
            if (!Network.IsNetworked)
                return true;

            if (owner != null && owner.TryGetComponent(out NetworkObject netObj))
                return netObj.IsOwner;

            return true;
        }

        private void EnableRope()
        {
            if (lineRenderer == null) return;
            lineRenderer.positionCount = ropeSegments;
            lineRenderer.enabled = true;
        }

        private void DisableRope()
        {
            if (lineRenderer == null) return;
            lineRenderer.enabled = false;
        }

        // headProgress 0→1 controls how far the rope tip has travelled toward _hookPoint.
        // At 1 the full rope is drawn; during the shoot animation it grows segment-by-segment.
        private void UpdateRopeWithProgress(float headProgress, Vector3 playerPos)
        {
            if (lineRenderer == null || !lineRenderer.enabled) return;

            Vector3 start = muzzle != null ? muzzle.position : playerPos;
            Vector3 tip   = Vector3.Lerp(start, _hookPoint, headProgress);
            float span    = (tip - start).magnitude;

            int activeSegments = Mathf.Max(2, Mathf.RoundToInt(headProgress * ropeSegments));
            lineRenderer.positionCount = activeSegments;

            for (int i = 0; i < activeSegments; i++)
            {
                float t = i / (float)(activeSegments - 1);
                Vector3 pos = Vector3.Lerp(start, tip, t);
                pos.y -= Mathf.Sin(t * Mathf.PI) * ropeGravity * (span / maxRange);
                lineRenderer.SetPosition(i, pos);
            }
        }

        private Vector3 GetRopeStart()
        {
            return muzzle != null ? muzzle.position : owner.transform.position;
        }
    }
}
