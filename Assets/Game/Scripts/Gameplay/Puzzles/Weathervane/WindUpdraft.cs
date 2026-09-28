// The gust out of the weathervane vent: step into it and it lifts you up the column, carries you
// across to the plateau, and sets you down.
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
    /// An owner-side lift column with a carry at the top.
    ///
    /// Order 200, the value FlungBody and LeashedBody use: PlayerMovement's FixedUpdate ASSIGNS
    /// horizontal velocity, so a carry written before it runs is lerped back to walking pace the
    /// same tick.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public class WindUpdraft : MonoBehaviour
    {
        private enum Phase { Idle, Rising, Carrying, Descending }

        [Header("Column")]
        [Tooltip("Horizontal radius of the column that picks a player up, around this transform.")]
        [SerializeField] private float columnRadius = 3.2f;

        [Tooltip("Height above this transform where the rise ends and the carry begins. " +
                 "Set it a few metres above the landing so the carry clears the rim.")]
        [SerializeField] private float liftHeight = 45f;

        [Tooltip("Climb speed the column settles a rider at, m/s.")]
        [SerializeField] private float riseSpeed = 14f;

        [Tooltip("How quickly the climb speed is reached, m/s². Low enough that stepping in reads " +
                 "as being caught by the wind rather than launched.")]
        [SerializeField] private float riseAcceleration = 22f;

        [Tooltip("How hard a rider drifting to the edge is drawn back to the axis, per second.")]
        [SerializeField] private float centering = 1.2f;

        [Tooltip("Leaving the column by this multiple of its radius during the rise drops the rider.")]
        [SerializeField] private float escapeRadiusFactor = 1.6f;

        [Header("Carry")]
        [Tooltip("Where the gust sets riders down.")]
        [SerializeField] private Transform landing;

        [Tooltip("Horizontal speed of the carry to the landing, m/s.")]
        [SerializeField] private float carrySpeed = 11f;

        [Tooltip("Within this horizontal distance of the landing the carry turns into a descent.")]
        [SerializeField] private float landingRadius = 4f;

        [Tooltip("Sink rate while being set down, m/s. Under PlayerMovement's fall-damage threshold.")]
        [SerializeField] private float descentSpeed = 3.5f;

        [Tooltip("How firmly the carry holds its altitude, per second.")]
        [SerializeField] private float altitudeHold = 2f;

        [Header("Presentation")]
        [Tooltip("Played while the gust is open, on every machine.")]
        [SerializeField] private ParticleSystem[] gustEffects;

        private bool open;
        private Phase phase;
        private PlayerController rider;
        private Rigidbody body;
        private PlayerMovement movement;

        public bool IsOpen => open;

        /// <summary>
        /// Open or shut the gust. Called by the ring on every machine. <paramref name="instant"/> —
        /// the state predates this machine looking — skips the effects' warm-up.
        /// </summary>
        public void SetOpen(bool value, bool instant)
        {
            open = value;
            if (!open) phase = Phase.Idle;

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

        private void OnDisable() => phase = Phase.Idle;

        private void FixedUpdate()
        {
            if (!open || !ResolveRider()) { phase = Phase.Idle; return; }

            Vector3 p = body.position;
            Vector3 vent = transform.position;
            float height = p.y - vent.y;
            Vector3 toAxis = Flat(vent - p);
            Vector3 toLanding = Flat(landing.position - p);

            phase = Next(phase, toAxis.magnitude, height, toLanding.magnitude);
            if (phase == Phase.Idle) return;

            // Every speed below is the one the body should MOVE at this step. Gravity is integrated
            // after FixedUpdate, so it is added back here or every phase sags by a step's worth.
            float gravityStep = body.useGravity ? -Physics.gravity.y * Time.fixedDeltaTime : 0f;
            Vector3 v = body.linearVelocity;

            switch (phase)
            {
                case Phase.Rising:
                    v.y = Mathf.MoveTowards(v.y, riseSpeed, riseAcceleration * Time.fixedDeltaTime);
                    v += toAxis * (centering * Time.fixedDeltaTime);
                    break;

                case Phase.Carrying:
                    v.y = Mathf.Clamp((liftHeight - height) * altitudeHold, -riseSpeed, riseSpeed);
                    Vector3 carry = toLanding.normalized * carrySpeed;
                    v.x = carry.x;
                    v.z = carry.z;
                    break;

                case Phase.Descending:
                    v.y = -descentSpeed;
                    v.x = 0f;
                    v.z = 0f;
                    break;
            }

            v.y += gravityStep;
            body.linearVelocity = v;
        }

        private Phase Next(Phase current, float fromAxis, float height, float fromLanding)
        {
            switch (current)
            {
                case Phase.Idle:
                    return fromAxis <= columnRadius && height > -1f && height < liftHeight
                        ? Phase.Rising : Phase.Idle;

                case Phase.Rising:
                    if (fromAxis > columnRadius * escapeRadiusFactor) return Phase.Idle;
                    return height >= liftHeight ? Phase.Carrying : Phase.Rising;

                case Phase.Carrying:
                    return fromLanding <= landingRadius ? Phase.Descending : Phase.Carrying;

                case Phase.Descending:
                    return movement.IsOnGround ? Phase.Idle : Phase.Descending;
            }
            return Phase.Idle;
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
                movement = local != null ? local.GetComponent<PlayerMovement>() : null;
                phase = Phase.Idle;
            }
            return body != null && movement != null && !body.isKinematic;
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        private void OnDrawGizmosSelected()
        {
            Vector3 vent = transform.position;
            Gizmos.color = new Color(0.6f, 0.9f, 1f, 0.8f);
            for (int i = 0; i < 24; i++)
            {
                float a0 = i * Mathf.PI * 2f / 24f, a1 = (i + 1) * Mathf.PI * 2f / 24f;
                Vector3 r0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * columnRadius;
                Vector3 r1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * columnRadius;
                Gizmos.DrawLine(vent + r0, vent + r1);
                Gizmos.DrawLine(vent + r0 + Vector3.up * liftHeight, vent + r1 + Vector3.up * liftHeight);
            }
            if (landing == null) return;
            Vector3 top = vent + Vector3.up * liftHeight;
            Gizmos.DrawLine(top, new Vector3(landing.position.x, top.y, landing.position.z));
            Gizmos.DrawWireSphere(landing.position, landingRadius);
        }
    }
}
