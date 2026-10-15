using System.Collections;
using UnityEngine;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// A slow arc round a subject, from the side the player was watching from: the shot for a moment
    /// that happens TO somebody else — the keepers swarming the witch's hat and pulling her last herb
    /// out (SpriteHamletQuest.md, "Witch").
    ///
    /// <para>
    /// Starts behind the player's own line of sight to the subject, so the cut into it does not
    /// jump the viewer across the clearing, and swings <see cref="sweepDegrees"/> round from there.
    /// Rotation and position only; the world goes on underneath, so what it frames is whatever the
    /// server is making happen — local presentation, nothing on the wire.
    /// </para>
    /// </summary>
    public class OrbitCutscene : Cutscene
    {
        [Tooltip("What the shot circles. Its own transform when left empty.")]
        [SerializeField] private Transform focus;

        [SerializeField, Min(0.5f)] private float radius = 5.5f;

        [Tooltip("Camera height above the focus's feet, metres.")]
        [SerializeField] private float height = 2.4f;

        [Tooltip("Where on the focus the camera looks, metres above its feet.")]
        [SerializeField] private float lookHeight = 1.7f;

        [Tooltip("How far round the shot swings, degrees. Negative swings the other way.")]
        [SerializeField] private float sweepDegrees = 110f;

        [SerializeField, Min(0.1f)] private float duration = 6.5f;

        [Tooltip("How much closer it pushes in by the end, as a fraction of the radius.")]
        [SerializeField, Range(0f, 0.8f)] private float pushIn = 0.3f;

        [Tooltip("What the shot will not pass through — it slides in front of it instead.")]
        [SerializeField] private LayerMask occluders = ~0;

        [Tooltip("How far in front of an occluder the camera stops, metres.")]
        [SerializeField, Min(0f)] private float occluderClearance = 0.35f;

        /// <summary>Points along a candidate swing tested for a clear view.</summary>
        private const int SweepSamples = 6;

        private static readonly RaycastHit[] Hits = new RaycastHit[16];

        public override IEnumerator Play(CutsceneContext ctx)
        {
            Transform subject = focus != null ? focus : transform;
            Vector3 viewer = ctx.PlayerCamera != null ? ctx.PlayerCamera.transform.position : subject.position - subject.forward;
            Vector3 away = viewer - subject.position;
            away.y = 0f;
            float startAngle = away.sqrMagnitude > 0.01f ? Mathf.Atan2(away.x, away.z) * Mathf.Rad2Deg : subject.eulerAngles.y + 180f;
            float sweep = ClearestSweep(subject, startAngle);

            var shot = new CutsceneCamera(ctx.PlayerCamera);
            try
            {
                float elapsed = 0f;
                while (elapsed < duration && subject != null)
                {
                    elapsed += Time.unscaledDeltaTime;
                    float t = Mathf.Clamp01(elapsed / duration);
                    float k = Mathf.SmoothStep(0f, 1f, t);

                    float angle = startAngle + sweep * k;
                    float distance = radius * (1f - pushIn * k);
                    Vector3 centre = subject.position;
                    Vector3 look = centre + Vector3.up * lookHeight;
                    Vector3 position = centre + Quaternion.Euler(0f, angle, 0f) * Vector3.forward * distance + Vector3.up * height;
                    position = Unoccluded(subject, look, position);

                    shot.Camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(look - position, Vector3.up));
                    yield return null;
                }
            }
            finally
            {
                shot.Dispose();
            }
        }

        /// <summary>
        /// The swing to make: the authored one, the other way round, or less of either — the first
        /// whose whole arc stays clear of scenery at full radius. A subject standing against a wall
        /// (the witch at her door) would otherwise be filmed from inside it, or from a hand's breadth
        /// away once <see cref="Unoccluded"/> pulled the camera in. With no clear arc, no swing.
        /// </summary>
        private float ClearestSweep(Transform subject, float startAngle)
        {
            Vector3 look = subject.position + Vector3.up * lookHeight;
            foreach (float sweep in new[] { sweepDegrees, -sweepDegrees, sweepDegrees * 0.5f, -sweepDegrees * 0.5f })
            {
                bool clear = true;
                for (int i = 0; i <= SweepSamples && clear; i++)
                {
                    float angle = startAngle + sweep * i / SweepSamples;
                    Vector3 at = subject.position + Quaternion.Euler(0f, angle, 0f) * Vector3.forward * radius + Vector3.up * height;
                    clear = (Unoccluded(subject, look, at) - at).sqrMagnitude < 0.01f;
                }

                if (clear) return sweep;
            }

            return 0f;
        }

        /// <summary>
        /// <paramref name="wanted"/>, or the nearest point to it on the line from <paramref name="look"/>
        /// that is in front of the scenery — a witch at her door would otherwise be filmed from inside
        /// her hut. Only static scenery occludes: a body (anything with a Rigidbody, the subject and
        /// the keepers swarming it included) is what the shot is OF.
        /// </summary>
        private Vector3 Unoccluded(Transform subject, Vector3 look, Vector3 wanted)
        {
            Vector3 toCamera = wanted - look;
            float reach = toCamera.magnitude;
            if (reach < 0.01f) return wanted;

            Vector3 direction = toCamera / reach;
            int count = Physics.RaycastNonAlloc(look, direction, Hits, reach, occluders, QueryTriggerInteraction.Ignore);
            float nearest = reach;
            for (int i = 0; i < count; i++)
            {
                Collider hit = Hits[i].collider;
                if (hit.attachedRigidbody != null || hit.transform.IsChildOf(subject)) continue;
                if (Hits[i].distance > 0f) nearest = Mathf.Min(nearest, Hits[i].distance - occluderClearance);
            }

            return look + direction * Mathf.Max(0.5f, nearest);
        }
    }
}
