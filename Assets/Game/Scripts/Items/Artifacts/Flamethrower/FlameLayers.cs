using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// One emitter and every layer hung under it, played, stopped and throttled as a single flame.
    ///
    /// <para>
    /// <b>This exists because of one Unity trap.</b> <c>EmissionModule.rateOverTimeMultiplier</c>
    /// looks like a scale on the authored rate and is not: on a <i>constant</i> MinMaxCurve — which
    /// is what every rate authored as a plain number is — the multiplier IS the constant, so
    /// writing a throttle of 0..1 into it does not scale 150 particles a second down, it REPLACES
    /// it with one particle a second. The flamethrower shipped that way and the symptom was exactly
    /// what you would expect: a fire that emitted a handful of specks and read as nothing at all.
    /// The authored rate is captured here at construction and multiplied by hand.
    /// </para>
    /// <para>
    /// The layers are children, so a layer added in the builder is picked up with no new serialized
    /// field on the component that owns the flame and no second thing to remember to put out.
    /// </para>
    /// </summary>
    public sealed class FlameLayers
    {
        private readonly ParticleSystem[] systems;

        /// <summary>Each system's authored rate, captured before anything scales it.</summary>
        private readonly float[] rates;

        public FlameLayers(ParticleSystem root)
        {
            systems = root != null
                ? root.GetComponentsInChildren<ParticleSystem>(includeInactive: true)
                : System.Array.Empty<ParticleSystem>();

            rates = new float[systems.Length];
            for (int i = 0; i < systems.Length; i++)
            {
                rates[i] = systems[i] != null ? systems[i].emission.rateOverTime.constant : 0f;
            }
        }

        public bool Any => systems.Length > 0;

        /// <summary>Is anything still in the air? What a dying flame waits on before it is reused.</summary>
        public bool Alive
        {
            get
            {
                for (int i = 0; i < systems.Length; i++)
                {
                    if (systems[i] != null && systems[i].IsAlive(withChildren: false)) return true;
                }

                return false;
            }
        }

        /// <summary>
        /// Start or stop emitting. Stopping lets what is already in the air burn out:
        /// <c>StopEmittingAndClear</c> would make the whole jet vanish the instant the trigger came
        /// up, which reads as a rendering glitch rather than as the flame dying back.
        /// </summary>
        public void SetEmitting(bool on)
        {
            for (int i = 0; i < systems.Length; i++)
            {
                if (systems[i] == null) continue;

                if (on)
                {
                    // Play on a system that is already playing RESTARTS it, so this must only ever
                    // be called on the edge — never per frame, or the jet is cleared sixty times a
                    // second and never gets past a stub.
                    systems[i].Play(withChildren: false);
                    continue;
                }

                systems[i].Stop(withChildren: false, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        /// <summary>How hard the flame is running, 0..1, as a share of every layer's authored rate.</summary>
        public void SetRate(float share)
        {
            float scale = Mathf.Max(0f, share);

            for (int i = 0; i < systems.Length; i++)
            {
                if (systems[i] == null) continue;

                ParticleSystem.EmissionModule emission = systems[i].emission;
                emission.rateOverTime = rates[i] * scale;
            }
        }

        /// <summary>Everything gone now, with nothing left in the air.</summary>
        public void Clear()
        {
            for (int i = 0; i < systems.Length; i++)
            {
                if (systems[i] == null) continue;

                systems[i].Stop(withChildren: false, ParticleSystemStopBehavior.StopEmitting);
                systems[i].Clear(withChildren: false);
            }
        }
    }
}
