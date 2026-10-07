// The warning on the ground: where a bolt is about to fall, and how long is left.
//
// ConjurerCastModule spawns this when a cast begins and destroys it when the bolt
// lands, so like ConjurerStaffCharge its whole lifetime is owned by the caster and
// it never decides when to stop.
//
// ---- why this exists at all ---------------------------------------------------
//
// The conjurer's attack drops lightning out of the sky onto the target rather than
// firing it along a line. That is an unblockable attack: no wall, no cover, no
// dodge behind something. The ONLY counterplay it can have is moving off the spot,
// and a player can only move off a spot they can see. Without this the wind-up is
// a creature holding a staff up, and the first information the player gets about
// where the bolt is going is the bolt.
//
// So this is not decoration, and the two things it draws are the two things the
// player has to read:
//
//   WHERE   the ring, on the ground, at the blast radius. Not smaller: a ring that
//           does not mean "everything inside this is hit" teaches the wrong lesson
//           the first time somebody stands just outside it and dies anyway.
//   WHEN    the column above it, descending. A ring that only brightens says
//           "something is coming"; a mark falling out of the sky says how long is
//           left, which is what decides whether you walk or sprint.
//
// ---- tracking, and the moment it stops ---------------------------------------
//
// The ring FOLLOWS its victim for most of the wind-up and then LOCKS. That is the
// whole fight: standing still is punished, and a late move beats it. Freeze() is
// what the caster calls at the lock, and the visual change there is deliberately
// loud -- the ring snaps wider, the light turns white and the pulse doubles --
// because a lock the player cannot see is a lock they cannot play against.
//
// Every machine runs its own copy and reaches the lock on its own clock, from the
// same cast start and the same authored duration. Nothing about this is sent: it
// is a picture, the server owns the damage, and a peer whose ring is a frame out
// of step with the host's is not a problem worth a message per frame.
using UnityEngine;

namespace SpaceGame.Gameplay
{
    public class StrikeTelegraph : MonoBehaviour
    {
        [Header("Parts")]
        [Tooltip("The flat ring laid on the ground. Scaled to the blast radius.")]
        [SerializeField] private Transform ring;

        [Tooltip("The column of glow above the ring. Its height is driven down over " +
                 "the wind-up, so the mark visibly falls out of the sky.")]
        [SerializeField] private Transform column;

        [SerializeField] private Light glow;

        [Header("Timing")]
        [Tooltip("Seconds from the cast beginning to the bolt landing. The caster " +
                 "sets this from the Attack clip's fire frame.")]
        [SerializeField] private float warningSeconds = 4f;

        [Header("Shape")]
        [Tooltip("Radius of the ring in metres. Must match the caster's blast radius " +
                 "or the warning lies about what it covers.")]
        [SerializeField] private float radius = 3.5f;

        [Tooltip("How high the descending column starts, in metres.")]
        [SerializeField] private float columnHeight = 60f;

        [Tooltip("Turns per second. Slow while tracking; doubled once locked.")]
        [SerializeField] private float spin = 0.35f;

        [Header("Ground")]
        [Tooltip("The ring is dropped onto whatever is under the aim point, so it " +
                 "lies on the floor rather than at the target's hip height.")]
        [SerializeField] private LayerMask groundMask = ~0;

        [Tooltip("How far above and below the aim point to look for ground.")]
        [SerializeField] private float groundProbe = 40f;

        [Tooltip("Clearance above the ground, to keep the ring out of the surface " +
                 "it is lying on.")]
        [SerializeField] private float groundOffset = 0.06f;

        [Header("Intensity")]
        [SerializeField] private Color trackingColour = new Color(0.25f, 0.7f, 1f);
        [SerializeField] private Color lockedColour = new Color(0.85f, 0.95f, 1f);
        [SerializeField] private float startIntensity = 1.5f;
        [SerializeField] private float endIntensity = 9f;

        private Transform follow;
        private Vector3 point;
        private bool locked;
        private float elapsed;

        /// <summary>Where the ring is sitting, after the ground snap.</summary>
        public Vector3 Point => point;

        /// <summary>
        /// Start warning. <paramref name="target"/> may be null, in which case the ring
        /// stays wherever <paramref name="at"/> put it -- which is also what happens for
        /// the whole wind-up under an aim mode that commits up front.
        /// </summary>
        public void Begin(Vector3 at, Transform target, float seconds, float blastRadius)
        {
            follow = target;
            warningSeconds = Mathf.Max(0.01f, seconds);
            radius = Mathf.Max(0.01f, blastRadius);
            locked = false;
            elapsed = 0f;

            Place(at);
            Apply(0f);
        }

        /// <summary>Stop tracking. The bolt is committed to wherever the ring is now.</summary>
        public void Freeze()
        {
            if (locked) return;
            locked = true;
            follow = null;
        }

        private void Awake()
        {
            // Struck once up front so the first frame draws the ring at its real size
            // rather than at whatever scale the prefab was saved with.
            Apply(0f);
        }

        private void Update()
        {
            elapsed += Time.deltaTime;

            if (!locked && follow != null) Place(follow.position);

            Apply(warningSeconds > 0f ? Mathf.Clamp01(elapsed / warningSeconds) : 1f);
        }

        /// Drop the ring onto the ground under <paramref name="at"/>.
        ///
        /// The probe starts ABOVE the point rather than at it, because the aim point is
        /// a target's transform origin and that can be a metre off the floor or, for
        /// something on a slope, briefly inside it. Starting overhead and casting down
        /// finds the surface in both cases; a cast from the origin itself misses the
        /// floor entirely whenever the origin is already below it.
        private void Place(Vector3 at)
        {
            point = at;

            if (Physics.Raycast(at + Vector3.up * groundProbe, Vector3.down,
                                out RaycastHit hit, groundProbe * 2f, groundMask,
                                QueryTriggerInteraction.Ignore))
                point = hit.point;

            transform.position = point + Vector3.up * groundOffset;
        }

        private void Apply(float t)
        {
            // Squared, so the last second escalates visibly. That acceleration is the
            // part the player reads as "now", and a linear ramp reads as a light that
            // is simply on.
            float e = t * t;

            if (ring != null)
            {
                ring.localScale = new Vector3(radius, 1f, radius);
                ring.localRotation = Quaternion.Euler(
                    0f, elapsed * spin * (locked ? 2f : 1f) * 360f, 0f);
            }

            if (column != null)
            {
                // Falls to nothing as the strike arrives, so the mark reads as coming
                // DOWN. Scaled rather than moved: the column's base stays welded to the
                // ring, which is the point it is describing.
                float h = Mathf.Max(0.01f, columnHeight * (1f - e));
                column.localScale = new Vector3(radius * 0.55f, h * 0.5f, radius * 0.55f);
                column.localPosition = new Vector3(0f, h * 0.5f, 0f);
            }

            if (glow != null)
            {
                glow.color = locked ? lockedColour : trackingColour;
                glow.intensity = Mathf.Lerp(startIntensity, endIntensity, e)
                                 * (locked ? 1.4f : 1f);
                glow.range = Mathf.Max(1f, radius * 3f);
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = locked ? Color.white : new Color(0.25f, 0.7f, 1f);
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
