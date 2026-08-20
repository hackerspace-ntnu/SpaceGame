// The dune foiler's wheel. Look at it, press E, steer with A and D.
//
// The wheel turns the FOIL. There is one thing on this craft in contact with the sand — a
// fourteen-metre strut with a blade on the end of it — so pointing that blade is the whole
// steering system, and you can watch it swing under the hull as you turn the wheel. What the hull
// then does about it is FoilRudder's and DuneFoilLocomotion's business; this is the handle.
//
// The rig is still the other helm. Sail force acting forward or aft of the craft's centre of
// resistance biases the turn, so a badly trimmed rig fights the wheel and a well trimmed one makes
// it light. The skill in sailing this thing survives; the frustration of not being able to aim it
// does not.
//
// Deliberately not a MountModule, for the same reason DeckBoarding is not: mounting takes over the
// camera and the controls, and this craft's controls ARE its deck. Taking the helm plants your feet
// at the wheel and borrows A/D. You keep your body, your camera and your ability to look around at
// the sails you are steering by.
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Characters;
using SpaceGame.Gameplay;
using SpaceGame.Vehicles.DuneFoil;

namespace SpaceGame.Vehicles
{
    [DefaultExecutionOrder(80)]     // before DuneFoilLocomotion (100) reads the rudder
    public class DuneFoilHelm : MonoBehaviour, IInteractable, IInteractionReadout
    {
        [Header("Craft")]
        [Tooltip("The craft this wheel steers. Found in the parents when empty.")]
        [SerializeField] private DuneFoilLocomotion locomotion;

        [Header("Wheel")]
        [Tooltip("The wheel mesh, spun so the player can see the helm answer.")]
        [SerializeField] private Transform wheelPivot;

        [Tooltip("Axis the wheel turns about, in the wheel's OWN local space. Y for the shipped " +
                 "model; the builder measures it off the mesh rather than trusting this default.")]
        [SerializeField] private Vector3 spinAxis = Vector3.up;

        [Tooltip("How far the wheel turns at full rudder, degrees. Well over 90 so it reads as " +
                 "several turns of a real ship's wheel rather than a dial.")]
        [SerializeField] private float wheelTravel = 220f;

        [Tooltip("How quickly the wheel mesh catches up to the rudder. Cosmetic only.")]
        [SerializeField, Min(0.1f)] private float wheelSpeed = 8f;

        [Header("Feel")]
        [Tooltip("Seconds from centred to hard over. The rudder ramps rather than snapping, so " +
                 "a tap of A is a nudge and a held A is a turn.")]
        [SerializeField, Min(0.05f)] private float rudderRampTime = 0.55f;

        [Tooltip("Seconds from hard over back to centre when the player lets go of A and D. " +
                 "Faster than the ramp: a helm that will not straighten up is the worst kind.")]
        [SerializeField, Min(0.05f)] private float rudderCentreTime = 0.3f;

        [Header("Standing at the wheel")]
        [Tooltip("Where the helmsman stands. Their feet are put here and held there for as long " +
                 "as they have the helm. Falls back to a spot just aft of the wheel.")]
        [SerializeField] private Transform stance;

        [Tooltip("How far the player can drift from the wheel before the helm lets go of them. " +
                 "A backstop for teleports, deaths and physics accidents, not a normal exit.")]
        [SerializeField, Min(0.5f)] private float releaseDistance = 3.5f;

        [Tooltip("The deck's carrier. While somebody has the helm it is told not to carry them, " +
                 "because this component is already placing them relative to the hull and being " +
                 "moved by the hull twice makes the helmsman slide off the stern. Found on the " +
                 "craft when empty.")]
        [SerializeField] private WalkerPlatformCarrier carrier;

        private Transform helmsman;
        private Rigidbody helmsmanBody;
        private PlayerInputManager helmsmanInput;
        private PlayerMovement helmsmanMovement;
        private bool restoreMovement;
        private int tookHelmFrame = -1;
        private Vector3 stanceOffset;

        private float rudder;
        private float wheelAngle;
        private Quaternion restRotation = Quaternion.identity;

        /// <summary>True while somebody is steering.</summary>
        public bool IsManned => helmsman != null;

        /// <summary>Rudder the helm is asking for, -1 to 1.</summary>
        public float Rudder => rudder;

        // --- IInteractionReadout ----------------------------------------------

        public string Label => "Helm";

        public string Prompt => IsManned
            ? "A / D: steer the foil   E: let go"
            : "E: take the helm";

        /// <summary>Centred reads as half, so the bar sits in the middle like a real rudder gauge.</summary>
        public float? Value01 => IsManned ? (rudder + 1f) * 0.5f : (float?)null;

        public string ValueText
        {
            get
            {
                if (!IsManned) return string.Empty;
                if (Mathf.Abs(rudder) < 0.05f) return "amidships";

                // Shown as the blade's real angle rather than as a percentage of a control the
                // player cannot see. The foil is the thing that turns the craft, so the foil is
                // the thing worth reading.
                float blade = locomotion != null && locomotion.Rudder != null
                    ? locomotion.Rudder.SteerAngle
                    : rudder * 30f;

                return blade < 0f
                    ? $"foil {-blade:F0}° to port"
                    : $"foil {blade:F0}° to starboard";
            }
        }

