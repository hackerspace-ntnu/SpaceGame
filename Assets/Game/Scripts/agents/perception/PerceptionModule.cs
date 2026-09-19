// Line-of-sight and field-of-view gate for entity targeting.
// Other modules call CanSee(target) before acting. Fully optional — remove it and modules
// revert to radius-only detection. Emits noise when target is spotted (for alert system).
// Also supports a "last seen" position used by SearchModule.
//
// Authoritative perception API — other modules should route here instead of re-implementing
// FOV/LoS. Public entry points:
//   CanSee(target)                     — full FOV + LoS from the eye, updates memory
//   HasLineOfSight(target)             — LoS from the eye to the body OR the head, no FOV, no memory update
//   HasLineOfSightFrom(origin, target) — LoS to the body only, from an arbitrary origin (e.g. a weapon muzzle)
using System.Collections.Generic;
using UnityEngine;
using FMODUnity;
using SpaceGame.Audio;

namespace SpaceGame.Agents
{
    public class PerceptionModule : MonoBehaviour
    {
        [Header("Field of View")]
        [SerializeField] private float fieldOfViewAngle = VisionBaseline.MinFieldOfView;
        [Tooltip("Extra FOV added while the agent is moving. Keep at 0 for realistic perception — raise only if you want widened peripheral awareness while walking.")]
        [SerializeField] private float movingFovBonus = 0f;
        [Tooltip("Origin for LoS raycasts. Typically a head bone so vision starts from eye height. " +
                 "NOTE: the FOV direction always comes from the agent's root transform — a skeletal bone's " +
                 ".forward is its local +Z in world space, which for rigs imported from Blender points up " +
                 "through the head or sideways, not where the character is facing.")]
        [SerializeField] private Transform eyeTransform;

        [Header("Line of Sight")]
        [SerializeField] private LayerMask occlusionLayers;
        [Tooltip("Fallback eye elevation when no eyeTransform is assigned.")]
        [SerializeField] private float eyeHeight = 1.6f;
        [Tooltip("Where on a target the sight line is aimed when the target has no collider on its " +
                 "root, in metres above its origin. Every character's origin is at its FEET, on the " +
                 "ground, and a ray aimed at the ground grazes every rise of terrain on the way and " +
                 "ends inside the ground itself -- which is how the Clankers came to see a player only " +
                 "at arm's length. A target with a root collider is aimed at that collider's centre.")]
        [SerializeField] private float targetAimHeight = 1f;
        [Tooltip("Where a target's head is taken to be when it has no solid collider, in metres above " +
                 "its origin. Sight tries the head when the body is hidden, so a standing player is " +
                 "seen over waist-high cover.")]
        [SerializeField] private float headAimHeight = 1.6f;
        [Tooltip("How far below the top of a target's collider its head point sits, in metres. The very " +
                 "top grazes a ceiling or an overhang the head itself would be under.")]
        [SerializeField] private float headInset = 0.15f;

        [Header("Memory")]
        [Tooltip("How long the entity remembers the last known position after losing sight.")]
        [SerializeField] private float memoryDuration = VisionBaseline.MinMemory;

        [Header("Noise on Spot")]
        [SerializeField] private bool emitNoiseOnSpot = true;
        [SerializeField] private float spotNoiseRadius = 12f;

        [Header("Audio")]
        [SerializeField] private bool playSpotSound = true;
        [SerializeField] private SfxId spotId = SfxId.EntityAlert;
        [SerializeField] private EventReference spotSound;

        public Vector3 LastKnownPosition { get; private set; }
        public bool HasLastKnownPosition { get; private set; }
        public float TimeSinceLastSeen { get; private set; }

        public Vector3 EyePosition => eyeTransform ? eyeTransform.position : transform.position + Vector3.up * eyeHeight;
        public float MemoryDuration => memoryDuration;

        private NoiseEmitter noiseEmitter;
        private Vector3 prevPosition;
        private bool isMoving;

        // Layers treated as sight blockers when occlusionLayers is left at Nothing. A mask of 0
        // makes every raycast return no hits, which reads as "line of sight confirmed" everywhere —
        // agents see and shoot through walls, and aimProfile.requireLineOfSight becomes a no-op.
        // Failing towards "solid geometry blocks sight" is the far less surprising default.
        private static readonly string[] FallbackOcclusionLayerNames = { "Default", "Ground", "Interior" };

        /// <summary>
        /// Solid geometry as vision understands it. Anything else that asks "is there world here" —
        /// a sky vessel probing for ground and headroom — uses this rather than its own layer list.
        /// </summary>
        public static LayerMask SolidGeometryLayers => solidGeometryLayers ??= LayerMask.GetMask(FallbackOcclusionLayerNames);

        // Layer names are project settings and never change at runtime; resolved on first use.
        private static LayerMask? solidGeometryLayers;

