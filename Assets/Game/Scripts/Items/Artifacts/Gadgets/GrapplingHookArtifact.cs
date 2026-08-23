using System.Collections;
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Characters;
using SpaceGame.Core;
using SpaceGame.Persistence;

namespace SpaceGame.Items
{
    /// <summary>
    /// Grappling hook artifact — extends ToolItem.
    ///
    /// First Use()  → animates rope shooting toward target, then starts pulling the player.
    /// Second Use() → releases the hook (or it auto-releases on arrival).
    ///
    /// Networking rides the same Use/Present split every other artifact uses, and nothing else:
    ///
    ///   • <see cref="OnRequestUse"/> — owner-side, the one machine with the camera. It resolves
    ///     the hook point and puts it in the message, because no peer can recompute an aim.
    ///   • <see cref="Present"/> — every machine. Rope, shoot animation and countdown run here, so
    ///     a peer sees the rope instead of a player mysteriously flying.
    ///
    /// The pendulum inside <see cref="PullRoutine"/> is the one part that stays owner-only. Their
    /// body is owner-authoritative, so the swing replicates through the transform they already own
    /// — and a peer running the constraint too would be a second authority on the same Rigidbody.
    ///
    /// This used to need a GrappleNetworkSync beside it: a NetworkBehaviour on the player with its
    /// own RPC triple and a replicated anchor. It carried nothing the use message does not, and the
    /// rope it existed to draw was never drawn — the LineRenderer it needed was unassigned on both
    /// player prefabs, so every remote grapple was invisible for as long as that component shipped.
    /// </summary>
    public class GrapplingHookArtifact : ToolItem, IItemDeferredRestore
    {
        /// <summary>
        /// Owner-run: the swing IS the item. A round trip through the server would sit inside
        /// every jump. Present() replicates the rope so peers see what the swing hangs from.
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

        // What the press meant, carried in NetArg.B. A is already the hotbar slot, so B it is.
        private const int Release = 0;
        private const int Attach  = 1;

        // ── Runtime state ──────────────────────────────────────────────────────
        private bool _isGrappling;
        private bool _isShooting;
        private Vector3 _hookPoint;
        private float _ropeLength;
        private float _shootHeadProgress;  // 0→1 during shoot animation
        private Coroutine _pullCoroutine;

        /// <summary>
        /// What the hook is stuck in, when it is stuck in something that can move.
        ///
        /// Only ever known on the machine that aimed the shot — the raycast lives in
        /// <see cref="OnRequestUse"/> and its collider does not travel in the message, because
        /// nothing at runtime needs it. A save does: a hook set into a moving vehicle and stored as
        /// a world point comes back anchored to a patch of empty air the vehicle has since left.
        /// </summary>
        private Transform _hookAttach;

        /// <summary>The aimed-at collider, waiting for the press it belongs to to be presented.</summary>
        private Transform _pendingAttach;

        // ── Owner side: describe the press ─────────────────────────────────────

        /// <summary>
        /// Owner-side: settle what this press is and, if it is a throw, where the rope lands.
        ///
        /// The raycast belongs here rather than in Use(). It is the only moment the aim is honest
        /// — this is the machine holding the camera — and resolving it once means every machine
        /// hangs its rope on the same point instead of each guessing its own.
        /// </summary>
        public override void OnRequestUse(ref NetArg arg)
        {
            arg.B = Release;

            // Rope already out: this press lets go, and there is nothing to aim at.
            if (_isGrappling || _isShooting) return;

            if (aimProvider == null) return;

            RaycastHit? hit = aimProvider.GetRayCast(maxRange);
            if (hit == null) return;

            // Respect hookable layer mask
            if ((hookableLayers.value & (1 << hit.Value.collider.gameObject.layer)) == 0)
                return;

            arg.B = Attach;
            arg.P = hit.Value.point;

            // Kept for the save, not for the swing. See _hookAttach.
            _pendingAttach = hit.Value.collider.transform;
        }

        /// <summary>
        /// Nothing. Both halves of this item are either the owner's own body moving or a rope being
        /// drawn, and both live in <see cref="Present"/> so that peers get them too.
        /// </summary>
        protected override void Use() { }

        // ── Every machine: the rope ────────────────────────────────────────────

        protected override void Present()
        {
            if (UseArg.B != Attach)
            {
                StopGrapple();
                return;
            }

            // A second attach with no release in between means a message arrived twice or out of
            // order. Keep the rope that is already flying rather than starting a rival coroutine.
            if (_isGrappling || _isShooting) return;
            if (owner == null) return;

            _hookPoint = UseArg.P;
            _ropeLength = Vector3.Distance(owner.transform.position, _hookPoint);

            _hookAttach = _pendingAttach;
            _pendingAttach = null;

            if (OwnsMovement())
                owner.GetComponent<PlayerMovement>()?.DisableGroundSnap(999f);

            EnableRope();

            _pullCoroutine = StartCoroutine(ShootThenPullRoutine());
        }

        // ── Shoot animation → pull coroutine ──────────────────────────────────
        //
        // Animates the rope extending from muzzle to hook point (headProgress 0→1),
        // then hands off to the pendulum grapple — or, on a peer, to the rope alone.

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

            // Yielded as a nested enumerator rather than StartCoroutine, so that StopGrapple
            // stopping _pullCoroutine stops the pendulum with it instead of leaving it to notice
            // on its own next frame that _isGrappling went false.
            yield return OwnsMovement() ? PullRoutine() : RemoteRopeRoutine();
        }

        // ── Pull coroutine ─────────────────────────────────────────────────────
        //
        // Pendulum constraint: shorten the rope each frame, then enforce it as a
        // hard inextensible constraint. Gravity acts freely — the player swings in
        // an arc rather than flying straight at the hook point.

