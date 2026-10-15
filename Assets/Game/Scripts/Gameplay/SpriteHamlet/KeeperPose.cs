using UnityEngine;
using SpaceGame.Agents;

namespace SpaceGame.Gameplay.SpriteHamlet
{
    /// <summary>
    /// How a keeper's little body SAYS what is happening to it in the witch fight, since its rig has
    /// only a walk and an idle: it squashes into a cower while she spins, stretches up to throw,
    /// flinches and coughs when the poison reaches it and goes greener with every ball, lies flat
    /// when it falls, kicks its legs in someone's arms — and in someone's arms is drawn exactly
    /// where this machine sees those arms (GDC-L1-ANIM-0003, GDC-L1-FEEL-0004).
    ///
    /// <para>
    /// <b>One writer of the armature root, layered on the animation.</b> The keeper's clips key the
    /// armature root's rotation, so every pose here is applied in <c>LateUpdate</c> ON TOP of what the
    /// animator wrote this frame — never instead of it — and a frame the animator did not write is
    /// recognised by the root still holding what this wrote last, so a pose can never compound.
    /// </para>
    /// <para>
    /// Pure presentation on every machine, from state that already replicates: the carrier, the
    /// poison count, the witch's spin, a throw's broadcast.
    /// </para>
    /// </summary>
    public class KeeperPose : MonoBehaviour
    {
        [Tooltip("The armature root the poses are layered onto.")]
        [SerializeField] private Transform armature;

        [Header("Cower")]
        [Tooltip("Scale while it cowers from a spin: squat and wide.")]
        [SerializeField] private Vector3 cowerScale = new(1.2f, 0.62f, 1.2f);

        [Tooltip("Trembles per second while it cowers.")]
        [SerializeField, Min(0f)] private float cowerTremble = 22f;

        [SerializeField, Min(1f)] private float duckRadius = 40f;

        [Header("Throw")]
        [Tooltip("How much taller it stretches as it throws, as a fraction.")]
        [SerializeField, Min(0f)] private float throwStretch = 0.3f;

        [SerializeField, Min(0.01f)] private float throwSeconds = 0.35f;

        [Header("Poison")]
        [Tooltip("How far it squashes when a ball reaches it, as a fraction.")]
        [SerializeField, Min(0f)] private float flinchSquash = 0.35f;

        [SerializeField, Min(0.01f)] private float flinchSeconds = 0.4f;

        [Tooltip("Spawned at its head when a ball reaches it. Optional.")]
        [SerializeField] private GameObject flinchPuff;

        [Tooltip("A small cough of poison, every so often once it has been reached. Optional.")]
        [SerializeField] private GameObject coughPuff;

        [Tooltip("Seconds between coughs at one ball; more balls, more coughing.")]
        [SerializeField, Min(0.2f)] private float coughEvery = 3.5f;

        [Tooltip("The colour its body is pulled towards, all the way at the last ball.")]
        [SerializeField] private Color poisonedTint = new(0.45f, 0.95f, 0.25f, 1f);

        [Tooltip("How far towards that colour the last ball pulls it.")]
        [SerializeField, Range(0f, 1f)] private float poisonedTintAmount = 0.65f;

        [Tooltip("Height of its head above its feet, metres.")]
        [SerializeField, Min(0f)] private float headHeight = 0.8f;

        [Header("Fallen")]
        [Tooltip("Degrees it tips over when it falls.")]
        [SerializeField] private float fallenTilt = 85f;

        [SerializeField, Min(0.01f)] private float fallSeconds = 0.5f;

        [Header("Carried")]
        [Tooltip("Degrees it wriggles either way in someone's arms.")]
        [SerializeField] private float wriggleDegrees = 12f;

        [SerializeField, Min(0f)] private float wrigglePerSecond = 7f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private KeeperCarry carry;
        private PoisonTally tally;
        private RueStrikeModule strike;
        private Renderer[] bodyRenderers = System.Array.Empty<Renderer>();
        private MaterialPropertyBlock block;

        private Vector3 restPosition;
        private Vector3 restScale;
        private Quaternion wroteRotation;
        private Quaternion animatedRotation;
        private bool wroteOnce;

        private float throwLeft;
        private float flinchLeft;
        private float fallen;
        private float nextCough;
        private float shownTint = -1f;

        private void Awake()
        {
            carry = GetComponent<KeeperCarry>();
            tally = GetComponent<PoisonTally>();
            strike = GetComponent<RueStrikeModule>();
            bodyRenderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
            block = new MaterialPropertyBlock();

            if (armature != null)
            {
                restPosition = armature.localPosition;
                restScale = armature.localScale;
            }
        }

        private void OnEnable()
        {
            if (strike != null) strike.Threw += OnThrew;
            if (tally != null) tally.HitsChanged += OnHitsChanged;
        }

