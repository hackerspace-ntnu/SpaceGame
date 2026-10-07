// One live storm, as a value.
//
// This is the entire networked payload of a sandstorm: about thirty bytes, written once when the
// storm is born and never touched again. Where the storm IS and how hard it is blowing are not
// stored — they are recomputed from these fields and the shared clock, which is why no per-frame
// traffic exists and why two clients cannot drift apart.
using System;
using Unity.Netcode;
using UnityEngine;

namespace SpaceGame.World.Weather
{
    /// <summary>A storm resolved to a moment: where it is, which way it is going, how hard.</summary>
    public struct StormState
    {
        public Vector2 Center;
        public Vector2 Heading;

        /// <summary>0 to 1, the lifecycle envelope and gusts combined. Multiplies shape density.</summary>
        public float Intensity;
    }

    public struct StormInstance : INetworkSerializable, IEquatable<StormInstance>
    {
        /// <summary>Server-assigned and unique for the session. Visual layers key their objects off it.</summary>
        public int Id;

        /// <summary>Index into the <see cref="SandstormCatalog"/>. A byte, so the asset reference costs one.</summary>
        public byte ProfileIndex;

        /// <summary>Drives wander and gusts. Two storms from the same profile blow differently.</summary>
        public uint Seed;

        /// <summary>World XZ the storm was at when it started.</summary>
        public Vector2 Origin;

        /// <summary>Compass bearing the storm travels toward: 0 is +Z, 90 is +X.</summary>
        public float HeadingDegrees;

        /// <summary>Weather-clock time the storm began. See <see cref="Sandstorms.WeatherTime"/>.</summary>
        public double StartTime;

        /// <summary>Seconds until it expires. Zero means never — a parked hazard region.</summary>
        public float Duration;

        public bool IsExpired(double now) => Duration > 0f && now >= StartTime + Duration;

        /// <summary>
        /// Seconds since the storm began, floored at zero. A client whose estimate of server time
        /// is a few milliseconds behind would otherwise evaluate a negative age and sample the
        /// lifecycle curve off its front end.
        /// </summary>
        public float Age(double now) => Mathf.Max(0f, (float)(now - StartTime));

        /// <summary>
        /// Where the storm is and how hard it is blowing. Pure: same inputs, same answer, on every
        /// machine — which is the whole contract this type exists to keep.
        /// </summary>
        public StormState Evaluate(SandstormProfile profile, double now)
        {
            float age = Age(now);
            Vector2 heading = StormShape.HeadingFromDegrees(HeadingDegrees);
            Vector2 across = new Vector2(-heading.y, heading.x);

            // The periods are clamped rather than trusted: the inspector cannot set them to zero,
            // but a profile built in code can, and a division by zero here would put the storm at
            // NaN — which reads downstream as a storm that is everywhere and nowhere at once.
            float wander = profile.wanderAmplitude > 0f
                ? profile.wanderAmplitude * StormNoise.Signed(Seed, age / Mathf.Max(0.01f, profile.wanderPeriod))
                : 0f;

            float envelope = Duration > 0f
                ? profile.intensityOverLife.Evaluate(Mathf.Clamp01(age / Duration))
                : profile.steadyIntensity;

            // A second, unrelated stream of noise for the gusts. Reusing the wander seed would tie
            // "the storm lurched sideways" to "the storm got heavier", and the eye picks that up.
            float gust = 1f + profile.gustAmplitude *
                         StormNoise.Signed(Seed ^ GustSeedOffset, age / Mathf.Max(0.01f, profile.gustPeriod));

            return new StormState
            {
                Center = Origin + heading * (profile.travelSpeed * age) + across * wander,
                Heading = heading,
                Intensity = Mathf.Clamp01(envelope * gust),
            };
        }

        private const uint GustSeedOffset = 0x9E3779B9u;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Id);
            serializer.SerializeValue(ref ProfileIndex);
            serializer.SerializeValue(ref Seed);
            serializer.SerializeValue(ref Origin);
            serializer.SerializeValue(ref HeadingDegrees);
            serializer.SerializeValue(ref StartTime);
            serializer.SerializeValue(ref Duration);
        }

        // Identity is the id alone: the rest of the struct never changes once the server has
        // written it, so two records with the same id are the same storm by definition.
        public bool Equals(StormInstance other) => Id == other.Id;

        public override bool Equals(object obj) => obj is StormInstance other && Equals(other);

        public override int GetHashCode() => Id;
    }
}
