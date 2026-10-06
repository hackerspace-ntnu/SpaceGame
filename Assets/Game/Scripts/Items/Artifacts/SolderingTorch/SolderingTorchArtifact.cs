using UnityEngine;
using SpaceGame.Gameplay;

namespace SpaceGame.Items
{
    /// <summary>
    /// The soldering torch. Hold Use and a short blue-white flame stands off the nozzle; held on a cracked seam it throws
    /// sparks and a molten glow and closes the seam (<see cref="TorchRepairable"/>), on anything else it only scatters a few
    /// sparks.
    ///
    /// <para>
    /// A <see cref="SprayerItem"/>: the valve, the hold stream, the timeout and the gas canister are the sprayers', so it
    /// lights, hisses and gutters the same way. What it does to what it covers is <see cref="Land"/>: the solder is the
    /// authority's alone, and every machine sees the seam close through the machine's own replicated progress. Harmless to
    /// bodies: it is a jeweller's flame, not a weapon, so it is not <c>menacing</c> and deals no damage. The canister lasts
    /// about a hundred seconds of flame and refills itself while the trigger is up — generous enough that a repair never
    /// waits on it.
    /// </para>
    /// </summary>
    public sealed class SolderingTorchArtifact : SprayerItem
    {
        [Header("Flame")]
        [Tooltip("How far past the nozzle the flame works, in metres: a step and an arm's length, which is how a torch is used.")]
        [SerializeField, Min(0.2f)] private float range = 2.2f;

        [Tooltip("How far off the aim line a seam may be and still be under the flame, in metres.")]
        [SerializeField, Min(0.01f)] private float flameRadius = 0.25f;

        [Tooltip("What stops the flame reaching a seam, and what it splashes sparks on. Triggers are always ignored.")]
        [SerializeField] private LayerMask surfaces = ~0;

        private readonly RaycastHit[] hits = new RaycastHit[16];

        /// <summary>
        /// Every machine, on the sweep schedule: a seam under the flame is soldered (on the authority) and sparks and glows; a
        /// bare surface in reach only sparks.
        /// </summary>
        protected override void Land(float elapsed)
        {
            if (owner == null) return;

            if (TryFindSeam(out TorchRepairable machine, out Vector3 seam))
            {
                if (Decides) machine.Solder(elapsed);
                if (Nozzle != null) Nozzle.SetLanding(true, seam, true);
                return;
            }

            bool struck = TryStrike(out Vector3 point);
            if (Nozzle != null) Nozzle.SetLanding(struck, point, false);
        }

        /// <summary>A machine whose working seam lies under the flame, in reach and in plain sight.</summary>
        private bool TryFindSeam(out TorchRepairable machine, out Vector3 seam)
        {
            foreach (TorchRepairable candidate in TorchRepairable.Active)
            {
                if (candidate == null || !candidate.TryGetWorkingSeam(out seam)) continue;

                Vector3 toSeam = seam - RayOrigin;
                float along = Vector3.Dot(toSeam, RayDirection);
                if (along < 0f || along > range + flameRadius) continue;
                if ((toSeam - RayDirection * along).sqrMagnitude > flameRadius * flameRadius &&
                    !candidate.IsOnWorkingSeam(RayOrigin + RayDirection * along)) continue;
                if (!InSight(candidate, seam)) continue;

                machine = candidate;
                return true;
            }

            machine = null;
            seam = default;
            return false;
        }

        /// <summary>Nothing solid between the nozzle and the seam, the holder and the machine itself aside.</summary>
        private bool InSight(TorchRepairable machine, Vector3 seam)
        {
            Vector3 toSeam = seam - RayOrigin;
            float distance = toSeam.magnitude;
            if (distance < 1e-3f) return true;

            int count = Physics.RaycastNonAlloc(RayOrigin, toSeam / distance, hits, distance, surfaces,
                                                QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider c = hits[i].collider;
                if (c == null || hits[i].distance <= 0f) continue;
                if (c.transform.IsChildOf(owner.transform.root)) continue;
                if (c.transform.IsChildOf(machine.transform)) continue;
                return false;
            }

            return true;
        }

        /// <summary>
        /// The nearest surface the flame touches, for its sparks. Filtered on the collider's transform, and a hit at distance 0
        /// (the ray starting inside the holder) is thrown away — see INVARIANTS: a query does not know what the solver was told.
        /// </summary>
        private bool TryStrike(out Vector3 point)
        {
            point = default;
            int count = Physics.RaycastNonAlloc(RayOrigin, RayDirection, hits, range, surfaces, QueryTriggerInteraction.Ignore);

            float nearest = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                Collider c = hits[i].collider;
                if (c == null || hits[i].distance <= 0f || hits[i].distance >= nearest) continue;
                if (c.transform.IsChildOf(owner.transform.root)) continue;

                nearest = hits[i].distance;
                point = hits[i].point;
            }

            return nearest < float.PositiveInfinity;
        }

        protected override void OnValidate()
        {
            base.OnValidate();
            range = Mathf.Max(0.2f, range);
        }
    }
}
