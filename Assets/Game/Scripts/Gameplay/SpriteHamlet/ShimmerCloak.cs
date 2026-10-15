using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using SpaceGame.Agents;
using SpaceGame.Core;
using SpaceGame.Gameplay.Status;

namespace SpaceGame.Gameplay.SpriteHamlet
{
    /// <summary>
    /// The witch, unseen between her attacks: her body is drawn as a faint shimmer, and every few
    /// seconds she pulses — a bright outline of herself and a ripple across the grass — so a player
    /// who is watching can always find her again.
    ///
    /// <para>
    /// <b>Why she hides, and why not completely</b> (SpriteHamletQuest.md, "Witch"): freezing a witch
    /// who stands in plain sight was the whole fight, and too easy. Hidden, she has to be FOUND before
    /// she can be frozen — by her pulse, her footprints (<see cref="FootprintTrail"/>), and the
    /// moment she shows herself to attack. A boss nobody can locate is not a challenge but a coin
    /// toss, so she is never fully gone (GDC-L1-BAL-0006). The stasis bolt still strikes her unseen:
    /// her colliders are untouched, only her look changes.
    /// </para>
    /// <para>
    /// <b>Shown</b> whenever hiding would be unfair or wrong: while the quest holds her for her
    /// monologue, while either attack is being drawn, while she is in stasis (the keepers need a
    /// target, the player needs to see the reward), during the finish, and once she is dead.
    /// </para>
    /// <para>
    /// <b>Every machine decides for itself, from state it already has</b> — the hold window, the
    /// attacks' presentation, the replicated stasis and health — and the pulse runs on the shared
    /// server clock, so every player sees her pulse at the same moment. Nothing is sent or saved.
    /// </para>
    /// </summary>
    public class ShimmerCloak : MonoBehaviour
    {
        [Tooltip("Drawn on every one of her meshes while she is hidden. SpaceGame/Effects/WitchShimmer.")]
        [SerializeField] private Material shimmerMaterial;

        [Header("Pulse")]
        [Tooltip("Seconds between pulses while she is hidden.")]
        [SerializeField, Min(0.5f)] private float pulseInterval = 4.5f;

        [Tooltip("Seconds a pulse takes to fade.")]
        [SerializeField, Min(0.05f)] private float pulseSeconds = 0.7f;

        [Tooltip("The ripple across the ground at her feet when she pulses. Optional.")]
        [SerializeField] private GameObject pulseRipple;

        [Header("Vanishing")]
        [Tooltip("Spawned at her middle whenever she vanishes or reappears. Optional.")]
        [SerializeField] private GameObject vanishPuff;

        [Tooltip("Height of her middle above her feet, for the puff, metres.")]
        [SerializeField, Min(0f)] private float middleHeight = 1f;

        [Header("What shows her")]
        [SerializeField] private QuestHoldModule hold;
        [SerializeField] private ConjurerCastModule cast;
        [SerializeField] private PoisonSpiralModule spiral;
        [SerializeField] private WitchFight fight;

        private static readonly int PulseId = Shader.PropertyToID("_Pulse");

        private readonly List<Renderer> bodyRenderers = new();
        private readonly List<Material[]> ownMaterials = new();
        private readonly List<ShadowCastingMode> ownShadows = new();

        private HealthComponent health;
        private StatusReceiver receiver;
        private MaterialPropertyBlock block;
        private bool cloaked;
        private int lastPulse = -1;

        /// <summary>Every machine: she is drawn as the shimmer right now.</summary>
        public bool IsCloaked => cloaked;

        private void Awake()
        {
            health = GetComponent<HealthComponent>();
            block = new MaterialPropertyBlock();

            // Her meshes and everything she wears — the hat's herbs too, or they would float
            // through the clearing on their own and give her away. Effects (particles, lines) stay.
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
                if (r is SkinnedMeshRenderer || r is MeshRenderer)
                    bodyRenderers.Add(r);
        }

        private void OnEnable() => receiver = StatusReceiver.Ensure(gameObject);

        private void OnDisable() => SetCloaked(false);

        private bool ShouldShow =>
            (health != null && !health.Alive) ||
            (hold != null && hold.IsHolding) ||
            (cast != null && cast.IsPresenting) ||
            (spiral != null && spiral.IsPresenting) ||
            (receiver != null && receiver.Has(StatusKind.Stasis)) ||
            (fight != null && fight.IsFinishing);

        private void Update()
        {
            bool hide = shimmerMaterial != null && !ShouldShow;
            if (hide != cloaked)
            {
                SetCloaked(hide);
                if (vanishPuff != null)
                    Destroy(Instantiate(vanishPuff, transform.position + Vector3.up * middleHeight, Quaternion.identity), 3f);
            }

            if (cloaked) Pulse();
        }

        /// <summary>The pulse, on the shared server clock so every machine shows it together.</summary>
        private void Pulse()
        {
            double now = SharedClock.Now;
            int index = (int)(now / pulseInterval);
            float into = (float)(now - index * (double)pulseInterval);
            float strength = into < pulseSeconds ? 1f - into / pulseSeconds : 0f;

            if (index != lastPulse && into < pulseSeconds)
            {
                lastPulse = index;
                if (pulseRipple != null)
                    Destroy(Instantiate(pulseRipple, transform.position, Quaternion.identity), 3f);
            }

            block.SetFloat(PulseId, strength * strength);
            foreach (Renderer r in bodyRenderers)
                if (r != null) r.SetPropertyBlock(block);
        }

        private void SetCloaked(bool value)
        {
            if (value == cloaked) return;
            cloaked = value;

            if (value)
            {
                ownMaterials.Clear();
                ownShadows.Clear();
                foreach (Renderer r in bodyRenderers)
                {
                    ownMaterials.Add(r != null ? r.sharedMaterials : null);
                    ownShadows.Add(r != null ? r.shadowCastingMode : ShadowCastingMode.On);
                    if (r == null) continue;

                    var shimmer = new Material[r.sharedMaterials.Length];
                    for (int i = 0; i < shimmer.Length; i++) shimmer[i] = shimmerMaterial;
                    r.sharedMaterials = shimmer;
                    r.shadowCastingMode = ShadowCastingMode.Off;
                }

                return;
            }

            for (int i = 0; i < bodyRenderers.Count && i < ownMaterials.Count; i++)
            {
                Renderer r = bodyRenderers[i];
                if (r == null) continue;

                r.sharedMaterials = ownMaterials[i];
                r.shadowCastingMode = ownShadows[i];
                r.SetPropertyBlock(null);
            }
        }
    }
}
