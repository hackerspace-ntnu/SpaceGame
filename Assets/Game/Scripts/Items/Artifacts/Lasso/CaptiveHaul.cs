using System.Collections.Generic;
using SpaceGame.Characters;
using SpaceGame.Core;
using SpaceGame.Gameplay.Status;
using SpaceGame.Presentation;
using SpaceGame.World;
using Unity.Netcode;
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// How somebody tied up is carried over the shoulder. Serialized on the lasso prefab, which is what ties them, and handed
    /// to every captive the lasso ties.
    /// </summary>
    [System.Serializable]
    public class CaptiveHaulSettings
    {
        [Tooltip("How close to the knot round the captive a player must stand to take hold, in metres across the ground. " +
                 "A tied body falls away from whoever tied it, so the knot lands a good 3 m from them; less than that " +
                 "and the binder cannot take hold without stepping in first.")]
        [SerializeField, Min(0.5f)] private float gripReach = 3.5f;

        [Tooltip("The carrier's walk with somebody over their shoulder, as a fraction of their ordinary walk.")]
        [SerializeField, Range(0.2f, 1f)] private float haulSpeedFraction = 0.55f;

        [Tooltip("The tallest captive a carrier can shoulder, as a multiple of the carrier's own height. Anything taller " +
                 "is left on the ground (a lasso lead still drags it).")]
        [SerializeField, Min(0.1f)] private float maxHeightShare = 1.25f;

        [Tooltip("How far above the carrier's shoulder joint the captive's hips ride, in metres: up on TOP of the suit's " +
                 "shoulder pad, plus the captive's belly. The astronaut's pad stands 0.3 m over the joint; less seats the " +
                 "hips inside the pad and the helmet, where the carrier sees nothing of the legs but their own shoulder.")]
        [SerializeField] private float shoulderLift = 0.41f;

        [Tooltip("How far out from the carrier's shoulder joint, away from the neck, the captive's hips ride, in metres: " +
                 "on the outside of the pad, clear of the helmet.")]
        [SerializeField] private float shoulderOut = 0.21f;

        [Tooltip("How far behind the carrier's shoulder joint the captive's hips ride, in metres. Behind the eye, so the " +
                 "carrier sees the legs come forward off the shoulder rather than the captive's seat.")]
        [SerializeField] private float shoulderBehind = 0.08f;

        [Tooltip("Degrees the carried body is turned across the carrier's chest, legs towards the middle. Straight ahead, " +
                 "the feet ride off the right-hand edge of the carrier's view.")]
        [SerializeField, Range(0f, 45f)] private float shoulderTurn = 20f;

        [Tooltip("Any part of a carried body seen above this height in the carrier's own view looking straight ahead — 0 " +
                 "the bottom of the screen, 1 the top — is hidden from that view only: a cape or a spear that would stand " +
                 "in front of the eye. The legs and feet stay below it. Everybody else, and every shadow, still sees it.")]
        [SerializeField, Range(0f, 1f)] private float eyeLine = 0.5f;

        [Tooltip("How far to the carrier's right a captive is thrown down, in metres. Clear of the carrier's own feet.")]
        [SerializeField, Min(0f)] private float restSide = 1.1f;

        [Tooltip("How far ahead of the carrier a captive is thrown down, in metres.")]
        [SerializeField] private float throwAhead = 0.8f;

        [Tooltip("How long throwing the captive off the shoulder takes, from the shrug to it lying on the ground, in " +
                 "seconds. The carrier stands still for it.")]
        [SerializeField, Min(0.2f)] private float throwSeconds = 2.2f;

        [Tooltip("How high above a straight line from the shoulder to the ground the body is tossed, in metres.")]
        [SerializeField, Min(0f)] private float throwArc = 0.5f;

        [Tooltip("Where the right hand holds the captive's legs, 0 at the hips to 1 at the knees.")]
        [SerializeField, Range(0f, 1f)] private float nearGrip = 0.3f;

        [Tooltip("Where the left hand holds the captive's legs, 0 at the hips to 1 at the knees.")]
        [SerializeField, Range(0f, 1f)] private float farGrip = 0.8f;

        [Tooltip("A carrier further than this from the captive is taken off them by the server, in metres: a respawn or a " +
                 "teleport leaves nobody carrying somebody across the map.")]
        [SerializeField, Min(1f)] private float releaseDistance = 6f;

        [Tooltip("How far into the take-hold clip the hands close on the captive, 0-1. The body comes up off the ground " +
                 "from here to the end of the clip.")]
        [SerializeField, Range(0.05f, 0.95f)] private float gripFraction = 0.45f;

        [Tooltip("Played by the carrier as they take hold: squat, take the body, heave it up.")]
        [SerializeField] private CharacterAction takeHoldAction;

        [Tooltip("Played by the carrier as they throw the body off their shoulder. Its own length is stretched to Throw " +
                 "Seconds' worth of motion.")]
        [SerializeField] private CharacterAction letGoAction;

        [Tooltip("Take-hold and let-go length when the body has no action to play, in seconds.")]
        [SerializeField, Min(0.1f)] private float fallbackSeconds = 1f;

        public float GripReach => gripReach;
        public float HaulSpeedFraction => haulSpeedFraction;
        public float MaxHeightShare => maxHeightShare;
        public float ShoulderLift => shoulderLift;
        public float ShoulderOut => shoulderOut;
        public float ShoulderBehind => shoulderBehind;
        public float ShoulderTurn => shoulderTurn;
        public float EyeLine => eyeLine;
        public float RestSide => restSide;
        public float ThrowAhead => throwAhead;
        public float ThrowSeconds => throwSeconds;
        public float ThrowArc => throwArc;
        public float NearGrip => nearGrip;
        public float FarGrip => farGrip;
        public float ReleaseDistance => releaseDistance;
        public float GripFraction => gripFraction;
        public CharacterAction TakeHoldAction => takeHoldAction;
        public CharacterAction LetGoAction => letGoAction;
        public float FallbackSeconds => fallbackSeconds;
    }

    /// <summary>
    /// Somebody tied up, carried over the shoulder: interact on them and the carrier squats, takes them round the legs and
    /// heaves them up — belly on the right shoulder, trunk hanging down the back, legs out in front where the carrier can
    /// see the feet. The oxygen plant's carry with a person in the hands: the same <see cref="LiftCarrier"/>, the same
    /// <see cref="LiftHands"/>, the same keys to put them down.
    ///
    /// <para>
    /// <b>The root rides the shoulder, placed by every machine</b> from its own copy of the carrier in the carrier's
    /// <c>LateUpdate</c> (<see cref="ILiftLoad.Hold"/>) — the rule <see cref="LiftCarrier"/> carries everything by, so the body
    /// sits on the shoulder of the carrier's own first-person view with no lag. While it does the body is out of physics
    /// (<see cref="BoundBody.BeginCarry"/>), and <see cref="TiedPose"/> blends from the hogtie into the carried pose as it
    /// comes up. Put down, it is laid beside the carrier and handed back to physics where it is owned.
    /// </para>
    /// <para>
    /// <b>Nothing of it stands in the carrier's eye line.</b> Once it is up, the carrier's own machine hides from that
    /// player's camera alone every part seen above <see cref="CaptiveHaulSettings.EyeLine"/>
    /// (<see cref="PlayerLook.HideFromEye"/>) — a Nomad's cape follows its spine straight up past the hips. A body with no
    /// humanoid avatar goes across both shoulders behind the head instead (<see cref="TiedPose.CarriedAcross"/>).
    /// </para>
    /// <para>
    /// <b>Who carries is the server's to say</b>, on this body's own channel: a request
    /// (<see cref="NetMsg.CaptiveHaulRequest"/>) and an absolute answer every machine applies
    /// (<see cref="NetMsg.CaptiveHauled"/>), re-sent to a late joiner. No <c>NetworkBehaviour</c>: this is added at runtime
    /// to a body that is already spawned, which a <c>NetworkVariable</c> cannot be.
    /// </para>
    /// <para>
    /// Nothing is saved. The hold hangs off the tie, which is never saved, so a load stands the captive up free.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("")] // added in code, never by hand
    public sealed class CaptiveHaul : MonoBehaviour, ILiftLoad
    {
        private CaptiveHaulSettings settings;

        /// <summary>Who has hold of this captive, on this machine, as the server last said. Null for nobody.</summary>
        private GameObject hauler;

        private LiftCarrier heldBy;
        private LiftHands hands;
        private bool lettingGo;
        private int handledFrame = -1;
        private BoundBody bound;
        private Animator animator;

        // The carrier's own camera, once this body's parts in front of it are hidden from it; and whether the body was all
        // the way up last frame.
        private PlayerLook clearedEye;
        private Renderer[] hiddenFromEye = System.Array.Empty<Renderer>();
        private bool wasUp;

        // Being set down, onto this root pose beside the carrier; and the ground it was picked up from.
        private bool lowering;
        private Pose putDownAt;
        private float pickedUpFromGround;

        /// <summary>The hauler, or null. For tests and for the prompt.</summary>
        public GameObject Hauler => hauler;

        /// <summary>The parts of this body hidden from its carrier's own camera. For tests.</summary>
        public Renderer[] HiddenFromCarrierEye => hiddenFromEye;

        /// <summary>Give <paramref name="body"/> the hold, unless it has one.</summary>
        public static void EnsureOn(GameObject body, CaptiveHaulSettings haul)
        {
            if (body == null || haul == null) return;

            CaptiveHaul held = body.GetComponent<CaptiveHaul>();
            if (held == null) held = body.AddComponent<CaptiveHaul>();
            held.settings ??= haul;
        }

        private void OnEnable()
        {
            this.NetOn(NetMsg.CaptiveHaulRequest, OnRequested);
            this.NetOn(NetMsg.CaptiveHauled, OnAnnounced);
            NetworkManager manager = NetworkManager.Singleton;
            if (manager != null) manager.OnClientConnectedCallback += OnPeerJoined;
        }

        private void OnDisable()
        {
            this.NetOff(NetMsg.CaptiveHaulRequest, OnRequested);
            this.NetOff(NetMsg.CaptiveHauled, OnAnnounced);
            NetworkManager manager = NetworkManager.Singleton;
            if (manager != null) manager.OnClientConnectedCallback -= OnPeerJoined;
            Apply(null);
        }

        private BoundBody Bound => bound != null ? bound : bound = BoundBody.Of(gameObject);

        // ── The press, on the presser's machine ──────────────────────────────

        private bool IsTied => StatusReceiver.HasStatus(gameObject, StatusKind.Bound);

        /// <summary>Is this machine's own player the one carrying?</summary>
        public bool HauledByLocalPlayer => hauler != null && Network.Owns(hauler.transform);

        /// <summary>Is this captive small enough to go over <paramref name="who"/>'s shoulder?</summary>
        public bool FitsOnShoulderOf(GameObject who)
        {
            if (settings == null || who == null || Bound == null) return false;

            float carrierHeight = TiedPose.BodyInLocalSpace(who.transform).size.y;
            return Bound.Pose.StandingHeight <= carrierHeight * settings.MaxHeightShare;
        }

        /// <summary>
        /// May <paramref name="who"/> take hold of this captive now? <paramref name="givingUpLead"/>: they are leading the
        /// captive on their own lasso and letting go of it for this, so that rope and its slowed walk do not count.
        /// </summary>
        public bool MayTakeHold(GameObject who, bool givingUpLead = false) =>
            settings != null && who != null && hauler == null && IsTied && FitsOnShoulderOf(who)
            && (givingUpLead || !CuttableRopes.AnyBinds(gameObject))
            && WithinReach(who.transform.position) && LiftHands.MayTakeHold(who, givingUpLead);

        /// <summary>
        /// The presser's machine: take hold, or put down if it is this player who carries. Once per frame: the crosshair and
        /// the interact-anywhere input both report the same press. <paramref name="givingUpLead"/>: see <see cref="MayTakeHold"/>
        /// — the server, which hears the lead let go first, asks without it.
        /// </summary>
        public void Toggle(GameObject who, bool givingUpLead = false)
        {
            if (who == null || !Network.Owns(who.transform) || handledFrame == Time.frameCount) return;
            handledFrame = Time.frameCount;

            if (hauler == who) LetGoLocal();
            else if (MayTakeHold(who, givingUpLead)) this.NetToServer(NetMsg.CaptiveHaulRequest, new NetArg { A = 1 }.With(who));
        }

        /// <summary>The carrier's own machine throws the captive down: the throw plays at once, and the server is told.</summary>
        private void LetGoLocal()
        {
            if (hauler == null || lettingGo) return;

            lettingGo = true;
            LiftHands.Play(hauler, settings.LetGoAction, settings.FallbackSeconds);
            ThrowOff(LiftCarrier.On(hauler));
            Hands.SettleEndsAt = Time.time + settings.ThrowSeconds;
            this.NetToServer(NetMsg.CaptiveHaulRequest, new NetArg { A = 0 }.With(hauler));
        }

        private bool WithinReach(Vector3 point)
        {
            Vector3 flat = point - Middle;
            flat.y = 0f;
            return flat.sqrMagnitude <= settings.GripReach * settings.GripReach;
        }

        /// <summary>Where the captive's body is, rather than its root: the knot it is taken by.</summary>
        private Vector3 Middle => Bound != null ? Bound.RopePoint : transform.position;

        private LiftHands Hands => hands ??= new LiftHands(this, () => HauledByLocalPlayer, LetGoLocal,
                                                           () => { if (hauler != null) Toggle(hauler); },
                                                           () => settings.HaulSpeedFraction);

        // ── The server's decision ────────────────────────────────────────────

        private static bool Decides => !Network.IsNetworked || Network.Server;

        private void OnRequested(in NetArg arg, ulong sender)
        {
            if (!Decides) return;

            GameObject who = arg.Resolve();
            if (who == null || !Network.MayActFor(who, sender)) return;

            if (arg.A == 1)
            {
                if (MayTakeHold(who)) Announce(who);
                return;
            }

            if (hauler == who) Announce(null);
        }

        /// <summary>Server: tell every machine, this one included, who has hold.</summary>
        private void Announce(GameObject who)
        {
            NetArg state = new NetArg { A = who != null ? 1 : 0 };
            this.NetToAll(NetMsg.CaptiveHauled, who != null ? state.With(who) : state);
        }

        /// <summary>Server: a joiner is told who has hold, which is absolute state — a no-op for everybody who knew.</summary>
        private void OnPeerJoined(ulong clientId)
        {
            if (Decides && hauler != null && clientId != NetworkManager.ServerClientId) Announce(hauler);
        }

        private void Update()
        {
            if (settings == null) return;

            // The ropes came off — the clock, a respawn, a cut — or the carrier went: every machine lets go here at the same
            // moment, and the server says so for any machine that missed it.
            if (hauler != null && (!IsTied || HaulerGone()))
            {
                if (Decides) Announce(null);
                else Apply(null);
            }

            Hands.Tick();
        }

        private bool HaulerGone()
        {
            if (hauler == null || !hauler.activeInHierarchy) return true;
            if (hauler.TryGetComponent(out Characters.PlayerController body) && body.IsDead) return true;

            Vector3 flat = hauler.transform.position - transform.position;
            flat.y = 0f;
            return flat.sqrMagnitude > settings.ReleaseDistance * settings.ReleaseDistance;
        }

        // ── What every machine shows ─────────────────────────────────────────

        private void OnAnnounced(in NetArg arg, ulong sender) => Apply(arg.A == 1 ? arg.Resolve() : null);

        /// <summary>Idempotent: acts only where who has hold here differs from <paramref name="who"/>.</summary>
        private void Apply(GameObject who)
        {
            if (who == hauler) return;

            if (hauler != null) LetGoHere();
            if (who == null || settings == null) return;

            LiftCarrier carrier = LiftCarrier.On(who);
            bool own = Network.Owns(who.transform);
            float seconds = own
                ? LiftHands.Play(who, settings.TakeHoldAction, settings.FallbackSeconds)
                : LiftHands.SecondsOf(settings.TakeHoldAction, who, settings.FallbackSeconds);

            lowering = false;
            if (!carrier.Lift(this, seconds)) return;

            hauler = who;

            if (!own) return;
            Hands.Take(who);
            Hands.SettleEndsAt = Time.time + seconds;
        }

        private void LetGoHere()
        {
            LiftCarrier carrier = hauler.GetComponent<LiftCarrier>();
            bool alreadyLowering = lettingGo;
            hauler = null;
            lettingGo = false;

            if (carrier == null || !ReferenceEquals(carrier.Load, this) || alreadyLowering) return;

            // The carrier's own machine started lowering when it let go; everybody else lets go as they hear it — and a
            // captive whose ropes came off is out of the hands at once.
            if (IsTied) ThrowOff(carrier);
            else carrier.Drop();
        }

        /// <summary>
        /// Every machine: thrown off <paramref name="carrier"/>'s shoulder over <see cref="CaptiveHaulSettings.ThrowSeconds"/>,
        /// onto the ground ahead of them and to their right. Public for tests; play reaches it through the let-go.
        /// </summary>
        public void ThrowOff(LiftCarrier carrier)
        {
            lowering = true;
            putDownAt = Beside(carrier);
            carrier.Lower(putDownAt, settings.ThrowSeconds);
        }

        /// <summary>
        /// Where the root sits with the body over <see cref="heldBy"/>'s right shoulder, turned a little across the chest —
        /// or across both, behind the head — facing the way they do.
        /// </summary>
        private Pose OnShoulder()
        {
            Quaternion facing = Facing(heldBy.transform);
            Quaternion turned = Bound.Pose.CarriedAcross
                ? facing * Bound.Pose.AcrossTurn
                : facing * Quaternion.AngleAxis(-settings.ShoulderTurn, Vector3.up);
            return new Pose(Seat(facing) - turned * Bound.CarryAnchor, turned);
        }

        /// <summary>The point on the carrier the body's <see cref="TiedPose.CarryAnchor"/> rests on.</summary>
        private Vector3 Seat(Quaternion facing)
        {
            TiedPose pose = Bound.Pose;
            Vector3 seat = pose.CarriedAcross
                ? Neck - facing * Vector3.forward * pose.AcrossHalfDepth
                : heldBy.Shoulder(right: true) + facing * new Vector3(settings.ShoulderOut, 0f, -settings.ShoulderBehind);
            return seat + Vector3.up * settings.ShoulderLift;
        }

        private Vector3 Neck => (heldBy.Shoulder(right: false) + heldBy.Shoulder(right: true)) * 0.5f;

        /// <summary>
        /// The carrier's own machine, once the body is up: hide from that player's camera every part of it seen above the
        /// eye line, measured on the pose it is carried in with the carrier looking straight ahead — never at the pitch the
        /// camera happens to have, which is looking down at the body it just picked up, and would put the feet above the
        /// line and hide the whole body with them.
        /// </summary>
        private void ClearEye()
        {
            clearedEye = heldBy.GetComponentInChildren<PlayerLook>();
            Camera eye = clearedEye != null && clearedEye.playerCamera != null ? clearedEye.playerCamera.GetComponent<Camera>() : null;
            if (eye == null) return;

            var hidden = new List<Renderer>();
            var baked = new UnityEngine.Mesh();
            var vertices = new List<Vector3>();
            Quaternion looking = eye.transform.rotation;
            eye.transform.rotation = Facing(heldBy.transform);
            try
            {
                foreach (Renderer part in GetComponentsInChildren<Renderer>())
                {
                    // Given back ShadowCastingMode.On when the hold ends, so only what casts shadows now may be registered.
                    if (!part.enabled || part.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.On) continue;
                    if (part is SkinnedMeshRenderer skinned ? SkinAboveEyeLine(skinned, eye, baked, vertices)
                                                            : part is MeshRenderer && BoxAboveEyeLine(part.bounds, eye))
                        hidden.Add(part);
                }
            }
            finally
            {
                eye.transform.rotation = looking;
            }

            RuntimeObjects.Discard(baked);
            hiddenFromEye = hidden.ToArray();
            clearedEye.HideFromEye(this, hiddenFromEye);
        }

        private bool SkinAboveEyeLine(SkinnedMeshRenderer skin, Camera eye, UnityEngine.Mesh baked, List<Vector3> vertices)
        {
            skin.BakeMesh(baked, true);
            baked.GetVertices(vertices);

            Transform at = skin.transform;
            Vector3 scale = at.lossyScale;
            int step = Mathf.Max(1, vertices.Count / EyeSamples);
            for (int i = 0; i < vertices.Count; i += step)
                if (AboveEyeLine(eye, at.position + at.rotation * Vector3.Scale(vertices[i], scale))) return true;
            return false;
        }

        private bool BoxAboveEyeLine(Bounds box, Camera eye)
        {
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = new Vector3((corner & 1) == 0 ? box.min.x : box.max.x,
                                            (corner & 2) == 0 ? box.min.y : box.max.y,
                                            (corner & 4) == 0 ? box.min.z : box.max.z);
                if (AboveEyeLine(eye, point)) return true;
            }

            return false;
        }

        private bool AboveEyeLine(Camera eye, Vector3 point)
        {
            Vector3 seen = eye.WorldToViewportPoint(point);
            return seen.z > eye.nearClipPlane && seen.x >= 0f && seen.x <= 1f && seen.y >= settings.EyeLine && seen.y <= 1f;
        }

        /// <summary>The most vertices of one skin tested against the eye line.</summary>
        private const int EyeSamples = 2000;

        private void GiveEyeBack()
        {
            if (clearedEye != null) clearedEye.HideFromEye(this, null);
            clearedEye = null;
            hiddenFromEye = System.Array.Empty<Renderer>();
        }

        /// <summary>
        /// Where the root goes for the body to lie on the ground ahead of <paramref name="carrier"/> and to their right, along
        /// the way they face — thrown clear of them rather than dropped at their feet.
        /// </summary>
        private Pose Beside(LiftCarrier carrier)
        {
            Quaternion facing = Facing(carrier.transform);
            Vector3 at = carrier.transform.position + facing * new Vector3(settings.RestSide, 0f, settings.ThrowAhead);
            float below = Bound != null ? Bound.Pose.GroundBelowRoot : 0f;
            float yaw = Bound != null ? Bound.Pose.Yaw : 0f;

            float ground = GroundUnder(at, carrier.transform, out float found) ? found : pickedUpFromGround;
            return new Pose(new Vector3(at.x, ground - below, at.z), facing * Quaternion.AngleAxis(-yaw, Vector3.up));
        }

        private static Quaternion Facing(Transform body) =>
            Quaternion.LookRotation(LiftPoseSolver.Flat(body.forward), Vector3.up);

        /// <summary>The height of the first solid ground below <paramref name="point"/> that is neither this body nor the carrier.</summary>
        private bool GroundUnder(Vector3 point, Transform carrier, out float height)
        {
            height = 0f;
            int count = Physics.RaycastNonAlloc(point + Vector3.up * GroundProbeAbove, Vector3.down, GroundHits,
                                                GroundProbeAbove + GroundProbeBelow, ~0, QueryTriggerInteraction.Ignore);
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                Transform hit = GroundHits[i].collider.transform;
                if (hit.IsChildOf(transform) || hit.IsChildOf(carrier) || GroundHits[i].distance >= nearest) continue;

                nearest = GroundHits[i].distance;
                height = GroundHits[i].point.y;
            }

            return !float.IsPositiveInfinity(nearest);
        }

        /// <summary>How far above and below the carrier's root the ground under a put-down is looked for, in metres.</summary>
        private const float GroundProbeAbove = 2f, GroundProbeBelow = 6f;

        private static readonly RaycastHit[] GroundHits = new RaycastHit[16];

        // ── In the hands (ILiftLoad) ─────────────────────────────────────────

        bool ILiftLoad.CanBeHeld => IsTied;

        // Nothing trails: the body rides the shoulder.
        float ILiftLoad.SwingRate => 0f;
        float ILiftLoad.MaxSwingLag => 90f;

        float ILiftLoad.GripFraction => settings != null ? settings.GripFraction : 0.5f;

        Vector3 ILiftLoad.Axis => transform.forward;

        /// <summary>The knot, wherever the body lies now: it is not posed from the hands, so a pose says nothing about it.</summary>
        Vector3 ILiftLoad.GripOf(Pose pose) => Middle;

        /// <summary>
        /// Round the legs: the right hand on the thighs by the shoulder, the left further out towards the knees. A body with
        /// no legs to name is held by its middle.
        /// </summary>
        void ILiftLoad.Grips(Transform carrier, Vector3 grip, out Vector3 left, out Vector3 right)
        {
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (animator == null || !animator.isHuman)
            {
                // Across the shoulders: a hand up on it over each shoulder.
                Vector3 middle = Bound != null ? transform.TransformPoint(Bound.CarryAnchor) : grip;
                left = heldBy != null ? middle + heldBy.Shoulder(right: false) - Neck : middle;
                right = heldBy != null ? middle + heldBy.Shoulder(right: true) - Neck : middle;
                return;
            }

            Vector3 thighs = Between(HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg);
            Vector3 knees = Between(HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg);
            right = Vector3.Lerp(thighs, knees, settings.NearGrip);
            left = Vector3.Lerp(thighs, knees, settings.FarGrip);
        }

        private Vector3 Between(HumanBodyBones a, HumanBodyBones b) =>
            (animator.GetBoneTransform(a).position + animator.GetBoneTransform(b).position) * 0.5f;

        void ILiftLoad.SetHeldBy(LiftCarrier by)
        {
            if (by != null)
            {
                heldBy = by;
                pickedUpFromGround = transform.position.y + (Bound != null ? Bound.Pose.GroundBelowRoot : 0f);
                if (Bound != null) Bound.BeginCarry(by.gameObject);
                return;
            }

            // Out of the hands while still tied: on the ground beside the carrier, wherever the lowering had got to.
            if (heldBy != null && IsTied)
            {
                Pose rest = lowering ? putDownAt : Beside(heldBy);
                transform.SetPositionAndRotation(rest.position, rest.rotation);
            }

            GiveEyeBack();
            wasUp = false;
            heldBy = null;
            lowering = false;
            if (Bound != null) Bound.EndCarry();
        }

        /// <summary>
        /// Every machine, from the carrier's LateUpdate: the root between the ground and the shoulder, as far up as the
        /// carrier's clip has heaved it — straight up from where it lay, or thrown off in an arc onto where it lands.
        /// </summary>
        void ILiftLoad.Hold(in LiftFrame frame)
        {
            if (heldBy == null || Bound == null || !IsTied) return;

            Bound.Shouldered = frame.Raised;
            Pose ground = lowering ? frame.Rest : frame.From;
            Pose on = OnShoulder();
            float off = 1f - frame.Raised;
            float toss = lowering ? settings.ThrowArc * 4f * off * frame.Raised : 0f;
            transform.SetPositionAndRotation(Vector3.Lerp(ground.position, on.position, frame.Raised) + Vector3.up * toss,
                                             Quaternion.Slerp(ground.rotation, on.rotation, frame.Raised));

            // Measured on the pose it is carried in, which BoundBody writes after this — so on the frame after the one that
            // brought it all the way up.
            bool up = !lowering && frame.Raised >= 1f;
            if (up && wasUp && clearedEye == null && Network.Owns(heldBy.transform)) ClearEye();
            wasUp = up;
        }

        Pose ILiftLoad.RestFromHere() => heldBy != null ? Beside(heldBy) : new Pose(transform.position, transform.rotation);
    }
}
