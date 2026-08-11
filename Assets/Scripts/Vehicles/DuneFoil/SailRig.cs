using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Vehicles.DuneFoil
{
    /// <summary>
    /// The whole sail plan. Ticks every <see cref="SailSurface"/> against the wind and adds up
    /// what they make: one drive force and one yaw torque for the locomotion to act on.
    ///
    /// Knows nothing about how the craft moves. It reports; it never writes the hull transform.
    /// That rule is what keeps the rig and the locomotion from fighting over the same frame.
    /// </summary>
    [DefaultExecutionOrder(50)]     // after input, before DuneFoilLocomotion (100)
    public class SailRig : MonoBehaviour
    {
        [Header("Sails")]
        [Tooltip("Every sail on the rig. Order is only for the inspector.")]
        [SerializeField] private List<SailSurface> sails = new List<SailSurface>();

        [Tooltip("The main. Its rope station is the primary steering control.")]
        [SerializeField] private SailSurface mainSail;

        [Tooltip("The headsail. Forward of the foil, so it bears the bow away.")]
        [SerializeField] private SailSurface jib;

        [Header("Aerodynamics")]
        [Tooltip("Force scale. Physically air density, but the honest description is that this " +
                 "is the master gain on how powerful the whole rig feels.")]
        [SerializeField, Min(0.01f)] private float airDensity = 1.2f;

        [Tooltip("Wind field to read. Falls back to WindField.Active when empty, which is the " +
                 "normal case.")]
        [SerializeField] private WindField wind;

        private Vector3 craftVelocity;

        /// <summary>Total force from all sails, world space.</summary>
        public Vector3 TotalForce { get; private set; }

        /// <summary>Total yaw torque about the craft's vertical axis.</summary>
        public float TotalTorque { get; private set; }

        /// <summary>Apparent wind the rig saw this frame.</summary>
        public Vector3 ApparentWind { get; private set; }

        /// <summary>True wind at the craft this frame.</summary>
        public Vector3 TrueWind { get; private set; }

        /// <summary>Whether the craft is pointing too close to the wind to sail.</summary>
        public bool InNoGoZone { get; private set; }

        public IReadOnlyList<SailSurface> Sails => sails;
        public SailSurface MainSail => mainSail;
        public SailSurface Jib => jib;

        /// <summary>Combined sail area currently set, m².</summary>
        public float SetArea
        {
            get
            {
                float total = 0f;
                foreach (SailSurface s in sails)
                    if (s != null) total += s.Area * s.Hoist01;
                return total;
            }
        }

        private void Reset() => CollectSails();

        private void Awake()
        {
            if (sails.Count == 0) CollectSails();
        }

        private void CollectSails()
        {
            sails.Clear();
            sails.AddRange(GetComponentsInChildren<SailSurface>(true));
        }

        /// <summary>
        /// Resolve the rig for this frame.
        /// Called by <see cref="DuneFoilLocomotion"/> with the velocity it had at the top of the
        /// frame, so apparent wind and the force it produces belong to the same instant.
        /// </summary>
        public void Tick(Vector3 velocity, Vector3 heading, float deltaTime)
        {
            craftVelocity = velocity;

            WindField field = wind != null ? wind : WindField.Active;
            TrueWind = field != null ? field.SampleAt(transform.position) : Vector3.zero;
            ApparentWind = SailAerodynamics.ApparentWind(TrueWind, craftVelocity);
            InNoGoZone = TrueWind.sqrMagnitude > 1e-4f &&
                         SailAerodynamics.IsInNoGoZone(heading, -TrueWind.normalized);

            Vector3 force = Vector3.zero;
            float torque = 0f;

            foreach (SailSurface sail in sails)
            {
                if (sail == null) continue;
                sail.Tick(ApparentWind, heading, airDensity, deltaTime);
                force += sail.Force;
                torque += sail.Torque;
            }

            TotalForce = force;
            TotalTorque = torque;
        }

        // --- rig-wide controls ------------------------------------------------

        /// <summary>Set every sail. The hoist station's Interact.</summary>
        public void HoistAll()
        {
            foreach (SailSurface s in sails)
                if (s != null) s.Hoist();
        }

        /// <summary>Take every sail down. The hoist station's Use.</summary>
        public void FurlAll()
        {
            foreach (SailSurface s in sails)
                if (s != null) s.Furl();
        }

        /// <summary>
        /// True when any sail is set. Mixed states resolve toward furling everything, so one
        /// press always leaves the rig in a state the player can predict.
        /// </summary>
        public bool AnyHoisted
        {
            get
            {
                foreach (SailSurface s in sails)
                    if (s != null && s.IsHoisted) return true;
                return false;
            }
        }

        /// <summary>Wire the rig up. Used by the prefab builder.</summary>
        public void Bind(List<SailSurface> allSails, SailSurface main, SailSurface headsail)
        {
            sails = allSails;
            mainSail = main;
            jib = headsail;
        }
    }
}