        // ----------------------------------------------------------------------

        private void Awake()
        {
            // Captured, not assumed: the wheel's authored orientation is where "amidships" is, and
            // every turn is composed onto it.
            if (wheelPivot != null) restRotation = wheelPivot.localRotation;

            if (locomotion == null) locomotion = GetComponentInParent<DuneFoilLocomotion>();
            if (carrier == null) carrier = GetComponentInParent<WalkerPlatformCarrier>();
            if (locomotion == null)
                Debug.LogWarning($"[{nameof(DuneFoilHelm)}] {name} has no DuneFoilLocomotion above " +
                                 "it; this wheel will spin and steer nothing.", this);
        }

        public bool CanInteract() => locomotion != null;

        public void Interact(Interactor interactor)
        {
            if (locomotion == null) return;

            if (IsManned)
            {
                // Only the person actually steering can stand down, so a second player looking at
                // the wheel cannot take it out from under them.
                if (interactor != null && interactor.transform.root == helmsman) Release();
                return;
            }

            TakeHelm(interactor);
        }

        private void TakeHelm(Interactor interactor)
        {
            if (interactor == null) return;

            Transform player = interactor.transform.root;
            if (player == null) return;

            PlayerController controller = interactor.GetComponentInParent<PlayerController>();
            if (controller == null) return;

            helmsman = player;
            helmsmanBody = player.GetComponent<Rigidbody>();
            helmsmanInput = controller.Input;
            tookHelmFrame = Time.frameCount;

            // Switch their own movement off rather than pinning them while it is still running.
            // That frees A and D for the wheel without the helm and the legs fighting over the
            // same two keys — and it is also what makes the pin below safe: PlayerMovement writes
            // the horizontal velocity every physics step, so pinning against it would be two
            // things arguing about the same Rigidbody.
            helmsmanMovement = controller.GetComponent<PlayerMovement>();
            restoreMovement = helmsmanMovement != null && helmsmanMovement.enabled;
            if (restoreMovement) helmsmanMovement.enabled = false;

            // Remember where they are standing relative to the deck, so the pin holds them at the
            // wheel rather than snapping them to one exact spot on it.
            // Hold them where they are standing, not on one exact spot: pinned to a fixed point
            // the helm would snatch the player off their feet the instant they pressed E, and the
            // craft would have one legal place to stand at the wheel. Only the horizontal reach is
            // clamped — the vertical offset is how far their pivot sits above their own feet, and
            // scaling that down buries them in the planks.
            Vector3 offset = StanceAnchor().InverseTransformPoint(player.position);
            Vector3 flat = Vector3.ClampMagnitude(new Vector3(offset.x, 0f, offset.z),
                                                  releaseDistance * 0.5f);
            stanceOffset = new Vector3(flat.x, offset.y, flat.z);

            // The deck stops carrying them: from here until they stand down, this component is
            // the only thing that decides where they are.
            if (carrier != null) carrier.ClaimRider(helmsmanBody);

            // E anywhere lets go, not just E while looking back at the wheel. Standing at a helm
            // you are looking at the sails, not at your own hands.
            if (helmsmanInput != null) helmsmanInput.OnInteractPressed += OnHelmsmanInteract;
        }

        private Transform StanceAnchor() => stance != null ? stance : transform;

        private void OnHelmsmanInteract()
        {
            // The press that took the helm must not also release it. Interactor raises this on the
            // same frame TakeHelm ran, and whether the freshly added handler sees that in-flight
            // invocation is a detail of how the delegate was built — so do not depend on it.
            if (Time.frameCount == tookHelmFrame) return;
            Release();
        }

        /// <summary>Stand down from the wheel and give the player their legs back.</summary>
        public void Release()
        {
            if (helmsmanInput != null) helmsmanInput.OnInteractPressed -= OnHelmsmanInteract;
            if (restoreMovement && helmsmanMovement != null) helmsmanMovement.enabled = true;
            if (carrier != null) carrier.ReleaseRider(helmsmanBody);

            helmsman = null;
            helmsmanBody = null;
            helmsmanInput = null;
            helmsmanMovement = null;
            restoreMovement = false;

            // Leave the rudder where it is and let the locomotion centre it, so stepping away
            // mid-turn finishes the turn rather than snapping the hull straight.
        }

        private void OnDisable() => Release();

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            if (IsManned && !StillAboard()) Release();

            if (IsManned)
            {
                float steer = helmsmanInput != null ? helmsmanInput.MoveInput.x : 0f;

                // Ramp toward what they are asking for, fall back to centre when they let go.
                float rate = Mathf.Abs(steer) > 0.01f ? 1f / rudderRampTime : 1f / rudderCentreTime;
                rudder = Mathf.MoveTowards(rudder, Mathf.Clamp(steer, -1f, 1f), rate * dt);

                if (locomotion != null) locomotion.SetRudder(rudder);
            }
            else
            {
                rudder = Mathf.MoveTowards(rudder, 0f, dt / rudderCentreTime);
            }

