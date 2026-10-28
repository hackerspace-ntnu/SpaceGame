// The gust out of the weathervane vent: step into it and the wind throws you up the column. Nothing
// carries you anywhere — you steer yourself off the top and drift down onto the plateau.
//
// Runs on EVERY machine, and on each one moves only that machine's own player. The player body is
// owner-authoritative — a push written anywhere else is overwritten by the owner's next state
// update, with nothing in the console — so the only thing that travels is whether the gust is open
// (WeathervaneRing), and each player's own machine flies its own body. Everyone else watches them
// rise through the transform sync that is already running.
using SpaceGame.Characters;
using SpaceGame.Presentation;
using UnityEngine;

namespace SpaceGame.Gameplay.Puzzles
{
    /// <summary>
    /// An owner-side updraft: a column that drags the rider's climb toward the wind's speed, and a
    /// wider crown round it that slows any fall to a safe sink.
    ///
    /// Only <c>linearVelocity.y</c> is ever written. Horizontal speed is PlayerMovement's air control,
    /// so the rider steers through the whole flight exactly as they would after a jump.
    ///
    /// Order 200, the value FlungBody and LeashedBody use, so the lift and the sink cap read the
    /// velocity PlayerMovement has already written this tick and have the last word on it.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public class WindUpdraft : MonoBehaviour
    {
        [Header("Column")]
        [Tooltip("Horizontal radius of the column that lifts a player, around this transform.")]
        [SerializeField] private float columnRadius = 3.2f;

        [Tooltip("Height above this transform where the wind has died away. Riders bob a little " +
                 "below it, where the lift left over balances gravity.")]
        [SerializeField] private float liftHeight = 62f;

        [Tooltip("Speed of the wind up the column, m/s. A rider climbs somewhat slower than this: " +
                 "gravity takes windSpeed − g/windGrip off it.")]
        [SerializeField] private float windSpeed = 20f;

        [Tooltip("How hard the wind drags the rider's climb toward its own speed, per second. The " +
                 "kick on stepping in is windGrip × windSpeed − g.")]
        [SerializeField] private float windGrip = 3f;

        [Tooltip("Metres below liftHeight over which the wind fades out, so the top of the ride is " +
                 "a soft bob rather than a ceiling.")]
        [SerializeField] private float liftFade = 10f;

        [Header("Crown")]
        [Tooltip("Within this horizontal distance of the vent, the gust spilling off the top slows " +
                 "any fall. Reaches past the plateau's rim so a rider who steers off the top lands.")]
        [SerializeField] private float reachRadius = 34f;

        [Tooltip("Fastest fall the crown allows, m/s. Under PlayerMovement's fall-damage threshold.")]
        [SerializeField] private float sinkSpeed = 3.5f;

        [Header("Presentation")]
        [Tooltip("Played while the gust is open, on every machine.")]
        [SerializeField] private ParticleSystem[] gustEffects;

        private bool open;
        private PlayerController rider;
        private Rigidbody body;

        public bool IsOpen => open;

        /// <summary>
        /// Open or shut the gust. Called by the ring on every machine. <paramref name="instant"/> —
        /// the state predates this machine looking — skips the effects' warm-up.
        /// </summary>
        public void SetOpen(bool value, bool instant)
        {
            open = value;

            foreach (ParticleSystem effect in gustEffects)
            {
                if (open)
                {
                    if (instant) effect.Simulate(effect.main.duration, true, true);
                    effect.Play(true);
                }
                else
                {
                    effect.Stop(true, instant ? ParticleSystemStopBehavior.StopEmittingAndClear
                                              : ParticleSystemStopBehavior.StopEmitting);
                }
            }
        }

        private void FixedUpdate()
        {
            if (!open || !ResolveRider()) return;

            Vector3 p = body.position;
            Vector3 vent = transform.position;
            float height = p.y - vent.y;
            float fromAxis = new Vector2(p.x - vent.x, p.z - vent.z).magnitude;
            if (height < -1f || fromAxis > reachRadius) return;

            Vector3 v = body.linearVelocity;
            if (fromAxis <= columnRadius && height < liftHeight)
            {
                // A drag toward the wind's speed, not a set speed: gravity still acts, so the climb
                // settles below the wind, and the rider keeps whatever they carried in.
                float wind = windSpeed * Mathf.Clamp01((liftHeight - height) / liftFade);
                v.y += (wind - v.y) * windGrip * Time.fixedDeltaTime;
            }
            else if (v.y < -sinkSpeed)
            {
                v.y = -sinkSpeed;
            }
            body.linearVelocity = v;
        }

        /// <summary>
        /// This machine's own player, if it has a body the gust may move. A mounted player, a
        /// ragdoll or a remote replica is kinematic, and is left alone.
        /// </summary>
        private bool ResolveRider()
        {
            PlayerController local = GameplayMenuScope.FindLocalPlayer();
            if (local != rider)
            {
                rider = local;
                body = local != null ? local.GetComponent<Rigidbody>() : null;
            }
            return body != null && !body.isKinematic;
        }

        private void OnDrawGizmosSelected()
        {
            Vector3 vent = transform.position;
            Gizmos.color = new Color(0.6f, 0.9f, 1f, 0.8f);
            DrawCircle(vent, columnRadius);
            DrawCircle(vent + Vector3.up * liftHeight, columnRadius);
            DrawCircle(vent + Vector3.up * (liftHeight - liftFade), columnRadius);
            Gizmos.color = new Color(0.6f, 0.9f, 1f, 0.35f);
            DrawCircle(vent + Vector3.up * liftHeight, reachRadius);
        }

        private static void DrawCircle(Vector3 centre, float radius)
        {
            for (int i = 0; i < 32; i++)
            {
                float a0 = i * Mathf.PI * 2f / 32f, a1 = (i + 1) * Mathf.PI * 2f / 32f;
                Gizmos.DrawLine(centre + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * radius,
                                centre + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * radius);
            }
        }
    }
}
