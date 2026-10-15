using UnityEngine;

namespace SpaceGame.Gameplay.SpriteHamlet
{
    /// <summary>
    /// Footprints pressed into the grass behind the hidden witch: left, right, left, fading as she
    /// goes — the trail a player follows to find somebody they cannot see.
    ///
    /// <para>
    /// Drawn only while her <see cref="ShimmerCloak"/> hides her; a visible witch needs no trail.
    /// Every machine lays its own from her replicated motion, so nothing is sent. The prints are
    /// particles laid flat on the ground through one world-space <see cref="ParticleSystem"/> —
    /// no object per footprint — with a small flick of grass blades from a second, optional one.
    /// </para>
    /// </summary>
    public class FootprintTrail : MonoBehaviour
    {
        [SerializeField] private ShimmerCloak cloak;

        [Tooltip("World-space system with a horizontal-billboard renderer; one particle per print.")]
        [SerializeField] private ParticleSystem prints;

        [Tooltip("A puff of grass blades kicked up at each print. Optional.")]
        [SerializeField] private ParticleSystem kicks;

        [Tooltip("Grass blades emitted per print.")]
        [SerializeField, Min(0)] private int kickCount = 4;

        [Tooltip("Metres travelled between prints.")]
        [SerializeField, Min(0.1f)] private float strideLength = 0.75f;

        [Tooltip("How far either side of her path a print lands, metres.")]
        [SerializeField, Min(0f)] private float stanceWidth = 0.14f;

        [Tooltip("Seconds a print stays before it has faded.")]
        [SerializeField, Min(0.1f)] private float printSeconds = 5f;

        [Tooltip("Length of a print, metres.")]
        [SerializeField, Min(0.01f)] private float printSize = 0.35f;

        [Tooltip("What a print lies on.")]
        [SerializeField] private LayerMask groundMask = ~0;

        [Tooltip("Clearance above the ground, to keep the print out of the surface.")]
        [SerializeField, Min(0f)] private float groundOffset = 0.03f;

        private Vector3 lastPrint;
        private bool placed;
        private bool leftFoot;

        private void Update()
        {
            if (cloak == null || prints == null || !cloak.IsCloaked)
            {
                placed = false;
                return;
            }

            Vector3 here = transform.position;
            if (!placed)
            {
                lastPrint = here;
                placed = true;
                return;
            }

            Vector3 travelled = here - lastPrint;
            travelled.y = 0f;
            if (travelled.magnitude < strideLength) return;

            Print(here, travelled.normalized);
            lastPrint = here;
        }

        private void Print(Vector3 at, Vector3 heading)
        {
            leftFoot = !leftFoot;
            Vector3 side = Vector3.Cross(Vector3.up, heading) * (leftFoot ? -stanceWidth : stanceWidth);
            Vector3 point = at + side;

            if (Physics.Raycast(point + Vector3.up, Vector3.down, out RaycastHit ground, 3f, groundMask,
                                QueryTriggerInteraction.Ignore))
                point = ground.point;

            // A horizontal billboard turns about the vertical by its rotation: the print points the
            // way she was going.
            float yaw = Mathf.Atan2(heading.x, heading.z) * Mathf.Rad2Deg;
            var print = new ParticleSystem.EmitParams
            {
                position = point + Vector3.up * groundOffset,
                rotation = yaw,
                startSize = printSize,
                startLifetime = printSeconds,
                applyShapeToPosition = false,
            };
            prints.Emit(print, 1);

            if (kicks != null && kickCount > 0)
            {
                var kick = new ParticleSystem.EmitParams { position = point + Vector3.up * 0.05f, applyShapeToPosition = true };
                kicks.Emit(kick, kickCount);
            }
        }
    }
}
