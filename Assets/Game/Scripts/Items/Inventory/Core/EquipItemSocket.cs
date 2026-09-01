using UnityEngine;
using SpaceGame.Weapons;

namespace SpaceGame.Items
{
    /// <summary>
    /// Puts an item in a hand and takes it out again.
    ///
    /// <para>
    /// Every held object in the game passes through here — the player's artifacts by way of
    /// <see cref="EquipmentController"/>, an NPC's weapons by way of
    /// <c>EntityEquipmentController</c> — so this is the one place that has to get the pose right,
    /// and the one place that can.
    /// </para>
    /// <para>
    /// Three things happen at equip time, and each fixes something that used to be wrong:
    /// </para>
    /// <list type="number">
    /// <item><b>Pose.</b> The item is rotated into the hand's <see cref="HandGripFrame"/> — an
    /// anatomy-derived frame, not the bone's arbitrary exported axes — and then translated so its
    /// grip point lands in the palm. Rotating and translating together is the point: seating the
    /// pivot and hoping, which is what this did before, only works for items whose pivot happens
    /// to be on the handle.</item>
    /// <item><b>Size.</b> The item is scaled to a real size in metres, measured from its own mesh
    /// and divided by whatever scale the rig carries, so the number an artist types means the same
    /// thing on every character.</item>
    /// <item><b>Physics.</b> Colliders and bodies are switched off <i>throughout the hierarchy</i>.
    /// Doing only the root — which is what <c>GetComponent</c> did here — left child colliders
    /// live on the end of an arm, shoving the holder about and snagging on the world as they
    /// walked.</item>
    /// </list>
    /// </summary>
    public class EquipItemSocket
    {
        /// <summary>Longest-axis size, in metres, given to an item that never declared one.</summary>
        private const float DefaultHoldSize = 0.30f;

        private readonly Transform socket;
        private readonly HandGripFrame frame;
        private readonly float scaleMultiplier;

        private GameObject currentObject;
        private Transform currentGrip;

        public EquipItemSocket(Transform socket)
            : this(socket, HandGripFrame.Identity("caller supplied no grip frame"), 1f)
        {
        }

        public EquipItemSocket(Transform socket, HandGripFrame frame, float scaleMultiplier = 1f)
        {
            this.socket = socket;
            this.frame = frame;
            this.scaleMultiplier = Mathf.Max(0.0001f, scaleMultiplier);
        }

        /// <summary>The bone items hang off.</summary>
        public Transform Socket => socket;

        /// <summary>Where in the world the hand actually grips — the palm, not the wrist.</summary>
        public Vector3 GripPosition =>
            socket != null ? socket.TransformPoint(frame.LocalPosition) : Vector3.zero;

        /// <summary>The orientation a held item is posed against.</summary>
        public Quaternion GripRotation =>
            socket != null ? socket.rotation * frame.LocalRotation : Quaternion.identity;

        /// <summary>
        /// The grip frame's rotation in the hand bone's own space.
        ///
        /// <para>
        /// Needed by anything that aims the HAND and wants the ITEM to end up pointing somewhere.
        /// The two differ by exactly this, and on this rig that difference is most of a right
        /// angle — see <see cref="HandGripFrame"/>.
        /// </para>
        /// </summary>
        public Quaternion FrameLocalRotation => frame.LocalRotation;

        public GameObject Current => currentObject;

        public GameObject Equip(GameObject prefab)
        {
            Unequip();

            if (!prefab || socket == null)
                return null;

            currentObject = Object.Instantiate(prefab, socket);

            Sanitize(currentObject);
            Seat(currentObject);

            return currentObject;
        }

        public void Unequip()
        {
            if (currentObject)
            {
                Object.Destroy(currentObject);
                currentObject = null;
            }
            currentGrip = null;
        }

        /// <summary>
        /// Slide the item back so its grip point sits in the palm again, leaving its rotation alone.
        ///
        /// <para>
        /// For anything that aims a held item by writing to its rotation. An item rotates about its
        /// own pivot, and the pivot is not the handle, so every turn walks the grip out of the hand
        /// unless it is put back.
        /// </para>
        /// </summary>
        public void ReseatGrip()
        {
            if (currentObject == null || currentGrip == null || socket == null) return;
            currentObject.transform.position += GripPosition - currentGrip.position;
        }

        // ── Seating ──────────────────────────────────────────────────────────────

