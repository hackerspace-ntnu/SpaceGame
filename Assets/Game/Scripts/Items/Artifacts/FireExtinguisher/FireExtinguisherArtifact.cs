using UnityEngine;
using SpaceGame.Core;

namespace SpaceGame.Items
{
    /// <summary>
    /// The fire extinguisher. Hold Use and a cone of CO2 fog comes out; a fire it covers loses
    /// strength until it goes out.
    ///
    /// <para>
    /// A <see cref="SprayerItem"/>: the valve, the hold stream, the tank and the plume are the cryo
    /// sprayer's, so it hisses, frosts at the horn and runs dry the same way. What it does to what
    /// it covers is <see cref="Land"/>. It puts out a <see cref="ShipPartFire"/> — the one fire in
    /// the game with a strength to knock down — on the authority only; every machine sees the
    /// result through the fire's own replicated strength.
    /// </para>
    /// <para>
    /// Charge persists because the tank is a <see cref="SupplyReservoir"/>, which
    /// <see cref="UsableItem"/> already captures into the slot's state. The extinguisher does not
    /// refill on its own: it is a few seconds of fog, and the fire is the reason to spend them.
    /// </para>
    /// </summary>
    public sealed class FireExtinguisherArtifact : SprayerItem
    {
        [Header("Plume")]
        [Tooltip("How far the fog reaches, in metres. A short throw: an extinguisher is used at " +
                 "arm's length plus a step. Serialized on the prefab, which is what ships.")]
        [SerializeField] private float range = 5f;

        [Tooltip("Half the fog cone's opening angle, in degrees. Should match the angle the plume " +
                 "is drawn at (CryoSprayerNozzle.openConeDegrees).")]
        [SerializeField, Range(0f, 45f)] private float coneHalfAngle = 20f;

        [Tooltip("What stops the fog reaching a fire. Triggers are always ignored.")]
        [SerializeField] private LayerMask sightBlockers = ~0;

        [Header("Douse")]
        [Tooltip("Fire strength knocked off per second of fog on it (a full-strength fire is 1). " +
                 "Must beat the fire's own growth by a clear margin, or a late spray loses.")]
        [SerializeField, Min(0.01f)] private float dousePerSecond = 0.4f;

        private readonly RaycastHit[] hits = new RaycastHit[16];

        /// <summary>
        /// Every machine, on the sweep schedule: the fog lands on each fire it covers. The douse is
        /// the authority's alone; the landing burst is drawn everywhere.
        /// </summary>
        protected override void Land(float elapsed)
        {
            if (owner == null) return;

            bool authority = Decides;
            bool landedOnFire = false;
            Vector3 landing = Vector3.zero;

            foreach (ShipPartFire fire in ShipPartFire.Active)
            {
                if (fire == null || !fire.IsBurning) continue;
                if (!Covers(fire, out float distance)) continue;

                landedOnFire = true;
                landing = RayOrigin + RayDirection * distance;

                if (authority) fire.Douse(dousePerSecond * elapsed);
            }

            if (Nozzle != null) Nozzle.SetLanding(landedOnFire, landing, false);
        }

        /// <summary>
        /// Is <paramref name="fire"/> inside the fog cone, in reach, and in plain sight? The cone is
        /// widened by the fire's own size, so fog washing over the edge of the flames counts.
        /// </summary>
        private bool Covers(ShipPartFire fire, out float distance)
        {
            Vector3 toFire = fire.Centre - RayOrigin;
            distance = toFire.magnitude;

            if (distance > range + fire.Reach) return false;
            if (distance < 1e-3f) return true;

            float allowance = Mathf.Atan2(fire.Reach, distance) * Mathf.Rad2Deg;
            if (Vector3.Angle(RayDirection, toFire) > coneHalfAngle + allowance) return false;

            return InSight(fire, toFire / distance, distance);
        }

        /// <summary>
        /// Nothing solid between the horn and the flames, the holder and the burning unit aside.
        /// Filtered on the collider's transform (see INVARIANTS: a query does not know what the
        /// solver was told), and a hit at distance 0 — the sweep starting inside the holder — is
        /// thrown away.
        /// </summary>
        private bool InSight(ShipPartFire fire, Vector3 direction, float distance)
        {
            int count = Physics.RaycastNonAlloc(RayOrigin, direction, hits, distance, sightBlockers,
                                                QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                Collider c = hits[i].collider;
                if (c == null || hits[i].distance <= 0f) continue;
                if (c.transform.IsChildOf(owner.transform.root)) continue;
                if (fire.Owns(c)) continue;

                return false;
            }

            return true;
        }

        protected override void OnValidate()
        {
            base.OnValidate();
            range = Mathf.Max(0.5f, range);
        }
    }
}
