using System;
using UnityEngine;

namespace SpaceGame.Vehicles.Ornithopter
{
    /// <summary>
    /// The ornithopter's flight, as pure functions. No MonoBehaviour, no Transform, no Time —
    /// split out for the same reason <c>FoilPhysics</c> and <c>SailAerodynamics</c> are, so the
    /// behaviour can be reasoned about and tested without a scene.
    ///
    /// This is a point-mass flight model, the standard one: the craft is a dot travelling along a
    /// flight path, and the pilot points a wing at that path. Two angles matter and confusing them
    /// is the single biggest source of "why does it fly like a brick" bugs:
    ///
    ///   • <b>Flight path angle</b> (gamma) — the direction the craft is actually MOVING.
    ///   • <b>Body pitch</b> (theta) — the direction the craft is POINTING. What the stick sets.
    ///
    /// The difference between them is the angle of attack, and angle of attack is what makes lift.
    /// Pull the nose up and you do not go up; you increase the angle of attack, which makes more
    /// lift, which curves the flight path upward a moment later. Pull too hard and the wing stalls
    /// — lift collapses, the path curves back down, and the machine falls until it has speed again.
    /// That whole behaviour falls out of these equations rather than being scripted, which is why
    /// gliding, diving, climbing and stalling all work without special cases for each.
    ///
    /// Energy is the through-line. There is no throttle: airspeed is bought with altitude or with
    /// flapping, and flapping costs stamina. A pilot who holds the nose up without flapping trades
    /// speed for height until there is no speed left.
    /// </summary>
    public static class OrnithopterFlightModel
    {
        /// Standard gravity. Not Physics.gravity: this model integrates weight itself and the
        /// Rigidbody it drives has useGravity off, so there is exactly one source of it.
        public const float Gravity = 9.81f;

        /// <summary>
        /// Airspeed below which the aerodynamic terms are divided by a floor rather than by the
        /// real speed. Turn rate and flight-path curvature both divide by airspeed, so a craft
        /// that has genuinely stopped would otherwise produce infinities.
        /// </summary>
        private const float SpeedFloor = 1.5f;

        /// <summary>One integration step. Returns the new state; does not mutate the old one.</summary>
        public static OrnithopterFlightState Step(
            in OrnithopterFlightState state,
            in OrnithopterFlightInput input,
            OrnithopterFlightConfig cfg,
            float dt)
        {
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));
            if (dt <= 0f) return state;

            OrnithopterFlightState s = state;

            // ── Wing spread ─────────────────────────────────────────────────────────────
            // Deployment is a one-way ramp owned by the caller (0 at launch, 1 when open).
            // Tucking folds the wings back in on top of it: less area, less lift, less drag —
            // which is exactly how a diving bird gets fast.
            float tuck = Mathf.Clamp01(-input.Flap);
            s.WingSpread = Mathf.Clamp01(s.Deployment * (1f - tuck * cfg.TuckSpreadLoss));

            // ── Stamina ─────────────────────────────────────────────────────────────────
            // Drains while flapping, recovers while gliding. This is what makes level flight a
            // rhythm rather than a held button.
            float flapDemand = Mathf.Clamp01(input.Flap);
            s.Stamina = Mathf.Clamp01(flapDemand > 0.01f
                ? s.Stamina - cfg.StaminaDrainPerSecond * flapDemand * dt
                : s.Stamina + cfg.StaminaRecoverPerSecond * dt);

            // Effort is what the wings can actually deliver, which is demand limited by stamina.
            // Scaling by stamina rather than cutting off at zero makes exhaustion a fade.
            s.FlapEffort = flapDemand * Mathf.Clamp01(s.Stamina / Mathf.Max(0.01f, cfg.StaminaFadeBand));

            // Flap phase advances with effort, and keeps advancing at a slow idle so the wings
            // never freeze mid-beat when the pilot lets off.
            float flapHz = cfg.FlapHzIdle + (cfg.FlapHzMax - cfg.FlapHzIdle) * s.FlapEffort;
            s.FlapPhase = Mathf.Repeat(s.FlapPhase + flapHz * dt, 1f);

            // ── Attitude ────────────────────────────────────────────────────────────────
            // Control authority fades with airspeed and collapses in a stall: a wing with no air
            // over it cannot push. Without this the pilot could fly a stalled machine like a
            // helicopter, and the stall would carry no consequence at all.
            float authority = Mathf.Clamp01(s.Airspeed / Mathf.Max(0.01f, cfg.FullAuthoritySpeed));
            if (s.Stalled) authority *= cfg.StalledAuthority;

            s.Pitch = Mathf.Clamp(
                s.Pitch + input.Pitch * cfg.PitchRate * authority * dt,
                -cfg.MaxPitch, cfg.MaxPitch);

            // Roll self-centres when the stick is released, so the craft returns to wings-level
            // on its own the way a dihedral wing does.
            float rollTarget = input.Roll * cfg.MaxRoll;
            float rollSpeed = cfg.RollRate * authority;
            s.Roll = Mathf.Abs(input.Roll) > 0.01f
                ? Mathf.MoveTowards(s.Roll, rollTarget, rollSpeed * dt)
                : Mathf.MoveTowards(s.Roll, 0f, cfg.RollCentringRate * authority * dt);

