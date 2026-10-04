// A cart somebody can take hold of and push: two grips, the places its wheels touch the ground, and one holder.
//
// A cart is scenery, like a seat: no NetworkObject, the same bytes in every machine's chunk scene. A holder does not move
// it by a transform of its own; the pusher's CartPusher poses it from the pusher's body every frame, on every machine, so a
// pushed cart needs no network transform and cannot disagree with the body that pushes it. What does have to be shared is
// where a cart STANDS when nobody holds it, and that is the PushableLedger's: the server writes the rest pose when a
// holder lets go, every machine shows it, a joiner reads it with the spawn, and a save keeps it.
//
// Identity is derived like a Seat's (SceneryId), so every machine names the same cart alike and the ledger can key on it.
using System;
using System.Collections.Generic;
using SpaceGame.Characters;
using SpaceGame.Core;
using SpaceGame.Gameplay;
using SpaceGame.Locomotion;
using SpaceGame.Presentation;
using UnityEngine;

namespace SpaceGame.World
{
    [DisallowMultipleComponent]
    public sealed class Pushable : MonoBehaviour, IInteractable, IContextualInteractable, IInteractionReadout, IInteractionMoment
    {
        [Tooltip("What the crosshair calls it.")]
        [SerializeField] private string displayName = "Cart";

        [Tooltip("Where the left hand closes. The cart's travel direction runs from the handles toward its wheels.")]
        [SerializeField] private Transform handleLeft;

        [Tooltip("Where the right hand closes.")]
        [SerializeField] private Transform handleRight;

        [Tooltip("The bottom of each wheel (or the foot of the hover plate), on level ground at rest.")]
        [SerializeField] private Transform[] wheelContacts = Array.Empty<Transform>();

        [Tooltip("Set for a cart on two wheels with shafts: the point it pitches about when the shafts are lifted. " +
                 "Leave empty for a cart that stands on three or more contacts.")]
        [SerializeField] private Transform axle;

        [Tooltip("Metres the cart floats above the ground: a hover cart. 0 for wheels.")]
        [SerializeField, Min(0f)] private float rideHeight;

        [Tooltip("How far, across the ground, a player may be from the handles and still take hold.")]
        [SerializeField, Min(0.5f)] private float gripReach = 4f;

        [Tooltip("What counts as ground under the wheels.")]
        [SerializeField] private LayerMask groundMask = ~0;

        [Tooltip("How far below a probe's start a wheel looks for ground.")]
        [SerializeField, Min(1f)] private float probeLength = 8f;

        [Tooltip("A cart rests within this many metres of where it was authored counts as having never moved.")]
        [SerializeField, Min(0.001f)] private float homeTolerance = 0.05f;

        [Tooltip("...or within this many degrees of its authored heading.")]
        [SerializeField, Range(0.1f, 45f)] private float homeTurnTolerance = 3f;

        [Tooltip("How quickly a cart settles to its rest pose when let go, per second (higher is quicker).")]
        [SerializeField, Min(0.1f)] private float settleRate = 8f;

        private static readonly Dictionary<int, Pushable> ById = new();

        private int id;
        private Transform holder;
        private Rigidbody body;
        private WalkerGround ground;
        private readonly HashSet<Collider> unseenByGround = new();
        private bool homeKnown;
        private CartPose home;
        private CartPose? settling;

        /// <summary>Raised when the holder lets go (or is thrown off because the cart went away). Not raised for a destroyed holder.</summary>
        public event Action<Pushable, Transform> Released;

        /// <summary>The id every machine derives alike; never 0, which is "none" on the wire.</summary>
        public int Id => id != 0 ? id : id = SceneryId.Of(gameObject);

        public static IEnumerable<Pushable> All => ById.Values;

        public string DisplayName => displayName;
        public Transform HandleLeft => handleLeft;
        public Transform HandleRight => handleRight;

        /// <summary>Who holds the handles; null when nobody does, or the holder has been destroyed.</summary>
        public Transform Holder => holder != null ? holder : null;

        public bool IsFree => Holder == null;

        /// <summary>The point between the grips, in the world.</summary>
        public Vector3 HandlePosition => (handleLeft.position + handleRight.position) * 0.5f;

