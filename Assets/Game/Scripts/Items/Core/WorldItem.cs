using UnityEngine;
using SpaceGame.Gameplay;

namespace SpaceGame.Items
{
    /// <summary>Where a world item's size comes from.</summary>
    public enum ItemWorldSizing
    {
        /// <summary>Sized by <see cref="ItemWorldScale"/>, like every other frame the item is drawn in.</summary>
        FromGrip,

        /// <summary>
        /// Left at the prefab's own scale. For the few items whose real size in the world is the
        /// point — a hull module is meant to be the same eleven metres in the sand as it is bolted
        /// to the roof, and shrinking it to gear-wall size would make hauling one meaningless.
        /// </summary>
        Authored,
    }

    /// <summary>
    /// An item lying in the world: how big it is, and how it behaves as a physical object.
    ///
    /// <para>
    /// The counterpart to <see cref="EquipItemSocket"/> (the same item in a hand) and
    /// <c>BackpackItemVisual</c> (the same item on a mat). Each frame takes a fresh copy of the one
    /// prefab and makes it right for where it is; this is the world's turn, and until it existed
    /// the world was the only frame that did nothing at all — a dropped item came out at the raw
    /// prefab scale with whatever collider somebody had reached for.
    /// </para>
    /// <para>
    /// <b>What this replaces.</b> <c>DropItemPhysics</c> froze a dropped item on its first contact
    /// with the Ground layer: velocity zeroed, <c>isKinematic</c> set, pivot snapped down to the
    /// raycast hit. Three things were wrong with it. Inside the ship the floor is not Ground, so it
    /// never fired and the item rolled around the deck forever on the sphere collider most item
    /// prefabs carry. Outside it fired once and the item became immovable — nothing could nudge it,
    /// and a rope tied to it could only slide it along by <c>MovePosition</c>, with no tumble and
    /// no resistance. And snapping the PIVOT to the ground buried anything whose pivot is not at
    /// its base, which is most of them, leaving half the item under the floor to aim at.
    /// </para>
    /// <para>
    /// The replacement is not a freeze at all. A fitted collider and honest damping settle an item
    /// within a second or so and Unity sleeps it; a bump, a shove or a rope wakes it again. Being
    /// dragged is then not a feature anything here implements — the leash's dynamic-body branch,
    /// the lasso, the grapple winch and a player walking into it all move a Rigidbody, and leaving
    /// one live IS the drag. <see cref="Core.NetAuthority"/> freezes the copies on machines that do
    /// not simulate the item, so they cannot fight, and the prefab's <c>NetworkTransform</c>
    /// carries the result outward. This is the same shape the seven hull modules already had, and
    /// they now come through here rather than repeating it in their builder.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public class WorldItem : MonoBehaviour
    {
        /// <summary>
        /// How hard an item may push itself out of something it was spawned inside.
        ///
        /// <para>
        /// A drop starts at the hand and items are now large enough to overlap the dropper, and a
        /// deep overlap resolved at Unity's default depenetration speed launches the item across
        /// the room. Not a feel tunable — a safety clamp on a case that should look like the item
        /// oozing free, so it stays a constant rather than 20 inspector copies of the same number.
        /// </para>
        /// </summary>
        private const float MaxDepenetrationSpeed = 2f;

        [Header("Size")]
        [Tooltip("Where this item's size in the world comes from. FromGrip is right for everything " +
                 "that is gear; Authored is for the few items whose true built size is the point.")]
        [SerializeField] private ItemWorldSizing sizing = ItemWorldSizing.FromGrip;

        [Header("Body")]
        [Tooltip("Mass in kg. 0 derives one from the item's own size, which is what nearly " +
                 "everything wants — set it only where the item is a known weight, like a hull module.")]
        [SerializeField] private float mass;

        [Tooltip("kg per cubic metre of the item's bounding box, used when mass is 0. Gear is " +
                 "mostly hollow, so this is well under the density of the material it looks like: " +
                 "it is picked so an item is heavy enough to stay put and light enough to shove.")]
        [SerializeField] private float density = 90f;

        [SerializeField] private float minimumMass = 1.5f;
        [SerializeField] private float maximumMass = 80f;

        [Tooltip("How quickly a loose item gives up its speed. Sand, not ice: a shoved item coasts " +
                 "a little and stops, rather than skating off down the dune.")]
        [SerializeField] private float linearDamping = 1.5f;

        [Tooltip("How quickly a loose item stops turning. Higher than the linear figure, so a long " +
                 "item settles onto a flank rather than rolling off — a dropped rifle turning on " +
                 "its own axis is the single thing that reads worst.")]
        [SerializeField] private float angularDamping = 4f;

        [Header("Picking it up")]
        [Tooltip("Smallest the item's grab volume may be, in metres. A trigger this size is added " +
                 "when the item's own collider is smaller, so a scanner lying against a floor seam " +
                 "is still something the crosshair can find. 0 disables it.")]
        [SerializeField] private float minimumGrabSize = 0.45f;

        /// <summary>
        /// Whether this item keeps the size it was built at instead of being drawn at the size the
        /// gear wall draws it. True only for the hull modules — see <see cref="ItemWorldSizing"/>.
        /// </summary>
        public bool KeepsAuthoredSize => sizing == ItemWorldSizing.Authored;

        /// <summary>The scale the prefab arrived with, so <see cref="Suppress"/> can put it back.</summary>
        private Vector3 authoredScale;

        /// <summary>
        /// Whether <see cref="Configure"/> has run. <see cref="Suppress"/> is a static reached from
        /// a shared helper, so it must not assume it: restoring an unrecorded scale writes a zero.
        /// </summary>
        private bool configured;

        private void Awake() => Configure();

        /// <summary>
        /// Make this instance into an item lying in the world.
        ///
        /// <para>
        /// Public and idempotent rather than <c>Awake</c>-only, per the project rule about
        /// initialising explicitly: <c>AddComponent</c> runs <c>Awake</c> before the caller's next
        /// statement in play mode and not at all outside it, so anything that has to be able to
        /// assert on the result needs a door it can knock on.
        /// </para>
        /// </summary>
        public void Configure()
        {
            if (configured) return;

            authoredScale = transform.localScale;
            configured = true;

            if (sizing == ItemWorldSizing.FromGrip)
                transform.localScale = ItemWorldScale.LocalScaleFor(gameObject);

            Collider solid = EnsureSolidCollider();

            ConfigureBody();
            EnsureGrabVolume(solid);
        }

        /// <summary>
        /// Take the world's sizing back off an instance that is not in the world after all.
        ///
        /// <para>
        /// Three paths instantiate an item prefab ACTIVE — the hand
        /// (<see cref="EquipItemSocket.Equip"/>) and the two worn seats — so <c>Awake</c> has
        /// already run by the time any of them gets a word in. Everything else <c>Awake</c> did is
        /// undone a line later by <see cref="EquipItemSocket.Sanitize"/>, which switches the
        /// colliders off and the body back to kinematic; the SCALE is the one thing that survives,
        /// and it must not, because <c>EquipItemSocket</c>'s zero-hold-size branch means literally
        /// "whatever scale this instance is carrying". Left alone, the four pinned Fitted items
        /// would come out of the drop-size change 1.9x too big in the hand, and nothing about a
        /// change to a dropped item's size would say why.
        /// </para>
        /// <para>
        /// Called from <c>Sanitize</c> rather than from each of the three sites, because that is
        /// already the one function all three share and is already defined as "turn a fresh
        /// instance into something that can hang off a bone".
        /// </para>
        /// </summary>
        public static void Suppress(GameObject instance)
        {
            if (instance == null) return;
            if (!instance.TryGetComponent(out WorldItem worldItem)) return;

            if (worldItem.configured) instance.transform.localScale = worldItem.authoredScale;
            worldItem.enabled = false;
        }

        // ── Shape ────────────────────────────────────────────────────────────────

        /// <summary>
        /// The collider this item collides and is aimed at with, adding a fitted box if the prefab
        /// carries none.
        ///
        /// <para>
        /// The fallback is for the authoring gap rather than for normal operation — every shipped
        /// item prefab is stamped with a box measured off its own mesh, and
        /// <c>WorldItemSetupTests</c> is what keeps it that way. It is here because the failure it
        /// covers is total and silent: the Grappling Hook shipped with no collider at all, so a
        /// dropped one fell through the world and was never seen again.
        /// </para>
        /// </summary>
        private Collider EnsureSolidCollider()
        {
            var existing = GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < existing.Length; i++)
                if (!existing[i].isTrigger) return existing[i];

            Bounds local = ItemBounds.Measure(gameObject, null);

            BoxCollider box = gameObject.AddComponent<BoxCollider>();
            box.center = local.center;
            box.size = local.size;
            return box;
        }

