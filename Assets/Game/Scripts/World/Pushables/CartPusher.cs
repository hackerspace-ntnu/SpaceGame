// A body with its hands on a cart: poses the cart from the body each frame and reaches the arms for the handles.
//
// On every machine, for every pusher, and from replicated state only: the body is already everywhere (a player's owner-
// authoritative transform, a resident's synced one), and WHICH cart it holds is an id each machine resolves, so the cart's pose
// is derived and never sent while it moves. The decision to grip or let go belongs to whoever decides for this body
// (PlayerPushing, ResidentPushing); this is only the presentation, plus the claim on the cart that keeps two bodies off one pair
// of handles.
//
// The hands are placed from the body, not from a pose: a spot at a fraction of the arm's own length from the shoulders, so the
// same numbers fit a Raxy, the astronaut and anything else with two arms. A cart that can tilt (two wheels and shafts) is lifted
// until its handles are there; one that cannot has handles at the height its own model gives them, and the hands go to that height
// (kept inside what an arm can comfortably reach) with the body stepping up or back to reach it. Then the arms reach for the
// handles; nothing here is baked against a rig.
using System;
using SpaceGame.Presentation;
using UnityEngine;

namespace SpaceGame.World
{
    // After the animator and the other late writers of the arms, so the hands end the frame on the handles.
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(200)]
    public sealed class CartPusher : MonoBehaviour
    {
        [Tooltip("How far from the shoulders the hands are held, as a fraction of the arm's length: 1 is a straight arm.")]
        [SerializeField, Range(0.5f, 1f)] private float armReach = 0.8f;

        [Tooltip("How far below the shoulders the hands are held on a cart that tilts to meet them, as a fraction of the arm's length.")]
        [SerializeField, Range(0f, 1f)] private float handsBelowShoulder = 0.6f;

        [Tooltip("The highest the hands are held on a cart whose handles stand where they stand, below the shoulders, as a fraction of the arm's length.")]
        [SerializeField, Range(0f, 1f)] private float highestBelowShoulder = 0.1f;

        [Tooltip("...and the lowest.")]
        [SerializeField, Range(0f, 1f)] private float lowestBelowShoulder = 0.95f;

        [Tooltip("How far the elbows bow outward, as a fraction of the arm's length.")]
        [SerializeField, Range(0f, 1f)] private float elbowsOut = 0.5f;

        [Tooltip("Seconds the cart takes to come to the hands, and the arms to reach it, when the grip starts.")]
        [SerializeField, Min(0.05f)] private float gripSeconds = 0.5f;

        // Where above the ground the probes start, relative to the hands.
        private const float ProbeAboveHands = 1.5f;

        private Pushable cart;
        private CartShape shape;
        private Animator animator;
        private ReachingArm leftArm, rightArm;
        private BodyLanguage language;
        private Collider[] bodyColliders = Array.Empty<Collider>();
        private Collider[] cartColliders = Array.Empty<Collider>();
        private float handsHeight, handsForward;
        private CartPose start;
        private float heldFor;
        private float probeFrom, lastGround;
        private Func<float, float, float> groundAt;

        /// <summary>Raised when the cart goes away from under the hands (its chunk unloaded); the owner of the decision tells everyone.</summary>
        public event Action<Pushable> Dropped;

        public Pushable Cart => cart;

        /// <summary>The pusher on <paramref name="body"/>, added if it has none.</summary>
        public static CartPusher On(GameObject body) =>
            body.TryGetComponent(out CartPusher pusher) ? pusher : body.AddComponent<CartPusher>();

        /// <summary>Whether this body has the two humanoid arms a grip needs.</summary>
        public bool CanReach => ResolveArms();

        /// <summary>
        /// Takes hold of <paramref name="target"/>. False when the body has no arms to hold with, or somebody else holds the handles.
        /// Idempotent for the cart already held; a different cart is let go of first.
        /// </summary>
        public bool Grip(Pushable target)
        {
            if (target == null || !target.IsAuthored) return false;
            if (cart == target) return true;
            if (!ResolveArms() || !target.TryClaim(transform)) return false;

            Release();
            cart = target;
            shape = cart.Shape;
            cart.Released += OnCartReleased;

            bodyColliders = GetComponentsInChildren<Collider>(false);
            cartColliders = cart.GetComponentsInChildren<Collider>(false);
            IgnoreEachOther(true);
            cart.SetSeenByGround(bodyColliders, false);

            PlaceHands();

            start = new CartPose(cart.transform.position, cart.transform.rotation);
            heldFor = 0f;
            lastGround = cart.transform.position.y;
            groundAt ??= GroundAt;
            if (language == null) language = GetComponentInChildren<BodyLanguage>(true);
            if (language != null) language.OccupyArms(BodyArms.Both);
            return true;
        }

        /// <summary>Lets go, and leaves the cart where it stands.</summary>
        public void Release()
        {
            Pushable let = Detach();
            if (let != null) let.Release(transform);
        }

        private void OnDisable() => Release();

        private void OnCartReleased(Pushable released, Transform who)
        {
            if (released != cart || who != transform) return;

            Detach();
            Dropped?.Invoke(released);
        }

        private Pushable Detach()
        {
            Pushable let = cart;
            if (let == null) return null;

            cart = null;
            let.Released -= OnCartReleased;
            IgnoreEachOther(false);
            let.SetSeenByGround(bodyColliders, true);
            bodyColliders = Array.Empty<Collider>();
            cartColliders = Array.Empty<Collider>();
            if (language != null) language.OccupyArms(BodyArms.None);
            return let;
        }

        private void LateUpdate() => Follow(Time.deltaTime);

        /// <summary>Poses the cart on the body and reaches the arms for its handles. Every frame from LateUpdate; public so a preview can step it.</summary>
        public void Follow(float deltaTime)
        {
            if (cart == null) return;

            heldFor += deltaTime;
            float weight = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(heldFor / gripSeconds));

            Vector3 hands = transform.position + Vector3.up * handsHeight + FlatForward * handsForward;
            probeFrom = hands.y + ProbeAboveHands;
            CartPose target = CartPoseSolver.Solve(shape, hands, transform.eulerAngles.y, groundAt);
            cart.PlaceAt(new CartPose(Vector3.Lerp(start.Position, target.Position, weight), Quaternion.Slerp(start.Rotation, target.Rotation, weight)));
            lastGround = cart.transform.position.y;

            Transform handleLeft = cart.HandleLeft, handleRight = cart.HandleRight;
            if (Vector3.Dot(handleRight.position - handleLeft.position, transform.right) < 0f) (handleLeft, handleRight) = (handleRight, handleLeft);

            Reach(leftArm, handleLeft.position, -transform.right, weight);
            Reach(rightArm, handleRight.position, transform.right, weight);
        }

        private void Reach(ReachingArm arm, Vector3 handle, Vector3 outward, float weight) =>
            arm.Reach(handle, arm.Shoulder + outward * (arm.Length * elbowsOut) + Vector3.down * arm.Length, weight);

        private float GroundAt(float x, float z) => cart.GroundY(x, z, probeFrom, lastGround);

        // Where the hands are held, relative to the body: the height first, then how far ahead puts it at the arm's reach.
        private void PlaceHands()
        {
            Vector3 shoulders = (leftArm.Shoulder + rightArm.Shoulder) * 0.5f;
            float arm = (leftArm.Length + rightArm.Length) * 0.5f;

            float below = handsBelowShoulder * arm;
            if (!shape.Axle.HasValue)
            {
                float ground = cart.GroundY(transform.position.x, transform.position.z, shoulders.y, transform.position.y);
                below = Mathf.Clamp(shoulders.y - (ground + shape.HandleRise), highestBelowShoulder * arm, lowestBelowShoulder * arm);
            }

            float ahead = Mathf.Sqrt(Mathf.Max(0f, armReach * armReach * arm * arm - below * below));
            handsHeight = shoulders.y - transform.position.y - below;
            handsForward = Vector3.Dot(shoulders - transform.position, FlatForward) + ahead;
        }

        private Vector3 FlatForward
        {
            get
            {
                Vector3 flat = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
                return flat.sqrMagnitude > 1e-6f ? flat.normalized : Vector3.forward;
            }
        }

        // The cart is solid to everyone else, but its own pusher walks into it all the time: the handles are inside the body's capsule.
        private void IgnoreEachOther(bool ignore)
        {
            foreach (Collider body in bodyColliders)
                foreach (Collider part in cartColliders)
                    if (body != null && part != null) Physics.IgnoreCollision(body, part, ignore);
        }

        private bool ResolveArms()
        {
            if (leftArm != null) return true;

            if (animator == null) animator = GetComponentInChildren<Animator>(true);
            if (animator == null || !animator.isHuman) return false;

            leftArm = ReachingArm.Of(animator, false);
            rightArm = ReachingArm.Of(animator, true);
            return leftArm != null && rightArm != null;
        }
    }
}
