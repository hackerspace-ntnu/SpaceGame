using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// Where the net is, at a given moment after the trigger.
    ///
    /// <para>
    /// Closed form and pure, so that every machine can draw the same flight from the same three
    /// inputs without a single message being sent about it. The alternative — each machine
    /// integrating the path frame by frame — diverges by exactly as much as their frame rates
    /// differ, which is the divergence the Dragon Bazooka's seeded closed-form wander was written
    /// to avoid.
    /// </para>
    /// <para>
    /// The seed only perturbs the flight; it never decides the outcome. What gets CAUGHT is the
    /// server's to say, for the reason <c>NetMsg.LassoRoped</c> documents: two machines integrating
    /// one arc at different frame rates can pick different creatures out of a crowd.
    /// </para>
    /// <para>
    /// The net is CARRIED along this arc rather than launched onto it. Handing the lattice a muzzle
    /// velocity and letting its own integrator fly it would also work, and would diverge: the
    /// lattice takes whole fixed substeps out of real frame deltas, so two machines take different
    /// numbers of them and their nets land in different places. Carrying the whole lattice by the
    /// difference between two samples of this keeps the arc identical everywhere while the unfurl
    /// and the bloom still play out underneath it.
    /// </para>
    /// </summary>
    public static class NetGunFlight
    {
        // ── The arc ────────────────────────────────────────────────────────────
        //
        // Tuned for the sensation rather than for a plausible thrown mass (GDC-L1-FEEL-0007), and
        // the sensation wanted here is a short-range capture tool that goes roughly where the
        // crosshair is. These were originally 26 m/s under 16 m/s^2 for 1.6 s, which is a 42 m
        // range with a 20 m drop — a mortar. At a normal 15 m engagement the net landed nearly
        // three metres below the point the player aimed at, and no amount of leading it reads as
        // anything but the gun being broken.
        //
        // The drop is what has to stay small, because it is the part the player cannot see coming.
        // Range is bounded by the flight time instead, which is honest: the net visibly stops.

        /// <summary>Metres per second the bundle leaves the canister.</summary>
        public const float MuzzleSpeed = 32f;

        /// <summary>
        /// Metres per second squared. Well under real gravity: enough that the net visibly arcs
        /// and cannot be used as a sniper rifle, little enough that inside its working range the
        /// crosshair is still where the net goes.
        /// </summary>
        public const float Gravity = 7f;

        /// <summary>
        /// Seconds after which the net is out of momentum and drops, even if it hit nothing.
        ///
        /// This is the RANGE limit — 27 m at the speed above — not a landing timer. A net that
        /// meets something before this lands there instead; see <c>SnareCatch.CarryAlongFlight</c>.
        /// </summary>
        public const float MaxFlightSeconds = 0.85f;

        /// <summary>Degrees of scatter the seed may add to the aim, in each axis.</summary>
        private const float Scatter = 0.6f;

        public static Vector3 PositionAt(Vector3 origin, Vector3 aim, int seed, float seconds)
        {
            Vector3 direction = Perturb(aim, seed);

            return origin
                 + direction * (MuzzleSpeed * seconds)
                 + Vector3.down * (0.5f * Gravity * seconds * seconds);
        }

        /// <summary>
        /// How fast, and which way, the net is going at that moment.
        ///
        /// <para>
        /// The derivative of <see cref="PositionAt"/>, and closed-form for the same reason it is:
        /// every machine needs the same answer without anything being sent. Two things read it.
        /// The net is kept SQUARE to this while it flies, so it leads with its face and tips
        /// forward as the arc falls away rather than sailing along as an upright pane; and on
        /// impact it is handed to the lattice as a real velocity, so the net collapses into what
        /// it hit instead of stopping dead in the air and dropping straight down.
        /// </para>
        /// </summary>
        public static Vector3 VelocityAt(Vector3 aim, int seed, float seconds)
        {
            return Perturb(aim, seed) * MuzzleSpeed + Vector3.down * (Gravity * seconds);
        }

        /// <summary>
        /// The aim, nudged by the seed.
        ///
        /// A hash of the seed rather than <c>Random</c>: <c>Random</c> carries global state that
        /// another system can advance between two machines' calls, and a flight that depends on
        /// global state is a flight two machines will disagree about.
        /// </summary>
        private static Vector3 Perturb(Vector3 aim, int seed)
        {
            Vector3 direction = aim.sqrMagnitude < 1e-4f ? Vector3.forward : aim.normalized;

            float yaw = (Hash(seed, 1) - 0.5f) * 2f * Scatter;
            float pitch = (Hash(seed, 2) - 0.5f) * 2f * Scatter;

            return Quaternion.Euler(pitch, yaw, 0f) * direction;
        }

        /// <summary>A stable 0-1 hash. Deterministic across machines and across runs.</summary>
        private static float Hash(int seed, int salt)
        {
            unchecked
            {
                int h = seed * 73856093 ^ salt * 19349663;
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0x7FFFFFFF) / (float)0x7FFFFFFF;
            }
        }
    }
}