            // ── Aerodynamics ────────────────────────────────────────────────────────────
            float aoaDeg = s.Pitch - s.Gamma;
            s.Stalled = aoaDeg > cfg.StallAngle;

            float cl = LiftCoefficient(aoaDeg, cfg);
            float cd = cfg.DragCoefficientZeroLift + cfg.InducedDragFactor * cl * cl;

            // Dynamic pressure over mass, so both terms below come out as accelerations.
            // Wing area scales with spread: folded wings make almost nothing.
            float qOverM = 0.5f * cfg.AirDensity * s.Airspeed * s.Airspeed
                           * cfg.WingArea / Mathf.Max(0.01f, cfg.Mass);

            float lift = qOverM * s.WingSpread * cl;
            float drag = qOverM * cd;

            // Flapping thrust pulses with the beat. Half-cosine so it is strongest on the
            // downstroke and never negative — an upstroke that pushed backwards would cancel the
            // beat out entirely.
            float beat = 0.5f * (1f - Mathf.Cos(s.FlapPhase * 2f * Mathf.PI));
            float thrust = cfg.FlapThrust * s.FlapEffort * s.WingSpread * beat;

            float gammaRad = s.Gamma * Mathf.Deg2Rad;
            float rollRad = s.Roll * Mathf.Deg2Rad;

            // ── Speed: the energy equation ──────────────────────────────────────────────
            // Nose down (gamma < 0) makes sin negative, so gravity ADDS speed in a dive and
            // takes it away in a climb. This one term is what "trade altitude for speed" means.
            float speedRate = thrust - drag - Gravity * Mathf.Sin(gammaRad);
            s.Airspeed = Mathf.Max(0f, s.Airspeed + speedRate * dt);

            float vEff = Mathf.Max(SpeedFloor, s.Airspeed);

            // ── Flight path: where lift actually takes the craft ────────────────────────
            // Only the vertical component of lift (cos roll) fights gravity, which is why a
            // steeply banked turn sinks unless the pilot pulls harder.
            float gammaRate = (lift * Mathf.Cos(rollRad) - Gravity * Mathf.Cos(gammaRad)) / vEff;
            s.Gamma = Mathf.Clamp(s.Gamma + gammaRate * Mathf.Rad2Deg * dt, -89f, 89f);

            // ── Heading: the coordinated turn ───────────────────────────────────────────
            // The horizontal component of lift is what turns an aircraft — banking, not the
            // rudder, is how you change direction. The tail fan adds a little direct yaw on top
            // so the machine still answers at low speed, where bank alone has nothing to work with.
            float turnRate = lift * Mathf.Sin(rollRad) / vEff * Mathf.Rad2Deg;
            turnRate += input.Turn * cfg.TailYawRate * authority;
            s.Heading = Mathf.Repeat(s.Heading + turnRate * dt, 360f);
            s.TurnRate = turnRate;

            return s;
        }

        /// <summary>
        /// Lift coefficient against angle of attack: linear up to the stall, then falling away.
        ///
        /// The post-stall side is what makes a stall recoverable rather than terminal. Lift does
        /// not go to zero — it drops to a fraction — so the nose falls, the flight path steepens,
        /// the angle of attack comes back down on its own and the wing flies again. A hard cut to
        /// zero would give a machine that never recovers.
        /// </summary>
        public static float LiftCoefficient(float aoaDeg, OrnithopterFlightConfig cfg)
        {
            float linear = cfg.LiftSlopePerDegree * aoaDeg;
            if (aoaDeg <= cfg.StallAngle)
                return linear;

            float peak = cfg.LiftSlopePerDegree * cfg.StallAngle;
            float over = aoaDeg - cfg.StallAngle;
            float fade = Mathf.Clamp01(over / Mathf.Max(0.01f, cfg.StallFadeAngle));
            return Mathf.Lerp(peak, peak * cfg.PostStallLiftFraction, fade);
        }

        /// <summary>
        /// World-space velocity implied by a state. Heading is measured from +Z toward +X, which
        /// is Unity's convention and the axis the model arrives on from Blender.
        /// </summary>
        public static Vector3 VelocityOf(in OrnithopterFlightState s)
        {
            float g = s.Gamma * Mathf.Deg2Rad;
            float h = s.Heading * Mathf.Deg2Rad;
            float horizontal = Mathf.Cos(g);
            return new Vector3(
                Mathf.Sin(h) * horizontal,
                Mathf.Sin(g),
                Mathf.Cos(h) * horizontal) * s.Airspeed;
        }

        /// <summary>
        /// The speed below which the wing cannot hold the craft up in level flight — the stall
        /// speed. Derived from the config rather than tuned separately, so it cannot drift out of
        /// agreement with the lift curve it is supposed to describe.
        /// </summary>
        public static float StallSpeed(OrnithopterFlightConfig cfg, float spread = 1f)
        {
            float clMax = cfg.LiftSlopePerDegree * cfg.StallAngle;
            float denom = 0.5f * cfg.AirDensity * cfg.WingArea * Mathf.Max(0.01f, spread) * clMax;
            if (denom <= 0f) return 0f;
            return Mathf.Sqrt(Gravity * cfg.Mass / denom);
        }
    }
}