        private void Seat(GameObject item)
        {
            Transform t = item.transform;
            var grip = item.GetComponent<ItemGrip>();

            // Scale before anything else. Both the grip point's position and the mesh bounds move
            // when the item is resized, so seating first would seat it to a size it no longer is.
            float holdSize = grip != null ? grip.HoldSize : DefaultHoldSize;

            // Measured against whatever the item says represents its size — the Lasso's handle
            // rather than the rope coiled in the same prefab. Narrowing only ever happens when
            // there IS an ItemGrip, and an ItemGrip also names the grip point, so the bounds are
            // never both narrowed and used for seating.
            Bounds bounds = ItemBounds.Measure(item, grip != null ? grip.SizeReference : null);

            ApplyScale(t, holdSize, bounds);

            // Orientation: the hand's frame, plus whatever the item asked for on top.
            Quaternion handRotation = GripRotation;
            t.rotation = handRotation * (grip != null ? Quaternion.Euler(grip.RotationOffset) : Quaternion.identity);

            // Position: put the grip point in the palm, then apply the item's offset along the
            // hand's own axes rather than the item's, so "slide it further out the thumb side"
            // stays true whatever angle the item ended up at.
            currentGrip = ResolveGripPoint(item, grip, bounds);

            Vector3 target = GripPosition;
            if (grip != null)
                target += handRotation * grip.PositionOffset;

            t.position += target - currentGrip.position;
        }

        /// <summary>
        /// Which point on the item the hand closes around, best source first.
        ///
        /// <para>
        /// The bounds-centre fallback is what makes this scale to artifacts nobody has tuned: an
        /// item with no grip declared is held through its middle, which is never exactly right and
        /// never badly wrong. Adding an <see cref="ItemGrip"/> is then a refinement rather than a
        /// rescue.
        /// </para>
        /// </summary>
        private Transform ResolveGripPoint(GameObject item, ItemGrip grip, Bounds localBounds)
        {
            if (grip != null) return grip.GripPoint;

            // A weapon already carries a hand-authored grip under a different name. Honouring it
            // here is what lets the special case that used to live in this class go away.
            var weapon = item.GetComponent<Weapon>();
            if (weapon != null && weapon.Handle1 != null) return weapon.Handle1;

            // Nothing authored. Hold it through the middle of its own mesh.
            var proxy = new GameObject("GripPoint (auto)").transform;
            proxy.SetParent(item.transform, false);
            proxy.localPosition = localBounds.center;
            proxy.localRotation = Quaternion.identity;
            return proxy;
        }

        /// <summary>
        /// Resize the item so its longest side is <paramref name="holdSize"/> metres in the world.
        ///
        /// <para>
        /// A <paramref name="holdSize"/> of zero means "keep the size the artist built", but even
        /// then the rig's own scale has to be divided out, or an item authored at a metre becomes
        /// a hundred on a character imported at scale 100.
        /// </para>
        /// </summary>
        private void ApplyScale(Transform t, float holdSize, Bounds localBounds)
        {
            Vector3 parentScale = socket.lossyScale;
            if (Mathf.Abs(parentScale.x) < 1e-6f || Mathf.Abs(parentScale.y) < 1e-6f || Mathf.Abs(parentScale.z) < 1e-6f)
                return;

            Vector3 authored = t.localScale;

            // Undo the rig, so local scale now means world scale.
            Vector3 neutral = new Vector3(authored.x / parentScale.x,
                                          authored.y / parentScale.y,
                                          authored.z / parentScale.z);

            if (holdSize <= 0f)
            {
                t.localScale = neutral * scaleMultiplier;
                return;
            }

            // Longest side of the mesh, at the size the artist built it.
            Vector3 worldSize = Vector3.Scale(localBounds.size, new Vector3(Mathf.Abs(authored.x), Mathf.Abs(authored.y), Mathf.Abs(authored.z)));
            float longest = Mathf.Max(worldSize.x, Mathf.Max(worldSize.y, worldSize.z));

            if (longest < 1e-5f)
            {
                // Nothing measurable — a pure-effect item, or all its geometry is spawned at use
                // time. Leaving it at the artist's scale beats resizing off a bogus measurement.
                t.localScale = neutral * scaleMultiplier;
                return;
            }

            t.localScale = neutral * (holdSize / longest) * scaleMultiplier;
        }

        // ── Physics ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Make the item inert for as long as it is held.
        ///
        /// <para>
        /// Nothing is restored on unequip because nothing survives it — the instance is destroyed
        /// and a dropped item is built from the prefab again.
        /// </para>
        /// </summary>
        private static void Sanitize(GameObject item)
        {
            var grip = item.GetComponent<ItemGrip>();
            if (grip != null && grip.KeepColliders) return;

            var colliders = item.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
                colliders[i].enabled = false;

            var bodies = item.GetComponentsInChildren<Rigidbody>(true);
            for (int i = 0; i < bodies.Length; i++)
            {
                Rigidbody rb = bodies[i];

                // detectCollisions before isKinematic: a body that is still awake and colliding
                // when it is pinned to a moving bone reports contacts against whatever the hand
                // sweeps through, and a kinematic body's contacts are not free.
                rb.detectCollisions = false;
                rb.isKinematic = true;
                rb.useGravity = false;
                rb.interpolation = RigidbodyInterpolation.None;
            }
        }
    }
}
