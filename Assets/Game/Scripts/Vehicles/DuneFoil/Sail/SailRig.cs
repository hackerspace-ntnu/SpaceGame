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

        /// <summary>Total yaw torque about the craft's vertical axis. Positive turns to starboard.</summary>
        public float TotalTorque { get; private set; }

        /// <summary>
        /// Total heeling moment, N·m, positive laying the craft over to starboard.
        ///
        /// Summed from the sails rather than worked out from <see cref="TotalForce"/>, because
        /// each post can be canted independently and how far a post leans is half of what decides
        /// the moment it makes. A rig with its post laid into the wind stands up under a load
        /// that would otherwise have the craft on its ear, and no amount of looking at the total
        /// force will tell you that.
        /// </summary>
        public float TotalHeelMoment { get; private set; }

        /// <summary>Drive along the craft's heading this frame, newtons. Negative is sternway.</summary>
        public float Drive { get; private set; }

        /// <summary>Apparent wind the rig saw this frame.</summary>
        public Vector3 ApparentWind { get; private set; }

        /// <summary>True wind at the craft this frame.</summary>
        public Vector3 TrueWind { get; private set; }

        /// <summary>Whether the craft is pointing too close to the wind to sail.</summary>
        public bool InNoGoZone { get; private set; }

        public IReadOnlyList<SailSurface> Sails => sails;
        public SailSurface MainSail => mainSail;
        public SailSurface Jib => jib;

        /// <summary>
        /// Combined sail area actually working, m². Counts what is hoisted, foreshortened by
        /// however far each post is leaned over — which is the area that makes the force.
        /// </summary>
        public float SetArea
        {
            get
            {
                float total = 0f;
                foreach (SailSurface s in sails)
                    if (s != null) total += s.EffectiveArea;
                return total;
            }
        }

        private void Reset() => CollectSails();

        private bool initialised;

        private void Awake() => Initialise();

        /// <summary>Idempotent, and called from <see cref="Tick"/> so a harness can step a rig
        /// that Unity never woke. See <c>SailSurface.Initialise</c>.</summary>
        private void Initialise()
        {
            if (initialised) return;
            initialised = true;
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
            Initialise();
            craftVelocity = velocity;

            WindField field = wind != null ? wind : WindField.Active;
            TrueWind = field != null ? field.SampleAt(transform.position) : Vector3.zero;
            ApparentWind = SailAerodynamics.ApparentWind(TrueWind, craftVelocity);
            InNoGoZone = TrueWind.sqrMagnitude > 1e-4f &&
                         SailAerodynamics.IsInNoGoZone(heading, -TrueWind.normalized);

            Vector3 force = Vector3.zero;
            float torque = 0f;
            float heel = 0f;

            foreach (SailSurface sail in sails)
            {
                if (sail == null) continue;
                sail.Tick(ApparentWind, heading, airDensity, deltaTime);
                force += sail.Force;
                torque += sail.Torque;
                heel += sail.HeelMoment;
            }

            TotalForce = force;
            TotalTorque = torque;
            TotalHeelMoment = heel;
            Drive = Vector3.Dot(SailAerodynamics.Flatten(force), heading);
        }

        /// <summary>
        /// How hard the rig is flogging, 0..1: the worst-behaved sail on the craft.
        ///
        /// The one number worth putting in front of the player, because a luffing sail is the
        /// difference between a trim that is nearly right and one that is doing nothing, and it
        /// is not otherwise visible from the helm at the far end of a seventeen-metre hull.
        /// </summary>
        public float Luffing
        {
            get
            {
                float worst = 0f;
                foreach (SailSurface s in sails)
                    if (s != null && s.IsHoisted) worst = Mathf.Max(worst, s.Luffing);
                return worst;
            }
        }

        // --- rig-wide controls ------------------------------------------------

        /// <summary>
        /// Work every halyard up while the player holds the control. The hoist station's Interact.
        /// </summary>
        public void RaiseHalyards(float deltaTime)
        {
            foreach (SailSurface s in sails)
                if (s != null) s.RaiseHalyard(deltaTime);
        }

        /// <summary>Work every halyard down. The hoist station's Use.</summary>
        public void LowerHalyards(float deltaTime)
        {
            foreach (SailSurface s in sails)
                if (s != null) s.LowerHalyard(deltaTime);
        }

        /// <summary>
        /// Run every sail all the way up over its hoist duration.
        ///
        /// Scripted only — no input reaches this. It and <see cref="FurlAll"/> used to be the
        /// hoist station's two buttons, and a single mis-click on FurlAll struck the whole rig
        /// mid-passage; the station now works the halyards continuously instead. Kept for
        /// spawning, cutscenes and tests, where "set everything, now" is what is actually meant.
        /// </summary>
        public void HoistAll()
        {
            foreach (SailSurface s in sails)
                if (s != null) s.Hoist();
        }

        /// <summary>Run every sail down. Scripted only — see <see cref="HoistAll"/>.</summary>
        public void FurlAll()
        {
            foreach (SailSurface s in sails)
                if (s != null) s.Furl();
        }

        /// <summary>True when any sail has cloth showing.</summary>
        public bool AnyHoisted
        {
            get
            {
                foreach (SailSurface s in sails)
                    if (s != null && s.IsHoisted) return true;
                return false;
            }
        }

        /// <summary>
        /// Mean hoist across the rig, 0..1. What the hoist station shows on its readout, since a
        /// single number is what the player is actually working when they hold the halyard.
        /// </summary>
        public float Hoist01
        {
            get
            {
                float total = 0f;
                int counted = 0;
                foreach (SailSurface s in sails)
                {
                    if (s == null) continue;
                    total += s.Hoist01;
                    counted++;
                }
                return counted == 0 ? 0f : total / counted;
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
