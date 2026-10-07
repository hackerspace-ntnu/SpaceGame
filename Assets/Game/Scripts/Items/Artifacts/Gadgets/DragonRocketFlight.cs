using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// Pure math for the dragon bazooka's rocket — the wandering flight path, and the burst that
    /// scatters the whelps.
    ///
    /// <para>
    /// <b>Every machine draws this rocket, so every machine must draw the SAME rocket.</b> The
    /// owner rolls one seed into the use message and everything erratic about the shot is derived
    /// from it here, free of scene and network state: the server bills the explosion the peers
    /// watched, rather than a second one that merely started in the same place.
    /// </para>
    /// <para>
    /// <b>Position is a closed-form function of elapsed time, not a random walk.</b> That is the
    /// load-bearing decision in this file. A per-frame jitter — the obvious way to write "moves
    /// like a bug" — is a function of the frame RATE, so a 144 Hz machine and a 60 Hz machine
    /// integrate different paths from identical seeds and the shot lands in two different places.
    /// Summed sinusoids in <see cref="Offset"/> have no such memory: ask for t = 1.4 s and you get
    /// the same answer on any machine, in any order, however many times you ask.
    /// </para>
    /// <para>
    /// The wander is added to the aim ray rather than replacing it, which is what keeps the
    /// weapon a weapon (GDC-L1-DESIGN-0006). Sinusoids average to zero, so the MEAN path is
    /// exactly where the player pointed and the chaos is spectacle around a line they authored.
    /// A free random walk would be more surprising and would also make every hit unattributable —
    /// the player could not tell a good shot from a lucky one, which is the point at which agency
    /// stops being felt and the weapon reads as a slot machine.
    /// </para>
    /// </summary>
    public static class DragonRocketFlight
    {
        /// <summary>
        /// Harmonics summed per lateral axis. Three is the whole trick: one gives a clean sine
        /// that reads as a scripted S-bend, two beat against each other visibly, and three
        /// incommensurable frequencies stop looking periodic long before the rocket's lifetime is
        /// up. More than three costs sin() calls for a difference nobody can see.
        /// </summary>
        private const int Harmonics = 3;

        /// <summary>
        /// Where the rocket is, <paramref name="t"/> seconds after leaving the muzzle.
        /// </summary>
        /// <param name="origin">Muzzle position.</param>
        /// <param name="aim">Rotation whose forward is the direction the player pointed.</param>
        public static Vector3 PositionAt(Vector3 origin, Quaternion aim, int seed, float t,
                                         float speed, float amplitude, float settleSeconds,
                                         float baseFrequency, float driftRate)
        {
            Vector2 lateral = Offset(seed, t, amplitude, settleSeconds, baseFrequency, driftRate);
            return origin + aim * new Vector3(lateral.x, lateral.y, speed * t);
        }

        /// <summary>
        /// Which way the rocket is travelling at <paramref name="t"/> — the analytic derivative of
        /// <see cref="PositionAt"/>, not a difference between two sampled positions.
        ///
        /// It is what the model is pointed along and what the exhaust plume trails from, so a
        /// finite difference here would make the rocket's nose stutter at low frame rates even
        /// though its path is smooth.
        /// </summary>
        public static Vector3 VelocityAt(Quaternion aim, int seed, float t, float speed,
                                         float amplitude, float settleSeconds,
                                         float baseFrequency, float driftRate)
        {
            Vector2 rate = OffsetRate(seed, t, amplitude, settleSeconds, baseFrequency, driftRate);
            return aim * new Vector3(rate.x, rate.y, speed);
        }

        /// <summary>
        /// The steady lean THIS rocket flies with, in metres per second across the aim.
        ///
        /// <para>
        /// The wander alone cannot make a shot go somewhere other than where it was pointed:
        /// sinusoids average to zero, so however violent the swerve, the rocket keeps coming back
        /// to the aim ray and converges on it. This is the term that lets an individual shot
        /// genuinely lean off-target — a random direction and a random amount, constant for the
        /// life of the rocket, so the flight reads as "this one has a mind of its own" rather than
        /// as noise about a line.
        /// </para>
        /// <para>
        /// It is deliberately drawn so that it averages to zero ACROSS seeds. One shot goes its
        /// own way; the weapon as a whole still shoots where it is pointed. That is what keeps it
        /// aimable at all (GDC-L1-DESIGN-0006) while making any single shot unreliable, which is
        /// the whole character of the item.
        /// </para>
        /// </summary>
        public static Vector2 Drift(int seed, float driftRate)
        {
            if (driftRate <= 0f) return Vector2.zero;

            float yaw = Unit(seed, 0x7F4A) * 2f * Mathf.PI;
            // Never less than a third of the authored lean: a drift that can roll near zero
            // produces the occasional suspiciously well-behaved shot, and one rocket in ten
            // flying straight reads as the effect being broken rather than as variety.
            float amount = Mathf.Lerp(0.35f, 1f, Unit(seed, 0x1B7D)) * driftRate;
            return new Vector2(Mathf.Cos(yaw), Mathf.Sin(yaw)) * amount;
        }

        /// <summary>
        /// How far off the aim ray the rocket has strayed, in the plane across it.
        ///
        /// <para>
        /// The envelope is what makes the shot readable. Amplitude eases in over
        /// <paramref name="settleSeconds"/>, so the rocket leaves the dragon's mouth going
        /// straight where the player aimed and only then starts misbehaving — the alternative,
        /// full swerve from frame one, reads as the gun having fired somewhere else entirely and
        /// costs the player the one moment where they can see their aim honoured
        /// (GDC-L1-DESIGN-0006). Squared rather than linear so the onset is soft at both ends.
        /// </para>
        /// </summary>
        public static Vector2 Offset(int seed, float t, float amplitude, float settleSeconds,
                                     float baseFrequency, float driftRate)
        {
            float shaped = Envelope(t, settleSeconds);
            if (shaped <= 0f) return Vector2.zero;

            float x = 0f, y = 0f;
            for (int k = 0; k < Harmonics; k++)
            {
                x += Amplitude(seed, k, 0) *
                     Mathf.Sin(Frequency(seed, k, 0, baseFrequency) * t + Phase(seed, k, 0));
                y += Amplitude(seed, k, 1) *
                     Mathf.Sin(Frequency(seed, k, 1, baseFrequency) * t + Phase(seed, k, 1));
            }

            // The lean rides the same envelope as the swerve, so the rocket still leaves the
            // muzzle exactly on the aim and only then starts going its own way.
            return new Vector2(x, y) * (shaped * amplitude)
                   + Drift(seed, driftRate) * (t * shaped);
        }

        /// <summary>
        /// d(<see cref="Offset"/>)/dt. The envelope is differentiated with it — dropping its term
        /// leaves the rocket's nose pointing wrong for exactly the stretch where the swerve is
        /// being introduced, which is the stretch the player is looking at.
        /// </summary>
        public static Vector2 OffsetRate(int seed, float t, float amplitude, float settleSeconds,
                                         float baseFrequency, float driftRate)
        {
            float shaped = Envelope(t, settleSeconds);
            float shapedRate = EnvelopeRate(t, settleSeconds);
            float envelope = shaped * amplitude;
            float envelopeRate = shapedRate * amplitude;

            float x = 0f, y = 0f, dx = 0f, dy = 0f;
            for (int k = 0; k < Harmonics; k++)
            {
                float ax = Amplitude(seed, k, 0), fx = Frequency(seed, k, 0, baseFrequency);
                float ay = Amplitude(seed, k, 1), fy = Frequency(seed, k, 1, baseFrequency);
                float px = Phase(seed, k, 0), py = Phase(seed, k, 1);

                x += ax * Mathf.Sin(fx * t + px);
                y += ay * Mathf.Sin(fy * t + py);
                dx += ax * fx * Mathf.Cos(fx * t + px);
                dy += ay * fy * Mathf.Cos(fy * t + py);
            }

            // Product rule twice over: the offset is envelope(t)*wave(t) + drift*t*envelope(t),
            // and every factor varies. Dropping either envelope term leaves the nose pointing
            // wrong across the settle ramp, which is the stretch the player is looking at.
            Vector2 drift = Drift(seed, driftRate) * (shaped + t * shapedRate);

            return new Vector2(envelopeRate * x + envelope * dx,
                               envelopeRate * y + envelope * dy) + drift;
        }

        /// <summary>
        /// The seed a burst child flies on.
        ///
        /// Derived from the parent's rather than rolled fresh, because the burst happens on every
        /// machine independently and nobody sends a message about it — a re-roll per machine would
        /// have four peers watching four different sets of whelps.
        /// </summary>
        public static int ChildSeed(int seed, int index) => Mix(seed, 0x9E37 + index * 101);

        /// <summary>
        /// Directions the whelps leave a burst in: a cone around <paramref name="axis"/>, spread
        /// evenly in yaw so the brood fans out instead of clumping.
        ///
        /// <para>
        /// Yaw is stratified — child <c>i</c> gets the <c>i</c>-th slice of the circle, jittered
        /// inside it — rather than drawn independently. Four independent draws land two whelps on
        /// top of each other about a third of the time, and a burst that fires two of its four
        /// rockets in the same direction reads as a bug rather than as a spread.
        /// </para>
        /// </summary>
        public static Vector3[] BurstDirections(int seed, Vector3 axis, int count, float spreadDeg)
        {
            var dirs = new Vector3[Mathf.Max(0, count)];
            if (dirs.Length == 0) return dirs;

            Quaternion frame = axis.sqrMagnitude > 1e-6f
                ? Quaternion.LookRotation(axis.normalized)
                : Quaternion.identity;

            for (int i = 0; i < dirs.Length; i++)
            {
                float slice = (i + Unit(seed, 0x51ED + i)) / dirs.Length;
                float yaw = slice * 2f * Mathf.PI;
                float tilt = Mathf.Lerp(spreadDeg * 0.35f, spreadDeg,
                                        Unit(seed, 0x2545 + i)) * Mathf.Deg2Rad;

                float sin = Mathf.Sin(tilt);
                dirs[i] = frame * new Vector3(Mathf.Cos(yaw) * sin, Mathf.Sin(yaw) * sin,
                                              Mathf.Cos(tilt));
            }

            return dirs;
        }

        // ── The envelope ───────────────────────────────────────────────────────

        private static float Envelope(float t, float settleSeconds)
        {
            if (settleSeconds <= 0f) return 1f;
            float u = Mathf.Clamp01(t / settleSeconds);
            return u * u;
        }

        private static float EnvelopeRate(float t, float settleSeconds)
        {
            if (settleSeconds <= 0f || t <= 0f || t >= settleSeconds) return 0f;
            return 2f * t / (settleSeconds * settleSeconds);
        }

        // ── Seeded coefficients ────────────────────────────────────────────────
        //
        // Hashed on demand rather than drawn once into an array. Same numbers every call, no
        // allocation, and nothing to keep in sync between the machine that decides the damage and
        // the machines that only draw the shot — a coefficient table would be one more thing that
        // could be built differently in two places.

        /// <summary>
        /// Per-harmonic weight, falling off with k so the first harmonic sets the broad swerve and
        /// the later ones only ripple it. Weights are normalised to sum to one, so `amplitude`
        /// means metres of stray however many harmonics there are.
        /// </summary>
        private static float Amplitude(int seed, int k, int axis)
        {
            float falloff = 1f / (k + 1f);
            float norm = 0f;
            for (int i = 0; i < Harmonics; i++) norm += 1f / (i + 1f);
            return falloff / norm * Mathf.Lerp(0.7f, 1.3f, Unit(seed, k * 7 + axis * 31 + 1));
        }

        /// <summary>
        /// Angular frequency, in radians per second. Harmonics are spaced by irrational-ish
        /// multipliers rather than integer ones: integer harmonics share a period and the whole
        /// path repeats visibly, which on a rocket that flies for two seconds is long enough to
        /// notice.
        /// </summary>
        private static float Frequency(int seed, int k, int axis, float baseFrequency)
        {
            float spread = 1f + k * 1.618f;
            return baseFrequency * spread *
                   Mathf.Lerp(0.8f, 1.25f, Unit(seed, k * 13 + axis * 53 + 5)) * 2f * Mathf.PI;
        }

        private static float Phase(int seed, int k, int axis)
            => Unit(seed, k * 17 + axis * 71 + 3) * 2f * Mathf.PI;

        /// <summary>A deterministic value in [0, 1) from a seed and a salt.</summary>
        private static float Unit(int seed, int salt)
            => (Mix(seed, salt) & 0x00FFFFFF) / (float)0x01000000;

        /// <summary>
        /// SplitMix32's finalizer. Chosen over <c>System.Random</c> because it is stateless: the
        /// n-th coefficient can be asked for directly, in any order, without stepping a generator
        /// that every machine would then have to step the same number of times.
        /// </summary>
        private static int Mix(int seed, int salt)
        {
            unchecked
            {
                uint z = (uint)seed + (uint)salt * 0x9E3779B9u;
                z = (z ^ (z >> 16)) * 0x85EBCA6Bu;
                z = (z ^ (z >> 13)) * 0xC2B2AE35u;
                return (int)(z ^ (z >> 16));
            }
        }
    }
}
