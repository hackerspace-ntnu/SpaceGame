using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Vehicles.DuneFoil
{
    /// <summary>
    /// One sail on the rig: its hoist state, how much sheet is paid out, how far its post is
    /// canted over, and the force it makes.
    ///
    /// The sail is never told what angle to sit at. It weathervanes to trail the apparent wind
    /// and the sheet stops it — so paying out rope lets it swing further and hauling in pins it
    /// closer to the centreline, exactly as on a boat. That is what makes the rope stations a
    /// real control rather than a dial labelled "sail angle".
    ///
    /// The post is a second, coarser control, and it leans the whole spar to port or to
    /// starboard rather than fore and aft. Leaning it into the wind stands the craft up and lets
    /// it carry its whole sail plan in a breeze that would otherwise have it on its ear; leaning
    /// it to leeward lies the craft down and bears the bow away. Either way it costs drive,
    /// because a leaning sail presents less of itself to the wind.
    ///
    /// Owns its own transforms and nothing else. It reports a force; it never moves the craft.
    /// </summary>
    public class SailSurface : MonoBehaviour
    {
        [Header("Rig nodes")]
        [Tooltip("Pivot at the foot of the post. Rotating it leans the whole spar to port or " +
                 "starboard.")]
        [SerializeField] private Transform cantPivot;

        [Tooltip("Pivot on the spar axis. Rotating it about `sparAxis` swings the sail about its " +
                 "own post, which is what keeps the cloth attached to a leaning spar.")]
        [SerializeField] private Transform sparPivot;

        [Tooltip("Which way the spar runs, in the spar pivot's OWN local space. Measured off the " +
                 "model by the builder; local up is what the exported rig happens to use.")]
        [SerializeField] private Vector3 sparAxis = Vector3.up;

        [Tooltip("The cloth and battens. Hidden when the sail is furled; the post is not under " +
                 "this node, so it stays up.")]
        [SerializeField] private GameObject cloth;

        [Tooltip("Where a sheet is made fast, for the rope to draw to — and the far end of the " +
                 "boom, which is how the trim is read back off the model.")]
        [SerializeField] private Transform clew;

        [Tooltip("The craft. Used only for its heading and its up, so the post leans across the " +
                 "hull rather than across the world. Found in the parents when empty.")]
        [SerializeField] private Transform craft;

        [Header("Sail")]
        [Tooltip("Area in square metres. Drives force directly; measured off the mesh by the " +
                 "builder, so it stays right if the model is re-authored.")]
        [SerializeField, Min(0.1f)] private float area = 60f;

        [Tooltip("Distance along the hull from the foil to this sail's centre of effort, " +
                 "positive forward. Sign decides which way the sail steers the craft.")]
        [SerializeField] private float leverArm;

        [Tooltip("How far up the post the centre of effort sits. Only used when the post is " +
                 "canted: it is the arm the drive gets to yaw the craft with, and the arm the " +
                 "rig's own weight gets to stand it up with.")]
        [SerializeField, Min(0f)] private float centreOfEffortHeight = 6f;

        [Header("Sheet")]
        [Tooltip("Rope paid out, 0..1. 0 is sheeted hard in on the centreline, 1 is all the " +
                 "way out. The player drives this from the rope station.")]
        [SerializeField, Range(0f, 1f)] private float sheetOut = 0.5f;

        [Tooltip("Boom angle off the centreline at sheetOut = 1. Around 80 degrees is a boom " +
                 "against the shrouds.")]
        [SerializeField, Range(0f, 90f)] private float maxSheetAngle = 80f;

        [Tooltip("Angle at sheetOut = 0. Never quite zero: a sail hauled flat amidships still " +
                 "sits a few degrees off.")]
        [SerializeField, Range(0f, 45f)] private float minSheetAngle = 5f;

        [Tooltip("Fraction of full sheet travel per second while the player holds the control. " +
                 "At 0.55 the sail swings from hard in to right out in a little under two " +
                 "seconds, which is about as fast as a winch this size has any business being.")]
        [SerializeField, Min(0.01f)] private float sheetRate = 0.55f;

        [Tooltip("Sails that copy this one's sheet. The wing panels are part of the main's sail " +
                 "plan and are trimmed with it, so no sail on the craft is left uncontrolled.")]
        [SerializeField] private List<SailSurface> sheetSlaves = new List<SailSurface>();

        [Header("Post")]
        [Tooltip("Post lean, -1 hard over to port, 0 upright, +1 hard over to starboard.")]
        [SerializeField, Range(-1f, 1f)] private float cant;

        [Tooltip("How far the post lays down at full lean, degrees.")]
        [SerializeField, Range(0f, 60f)] private float maxCantAngle = 32f;

        [Tooltip("Fraction of full lean per second while the player works the control. Slower " +
                 "than the sheet: this is a whole spar, not a rope.")]
        [SerializeField, Min(0.01f)] private float cantRate = 0.45f;

        [Tooltip("Moment the rig's own weight makes when the post is laid right over, N·m. This " +
                 "is what stands the craft up when the post is leaned into the wind.")]
        [SerializeField, Min(0f)] private float cantRightingMoment = 9000f;

        [Header("Hoist")]
        [Tooltip("Where the halyard is, 0 fully struck to 1 fully set. Continuous, not a switch: " +
                 "see RaiseHalyard.")]
        [SerializeField, Range(0f, 1f)] private float hoist01 = 1f;

        [Tooltip("Fraction of full hoist per second while the player works the halyard. A full " +
                 "strike takes about two seconds of deliberate holding.")]
        [SerializeField, Min(0.01f)] private float hoistRate = 0.5f;

        [Tooltip("Seconds for the sail to run up or down when something sets it outright — the " +
                 "mooring, a script, a test. Player input uses hoistRate instead.")]
        [SerializeField, Min(0.05f)] private float hoistDuration = 1.6f;

        [Header("Response")]
        [Tooltip("How fast the sail swings to its trimmed angle. Cloth has inertia; a sail that " +
                 "snaps to its new angle reads as a rigid board.")]
        [SerializeField, Min(0.1f)] private float swingSpeed = 2.5f;

        [Tooltip("How fast the post follows the cant control. Heavy, so it lags the winch.")]
        [SerializeField, Min(0.1f)] private float cantSpeed = 2f;

        [Tooltip("Wind speed the shader treats as 'fully powered up' when setting the belly.")]
        [SerializeField, Min(0.1f)] private float referenceWindSpeed = 14f;

        private static readonly int BillowId = Shader.PropertyToID("_Billow");
        private static readonly int LuffId = Shader.PropertyToID("_Luff");
        private static readonly int WindDirId = Shader.PropertyToID("_WindDirection");
        private static readonly int HoistId = Shader.PropertyToID("_Hoist");

        private MaterialPropertyBlock block;
        private Renderer[] clothRenderers = System.Array.Empty<Renderer>();

        private Quaternion restCant = Quaternion.identity;
        private Quaternion restSpar = Quaternion.identity;
        private Vector3 cantAxisInParent = Vector3.forward;
        private float sparAngle;
        private float sparSign = 1f;
        private float measuredBoomAngle;
        private float appliedCantAngle;
        private float hoistTarget = 1f;

        // --- reported state ---------------------------------------------------

        /// <summary>Force this sail is making, world space, this frame.</summary>
        public Vector3 Force { get; private set; }

        /// <summary>Yaw torque it contributes about the craft's vertical axis.</summary>
        public float Torque { get; private set; }

        /// <summary>
        /// Heeling moment it contributes, N·m. Positive lays the craft over to starboard.
        /// Summed by the rig rather than derived from the total force, because each post can be
        /// canted a different way and the cant is half of what decides this.
        /// </summary>
        public float HeelMoment { get; private set; }

        /// <summary>Angle of attack in degrees: 0 edge-on and flogging, 90 broadside and stalled.</summary>
        public float AngleOfAttack { get; private set; }

        /// <summary>0..1, how much this sail is flogging.</summary>
        public float Luffing { get; private set; }

        /// <summary>Rope paid out, 0..1.</summary>
        public float SheetOut => sheetOut;

        /// <summary>Where the sheet lets the boom reach, degrees off the centreline.</summary>
        public float SheetLimit => Mathf.Lerp(minSheetAngle, maxSheetAngle, sheetOut);

        /// <summary>Where the boom actually is, degrees off the centreline. Read off the model.</summary>
        public float BoomAngle => measuredBoomAngle;

        /// <summary>Post lean, -1 hard to port to +1 hard to starboard.</summary>
        public float Cant => cant;

        /// <summary>The same as 0..1 with 0.5 upright, for a readout bar that centres.</summary>
        public float Cant01 => (cant + 1f) * 0.5f;

        /// <summary>Post lean in degrees. Positive leans the post to starboard.</summary>
        public float CantAngle => cant * maxCantAngle;

        /// <summary>Whether there is any cloth showing at all.</summary>
        public bool IsHoisted => hoist01 > 0.01f;

        /// <summary>0 fully struck, 1 fully set. Every value between is reachable.</summary>
        public float Hoist01 => hoist01;

        /// <summary>Where a sheet attaches, for <see cref="RiggingRope"/>.</summary>
        public Transform Clew => clew != null ? clew : transform;

        /// <summary>Sail area, m².</summary>
        public float Area => area;

        /// <summary>
        /// Area actually working: what is hoisted, foreshortened by however far the post leans.
        /// </summary>
        public float EffectiveArea =>
            area * hoist01 * SailAerodynamics.CantDriveFactor(CantAngle);

        /// <summary>Distance from the foil, positive forward.</summary>
        public float LeverArm { get => leverArm; set => leverArm = value; }

        // --- player controls --------------------------------------------------

        /// <summary>Pay out sheet. Lets the sail swing further off the centreline.</summary>
        public void EaseSheet(float deltaTime) => SetSheet(sheetOut + sheetRate * deltaTime);

        /// <summary>Haul in sheet. Pins the sail closer to the centreline.</summary>
        public void TrimSheet(float deltaTime) => SetSheet(sheetOut - sheetRate * deltaTime);

        /// <summary>
        /// Put the sheet here, 0..1, and put every slaved sail on the same setting.
        ///
        /// The wing panels have no winch of their own. Left free they would still make force —
        /// a sail nobody can trim, quietly biasing the helm — so the main's sheet works them
        /// too, which is also what the model depicts: they are panels of one sail plan.
        /// </summary>
        public void SetSheet(float value)
        {
            sheetOut = Mathf.Clamp01(value);
            foreach (SailSurface slave in sheetSlaves)
                if (slave != null && slave != this) slave.sheetOut = sheetOut;
        }

        /// <summary>Lean the post to starboard.</summary>
        public void CantToStarboard(float deltaTime) => SetCant(cant + cantRate * deltaTime);

        /// <summary>Lean the post to port.</summary>
        public void CantToPort(float deltaTime) => SetCant(cant - cantRate * deltaTime);

        /// <summary>Put the post here: -1 hard to port, 0 upright, +1 hard to starboard.</summary>
        public void SetCant(float value) => cant = Mathf.Clamp(value, -1f, 1f);

        /// <summary>
        /// Work the halyard up. Continuous, and that is the whole point.
        ///
        /// This used to be a pair of one-press verbs, and the "down" one struck EVERY sail on the
        /// craft instantly. Its winch sat 1.5 m from the jib sheet winch, both answered the same
        /// mouse button, and the interaction handles were 0.6 m spheres — so a stray click while
        /// trimming the jib silently dropped the entire rig and the cloth vanished mid-passage.
        /// A halyard you have to hold cannot do that: a mis-click costs a few centimetres of
        /// hoist and you can see it happen on the sail.
        /// </summary>
        public void RaiseHalyard(float deltaTime) => SetHoist(hoist01 + hoistRate * deltaTime);

        /// <summary>Work the halyard down. See <see cref="RaiseHalyard"/>.</summary>
        public void LowerHalyard(float deltaTime) => SetHoist(hoist01 - hoistRate * deltaTime);

        /// <summary>Put the halyard exactly here, 0..1. Cancels any run in progress.</summary>
        public void SetHoist(float value)
        {
            hoist01 = Mathf.Clamp01(value);
            hoistTarget = hoist01;
        }

        /// <summary>Run the sail all the way up over <c>hoistDuration</c>. For scripts and tests.</summary>
        public void Hoist() => hoistTarget = 1f;

        /// <summary>Run it all the way down over <c>hoistDuration</c>. Not reachable from input.</summary>
        public void Furl() => hoistTarget = 0f;

        public void SetHoisted(bool value) => hoistTarget = value ? 1f : 0f;

        // ----------------------------------------------------------------------

        private bool initialised;

        private void Awake() => Initialise();

        /// <summary>
        /// Cache the rest pose and work out which way the pivots turn.
        ///
        /// Idempotent and called from <see cref="Tick"/> as well as from Awake, so a harness can
        /// step a freshly instantiated craft without a play session — Unity does not run Awake on
        /// an object created in the editor, and everything below is what makes the difference
        /// between a sail that trims and a mast that falls over.
        /// </summary>
        private void Initialise()
        {
            if (initialised) return;
            initialised = true;

            block = new MaterialPropertyBlock();
            if (cloth != null) clothRenderers = cloth.GetComponentsInChildren<Renderer>(true);
            if (craft == null)
            {
                SailRig rig = GetComponentInParent<SailRig>();
                craft = rig != null ? rig.transform : transform.root;
            }

            if (cantPivot != null)
            {
                restCant = cantPivot.localRotation;
                cantAxisInParent = MeasureCantAxis();
            }
            if (sparPivot != null) restSpar = sparPivot.localRotation;

            CalibrateSparSign();

            hoistTarget = hoist01;
            ApplyHoistVisibility();
            ApplyCant(1f);
            measuredBoomAngle = ReadBoomAngle(CraftHeading());
        }

        /// <summary>
        /// Which way to turn the cant pivot so a positive lean puts the post to starboard.
        ///
        /// Measured rather than assumed. Everything under an imported FBX inherits the importer's
        /// Blender-to-Unity axis correction, so "local X" means nothing predictable this far down
        /// the hierarchy — the mast rake control shipped rotating about a local axis that
        /// happened to be right, and the sail trim control shipped rotating about one that was
        /// not, and neither is discoverable from the inspector. Asking the transform which way
        /// the hull's bow points, in the pivot's own parent space, cannot be wrong.
        ///
        /// Negated because a positive turn about the bow direction drops the starboard side and
        /// therefore throws the masthead to PORT, and every other signed quantity on this craft
        /// takes positive as starboard.
        /// </summary>
        private Vector3 MeasureCantAxis()
        {
            Transform parent = cantPivot.parent != null ? cantPivot.parent : cantPivot;
            Vector3 forward = craft != null ? craft.forward : Vector3.forward;
            Vector3 axis = parent.InverseTransformDirection(forward);
            return axis.sqrMagnitude < 1e-8f ? Vector3.forward : -axis.normalized;
        }

        /// <summary>
        /// Find out which way turning the spar swings the boom, by turning it and looking.
        ///
        /// The spar axis comes off the model and the boom hangs off the far end of a curved yard,
        /// so whether a positive turn sends the clew to port or to starboard is a property of how
        /// this particular sail was built. Two probes at Awake settle it for good, and the trim
        /// loop below can then be written as if it were always positive.
        /// </summary>
        private void CalibrateSparSign()
        {
            sparSign = 1f;
            if (sparPivot == null || clew == null) return;

            Vector3 heading = CraftHeading();
            float before = ReadBoomAngle(heading);

            SetSpar(12f);
            float after = ReadBoomAngle(heading);
            SetSpar(0f);

            float swing = Mathf.DeltaAngle(before, after);
            if (Mathf.Abs(swing) < 0.5f)
            {
                // Turning the spar does not move the boom at all: the axis is wrong, or the clew
                // sits on it. Leave the sail where the model put it rather than spinning the
                // whole rig looking for an angle it can never reach.
                Debug.LogWarning($"[{nameof(SailSurface)}] {name}: turning the spar about " +
                                 $"{sparAxis} does not swing the boom, so this sail cannot be " +
                                 "trimmed. Check the spar axis against the model.", this);
                return;
            }
            if (swing < 0f) sparSign = -1f;
        }

        private Vector3 CraftHeading()
        {
            Vector3 h = SailAerodynamics.Flatten(craft != null ? craft.forward : Vector3.forward);
            return h.sqrMagnitude < 1e-8f ? Vector3.forward : h.normalized;
        }

        /// <summary>
        /// Update the sail against the wind and report what it makes.
        /// Driven by <see cref="SailRig"/> rather than by Unity, so the whole rig resolves in a
        /// known order within one frame.
        /// </summary>
        /// <param name="apparentWind">Apparent wind, world space, horizontal.</param>
        /// <param name="heading">Craft heading, world space, horizontal, unit length.</param>
        /// <param name="airDensity">Force scale.</param>
        /// <param name="deltaTime">Frame time.</param>
        public void Tick(Vector3 apparentWind, Vector3 heading, float airDensity, float deltaTime)
        {
            Initialise();
            AdvanceHoist(deltaTime);
            ApplyCant(1f - Mathf.Exp(-cantSpeed * deltaTime));

            // Trim as a closed loop on the model's own geometry rather than on a number.
            //
            // The boom hangs off a curved yard that is itself leaning over, so the angle the spar
            // is turned through and the angle the boom ends up at are not the same number and the
            // relation between them changes with the cant. Steering the measured angle toward the
            // wanted one costs one extra read, is self-correcting, and guarantees the force below
            // is computed for the sail the player can actually see.
            float wanted = SailAerodynamics.TrimmedSailAngle(
                SailAerodynamics.WeathervaneAngle(heading, apparentWind), SheetLimit);

            measuredBoomAngle = ReadBoomAngle(heading);
            float error = Mathf.DeltaAngle(measuredBoomAngle, wanted);
            SetSpar(sparAngle + error * sparSign * (1f - Mathf.Exp(-swingSpeed * deltaTime)));
            measuredBoomAngle = ReadBoomAngle(heading);

            if (hoist01 <= 0.01f)
            {
                Force = Vector3.zero;
                Torque = 0f;
                HeelMoment = 0f;
                AngleOfAttack = 0f;
                Luffing = 0f;
                PushShaderParams(apparentWind, 0f);
                return;
            }

            Vector3 normal = SailAerodynamics.NormalFromBoomAngle(measuredBoomAngle, heading);

            Force = SailAerodynamics.SailForce(apparentWind, normal, EffectiveArea, airDensity,
                                               out float aoa);
            AngleOfAttack = aoa;

            float cantAngle = CantAngle;
            float offset = SailAerodynamics.CantLateralOffset(cantAngle, centreOfEffortHeight);
            Torque = SailAerodynamics.YawTorque(Force, heading, leverArm, offset);

            Vector3 right = Vector3.Cross(Vector3.up, heading);
            float lateral = Vector3.Dot(SailAerodynamics.Flatten(Force), right);
            float cantRadians = cantAngle * Mathf.Deg2Rad;
            HeelMoment = lateral * Mathf.Cos(cantRadians)
                       + cantRightingMoment * Mathf.Sin(cantRadians);

            Luffing = SailAerodynamics.LuffAmount(aoa);
            PushShaderParams(apparentWind, SailAerodynamics.BillowAmount(
                aoa, apparentWind.magnitude, referenceWindSpeed));
        }

        /// <summary>
        /// Where the boom is pointing right now, off the centreline, read off the model.
        /// Falls back to the commanded angle if there is no clew to measure to.
        /// </summary>
        private float ReadBoomAngle(Vector3 heading)
        {
            if (clew == null || sparPivot == null) return measuredBoomAngle;

            Vector3 boom = SailAerodynamics.Flatten(clew.position - sparPivot.position);
            // A boom seen almost end-on from above has no horizontal direction worth trusting;
            // hold the last good reading rather than letting the trim loop chase noise.
            if (boom.magnitude < 0.15f) return measuredBoomAngle;

            return SailAerodynamics.BoomAngle(boom, heading);
        }

        private void SetSpar(float angle)
        {
            sparAngle = Mathf.Clamp(angle, -170f, 170f);
            if (sparPivot == null) return;

            Vector3 axis = sparAxis.sqrMagnitude < 1e-6f ? Vector3.up : sparAxis.normalized;
            sparPivot.localRotation = restSpar * Quaternion.AngleAxis(sparAngle, axis);
        }

        /// <summary>Lean the post toward where the control is asking for, at <paramref name="t"/>.</summary>
        private void ApplyCant(float t)
        {
            if (cantPivot == null) return;

            appliedCantAngle = Mathf.Lerp(appliedCantAngle, CantAngle, Mathf.Clamp01(t));
            cantPivot.localRotation =
                Quaternion.AngleAxis(appliedCantAngle, cantAxisInParent) * restCant;
        }

        private void AdvanceHoist(float deltaTime)
        {
            // Only moves when something asked for a run (Hoist/Furl/SetHoisted). Player input
            // writes hoist01 and hoistTarget together, so this is a no-op while they hold the
            // halyard and the sail answers the control with no lag at all.
            hoist01 = Mathf.MoveTowards(hoist01, hoistTarget, deltaTime / hoistDuration);
            ApplyHoistVisibility();
        }

        /// <summary>
        /// The cloth and battens disappear; the post is parented above this node so it stays.
        /// Kept as a hard show/hide rather than a scale-down because a sail on this rig is
        /// lashed to its spars — there is nowhere for it to roll to.
        /// </summary>
        private void ApplyHoistVisibility()
        {
            if (cloth == null) return;
            bool visible = hoist01 > 0.005f;
            if (cloth.activeSelf != visible) cloth.SetActive(visible);
        }

        private void PushShaderParams(Vector3 apparentWind, float billow)
        {
            if (clothRenderers.Length == 0) return;

            Vector3 dir = apparentWind.sqrMagnitude > 1e-6f
                ? apparentWind.normalized
                : Vector3.forward;

            foreach (Renderer r in clothRenderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(block);
                block.SetFloat(BillowId, billow);
                block.SetFloat(LuffId, Luffing);
                block.SetFloat(HoistId, hoist01);
                block.SetVector(WindDirId, new Vector4(dir.x, dir.y, dir.z, 0f));
                r.SetPropertyBlock(block);
            }
        }

        /// <summary>Wire the rig nodes up. Used by the prefab builder.</summary>
        public void Bind(Transform cant_, Transform spar_, GameObject cloth_, Transform clew_,
                         Transform craft_, float area_, float lever)
        {
            cantPivot = cant_;
            sparPivot = spar_;
            cloth = cloth_;
            clew = clew_;
            craft = craft_;
            area = Mathf.Max(0.1f, area_);
            leverArm = lever;
        }

        /// <summary>Set the sheet travel limits. Used by the prefab builder.</summary>
        public void ConfigureSheet(float minAngle, float maxAngle)
        {
            minSheetAngle = minAngle;
            maxSheetAngle = maxAngle;
        }

        /// <summary>Set the post geometry. Used by the prefab builder.</summary>
        public void ConfigurePost(Vector3 spar, float centreHeight, float rightingMoment,
                                  float maxCant)
        {
            sparAxis = spar.sqrMagnitude < 1e-6f ? Vector3.up : spar.normalized;
            centreOfEffortHeight = Mathf.Max(0f, centreHeight);
            cantRightingMoment = Mathf.Max(0f, rightingMoment);
            maxCantAngle = Mathf.Clamp(maxCant, 0f, 60f);
        }

        /// <summary>Trim these sails with this one. Used by the prefab builder.</summary>
        public void BindSheetSlaves(List<SailSurface> slaves)
        {
            sheetSlaves = slaves ?? new List<SailSurface>();
        }
    }
}