        /// <summary>
        /// A trigger big enough to aim at, on the item root, when the item itself is too small.
        ///
        /// <para>
        /// <b>On the root, and that is not incidental.</b> <c>Interactor.ResolveAlongRay</c> treats
        /// a trigger as see-through unless it carries the <c>IInteractable</c> ITSELF — a rule that
        /// exists so a vehicle's carry volume cannot answer for every control inside it. A grab
        /// volume parented under the item would therefore be invisible to the E key. It has to sit
        /// on the same GameObject as <c>PickupableItem</c>, which is the root.
        /// </para>
        /// <para>
        /// Added only where it earns its place. Most items are around a metre once
        /// <see cref="ItemWorldScale"/> has sized them and need nothing; the small ones — a scanner,
        /// a coiled leash — are the ones that used to be unpickable on a deck full of other
        /// collision, because the ray found the floor before it found them.
        /// </para>
        /// </summary>
        private void EnsureGrabVolume(Collider solid)
        {
            if (minimumGrabSize <= 0f || solid == null) return;

            // Nothing to reach: without an interactable on this very object the trigger would be
            // one more see-through volume for every ray in the game to walk through.
            if (GetComponent<IInteractable>() == null) return;

            float scale = Mathf.Abs(transform.lossyScale.x);
            if (scale < 1e-5f) return;

            // The minimum is a size in the WORLD, and a collider is authored in local space.
            float floor = minimumGrabSize / scale;

            Bounds local = ItemBounds.Measure(gameObject, null);
            Vector3 size = local.size;

            if (size.x >= floor && size.y >= floor && size.z >= floor) return;

            BoxCollider grab = gameObject.AddComponent<BoxCollider>();
            grab.isTrigger = true;
            grab.center = local.center;
            grab.size = new Vector3(
                Mathf.Max(size.x, floor),
                Mathf.Max(size.y, floor),
                Mathf.Max(size.z, floor));
        }

