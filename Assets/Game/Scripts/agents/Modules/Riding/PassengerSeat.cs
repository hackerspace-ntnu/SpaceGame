// A seat that carries somebody without handing them the controls.
//
// MountModule already knows how to seat a rider, disable their movement, hand them a camera,
// replicate the seating and put them back on the ground afterwards. What it assumes is that the
// person in the seat is DRIVING: SteerModule reads their stick and feeds the motor, the mount's own
// behaviour modules are switched off for the duration, and the netcode hands the mount to the
// rider's client so their input replicates outward.
//
// A passenger is the other half of that. The machine keeps its own AI -- it wanders, it hunts, it
// fights, exactly as it would with nobody aboard -- and the rider is cargo. Three things have to be
// true for that to work, and this component is the three of them:
//
//   1. The carrier cannot see its passenger. Left alone, a robot hostile to players notices the one
//      sitting on its shoulder, turns to fight, and cannot reach them; it stands there re-acquiring
//      somebody it is carrying, or drops lightning on its own feet. EntityFaction.Ignore is the
//      exemption and this is what grants it, for exactly as long as the seat is occupied.
//
//   2. The rider stays on the seat. MountModule parents them and folds the seat offset in ONCE --
//      and when both sides are networked it has to parent to the mount's NetworkObject rather than
//      to the seat marker, because netcode refuses any other parent. A seat that is a BONE therefore
//      tracks in single player and does not in a session. Writing the pose every frame makes both
//      cases the same case, and lets the seat sit at a sane angle on a bone pointing down an arm.
//
//   3. The rider can get off. Escape lives in SteerModule, and a passenger seat has no SteerModule.
//
// Compose it as: MountModule (seatBone set, allowAISelfMovementWhenMounted ON, no SteerModule)
// + MountNetworkSync + this.
using UnityEngine;
using UnityEngine.InputSystem;
using SpaceGame.Characters;

