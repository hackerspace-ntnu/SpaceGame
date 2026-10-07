// Assets/Game/Scripts/Vehicles/Ornithopter/NpcFlight/NpcWings.cs
// The NPC craft's wings, read off how it is moving — there is no flight model behind an NPC craft to ask
// (NpcFlightPlan + FlyingRigidbodyMotor fly it). Pure, so NpcWingsTests can drive it.
using System;
using UnityEngine;

namespace SpaceGame.Vehicles.Ornithopter
{
    [Serializable]
    public class NpcWingSettings
    {
        [Tooltip("Below this speed the craft is parked and its wings stay shut, m/s.")]
        [Min(0f)] public float SpreadSpeed = 3f;

        [Tooltip("Seconds for the wings to open or close.")]
        [Min(0.05f)] public float SpreadSeconds = 0.6f;

        [Tooltip("Wing beats per second gliding and at full effort.")]
        [Min(0.01f)] public float FlapHzIdle = 0.35f;
        [Min(0.01f)] public float FlapHzMax = 1.6f;

        [Tooltip("Beat effort in level flight, 0..1.")]
        [Range(0f, 1f)] public float GlideEffort = 0.15f;

        [Tooltip("Extra effort per m/s of climb.")]
        [Min(0f)] public float EffortPerClimbSpeed = 0.25f;

        [Tooltip("Sinking faster than this reads as a wreck: wings half shut, no beat, m/s.")]
        [Min(0f)] public float WreckSinkSpeed = 12f;

        [Tooltip("How far a wreck's wings stay open, 0..1.")]
        [Range(0f, 1f)] public float WreckSpread = 0.4f;

        [Tooltip("Turn rate that reads as full turn input, degrees per second.")]
        [Min(1f)] public float TurnRateForFullTurn = 45f;
    }

    public struct NpcWingState
    {
        public float Airspeed;
        public float FlapPhase;
        public float FlapEffort;
        public float WingSpread;
        public float Bank;
        public float Turn;
    }

    public static class NpcWings
    {
        public static NpcWingState Step(NpcWingState s, Vector3 velocity, float bankDegrees, float turnRateDegrees,
                                        float dt, NpcWingSettings cfg)
        {
            s.Airspeed = velocity.magnitude;
            s.Bank = bankDegrees;
            s.Turn = Mathf.Clamp(turnRateDegrees / cfg.TurnRateForFullTurn, -1f, 1f);

            bool wrecked = -velocity.y >= cfg.WreckSinkSpeed;
            float spreadTarget = s.Airspeed < cfg.SpreadSpeed ? 0f : wrecked ? cfg.WreckSpread : 1f;
            s.WingSpread = Mathf.MoveTowards(s.WingSpread, spreadTarget, dt / cfg.SpreadSeconds);

            s.FlapEffort = wrecked || s.WingSpread <= 0f
                ? 0f
                : Mathf.Clamp01(cfg.GlideEffort + velocity.y * cfg.EffortPerClimbSpeed);

            if (s.WingSpread > 0f)
            {
                float hz = cfg.FlapHzIdle + (cfg.FlapHzMax - cfg.FlapHzIdle) * s.FlapEffort;
                s.FlapPhase = Mathf.Repeat(s.FlapPhase + hz * dt, 1f);
            }

            return s;
        }
    }
}
