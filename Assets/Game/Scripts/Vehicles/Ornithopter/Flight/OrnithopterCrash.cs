// What arriving at a surface costs the pilot.
//
// Split out of the motor for the same reason OrnithopterFlightModel is: "does this hurt, and how
// much" is arithmetic, and arithmetic living inside a MonoBehaviour can only be checked by flying a
// prefab into a cliff and squinting at the health bar.
//
// The one idea worth holding on to is CLOSING speed. A crash is not about how fast the craft was
// going — it is about how fast it was going TOWARDS the thing it hit. Flying flat out at 30 m/s a
// metre above the sand is not a crash; touching the same sand at 30 m/s straight down is. Both
// touchdown paths measure the same quantity, so a shallow glide-in is free and a dive is not,
// without either path needing a special case.
using System;
using UnityEngine;

namespace SpaceGame.Vehicles.Ornithopter
{
    /// <summary>Tuning for what a touchdown does to the pilot, and where it leaves them.</summary>
    [Serializable]
    public class OrnithopterCrashConfig
    {
        [Header("Impact")]
        [Tooltip("Closing speed a touchdown can carry for free, m/s. A wing flown onto the ground " +
                 "properly arrives at only a few m/s vertically, so this is what separates a landing " +
                 "from a crash — raise it and the machine forgives sloppier arrivals.")]
        [Min(0f)] public float SafeClosingSpeed = 8f;

        [Tooltip("Closing speed that does the full MaxDamage, m/s. The craft glides in the low " +
                 "twenties and dives well past forty, so a pilot who holds a dive into the ground " +
                 "should reach this and a pilot who flares should not.")]
        [Min(0.1f)] public float LethalClosingSpeed = 32f;

        [Tooltip("Damage at LethalClosingSpeed and beyond. The player prefab carries 100 health, so " +
                 "the default makes a full-speed nose-in fatal from full health.")]
        [Min(0)] public int MaxDamage = 100;

        [Header("Recovery")]
        [Tooltip("How far below the impact to look for ground to stand the pilot on. Beyond this " +
                 "they are simply released at the crash site and fall — which is the right answer " +
                 "for flying into a cliff face two hundred metres up, where teleporting them to the " +
                 "valley floor would be worse than the fall.")]
        [Min(0f)] public float GroundSearchDistance = 12f;

        [Tooltip("How far out of the surface just hit to step before looking for ground, m. Without " +
                 "it the search starts inside the rock the craft is embedded in.")]
        [Min(0f)] public float SurfaceClearance = 0.6f;
    }

    /// <summary>How a flight ended: where the craft met the world, how hard, and where that leaves the pilot.</summary>
    public readonly struct OrnithopterTouchdown
    {
        /// <summary>Where the craft met the surface, in world space.</summary>
        public readonly Vector3 ContactPoint;

        /// <summary>Surface normal at the contact, pointing out of whatever was hit.</summary>
        public readonly Vector3 SurfaceNormal;

        /// <summary>How fast the craft was closing on that surface, m/s. Never negative.</summary>
        public readonly float ClosingSpeed;

        /// <summary>
        /// Somewhere solid to stand the pilot, already resolved against the world. Falls back to the
        /// contact point stepped clear of the surface when no ground was within reach.
        /// </summary>
        public readonly Vector3 GroundPosition;

        /// <summary>
        /// True when the craft flew into something, false when it settled onto ground beneath it.
        /// The damage does not read this — closing speed already says how bad it was — but the
        /// distinction is worth carrying for audio and for anything that wants to react.
        /// </summary>
        public readonly bool WasImpact;

        public OrnithopterTouchdown(Vector3 contactPoint, Vector3 surfaceNormal, float closingSpeed,
                                    Vector3 groundPosition, bool wasImpact)
        {
            ContactPoint = contactPoint;
            SurfaceNormal = surfaceNormal;
            ClosingSpeed = closingSpeed;
            GroundPosition = groundPosition;
            WasImpact = wasImpact;
        }
    }

    /// <summary>The crash, as pure functions. No MonoBehaviour, no Transform, no Time.</summary>
    public static class OrnithopterCrash
    {
        /// <summary>
        /// How fast <paramref name="velocity"/> is closing on a surface with the given outward
        /// normal, m/s. Zero when the craft is moving along the surface or away from it, so a
        /// wingtip dragged down a rock wall is not treated as a head-on hit.
        /// </summary>
        public static float ClosingSpeed(Vector3 velocity, Vector3 surfaceNormal)
        {
            // A degenerate normal means nothing useful can be projected onto it. Charging the full
            // speed is the safe reading: the alternative is a crash that silently costs nothing.
            if (surfaceNormal.sqrMagnitude < 1e-6f)
                return velocity.magnitude;

            return Mathf.Max(0f, Vector3.Dot(-velocity, surfaceNormal.normalized));
        }

        /// <summary>
        /// Damage the pilot takes for arriving at a surface at <paramref name="closingSpeed"/>.
        /// Zero at or below the safe speed, <see cref="OrnithopterCrashConfig.MaxDamage"/> at or
        /// above the lethal one.
        /// </summary>
        public static int ImpactDamage(float closingSpeed, OrnithopterCrashConfig cfg)
        {
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));

            float safe = Mathf.Max(0f, cfg.SafeClosingSpeed);
            float lethal = Mathf.Max(safe + 0.1f, cfg.LethalClosingSpeed);

            if (closingSpeed <= safe || cfg.MaxDamage <= 0)
                return 0;

            // Energy, not speed. What a crash has to get rid of is ½mv², so doubling the speed
            // quadruples the cost. A linear ramp makes the middle of the range hurt too much and the
            // top of it too little — the opposite of what the pilot can see coming out of the cradle.
            float excess = closingSpeed * closingSpeed - safe * safe;
            float span = lethal * lethal - safe * safe;
            float t = Mathf.Clamp01(excess / span);

            // Floor of 1: past the safe speed something always happens. A crash that rounds down to
            // "no damage" reads as the system being broken rather than as a lucky landing.
            return Mathf.Max(1, Mathf.RoundToInt(t * cfg.MaxDamage));
        }
    }
}