namespace SpaceGame.Agents
{
    // Same slot as MountedRiderPose: after Unity has evaluated the Animator (PreLateUpdate, ahead of
    // every LateUpdate) so the seat bone has moved for this frame, and before MountModule's
    // order-1000 camera pass, which frames the shot on where the rider ended up.
    [DefaultExecutionOrder(900)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MountModule))]
    public class PassengerSeat : MonoBehaviour
    {
        [Tooltip("The seat this rides on. Found on this GameObject when empty.")]
        [SerializeField] private MountModule mountModule;

        [Header("Pose")]
        [Tooltip("Where the rider sits relative to MountModule's seat point, in the CARRIER's own " +
                 "frame -- X right, Y up, Z forward -- not the seat bone's. The bone's own axes are " +
                 "whatever the rig happens to give it (an arm bone points down the arm), so " +
                 "offsetting in its local space lays the rider on their side. A player's origin is " +
                 "at their FEET, so this wants to land on the seat surface, not at the hip.")]
        [SerializeField] private Vector3 seatOffset = new Vector3(0f, 0.4f, 0f);

        [Tooltip("Which way the rider faces, in degrees off the carrier's forward. 0 rides facing " +
                 "the way the machine walks.")]
        [SerializeField] private float facingYaw;

        [Tooltip("Rewrite the rider's pose every frame from the seat point. Leave on for a seat on " +
                 "an animated bone; turn it off only if something else owns the rider's transform.")]
        [SerializeField] private bool holdRiderOnSeat = true;

        [Header("Concealment")]
        [Tooltip("Hide the rider from this entity's own targeting while they are aboard. The whole " +
                 "point of a passenger seat -- off, the carrier fights whoever it is carrying.")]
        [SerializeField] private bool hideRiderFromCarrier = true;

        [Header("Dismount")]
        [Tooltip("Let the rider get off with Escape. SteerModule owns that key on a steered mount; " +
                 "a passenger seat has no SteerModule, so without this there is no way down.")]
        [SerializeField] private bool escapeDismounts = true;

        private MountModule Mount => mountModule != null ? mountModule : mountModule = GetComponent<MountModule>();

        private EntityFaction carrierFaction;
        private AgentTargeting carrierTargeting;

        /// The exemption currently granted, held so it is revoked on the exact faction it was granted
        /// to even once MountModule has forgotten who the rider was. Non-null means we hold one.
        private EntityFaction concealedRider;

        private void Awake()
        {
            carrierFaction = GetComponent<EntityFaction>();
            carrierTargeting = GetComponent<AgentTargeting>();

            if (hideRiderFromCarrier && carrierFaction == null)
            {
                Debug.LogWarning($"{name}: PassengerSeat has no EntityFaction to hide the rider " +
                                 "from, so the carrier will target whoever sits on it. Add one, or " +
                                 "turn hideRiderFromCarrier off if that is deliberate.", this);
            }
        }

        private void OnEnable()
        {
            Mount.Mounted += HandleMounted;
            Mount.Dismounted += HandleDismounted;

            // Already occupied when this switched on -- a component toggled at runtime, or a
            // streamed-in carrier whose rider was restored before this ran.
            if (Mount.IsMounted)
                Conceal(Mount.MountedPlayerTransform);
        }

        private void OnDisable()
        {
            Mount.Mounted -= HandleMounted;
            Mount.Dismounted -= HandleDismounted;
            Reveal();
        }

        private void HandleMounted(PlayerMovement rider) => Conceal(Mount.MountedPlayerTransform);

        // Dismounted fires BEFORE MountModule clears its rider references, so the transform is still
        // there to be read -- but the exemption is revoked from the reference this component kept,
        // which does not depend on that ordering holding.
        private void HandleDismounted(PlayerMovement rider) => Reveal();

        private void Update()
        {
            if (!escapeDismounts || !Mount.IsMounted || !Mount.RiderIsLocal)
                return;

            // Local rider only, for the reason SteerModule spells out: mounting replicates, so every
            // peer's copy of this component is live the moment anybody sits down, and an unguarded
            // key check means one player's Escape throws a different player out of the seat.
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                RequestDismount();
        }

        private void LateUpdate()
        {
            // The catch-all revoke. AbandonRider -- the teardown path, taken when the rider is being
            // destroyed or the carrier's scene is unloading -- empties the seat and raises no event
            // at all, so a subscription alone would leave the exemption standing for somebody who has
            // gone. Comparing two references once a frame cannot miss a path added later.
            if (concealedRider != null && !Mount.IsMounted)
                Reveal();

            if (!holdRiderOnSeat || !Mount.IsMounted)
                return;

            Transform rider = Mount.MountedPlayerTransform;
            Transform seat = Mount.ActiveSeatPoint;
            if (rider == null || seat == null)
                return;

            // Upright, and facing where the machine faces. Taking the seat bone's rotation instead
            // would ride whatever the rig does with it: an arm bone points down the arm, so the
            // rider would arrive lying sideways in the air.
            Vector3 forward = transform.forward;
            forward.y = 0f;
            Quaternion upright = forward.sqrMagnitude > 1e-4f
                ? Quaternion.LookRotation(forward, Vector3.up)
                : Quaternion.identity;
            upright *= Quaternion.Euler(0f, facingYaw, 0f);

            rider.SetPositionAndRotation(seat.position + transform.rotation * seatOffset, upright);
        }

        // Through the server when this seat is networked, or the passenger stands up on their own
        // screen and stays welded to the shoulder on everybody else's. Same routing as SteerModule.
        private void RequestDismount()
        {
            if (Mount.TryGetComponent(out MountNetworkSync sync))
            {
                sync.RequestDismount();
                return;
            }

            Mount.Dismount();
        }

        // -- The exemption --------------------------------------------------------

        private void Conceal(Transform rider)
        {
            if (!hideRiderFromCarrier || carrierFaction == null || rider == null)
                return;

            EntityFaction riderFaction = rider.GetComponentInParent<EntityFaction>();
            if (riderFaction == null || riderFaction == concealedRider)
                return;

            // Whoever was here before leaves visible. Two riders cannot occupy one MountModule, so
            // this only fires if something reseated without a dismount in between.
            Reveal();

            carrierFaction.Ignore(riderFaction);
            concealedRider = riderFaction;

            // Granting the exemption keeps the rider out of FUTURE queries and says nothing about a
            // target already held. Without this the carrier keeps fighting the person who has just
            // climbed aboard until its next re-evaluation -- long enough to get a shot off.
            carrierTargeting?.ForgetIgnored();
        }

        private void Reveal()
        {
            if (concealedRider == null)
                return;

            carrierFaction?.StopIgnoring(concealedRider);
            concealedRider = null;
        }

        private void OnDrawGizmosSelected()
        {
            MountModule mount = mountModule != null ? mountModule : GetComponent<MountModule>();
            Transform seat = mount != null ? mount.ActiveSeatPoint : null;
            if (seat == null)
                return;

            Gizmos.color = new Color(0.4f, 0.85f, 1f);
            Gizmos.DrawWireSphere(seat.position + transform.rotation * seatOffset, 0.35f);
        }
    }
}