        /// <summary>Whether the cart has a handle pair and wheels: a half-authored one cannot be pushed.</summary>
        public bool IsAuthored => handleLeft != null && handleRight != null && wheelContacts.Length > 0;

        /// <summary>
        /// The cart's own geometry, measured from its root along its own axes and in metres: a settlement places carts at 1.4 to 1.8
        /// times the prefab's size, and the pose the solver returns is a position and a rotation, so the scale is taken out here.
        /// </summary>
        public CartShape Shape
        {
            get
            {
                float scale = transform.lossyScale.x;
                var contacts = new Vector3[wheelContacts.Length];
                for (int i = 0; i < contacts.Length; i++) contacts[i] = Measured(wheelContacts[i].position, scale);
                Vector3? pitchAbout = axle != null ? Measured(axle.position, scale) : (Vector3?)null;
                return new CartShape(Measured(HandlePosition, scale), contacts, pitchAbout, rideHeight);
            }
        }

        private Vector3 Measured(Vector3 world, float scale) => transform.InverseTransformPoint(world) * scale;

        /// <summary>A point of <see cref="Shape"/> as it stands in the world now.</summary>
        public Vector3 WorldOf(Vector3 shapePoint) => transform.position + transform.rotation * shapePoint;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => ById.Clear();

        /// <summary>The cart this id names on this machine, or null (not loaded yet, or never was).</summary>
        public static Pushable Find(int cartId) => cartId != 0 && ById.TryGetValue(cartId, out Pushable cart) && cart != null ? cart : null;

        /// <summary>The nearest free cart whose handles are within <paramref name="reach"/> metres of <paramref name="point"/> across the ground, or null.</summary>
        public static Pushable NearestFree(Vector3 point, float reach)
        {
            Pushable best = null;
            float bestSqr = reach * reach;
            foreach (Pushable cart in ById.Values)
            {
                if (cart == null || !cart.IsFree || !cart.IsAuthored) continue;

                float sqr = cart.FlatSqrDistanceTo(point);
                if (sqr > bestSqr) continue;
                best = cart;
                bestSqr = sqr;
            }
            return best;
        }

        /// <summary>Squared distance from the handles to <paramref name="point"/>, ignoring height.</summary>
        public float FlatSqrDistanceTo(Vector3 point) => Vector3.ProjectOnPlane(HandlePosition - point, Vector3.up).sqrMagnitude;

        /// <summary>Whether <paramref name="point"/> is close enough to the handles, across the ground, to take hold.</summary>
        public bool IsWithinGrip(Vector3 point) => FlatSqrDistanceTo(point) <= gripReach * gripReach;

        private void Awake() => body = GetComponent<Rigidbody>();

        private void OnEnable()
        {
            Register();
            PushableLedger.Shown(this);
        }

        private void OnDisable() => Unregister();

        /// <summary>
        /// Makes this cart findable, and remembers where it was authored the first time (its pose as the scene loaded it, before
        /// the ledger moves it). Done from OnEnable; public so an edit-mode test can stand one up.
        /// </summary>
        public void Register()
        {
            if (!homeKnown)
            {
                homeKnown = true;
                home = new CartPose(transform.position, transform.rotation);
            }

            if (!ById.TryGetValue(Id, out Pushable other) || other == null)
            {
                ById[Id] = this;
                return;
            }

            if (other != this)
                Debug.LogError($"[Pushable] '{name}' and '{other.name}' derive the same id {Id}; the second cannot be pushed by id. " +
                               "Rename one or move it in its hierarchy.", this);
        }

        /// <summary>Lets the holder go and makes this cart unfindable. Done from OnDisable; public so an edit-mode test can take a cart down.</summary>
        public void Unregister()
        {
            Release(holder);
            if (ById.TryGetValue(Id, out Pushable registered) && registered == this) ById.Remove(Id);
        }

        /// <summary>Takes the handles for <paramref name="who"/>. False when somebody else holds them; true again for the holder.</summary>
        public bool TryClaim(Transform who)
        {
            if (who == null) return false;
            if (Holder == who) return true;
            if (!IsFree) return false;

            holder = who;
            settling = null;
            return true;
        }

