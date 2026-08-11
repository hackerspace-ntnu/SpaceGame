using UnityEngine;

namespace SpaceGame.Vehicles.DuneFoil
{
    /// <summary>
    /// One sail on the rig: its hoist state, how much sheet is paid out, the rake of its post,
    /// and the force it makes.
    ///
    /// The sail is never told what angle to sit at. It weathervanes to trail the apparent wind
    /// and the sheet stops it — so paying out rope lets it swing further and hauling in pins it
    /// closer to the centreline, exactly as on a boat. That is what makes the rope stations a
    /// real control rather than a dial labelled "sail angle".
    ///
    /// Owns its own transforms and nothing else. It reports a force; it never moves the craft.
    /// </summary>
    public class SailSurface : MonoBehaviour
    {
        [Header("Rig nodes")]
        [Tooltip("Pivot at the foot of the post. Rotating it rakes the whole spar fore and aft.")]
        [SerializeField] private Transform rakePivot;

        [Tooltip("Pivot on the spar axis. Rotating its local Y swings the sail about its own " +
                 "post, which is what keeps the cloth attached to a raked mast.")]
        [SerializeField] private Transform yawPivot;

        [Tooltip("The cloth and battens. Hidden when the sail is furled; the post is not under " +
                 "this node, so it stays up.")]
        [SerializeField] private GameObject cloth;

        [Tooltip("Where a sheet is made fast, for the rope to draw to.")]
        [SerializeField] private Transform clew;

        [Header("Sail")]
        [Tooltip("Area in square metres. Drives force directly; measured off the mesh by the " +
                 "builder, so it stays right if the model is re-authored.")]
        [SerializeField, Min(0.1f)] private float area = 60f;

        [Tooltip("Distance along the hull from the foil to this sail's centre of effort, " +
                 "positive forward. Sign decides which way the sail steers the craft.")]
        [SerializeField] private float leverArm;

        [Tooltip("Off. A sail forward of the foil bears away, one aft luffs up; if this sail " +
                 "reads backwards on the model, flip it here rather than moving the pivot.")]
        [SerializeField] private bool invertYaw;

        [Header("Sheet")]
        [Tooltip("Rope paid out, 0..1. 0 is sheeted hard in on the centreline, 1 is all the " +
                 "way out. The player drives this from the rope station.")]
        [SerializeField, Range(0f, 1f)] private float sheetOut = 0.5f;

        [Tooltip("Sail angle off the centreline at sheetOut = 1. Around 80 degrees is a boom " +
                 "against the shrouds.")]
        [SerializeField, Range(0f, 90f)] private float maxSheetAngle = 80f;

        [Tooltip("Angle at sheetOut = 0. Never quite zero: a sail hauled flat amidships still " +
                 "sits a few degrees off.")]
        [SerializeField, Range(0f, 45f)] private float minSheetAngle = 5f;

        [Tooltip("Degrees per second the sheet moves while the player holds the control.")]
        [SerializeField, Min(0.01f)] private float sheetRate = 0.4f;

        [Header("Rake")]
        [Tooltip("Post rake, 0..1, from fully forward to fully aft.")]
        [SerializeField, Range(0f, 1f)] private float rake = 0.5f;
        [SerializeField] private float rakeMinAngle = -12f;
        [SerializeField] private float rakeMaxAngle = 22f;
        [SerializeField, Min(0.01f)] private float rakeRate = 0.3f;

        [Header("Hoist")]
        [SerializeField] private bool hoisted = true;
        [Tooltip("Seconds for the sail to go up or come down.")]
        [SerializeField, Min(0.05f)] private float hoistDuration = 1.6f;

        [Header("Response")]
        [Tooltip("How fast the sail swings to its trimmed angle. Cloth has inertia; a sail that " +
                 "snaps to its new angle reads as a rigid board.")]
        [SerializeField, Min(0.1f)] private float swingSpeed = 2.5f;

        [Tooltip("Wind speed the shader treats as 'fully powered up' when setting the belly.")]
        [SerializeField, Min(0.1f)] private float referenceWindSpeed = 14f;

        private static readonly int BillowId = Shader.PropertyToID("_Billow");
        private static readonly int LuffId = Shader.PropertyToID("_Luff");
        private static readonly int WindDirId = Shader.PropertyToID("_WindDirection");
        private static readonly int HoistId = Shader.PropertyToID("_Hoist");

        private MaterialPropertyBlock block;
        private Renderer[] clothRenderers = System.Array.Empty<Renderer>();
        private float currentAngle;
        private float hoist01 = 1f;
        private float restYawLocalY;

        // --- reported state ---------------------------------------------------

        /// <summary>Force this sail is making, world space, this frame.</summary>
        public Vector3 Force { get; private set; }

        /// <summary>Yaw torque it contributes about the craft's vertical axis.</summary>
        public float Torque { get; private set; }

        /// <summary>Angle of attack in degrees, for UI and the shader.</summary>
        public float AngleOfAttack { get; private set; }

        /// <summary>0..1, how much this sail is flogging.</summary>
        public float Luffing { get; private set; }

        /// <summary>Rope paid out, 0..1.</summary>
        public float SheetOut => sheetOut;

        /// <summary>Post rake, 0..1.</summary>
        public float Rake => rake;

        /// <summary>Whether the sail is set. Reflects intent, not the animation.</summary>
        public bool IsHoisted => hoisted;