        // ── Body ─────────────────────────────────────────────────────────────────

        private void ConfigureBody()
        {
            if (!TryGetComponent(out Rigidbody body)) return;

            body.mass = mass > 0f ? mass : DerivedMass();
            body.linearDamping = linearDamping;
            body.angularDamping = angularDamping;
            body.useGravity = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.maxDepenetrationVelocity = MaxDepenetrationSpeed;

            // Thrown items cross a floor's thickness in a step at drop speed, and the ship's decks
            // move under them besides.
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            // Most item prefabs are authored kinematic because that is the state they are HELD in,
            // and the equip path re-applies it on every equip anyway (EquipItemSocket.Sanitize).
            // In the world the body is live; NetAuthority is what puts the copies on other machines
            // back to sleep, and it reads this value as the one to restore.
            body.isKinematic = false;
        }

        /// <summary>
        /// A mass from the item's own bulk, so a hull plate is not the same weight as a scanner.
        ///
        /// <para>
        /// Off the bounding box rather than the mesh: the box is what the item collides with, so a
        /// mass derived from it is the mass of the thing the player actually bumps into. Clamped at
        /// both ends because the derivation is a guess, and a guess that reaches zero gives a body
        /// that is flung by its own contacts.
        /// </para>
        /// </summary>
        private float DerivedMass()
        {
            Bounds local = ItemBounds.Measure(gameObject, null);
            Vector3 scale = transform.lossyScale;

            Vector3 size = new Vector3(
                Mathf.Abs(local.size.x * scale.x),
                Mathf.Abs(local.size.y * scale.y),
                Mathf.Abs(local.size.z * scale.z));

            float volume = size.x * size.y * size.z;

            return Mathf.Clamp(volume * density, minimumMass, Mathf.Max(minimumMass, maximumMass));
        }

        private void OnValidate()
        {
            mass = Mathf.Max(0f, mass);
            density = Mathf.Max(0f, density);
            minimumMass = Mathf.Max(0.01f, minimumMass);
            maximumMass = Mathf.Max(minimumMass, maximumMass);
            linearDamping = Mathf.Max(0f, linearDamping);
            angularDamping = Mathf.Max(0f, angularDamping);
            minimumGrabSize = Mathf.Max(0f, minimumGrabSize);
        }
    }
}