        /// <summary>
        /// Lets <paramref name="who"/> go and leaves the cart standing where it is: the shafts come down, the wheels stay. Ignores
        /// anybody who is not the holder, so a late release cannot evict the next one. The deciding machine records where it stands.
        /// </summary>
        public bool Release(Transform who)
        {
            if (who == null || Holder != who) return false;

            holder = null;
            CartPose rest = RestPose();
            Settle(rest);
            if (Network.Decides) PushableLedger.Rest(this, rest);
            Released?.Invoke(this, who);
            return true;
        }

        /// <summary>Where the cart was authored: its pose the first time it appeared on this machine.</summary>
        public CartPose HomePose => home;

        /// <summary>Whether a pose is where this cart was authored (so the ledger need not remember it).</summary>
        public bool IsHome(CartPose pose) =>
            (pose.Position - home.Position).sqrMagnitude <= homeTolerance * homeTolerance
            && Quaternion.Angle(pose.Rotation, home.Rotation) <= homeTurnTolerance;

        /// <summary>Puts the cart at <paramref name="pose"/> at once. For the holder's pose each frame, and a restored rest pose.</summary>
        public void PlaceAt(CartPose pose)
        {
            settling = null;
            transform.SetPositionAndRotation(pose.Position, pose.Rotation);
            if (body != null) body.position = pose.Position;
            if (body != null) body.rotation = pose.Rotation;
        }

        /// <summary>Eases the cart to <paramref name="pose"/>: the shafts coming down after a let-go.</summary>
        public void Settle(CartPose pose) => settling = pose;

        // The cart where it stands unheld: the wheels where they are, the shafts down, the heading kept.
        private CartPose RestPose()
        {
            CartShape shape = Shape;
            Vector3 wheels = WorldOf(shape.ContactCentre);
            return CartPoseSolver.Rest(shape, wheels, transform.eulerAngles.y, (x, z) => GroundY(x, z, wheels.y + 1f, wheels.y));
        }

        /// <summary>
        /// Ground height at a world x, z, probed down from <paramref name="fromY"/>; <paramref name="fallback"/> where nothing is
        /// there (the chunk is not loaded). Never the cart itself, never the body that pushes it, never anything on its own physics.
        /// </summary>
        public float GroundY(float x, float z, float fromY, float fallback)
        {
            ground ??= new WalkerGround(transform, groundMask, 0f, probeLength, unseenByGround);
            return ground.Ray(new Vector3(x, fromY, z), probeLength, out RaycastHit hit) ? hit.point.y : fallback;
        }

        /// <summary>Makes the ground probes pretend these colliders (the pusher's body) are not there, or sees them again.</summary>
        public void SetSeenByGround(IEnumerable<Collider> colliders, bool seen)
        {
            foreach (Collider collider in colliders)
            {
                if (seen) unseenByGround.Remove(collider);
                else unseenByGround.Add(collider);
            }
        }

        private void LateUpdate()
        {
            if (settling == null || !IsFree) return;

            CartPose target = settling.Value;
            float blend = 1f - Mathf.Exp(-settleRate * Time.deltaTime);
            Vector3 position = Vector3.Lerp(transform.position, target.Position, blend);
            Quaternion rotation = Quaternion.Slerp(transform.rotation, target.Rotation, blend);
            bool arrived = (position - target.Position).sqrMagnitude < 1e-6f && Quaternion.Angle(rotation, target.Rotation) < 0.05f;

            transform.SetPositionAndRotation(arrived ? target.Position : position, arrived ? target.Rotation : rotation);
            if (body != null) body.position = transform.position;
            if (body != null) body.rotation = transform.rotation;
            if (arrived) settling = null;
        }

        // ── the player's way onto it ─────────────────────────────────────────────────────────────

        public bool CanInteract() => IsFree && IsAuthored;

        public bool CanInteract(Interactor interactor)
        {
            PlayerPushing pushing = PlayerPushing.Of(interactor);
            return pushing != null && pushing.CanGrip && IsWithinGrip(interactor.transform.position);
        }

        public void Interact(Interactor interactor) => PlayerPushing.Of(interactor)?.RequestGrip(this);

        public CharacterMoment InteractionMoment => CharacterMoment.None;

        public string Label => displayName;
        public string Prompt => "RMB: push";
        public float? Value01 => null;
        public string ValueText => null;
    }
}