        // Shared rather than one list per agent: HeadPointOf fills and consumes it in one call.
        private static readonly List<Collider> colliderBuffer = new List<Collider>(8);

        private void Awake()
        {
            noiseEmitter = GetComponent<NoiseEmitter>();
            prevPosition = transform.position;

            if (occlusionLayers == 0)
            {
                occlusionLayers = SolidGeometryLayers;
                Debug.LogWarning(
                    $"{name}: PerceptionModule.occlusionLayers is Nothing — line-of-sight would always " +
                    $"succeed. Falling back to [{string.Join(", ", FallbackOcclusionLayerNames)}]. " +
                    "Set the mask explicitly on the prefab to silence this.", this);
            }
        }

        private void Update()
        {
            if (HasLastKnownPosition)
                TimeSinceLastSeen += Time.deltaTime;

            if (TimeSinceLastSeen > memoryDuration)
            {
                HasLastKnownPosition = false;
                TimeSinceLastSeen = 0f;
            }

            isMoving = (transform.position - prevPosition).sqrMagnitude > 0.0001f;
            prevPosition = transform.position;
        }

        // Full perception check: FOV + LoS from the eye. Updates last-known memory when visible.
        // Only call this for the target the agent is actually committed to — see IsVisible().
        public bool CanSee(Transform target)
        {
            if (!IsVisible(target))
                return false;

            LastKnownPosition = target.position;
            HasLastKnownPosition = true;
            TimeSinceLastSeen = 0f;

            return true;
        }

        // FOV + LoS with no memory side effect. Use when testing candidates the agent has not
        // committed to: CanSee() writes LastKnownPosition, so scoring a crowd with it would
        // overwrite the memory of the target actually being tracked.
        public bool IsVisible(Transform target)
        {
            if (!target)
                return false;

            Vector3 origin = EyePosition;

            // FOV check — horizontalized so vertical offset (tall/short targets) doesn't exclude them,
            // and the direction comes from the root transform, not eyeTransform.forward.
            Vector3 flatForward = FlattenHorizontal(GetForward());
            Vector3 flatToTarget = FlattenHorizontal(target.position - origin);
            if (flatForward.sqrMagnitude < 1e-6f || flatToTarget.sqrMagnitude < 1e-6f)
                return false;

            // Effective FOV: base cone, optionally widened while moving. No per-frame sweep bonus —
            // it produced erratic detection during fast turns.
            float effectiveFov = fieldOfViewAngle + (isMoving ? movingFovBonus : 0f);
            if (Vector3.Angle(flatForward, flatToTarget) > effectiveFov * 0.5f)
                return false;

            return CanSightReach(origin, target);
        }

        // LoS from the eye only — no FOV, no memory update. Body or head, like IsVisible.
        public bool HasLineOfSight(Transform target) => CanSightReach(EyePosition, target);

        // An eye sees a target when either its body or its head is unobstructed, so waist-high cover
        // does not hide a standing player. The head ray is cast only when the body ray was blocked.
        // Deliberately not folded into HasLineOfSightFrom: a muzzle check asks whether a shot aimed
        // at the BODY lands, and a visible head over a wall is exactly when it would not.
        private bool CanSightReach(Vector3 origin, Transform target)
        {
            return HasLineOfSightFrom(origin, target)
                   || IsUnobstructed(origin, HeadPointOf(target), target);
        }

        /// <summary>
        /// The point on <paramref name="target"/> a sight line is aimed at: the centre of its root
        /// collider, or targetAimHeight above its origin. Never the origin itself, which is on the
        /// ground (see the field).
        /// </summary>
        public Vector3 AimPointOf(Transform target)
        {
            if (target.TryGetComponent(out Collider body))
                return body.bounds.center;
            return target.position + Vector3.up * targetAimHeight;
        }

        /// <summary>
        /// The top of the target's first solid collider, less headInset, or headAimHeight above its
        /// origin. Searches children, unlike <see cref="AimPointOf"/>: the player's capsule is on a
        /// child called "Collider", not on the root.
        /// </summary>
        private Vector3 HeadPointOf(Transform target)
        {
            target.GetComponentsInChildren(colliderBuffer);
            Vector3 head = target.position + Vector3.up * headAimHeight;
            foreach (Collider body in colliderBuffer)
            {
                if (!body.enabled || body.isTrigger)
                    continue;
                Bounds bounds = body.bounds;
                float top = Mathf.Max(bounds.center.y, bounds.max.y - headInset);
                head = new Vector3(bounds.center.x, top, bounds.center.z);
                break;
            }
            colliderBuffer.Clear();
            return head;
        }