        private IEnumerator PullRoutine()
        {
            var rb = owner.GetComponent<Rigidbody>();

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

        /// <summary>
        /// A peer's half of the swing: the rope, and only the rope.
        ///
        /// It reels in on the same clock the owner's pendulum uses, so both machines let go at the
        /// same rope length without anyone having to send a second message for the arrival. Where
        /// the swinging player actually ends up arrives through their own NetworkTransform — this
        /// routine must never touch that body.
        /// </summary>
        private IEnumerator RemoteRopeRoutine()
        {
            while (_isGrappling && owner != null)
            {
                _ropeLength = Mathf.Max(arrivalDistance, _ropeLength - reelSpeed * Time.deltaTime);

                UpdateRopeWithProgress(1f, owner.transform.position);

                if (_ropeLength <= arrivalDistance)
                {
                    StopGrapple();
                    yield break;
                }

                yield return null;
            }
        }

        // ── Per-instance state ─────────────────────────────────────────────────
        //
        // Quitting mid-swing used to reload the player at exactly the coordinates they were hanging
        // at, with no rope and no anchor — so the reward for saving over a canyon was falling into
        // it. The rope is restored here, at the length it had reached, and the pendulum simply picks
        // up where it left off.

        private const string HookKey = "hook";     // world point the hook is set in
        private const string RopeKey = "rope";     // rope length, mid-reel
        private const string AttachKey = "at";     // what it is set INTO, when that can move
        private const string OffsetKey = "off";    // the hook point in that thing's local space

        private SaveRef _pendingAttachRef;
        private Vector3 _pendingAttachOffset;
        private bool _pendingRestore;

        public bool HasPendingRestore => _pendingRestore;

        public override void CaptureItemState(ItemState state)
        {
            base.CaptureItemState(state);
            if (state == null) return;

            // Mid-throw counts as attached: the hook point is already settled, and coming back
            // hanging from it is closer to what the player was doing than coming back falling.
            if (!_isGrappling && !_isShooting) return;

            state.Set(HookKey, _hookPoint);
            state.Set(RopeKey, _ropeLength);

            if (_hookAttach == null) return;

            SaveRef anchor = SaveRef.From(_hookAttach.gameObject);
            if (!anchor.IsSet) return;   // static geometry: the world point is the whole answer

            state.Set(AttachKey, anchor);
            state.Set(OffsetKey, _hookAttach.InverseTransformPoint(_hookPoint));
        }

        public override void RestoreItemState(ItemState state)
        {
            base.RestoreItemState(state);

            _pendingRestore = false;
            _pendingAttachRef = SaveRef.None;

            // No rope in the record means the player was not hanging from anything, and a fresh
            // instance already is not. StopGrapple covers the case where this instance was handed a
            // second bag after being handed a first one.
            if (state == null || !state.Has(HookKey))
            {
                StopGrapple();
                return;
            }

            Vector3 hookPoint = state.GetVector3(HookKey);
            float ropeLength = state.GetFloat(RopeKey);

            // Resumed now rather than in the deferred pass, because the swing is self-contained:
            // a world point is a complete anchor on its own. The reference below only REFINES it,
            // for the case where the thing it is set into has moved since the save.
            ResumeGrapple(hookPoint, ropeLength);

            SaveRef anchor = state.GetRef(AttachKey);
            if (!anchor.IsSet) return;

            _pendingAttachRef = anchor;
            _pendingAttachOffset = state.GetVector3(OffsetKey);
            _pendingRestore = true;
        }

        /// <summary>
        /// Re-anchor the rope onto the object it was actually set into, once that object exists.
        ///
        /// Idempotent and consumed only on success, the house rule for a reference that may name
        /// something still streaming in.
        /// </summary>
        public void TryCompleteRestore()
        {
            if (!_pendingRestore) return;
            if (!_pendingAttachRef.TryResolve(out GameObject anchor)) return;

            _pendingRestore = false;

            if (!_isGrappling && !_isShooting) return;

            _hookAttach = anchor.transform;
            _hookPoint = _hookAttach.TransformPoint(_pendingAttachOffset);
        }

        /// <summary>
        /// Put the player back on a rope that is already out.
        ///
        /// The throw is skipped on purpose — <see cref="ShootThenPullRoutine"/> animates a rope
        /// travelling to its anchor, and the player watched that happen before they saved. This
        /// starts at the pendulum, which is the part they were in the middle of.
        /// </summary>
        private void ResumeGrapple(Vector3 hookPoint, float ropeLength)
        {
            if (owner == null) return;
            if (_isGrappling || _isShooting) return;

            _hookPoint = hookPoint;
            _ropeLength = ropeLength > 0.01f
                ? ropeLength
                : Vector3.Distance(owner.transform.position, hookPoint);

            if (OwnsMovement())
                owner.GetComponent<PlayerMovement>()?.DisableGroundSnap(999f);

            EnableRope();

            _isShooting = false;
            _isGrappling = true;
            _shootHeadProgress = 1f;

            _pullCoroutine = StartCoroutine(OwnsMovement() ? PullRoutine() : RemoteRopeRoutine());
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private void StopGrapple()
        {
            // Also the miss case: a press that hit nothing presents a Release, and there is no
            // rope to drop. Bailing keeps a miss from cancelling the ground snap for no reason.
            if (!_isGrappling && !_isShooting) return;

            _isGrappling = false;
            _isShooting = false;
            _hookAttach = null;
            _pendingRestore = false;

            if (_pullCoroutine != null)
            {
                StopCoroutine(_pullCoroutine);
                _pullCoroutine = null;
            }

            DisableRope();

            if (OwnsMovement() && owner != null)
                owner.GetComponent<PlayerMovement>()?.DisableGroundSnap(0.15f);
        }

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
