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
//
// ── Crewed by more than one person ──
// The claim is a VehicleStation, so the server decides who got the wheel and every machine hears
// the same answer. Two things follow from that, and both are load-bearing:
//
//   • Taking the helm hands the whole hull's ownership to the helmsman (TakesVehicleOwnership), so
//     their local input drives the craft and the motion replicates outward from them. Without it
//     they steer a body they do not own and the server's NetworkTransform overwrites the input
//     every tick.
//   • Everything that takes the helmsman's own controls away — their movement script, their input
//     subscription, the pin that holds their feet at the wheel — happens ONLY on the machine that
//     owns that player. A remote peer knows who is steering, and draws them standing there because
//     their own machine puts them there and the position replicates; it must never reach across and
//     switch off a body it does not own.
//
// DuneFoilLocomotion is deliberately NOT switched off on the machines that are only watching — no
// NetAuthority here, unlike a server-driven creature. Three reasons, and they all matter:
//
//   • It cannot fight the replicated pose. A non-authority NetworkTransform writes the transform at
//     NetworkUpdateStage.PreLateUpdate, which is AFTER every Update and before every LateUpdate, so
//     the locomotion's write is overwritten before anything reads it or renders it. Nothing on this
//     craft reads the hull between those two points: the carrier runs in FixedUpdate, the HUD and
//     the helm's pin run in LateUpdate.
//   • It is what animates the craft everywhere. The sails, the boom swing, the mast cant, the foil
//     blade and the ride height are all produced by DuneFoilLocomotion.Step calling into the rig
//     and the foil. Switched off, a watching machine would see a hull sliding across the sand with
//     dead cloth on it — and DeckBoarding's "no boarding a craft up on its foil" gate, which reads
//     FoilLift.RideHeight01, would answer from a foil that never ticked.
//   • It keeps every machine holding a live velocity for the craft, so the moment the helm changes
//     hands the new owner carries on from a plausible speed instead of accelerating up from zero.
//     DuneFoilLocomotion keeps its velocity private and has no way to be told one, so a machine
//     that had stopped simulating would have no way back in.
//
// The one prefab requirement that follows: the ClientNetworkTransform must synchronise every
// position and rotation axis. An axis left unsynced is one the local prediction writes and nothing
// ever corrects, and the drift on it accumulates for the whole session.
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Characters;
using SpaceGame.Gameplay;
using SpaceGame.Vehicles.DuneFoil;

namespace SpaceGame.Vehicles
{
    [DefaultExecutionOrder(80)]     // before DuneFoilLocomotion (100) reads the rudder
    public class DuneFoilHelm : VehicleStation, IInteractionReadout
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

        // Local-only cached views of whoever is at the wheel. Only the machine that OWNS that
        // player fills in the input/movement halves; every other machine keeps the body reference
        // alone, which is all the carrier claim below needs.
        private Rigidbody helmsmanBody;
        private PlayerInputManager helmsmanInput;
        private PlayerMovement helmsmanMovement;
        private bool restoreMovement;
        private bool drivingLocally;
        private int tookHelmFrame = -1;
        private Vector3 stanceOffset;

        private float rudder;
        private float wheelAngle;
        private Quaternion restRotation = Quaternion.identity;

        /// <summary>Rudder the helm is asking for, -1 to 1.</summary>
        public float Rudder => rudder;

        /// <summary>The helmsman's body, or null. Named for what it is; the base calls it the occupant.</summary>
        public Transform Helmsman => Occupant != null ? Occupant.transform : null;

        // --- What kind of station this is -------------------------------------

        /// <summary>One wheel, one helmsman. This is the whole reason the claim goes to the server.</summary>
        protected override bool Exclusive => true;

        /// <summary>Held until the helmsman stands down, so no timeout — see the liveness poll instead.</summary>
        protected override float ClaimTimeout => 0f;

        /// <summary>The helmsman drives the hull, so the hull becomes theirs.</summary>
        protected override bool TakesVehicleOwnership => true;

        // --- IInteractionReadout ----------------------------------------------

        public string Label => "Helm";

        public string Prompt => !IsManned
            ? "E: take the helm"
            : IsMannedByLocalPlayer
                ? "A / D: steer the foil   E: let go"
                : "Somebody else is at the wheel";

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

        public override bool CanInteract() => locomotion != null;

