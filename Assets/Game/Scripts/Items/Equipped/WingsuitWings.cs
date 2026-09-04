// The two membranes: where they sit, when they are seen, and what the air does to them.
//
// Everything here runs on every machine and none of it is on the wire. What it is driven by — the
// glide bool and the body's own motion — is already replicated for other reasons, which is what
// makes a peer's copy of a gliding player look like the flight the owner is actually flying.
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// Straps a wingsuit's membranes to the wearer's arms and blows air into them.
    ///
    /// <para>
    /// Each membrane is reparented onto the upper-arm bone rather than left on the pack, so an arm
    /// that moves takes its wing with it. That matters exactly once — a gauntlet fired mid-glide
    /// raises an arm — and the alternative is a rigid panel that stays behind while the arm inside
    /// it swings out.
    /// </para>
    /// </summary>
    public class WingsuitWings : MonoBehaviour
    {
        [Header("Membranes")]
        [Tooltip("The wing that goes on the wearer's LEFT arm. A child of this prefab until the " +
                 "suit is worn, then a child of the arm bone.")]
        [SerializeField] private Transform leftMembrane;

        [Tooltip("The wing that goes on the wearer's RIGHT arm.")]
        [SerializeField] private Transform rightMembrane;

        [Tooltip("The left wing's leading-edge spar. Adopted by its membrane at runtime so the " +
                 "two behave as one wing.")]
        [SerializeField] private Transform leftBatten;

        [Tooltip("The right wing's leading-edge spar.")]
        [SerializeField] private Transform rightBatten;

        // Two authored fits rather than one mirrored in code, deliberately. The two membranes are
        // already true mirrors of each other in the model, and a humanoid rig's left and right arm
        // bones are NOT mirror-image frames — the gauntlets found that out and pay for it with a
        // negative scale and a hand-derived dorsal axis. Two poses tuned by eye have no signs in
        // them to get backwards.

        [Header("Fit — right arm")]
        [Tooltip("Where Mesh_Wingsuit_Membrane_R sits on the right upper-arm bone.")]
        [SerializeField] private Vector3 rightLocalPosition = Vector3.zero;

        [Tooltip("How it is turned on that bone, degrees.")]
        [SerializeField] private Vector3 rightLocalEuler = Vector3.zero;

        [Header("Fit — left arm")]
        [Tooltip("Where Mesh_Wingsuit_Membrane_L sits on the left upper-arm bone.")]
        [SerializeField] private Vector3 leftLocalPosition = Vector3.zero;

        [Tooltip("How it is turned on that bone, degrees.")]
        [SerializeField] private Vector3 leftLocalEuler = Vector3.zero;

        [Header("Air")]
        [Tooltip("Airspeed at which the membrane billows its hardest, m/s. The shipped glide sits " +
                 "around 23, so the wing is near full at cruise and slack in a stall.")]
        [SerializeField, Min(1f)] private float fullBillowSpeed = 24f;

        [Tooltip("How far the membrane bulges at that speed, metres. This is ClothWind's " +
                 "_WindStrength, which the shader treats as a displacement amplitude.")]
        [SerializeField, Range(0f, 2f)] private float maxBillow = 0.35f;

        [Tooltip("How much the airflow is bent UPWARD before it is handed to the shader, 0..1. " +
                 "The air a wingsuit meets comes at it almost edge-on, and cloth pushed edge-on " +
                 "does not read as lift — this is what makes it billow up into the underside " +
                 "instead of merely rippling along it.")]
        [SerializeField, Range(0f, 1f)] private float upwardBias = 0.55f;

        [Tooltip("How quickly the billow follows the airspeed, per second.")]
        [SerializeField, Min(0.01f)] private float response = 6f;

        private static readonly int WindDirId = Shader.PropertyToID("_WindDirection");
        private static readonly int WindStrengthId = Shader.PropertyToID("_WindStrength");

        // Two lists, because they answer different questions. Everything under a membrane —
        // the cloth and the leading-edge spar nested with it — is shown and hidden together. Only
        // the cloth itself is handed wind: the spar is steel, and writing a billow onto it would
        // be a property nothing reads sitting on a material that does not want it.
        private Renderer[] wingRenderers;
        private Renderer[] clothRenderers;
        private MaterialPropertyBlock block;

        private Transform body;
        private Vector3 lastPosition;
        private float billow;
        private bool spread;

        /// <summary>
        /// Whether the wings are out. Set once a frame by <c>WingsuitItem</c> from the replicated
        /// glide bool — this component never asks the Animator itself, so there is one reader of
        /// that parameter and this stays presentation.
        /// </summary>
        public bool Spread
        {
            get => spread;
            set
            {
                if (spread == value) return;

                spread = value;
                SetWingsVisible(value);
                RefreshWornVisual();
            }
        }

        /// <summary>
        /// Show the worn wing exactly when the flight wing is not out.
        ///
        /// <para>
        /// The two are the same wing in two states — folded across the shoulders, and spread on
        /// the arms — so exactly one of them may be visible. <see cref="WornSeat"/> turns the worn
        /// one on when the suit is put on; this turns it off again for the length of a glide and
        /// back on at the end of it, and does nothing at all for a suit that is in a hand or on
        /// the ground, where the worn model has no business being seen.
        /// </para>
        /// </summary>
        private void RefreshWornVisual()
        {
            var usable = GetComponent<UsableItem>();
            bool worn = usable != null && usable.Worn;
            WornVisual.SetWorn(gameObject, worn && !spread);
        }

        private void Awake()
        {
            block = new MaterialPropertyBlock();
            CacheRenderers();
            SetWingsVisible(false);
        }

        /// <summary>
        /// Hang the membranes off the wearer's arms. Called when the suit is worn, on every
        /// machine, from <c>WingsuitItem.OnEquipped</c>.
        /// </summary>
        public void AttachTo(GameObject holder)
        {
            if (holder == null) return;

            body = holder.transform;
            lastPosition = body.position;

            var animator = holder.GetComponentInChildren<Animator>();

            Transform rightArm =
                BoneResolver.Resolve(animator, body, HumanBodyBones.RightUpperArm, RightArmHints);
            Transform leftArm =
                BoneResolver.Resolve(animator, body, HumanBodyBones.LeftUpperArm, LeftArmHints);

            // The spar goes on the SAME bone at the SAME pose as its membrane rather than being
            // parented to it. Parenting was the obvious shape and it does not work: the model is a
            // nested prefab instance, and Unity refuses to reparent a transform that lives inside
            // one — loudly in the editor, and the reparent simply does not happen. Two objects with
            // one fit coincide exactly, because both are authored on the same origin.
            Seat(rightMembrane, rightArm, rightLocalPosition, rightLocalEuler);
            Seat(rightBatten, rightArm, rightLocalPosition, rightLocalEuler);

            Seat(leftMembrane, leftArm, leftLocalPosition, leftLocalEuler);
            Seat(leftBatten, leftArm, leftLocalPosition, leftLocalEuler);
        }

        /// <summary>
        /// Bring the membranes home before the item is destroyed.
        ///
        /// Without this they are children of a skeleton that is about to outlive them: the item's
        /// own destroy takes its hierarchy, and anything reparented out of it is no longer in that
        /// hierarchy. Two wings would be left hanging off the astronaut's arms for the rest of the
        /// session, invisible in the Inspector under an item that no longer exists.
        /// </summary>
        public void Detach()
        {
            Bring(leftMembrane);
            Bring(leftBatten);
            Bring(rightMembrane);
            Bring(rightBatten);

            body = null;
        }

        private void Bring(Transform part)
        {
            if (part != null) part.SetParent(transform, false);
        }

        private static readonly string[] RightArmHints = { "RightArm", "Arm.R", "Arm_R", "upperarm_r" };
        private static readonly string[] LeftArmHints = { "LeftArm", "Arm.L", "Arm_L", "upperarm_l" };

        /// <summary>
        /// One membrane onto one bone, at that side's authored fit. The scale is left exactly as
        /// the prefab has it: the mesh is already the right hand of the pair, so nothing here needs
        /// a sign.
        /// </summary>
        private void Seat(Transform membrane, Transform bone, Vector3 localPosition,
                          Vector3 localEuler)
        {
            if (membrane == null) return;

            if (bone == null)
            {
                Debug.LogError($"[WingsuitWings] '{name}' found no upper-arm bone to seat a " +
                               "membrane on; the wing will stay on the pack.", this);
                return;
            }

            membrane.SetParent(bone, false);
            membrane.localPosition = localPosition;
            membrane.localRotation = Quaternion.Euler(localEuler);
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || body == null) return;

            Vector3 delta = body.position - lastPosition;
            lastPosition = body.position;

            // Measured off the transform rather than a Rigidbody, because a peer's body is
            // kinematic and its velocity reads zero everywhere but on its owner's machine.
            float speed = delta.magnitude / dt;

            float target = spread ? Mathf.Clamp01(speed / fullBillowSpeed) * maxBillow : 0f;
            billow = Mathf.Lerp(billow, target, 1f - Mathf.Exp(-response * dt));

            if (!spread) return;

            ApplyWind(RelativeAirflow(delta));
        }

        /// <summary>
        /// Where the air is coming from, bent upward.
        ///
        /// <para>
        /// The honest answer is simply the reverse of the motion, and the honest answer looks
        /// wrong: a wingsuit meets its air nearly edge-on, and ClothWind displaces along the wind,
        /// so the true airflow ripples the membrane lengthwise instead of filling it. Tilting the
        /// vector toward world up puts the push under the wing where the player expects to see it.
        /// GDC-L1-FEEL-0007 — the sensation is the target, and the sensation is air holding you up.
        /// </para>
        /// </summary>
        private Vector3 RelativeAirflow(Vector3 delta)
        {
            Vector3 flow = -delta;
            if (flow.sqrMagnitude < 1e-6f) return Vector3.up;

            return Vector3.Slerp(flow.normalized, Vector3.up, upwardBias);
        }

        private void ApplyWind(Vector3 direction)
        {
            if (clothRenderers == null) return;

            for (int i = 0; i < clothRenderers.Length; i++)
            {
                Renderer renderer = clothRenderers[i];
                if (renderer == null) continue;

                renderer.GetPropertyBlock(block);
                block.SetVector(WindDirId, direction);
                block.SetFloat(WindStrengthId, billow);
                renderer.SetPropertyBlock(block);
            }
        }

        private void CacheRenderers()
        {
            var all = new System.Collections.Generic.List<Renderer>();
            var cloth = new System.Collections.Generic.List<Renderer>();

            // The spars are wing too: they are hidden and shown with the cloth, and — the part
            // that actually bit — they must be OFF while the suit is folded, because ItemBounds
            // measures only what is switched on and WornSeat scales the whole item to match. Two
            // visible 0.93 m spars made the folded suit measure 2.5 m and the pack on the wearer's
            // back was scaled to a sliver 8 cm tall. That was "I cannot see it".
            Collect(leftMembrane, all, cloth, isCloth: true);
            Collect(rightMembrane, all, cloth, isCloth: true);
            Collect(leftBatten, all, cloth, isCloth: false);
            Collect(rightBatten, all, cloth, isCloth: false);

            wingRenderers = all.ToArray();
            clothRenderers = cloth.ToArray();
        }

        private static void Collect(Transform part,
                                    System.Collections.Generic.List<Renderer> all,
                                    System.Collections.Generic.List<Renderer> cloth,
                                    bool isCloth)
        {
            if (part == null) return;

            all.AddRange(part.GetComponentsInChildren<Renderer>(true));

            if (!isCloth) return;

            var own = part.GetComponent<Renderer>();
            if (own != null) cloth.Add(own);
        }

        private void SetWingsVisible(bool visible)
        {
            if (wingRenderers == null) return;

            for (int i = 0; i < wingRenderers.Length; i++)
                if (wingRenderers[i] != null) wingRenderers[i].enabled = visible;
        }
    }
}