        // LoS to the target's body from an arbitrary origin (e.g. a weapon muzzle). Ignores hits on
        // self and the target itself.
        public bool HasLineOfSightFrom(Vector3 origin, Transform target)
        {
            if (!target)
                return false;

            return IsUnobstructed(origin, AimPointOf(target), target);
        }

        private bool IsUnobstructed(Vector3 origin, Vector3 point, Transform target)
        {
            Vector3 toTarget = point - origin;
            float distance = toTarget.magnitude;
            if (distance < 1e-4f)
                return true;

            Vector3 dir = toTarget / distance;

            // RaycastAll does NOT sort by distance -- Unity returns hits in whatever order the
            // physics broadphase produced them. Deciding the verdict on the first non-self
            // element therefore asked "is some arbitrary collider on this line the target?"
            // rather than "is the target the FIRST thing on this line". With the player on layer
            // 0, which is inside every agent's occlusion mask, a wall and the player both hit;
            // whenever the player happened to come back first the agent acquired and fired
            // straight through the wall. Intermittent, because the order is not stable -- which
            // is why it read as "the robots sometimes shoot through walls".
            //
            // Triggers are ignored: the project queries them by default (queriesHitTriggers), and a
            // trigger is never a wall. A ship's breathable-air volume hid a player 370 m away from
            // twelve of fifteen NPCs; interaction zones and streaming volumes would do the same.
            RaycastHit[] hits = Physics.RaycastAll(origin, dir, distance, occlusionLayers,
                                                   QueryTriggerInteraction.Ignore);
            Transform blocker = null;
            float blockerDistance = float.PositiveInfinity;
            for (int i = 0; i < hits.Length; i++)
            {
                Transform t = hits[i].transform;
                if (t == transform || t.IsChildOf(transform))
                    continue;
                if (hits[i].distance >= blockerDistance)
                    continue;
                blockerDistance = hits[i].distance;
                blocker = t;
            }

            // Nothing in the way, or the nearest thing in the way IS the target.
            return blocker == null || blocker == target || blocker.IsChildOf(target);
        }

        /// <summary>
        /// Restore-only. Called by the save system; do not call from gameplay.
        ///
        /// This component keeps a second copy of the same memory <c>AgentTargeting</c> keeps, written
        /// from <see cref="CanSee"/>. Only one of them is persisted — AgentTargeting's, which is the
        /// authority, since it is the caller that decides when <see cref="CanSee"/> runs at all. This
        /// method exists so <c>AgentStateSaveable</c> can push that one answer into both, rather than
        /// letting a second saver restore a copy that could disagree with the first.
        ///
        /// The elapsed time is clamped to this component's own <c>memoryDuration</c>, which may be
        /// shorter than the targeting profile's — a memory this module would already have dropped
        /// must not come back alive.
        /// </summary>
        public void RestoreMemory(Vector3 lastKnownPosition, bool hasLastKnownPosition, float timeSinceLastSeen)
        {
            LastKnownPosition = lastKnownPosition;
            HasLastKnownPosition = hasLastKnownPosition;
            TimeSinceLastSeen = hasLastKnownPosition
                ? Mathf.Clamp(timeSinceLastSeen, 0f, memoryDuration)
                : 0f;
        }

        // Call when a target is spotted for the first time to alert nearby allies.
        public void NotifySpotted(Transform target)
        {
            if (emitNoiseOnSpot && noiseEmitter)
                noiseEmitter.Emit(NoiseType.Alert, spotNoiseRadius);

            if (playSpotSound)
                Sfx.Play(spotId, transform.position, spotSound, GetInstanceID());
        }

        private Vector3 GetForward() => transform.forward;

        private static Vector3 FlattenHorizontal(Vector3 v)
        {
            v.y = 0f;
            return v;
        }

        private void OnValidate()
        {
            fieldOfViewAngle = Mathf.Clamp(fieldOfViewAngle, 1f, 360f);
            eyeHeight = Mathf.Max(0f, eyeHeight);
            targetAimHeight = Mathf.Max(0f, targetAimHeight);
            headAimHeight = Mathf.Max(0f, headAimHeight);
            headInset = Mathf.Max(0f, headInset);
            memoryDuration = Mathf.Max(0f, memoryDuration);
            spotNoiseRadius = Mathf.Max(0f, spotNoiseRadius);
        }

        private void OnDrawGizmosSelected()
        {
            Vector3 origin = EyePosition;
            Vector3 forward = FlattenHorizontal(GetForward());
            if (forward.sqrMagnitude < 1e-6f)
                return;
            forward.Normalize();
            float half = fieldOfViewAngle * 0.5f;

            Gizmos.color = Color.yellow;
            Gizmos.DrawRay(origin, Quaternion.Euler(0, half, 0) * forward * 5f);
            Gizmos.DrawRay(origin, Quaternion.Euler(0, -half, 0) * forward * 5f);
        }
    }
}