        /// <summary>
        /// The press only ASKS. The server decides and tells everyone, including us.
        ///
        /// The stand-down is still refused to anybody but the helmsman, and the refusal is still
        /// written here — but it is no longer what ENFORCES it. That has moved to
        /// <see cref="VehicleStation"/>'s server-side handler, because a client that can be asked
        /// "are you the helmsman" can also be asked to lie about it, and because two clients
        /// disagreeing about who is steering is precisely what routing this through one machine
        /// exists to prevent. What is left here is the same courtesy ArticulatedPartInteraction
        /// pays: do not put a message on the wire the server is only going to refuse and then have
        /// to correct us about.
        ///
        /// Deliberately not an <see cref="IContextualInteractable"/> refusal, which would have been
        /// the tidier way to say it. That hides the prompt as well as blocking the press, and the
        /// prompt is the only thing that tells a second player why the wheel will not answer them.
        /// </summary>
        public override void Interact(Interactor interactor)
        {
            if (locomotion == null) return;

            if (!IsManned)
            {
                RequestClaim(interactor, rudder);
                return;
            }

            if (ResolvePlayer(interactor) == Occupant) RequestRelease(interactor);
        }

        // --- Taking and leaving the wheel --------------------------------------

        protected override void OnManned(GameObject player)
        {
            helmsmanBody = player.GetComponent<Rigidbody>();

            // Every machine: the deck stops carrying them. From here until they stand down, this
            // component is the only thing that decides where they are — and on the machines that
            // are only watching, nothing here places them at all, which is exactly right because
            // their own machine is doing it and the result replicates.
            if (carrier != null) carrier.ClaimRider(helmsmanBody);

            // Everything below takes control away from a player, so it may only happen on the
            // machine that has that control in the first place. A remote peer running it would
            // switch off a body it does not own and — worse — switch it back on again on release,
            // handing a remote player's movement to the wrong machine.
            if (!Network.Owns(player.transform)) return;

            PlayerController controller = player.GetComponentInChildren<PlayerController>(true);
            if (controller == null) return;

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

            // Hold them where they are standing, not on one exact spot: pinned to a fixed point
            // the helm would snatch the player off their feet the instant they pressed E, and the
            // craft would have one legal place to stand at the wheel. Only the horizontal reach is
            // clamped — the vertical offset is how far their pivot sits above their own feet, and
            // scaling that down buries them in the planks.
            //
            // Measured on the owner's machine alone, and that is fine: nothing else pins them, so
            // nothing else needs the number.
            Vector3 offset = StanceAnchor().InverseTransformPoint(player.transform.position);
            Vector3 flat = Vector3.ClampMagnitude(new Vector3(offset.x, 0f, offset.z),
                                                  releaseDistance * 0.5f);
            stanceOffset = new Vector3(flat.x, offset.y, flat.z);

            // E anywhere lets go, not just E while looking back at the wheel. Standing at a helm
            // you are looking at the sails, not at your own hands.
            if (helmsmanInput != null) helmsmanInput.OnInteractPressed += OnHelmsmanInteract;

            // Set last, and it gates the pin. Everything above can bail — a body with no
            // PlayerController is not somebody who can stand at a wheel — and a pin that ran anyway
            // would have no stance offset to place them by, so it would snatch them onto the wheel
            // itself and hold them there.
            drivingLocally = true;
        }

        /// <summary>
        /// Give the player their legs back.
        ///
        /// <paramref name="player"/> may already be destroyed — this is also the path a
        /// disconnecting helmsman takes — so everything here works off references captured when
        /// they took the wheel rather than off the body.
        /// </summary>
        protected override void OnUnmanned(GameObject player)
        {
            if (helmsmanInput != null) helmsmanInput.OnInteractPressed -= OnHelmsmanInteract;
            if (restoreMovement && helmsmanMovement != null) helmsmanMovement.enabled = true;
            if (carrier != null) carrier.ReleaseRider(helmsmanBody);

            helmsmanBody = null;
            helmsmanInput = null;
            helmsmanMovement = null;
            restoreMovement = false;
            drivingLocally = false;

            // Leave the rudder where it is and let the locomotion centre it, so stepping away
            // mid-turn finishes the turn rather than snapping the hull straight.
        }

        private Transform StanceAnchor() => stance != null ? stance : transform;

        private void OnHelmsmanInteract()
        {
            // The press that took the helm must not also release it. Interactor raises this on the
            // same frame the claim landed, and whether the freshly added handler sees that in-flight
            // invocation is a detail of how the delegate was built — so do not depend on it.
            if (Time.frameCount == tookHelmFrame) return;
            Release();
        }