        /// <summary>0 fully furled, 1 fully set. Mid values while it runs up or down.</summary>
        public float Hoist01 => hoist01;

        /// <summary>Where a sheet attaches, for <see cref="RiggingRope"/>.</summary>
        public Transform Clew => clew != null ? clew : transform;

        /// <summary>Sail area, m².</summary>
        public float Area => area;

        /// <summary>Distance from the foil, positive forward.</summary>
        public float LeverArm { get => leverArm; set => leverArm = value; }

        // --- player controls --------------------------------------------------

        /// <summary>Pay out sheet. Lets the sail swing further off the centreline.</summary>
        public void EaseSheet(float deltaTime) => SetSheet(sheetOut + sheetRate * deltaTime);

        /// <summary>Haul in sheet. Pins the sail closer to the centreline.</summary>
        public void TrimSheet(float deltaTime) => SetSheet(sheetOut - sheetRate * deltaTime);

        public void SetSheet(float value) => sheetOut = Mathf.Clamp01(value);

        public void RakeAft(float deltaTime) => SetRake(rake + rakeRate * deltaTime);
        public void RakeForward(float deltaTime) => SetRake(rake - rakeRate * deltaTime);
        public void SetRake(float value) => rake = Mathf.Clamp01(value);

        public void Hoist() => hoisted = true;
        public void Furl() => hoisted = false;
        public void SetHoisted(bool value) => hoisted = value;

        // ----------------------------------------------------------------------

        private void Awake()
        {
            block = new MaterialPropertyBlock();
            if (cloth != null) clothRenderers = cloth.GetComponentsInChildren<Renderer>(true);
            if (yawPivot != null) restYawLocalY = yawPivot.localEulerAngles.y;
            hoist01 = hoisted ? 1f : 0f;
            ApplyHoistVisibility();
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
            AdvanceHoist(deltaTime);
            ApplyRake();

            float targetAngle = ResolveTrimmedAngle(apparentWind, heading);
            currentAngle = Mathf.LerpAngle(currentAngle, targetAngle,
                                           1f - Mathf.Exp(-swingSpeed * deltaTime));
            ApplyYaw();

            if (hoist01 <= 0.01f)
            {
                Force = Vector3.zero;
                Torque = 0f;
                AngleOfAttack = 0f;
                Luffing = 0f;
                PushShaderParams(apparentWind, 0f);
                return;
            }

            Vector3 normal = SailNormal(heading);
            // A half-hoisted sail catches proportionally less wind.
            float effectiveArea = area * hoist01;

            Force = SailAerodynamics.SailForce(apparentWind, normal, effectiveArea, airDensity,
                                               out float aoa);
            AngleOfAttack = aoa;
            Torque = SailAerodynamics.YawTorque(Force, heading, leverArm);
            Luffing = SailAerodynamics.LuffAmount(aoa);

            PushShaderParams(apparentWind, SailAerodynamics.BillowAmount(
                aoa, apparentWind.magnitude, referenceWindSpeed));
        }

        /// <summary>
        /// Where the sail settles: trailing the wind, unless the sheet stops it first.
        /// </summary>
        private float ResolveTrimmedAngle(Vector3 apparentWind, Vector3 heading)
        {
            float limit = Mathf.Lerp(minSheetAngle, maxSheetAngle, sheetOut);
            float vane = SailAerodynamics.WeathervaneAngle(heading, apparentWind);
            return SailAerodynamics.TrimmedSailAngle(vane, limit);
        }

        /// <summary>The sail's chord-plane normal in world space, horizontal.</summary>
        private Vector3 SailNormal(Vector3 heading)
        {
            float sign = invertYaw ? -1f : 1f;
            Vector3 chord = Quaternion.AngleAxis(currentAngle * sign, Vector3.up) * heading;
            return Vector3.Cross(Vector3.up, chord).normalized;
        }

        private void ApplyYaw()
        {
            if (yawPivot == null) return;
            float sign = invertYaw ? -1f : 1f;
            Vector3 e = yawPivot.localEulerAngles;
            // Local Y is the spar axis: the rig script aligned this node's up with the post.
            yawPivot.localEulerAngles = new Vector3(e.x, restYawLocalY + currentAngle * sign, e.z);
        }

        private void ApplyRake()
        {
            if (rakePivot == null) return;
            float angle = Mathf.Lerp(rakeMinAngle, rakeMaxAngle, rake);
            Vector3 e = rakePivot.localEulerAngles;
            rakePivot.localEulerAngles = new Vector3(angle, e.y, e.z);
        }

        private void AdvanceHoist(float deltaTime)
        {
            float target = hoisted ? 1f : 0f;
            hoist01 = Mathf.MoveTowards(hoist01, target, deltaTime / hoistDuration);
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
        public void Bind(Transform rake_, Transform yaw_, GameObject cloth_, Transform clew_,
                         float area_, float lever)
        {
            rakePivot = rake_;
            yawPivot = yaw_;
            cloth = cloth_;
            clew = clew_;
            area = Mathf.Max(0.1f, area_);
            leverArm = lever;
        }

        /// <summary>Set the sheet travel limits. Used by the prefab builder.</summary>
        public void ConfigureSheet(float minAngle, float maxAngle, bool invert)
        {
            minSheetAngle = minAngle;
            maxSheetAngle = maxAngle;
            invertYaw = invert;
        }
    }
}