        private void OnDisable()
        {
            if (strike != null) strike.Threw -= OnThrew;
            if (tally != null) tally.HitsChanged -= OnHitsChanged;
        }

        private void OnThrew() => throwLeft = throwSeconds;

        private void OnHitsChanged(int previous, int current)
        {
            if (current <= previous) return;

            flinchLeft = flinchSeconds;
            nextCough = Time.time + coughEvery * 0.5f;
            Puff(flinchPuff);
        }

        private void Puff(GameObject prefab)
        {
            if (prefab != null)
                Destroy(Instantiate(prefab, transform.position + Vector3.up * headHeight, Quaternion.identity), 3f);
        }

        private void Update()
        {
            int hits = tally != null ? tally.Hits : 0;
            Tint(tally != null && tally.FallsAt > 0 ? hits / (float)tally.FallsAt : 0f);

            if (hits > 0 && !Fallen && Time.time >= nextCough)
            {
                nextCough = Time.time + coughEvery / hits;
                Puff(coughPuff);
            }
        }

        private bool Fallen => tally != null && tally.Fallen;

        private void Tint(float share)
        {
            if (Mathf.Approximately(share, shownTint)) return;
            shownTint = share;

            foreach (Renderer r in bodyRenderers)
            {
                if (r == null) continue;

                // Per material slot, each pulled from its OWN colour: the eyes and the blush go
                // sickly with the body rather than all turning one green.
                Material[] slots = r.sharedMaterials;
                for (int i = 0; i < slots.Length; i++)
                {
                    if (share <= 0f || slots[i] == null || !slots[i].HasProperty(BaseColorId))
                    {
                        r.SetPropertyBlock(null, i);
                        continue;
                    }

                    block.Clear();
                    block.SetColor(BaseColorId, Color.Lerp(slots[i].GetColor(BaseColorId), poisonedTint, share * poisonedTintAmount));
                    r.SetPropertyBlock(block, i);
                }
            }
        }

        private void LateUpdate()
        {
            if (armature == null) return;

            // What the animator wrote this frame, or — when it wrote nothing — what it wrote last.
            if (!wroteOnce || armature.localRotation != wroteRotation) animatedRotation = armature.localRotation;

            float dt = Time.deltaTime;
            throwLeft = Mathf.Max(0f, throwLeft - dt);
            flinchLeft = Mathf.Max(0f, flinchLeft - dt);
            fallen = Mathf.MoveTowards(fallen, Fallen ? 1f : 0f, dt / fallSeconds);

            Vector3 scale = Vector3.one;
            Quaternion tilt = Quaternion.identity;

            bool held = carry != null && carry.IsCarried;
            bool cowering = !held && !Fallen && PoisonSpiralModule.SpinningNear(transform.position, duckRadius);

            if (cowering)
            {
                scale = cowerScale;
                tilt = Quaternion.AngleAxis(Mathf.Sin(Time.time * cowerTremble) * 3f, transform.forward);
            }

            if (throwLeft > 0f)
            {
                float s = 1f - throwLeft / throwSeconds;
                float stretch = Mathf.Sin(s * Mathf.PI) * throwStretch;
                scale = Vector3.Scale(scale, new Vector3(1f - stretch * 0.4f, 1f + stretch, 1f - stretch * 0.4f));
                tilt = Quaternion.AngleAxis(-20f * Mathf.Sin(s * Mathf.PI), transform.right) * tilt;
            }

            if (flinchLeft > 0f)
            {
                float f = flinchLeft / flinchSeconds;
                float squash = flinchSquash * f * f;
                scale = Vector3.Scale(scale, new Vector3(1f + squash * 0.6f, 1f - squash, 1f + squash * 0.6f));
            }

            if (held)
                tilt = Quaternion.AngleAxis(Mathf.Sin(Time.time * wrigglePerSecond * Mathf.PI * 2f) * wriggleDegrees, transform.forward) * tilt;

            if (fallen > 0f)
                tilt = Quaternion.AngleAxis(fallenTilt * Mathf.SmoothStep(0f, 1f, fallen), transform.right) * tilt;

            Transform parent = armature.parent;
            Quaternion parentRotation = parent != null ? parent.rotation : Quaternion.identity;
            armature.localRotation = Quaternion.Inverse(parentRotation) * tilt * parentRotation * animatedRotation;
            armature.localScale = Vector3.Scale(restScale, scale);
            armature.localPosition = restPosition;

            // In someone's arms: drawn at THIS machine's view of those arms, not at the server's copy
            // of the body, which is a round trip behind them.
            Transform carrier = held ? carry.Carrier : null;
            if (carrier != null)
                armature.position += carry.ArmPoint(carrier) - transform.position;

            wroteRotation = armature.localRotation;
            wroteOnce = true;
        }
    }
}