        /// <summary>
        /// Ask to stand down from the wheel.
        ///
        /// Still public and still called "Release", but it is now a request rather than an act: the
        /// server owns who is at the helm, and it hands the hull back to itself as part of granting
        /// this. Offline the whole round trip happens inside this call.
        /// </summary>
        public void Release() => RequestRelease(Occupant);

        // --- Steering ----------------------------------------------------------

        /// <summary>
        /// What the helmsman's machine reports to the server ten times a second: the rudder itself,
        /// already ramped. The wheel is the one control whose occupant also owns the vehicle, so it
        /// integrates its own input locally and the server publishes the result rather than the
        /// other way round — a wheel that waits a round trip for its own input is unsteerable.
        /// </summary>
        protected override float LocalRequest() => rudder;

        /// <summary>
        /// Every machine but the helmsman's: put the blade where the helmsman has it.
        ///
        /// Only the blade command, never the hull. The hull's motion arrives through the
        /// NetworkTransform; this is what makes the strut visibly swing and the wheel visibly turn
        /// on a machine that is watching somebody else steer.
        /// </summary>
        protected override void ApplyValue(float position) => rudder = Mathf.Clamp(position, -1f, 1f);

        /// <summary>
        /// Server side: has the helmsman gone? Teleported, died, fallen off, or been despawned.
        ///
        /// Judged on the server rather than locally, because it ends somebody's claim and only one
        /// machine may do that. The helmsman's position is on the server like everybody else's, so
        /// there is nothing here the server cannot see.
        /// </summary>
        protected override bool ShouldRelease(GameObject player)
        {
            if (player == null || !player.activeInHierarchy) return true;

            return (player.transform.position - transform.position).sqrMagnitude
                   > releaseDistance * releaseDistance;
        }

        /// <summary>
        /// The wheel is read here, at execution order 80, so DuneFoilLocomotion (100) integrates
        /// this frame's rudder rather than last frame's. <c>base.Update()</c> is not optional — see
        /// <see cref="VehicleStation.Update"/> for what silently stops working without it.
        /// </summary>
        protected override void Update()
        {
            base.Update();

            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            SteerOrCentre(dt);
            SpinWheel(dt);
        }

        // The pin runs after the hull has moved, which is why it is not up in Update with the rest.
        private void LateUpdate() => HoldHelmsmanAtTheWheel();

        private void SteerOrCentre(float dt)
        {
            if (locomotion == null) return;

            if (IsMannedByLocalPlayer)
            {
                float steer = helmsmanInput != null ? helmsmanInput.MoveInput.x : 0f;

                // Ramp toward what they are asking for, fall back to centre when they let go.
                float rate = Mathf.Abs(steer) > 0.01f ? 1f / rudderRampTime : 1f / rudderCentreTime;
                rudder = Mathf.MoveTowards(rudder, Mathf.Clamp(steer, -1f, 1f), rate * dt);

                locomotion.SetRudder(rudder);
                return;
            }

            if (IsManned)
            {
                // Somebody else is steering. Their rudder arrives ten times a second and is fed to
                // the craft EVERY frame in between, not just on the frames a message lands.
                //
                // Through SetRudder rather than straight onto the blade, and that is the whole
                // point: SetRudder is also what tells the locomotion the helm is manned. Written
                // directly to FoilRudder.Command instead, the locomotion would see an unmanned helm
                // on every frame between two updates and ease the blade back toward centre at 1.5
                // per second — a tenth of a second of that is a sixth of full rudder, so the strut
                // would visibly flutter on every machine that is only watching.
                locomotion.SetRudder(rudder);
                return;
            }

            // Nobody has the helm anywhere. The locomotion centres the blade itself, on every
            // machine, so read the wheel off the blade rather than easing a number of our own: the
            // mesh and the strut then cannot disagree about where the helm is.
            if (locomotion.Rudder != null) rudder = locomotion.Rudder.Command;
            else rudder = Mathf.MoveTowards(rudder, 0f, dt / rudderCentreTime);
        }

        /// <summary>
        /// Hold the helmsman at the wheel — on their own machine and nowhere else.
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
        ///
        /// The ownership gate is not a nicety. The player's transform is owner-authoritative, so a
        /// peer writing a remote helmsman's position here would be overwritten within the tick
        /// anyway — after having zeroed a velocity that was not theirs to zero, once per frame,
        /// which is the same class of mistake as a server-side teleport of a client's body.
        /// </summary>
        private void HoldHelmsmanAtTheWheel()
        {
            if (!drivingLocally || !IsMannedByLocalPlayer) return;

            Transform helmsman = Occupant.transform;
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