            SpinWheel(dt);
        }

        /// <summary>
        /// Hold the helmsman at the wheel.
        ///
        /// The hull has already moved this frame — the locomotion writes it in Update — so a spot
        /// measured off the wheel now is a spot on the deck as it currently is. The carrier has
        /// been told to leave this rider alone (see <c>ClaimRider</c>), which is what makes that
        /// safe: exactly one thing is placing them.
        ///
        /// It has to be a pin rather than a nudge. With their own movement switched off, nothing
        /// is holding the player's horizontal velocity at zero any more, so on a heeling deck they
        /// slide — slowly at first, then off the leeward rail, at which point the craft is sailing
        /// itself with its helmsman somewhere astern. Their own body, their own camera and their
        /// own view are all untouched; only their feet are nailed down, which is what standing at
        /// a wheel is.
        /// </summary>
        private void LateUpdate()
        {
            if (!IsManned) return;

            Vector3 target = StanceAnchor().TransformPoint(stanceOffset);

            if (helmsmanBody != null)
            {
                helmsmanBody.linearVelocity = Vector3.zero;
                helmsmanBody.angularVelocity = Vector3.zero;
                helmsmanBody.position = target;
            }
            helmsman.position = target;

            // Their movement script is off, so nothing is feeding the animator any more and it
            // would otherwise hold whatever pose they walked up in — usually mid-stride.
            if (helmsmanMovement != null) helmsmanMovement.ForceIdleAnimation();
        }

        /// <summary>Has the helmsman gone? Teleported, died, fallen off, or been despawned.</summary>
        private bool StillAboard()
        {
            if (helmsman == null || !helmsman.gameObject.activeInHierarchy) return false;
            return (helmsman.position - transform.position).sqrMagnitude
                   <= releaseDistance * releaseDistance;
        }

        private void SpinWheel(float dt)
        {
            if (wheelPivot == null) return;

            wheelAngle = Mathf.Lerp(wheelAngle, rudder * wheelTravel,
                                    1f - Mathf.Exp(-wheelSpeed * dt));

            // Turned about an explicit axis, composed onto the rest pose rather than written into
            // localEulerAngles.
            //
            // The first version drove localEulerAngles.z, on the reasoning that Blender's -Y
            // forward becomes Unity's +Z. That is true of the imported ROOT, and false of every
            // node beneath it: the FBX is exported with bake_space_transform off, so the Z-up to
            // Y-up correction lives on the root transform alone and children keep Blender-native
            // local axes. Under Station_Helm, local Z points at the sky — so the wheel span like
            // a turntable lying flat instead of turning like a wheel. The mesh settles the
            // argument: Helm_Wheel measures 0.91 x 0.26 x 0.91, and the thin axis of a wheel is
            // the one it turns about.
            Vector3 axis = spinAxis.sqrMagnitude < 1e-6f ? Vector3.up : spinAxis.normalized;
            wheelPivot.localRotation = restRotation * Quaternion.AngleAxis(-wheelAngle, axis);
        }

        /// <summary>Wire it up. Used by the prefab builder.</summary>
        public void Bind(DuneFoilLocomotion craft, Transform wheel, Transform stancePoint = null)
        {
            locomotion = craft;
            wheelPivot = wheel;
            spinAxis = MeasureSpinAxis(wheel);
            stance = stancePoint;
        }

        /// <summary>
        /// The axis a wheel turns about is the one it is thinnest along — measured off the mesh
        /// rather than hardcoded, because the local axes of anything under an imported FBX depend
        /// on how the model was exported, and getting it wrong spins the wheel like a turntable.
        /// Falls back to Y, which is what the shipped model uses.
        /// </summary>
        public static Vector3 MeasureSpinAxis(Transform wheel)
        {
            MeshFilter filter = wheel != null ? wheel.GetComponent<MeshFilter>() : null;
            if (filter == null || filter.sharedMesh == null) return Vector3.up;

            Vector3 size = filter.sharedMesh.bounds.size;
            if (size.x <= size.y && size.x <= size.z) return Vector3.right;
            if (size.y <= size.z) return Vector3.up;
            return Vector3.forward;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.9f);
            foreach (Collider c in GetComponentsInChildren<Collider>())
                Gizmos.DrawWireCube(c.bounds.center, c.bounds.size);

            Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.35f);
            Gizmos.DrawWireSphere(transform.position, releaseDistance);

            if (stance != null)
            {
                Gizmos.color = new Color(0.95f, 0.35f, 0.30f, 0.9f);
                Gizmos.DrawWireSphere(stance.position, 0.35f);
                Gizmos.DrawLine(stance.position, stance.position + Vector3.up * 1.8f);
            }
        }
    }
}
