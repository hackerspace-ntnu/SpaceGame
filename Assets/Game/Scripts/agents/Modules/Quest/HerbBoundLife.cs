// The witch's life, bound up in the stolen sacred herbs stuck in her hat: no blade or bullet takes
// it, only rue pressed against her by the hamlet's keepers -- and they can only reach her while
// she is held in stasis.
//
// Why it is shaped like this (SpriteHamletQuest.md, "Witch"):
//   * Her health is cut into one slice per herb. A stasis lets the keepers burn at most ONE herb,
//     however many of them strike and however long they keep at it, so the fight is paced by the
//     freezes the player lands -- three freezes for three herbs -- and cannot be skipped by stacking
//     damage (GDC-L1-LEVEL-0003). The last herb's floor is zero: that stasis can end her.
//   * The player's own weapons do nothing to her health, but a hit staggers her for a moment --
//     guns set up the stasis shot instead of being dead weight -- with a recovery window after, so
//     sustained fire cannot pin her in place forever.
//   * The herbs ARE the health bar: each one on the hat goes dark as it burns, on every machine,
//     read off her replicated health, so nothing extra travels or saves. A burn is the biggest
//     moment in the fight and is sold as one: a flash on the herb, a jolt through her frozen body,
//     and a shake for anyone close (GDC-L1-FEEL-0004). Not an animator hit-pause: a herb only burns
//     while she is in stasis, whose shell has already stopped her animator, and a pause that gave
//     the speed back would thaw her pose under the shell.
//   * The LAST herb is not burned but plucked. With lastHerbPlucked on, rue can take her down to
//     a sliver and no further; WitchFight sees her broken and sends the keepers up her hat to pull
//     the last herb out by hand (PluckLastHerb), which is the finishing cutscene.
//
// Override priority while staggered: standing there IS the behaviour, and a cast or chase below
// must not move her. Her QuestHoldModule (Scripted) still outranks it. Otherwise it passes.
using FirstGearGames.SmoothCameraShaker;
using UnityEngine;
using SpaceGame.Audio;
using SpaceGame.Gameplay;
using SpaceGame.Gameplay.Objectives;
using SpaceGame.Gameplay.Status;

namespace SpaceGame.Agents
{
    [RequireComponent(typeof(HealthComponent))]
    public class HerbBoundLife : BehaviourModuleBase, IDamageFilter, IQuestStatus
    {
        [Header("Herbs")]
        [Tooltip("How many sacred herbs her life is bound to — one slice of her health each, and one " +
                 "burned per stasis at most.")]
        [SerializeField, Min(1)] private int herbs = 3;

        [Tooltip("One per herb, first-burned first. Each is hidden once its herb is burned.")]
        [SerializeField] private GameObject[] herbVisuals = System.Array.Empty<GameObject>();

        [Tooltip("Rue stops one hit short of the last herb: it is plucked from her hat by the keepers " +
                 "in the finishing cutscene (WitchFight) rather than burned.")]
        [SerializeField] private bool lastHerbPlucked = true;

        [Header("Burn")]
        [Tooltip("Spawned at a herb the moment it burns, on every machine. Optional.")]
        [SerializeField] private GameObject herbBurnFx;

        [Tooltip("The model jolted when a herb burns. Her Animator's object when left empty.")]
        [SerializeField] private Transform joltPivot;

        [Tooltip("How much bigger the jolt swells her for an instant, as a fraction.")]
        [SerializeField, Min(0f)] private float joltAmount = 0.12f;

        [Tooltip("Seconds the jolt takes to settle.")]
        [SerializeField, Min(0.01f)] private float joltSeconds = 0.3f;

        [Tooltip("Shake for anyone within burnShakeRadius when a herb burns. Optional.")]
        [SerializeField] private ShakeData burnShake;

        [SerializeField, Min(0f)] private float burnShakeRadius = 30f;

        [Tooltip("Heard when a herb burns, on every machine.")]
        [SerializeField] private SfxId burnSound = SfxId.ImpactCritical;

        [Header("Stagger")]
        [Tooltip("Seconds a hit from anything but rue stops her where she stands.")]
        [SerializeField, Min(0f)] private float staggerSeconds = 0.6f;

        [Tooltip("Seconds after a stagger before another hit can stagger her, so sustained fire " +
                 "cannot pin her down.")]
        [SerializeField, Min(0f)] private float staggerRecoverySeconds = 2.5f;

        [Header("Status line")]
        [Tooltip("{0} is how many herbs are burned, {1} how many there are.")]
        [SerializeField] private string statusFormat = "Sacred herbs burned  <b>{0}/{1}</b>";

        [Tooltip("Added while she is free.")]
        [SerializeField] private string freeText = "Freeze her so the keepers' rue can land!";

        [Tooltip("Added while she is in stasis. {0} is how far the current herb has burned, 0–100.")]
        [SerializeField] private string heldFormat = "<b>Held!</b> Rue burning  <b>{0}%</b>";

        [Tooltip("Between the two parts of the status line.")]
        [SerializeField] private string statusSeparator = "  ·  ";

        private HealthComponent health;
        private StatusReceiver receiver;
        private int windowFloor;
        private bool plucking;
        private int herbsShown = -1;
        private float joltLeft;
        private Vector3 joltRest = Vector3.one;
        private float staggeredUntil = float.NegativeInfinity;
        private float staggerReadyAt = float.NegativeInfinity;

        private float SliceSize => health.GetMaxHealth / (float)herbs;

        /// <summary>Herbs already burned, 0 … <see cref="herbs"/>. Every machine, off replicated health.</summary>
        public int HerbsBurned =>
            herbs - Mathf.CeilToInt(Mathf.Max(0, health.GetHealth) / SliceSize - 0.0001f);

        /// <summary>Is she held in stasis right now? Every machine.</summary>
        public bool InStasis => receiver != null && receiver.Has(StatusKind.Stasis);

        /// <summary>
        /// Every machine: rue has taken all it may and only the plucked herb is left. What
        /// <c>WitchFight</c> waits for to start the finish.
        /// </summary>
        public bool IsBroken => lastHerbPlucked && health.Alive && health.GetHealth <= PluckFloor;

        /// <summary>The health the last herb holds at once rue is done with her: one point.</summary>
        private const int PluckFloor = 1;

        public string QuestStatus
        {
            get
            {
                string burned = string.Format(statusFormat, HerbsBurned, herbs);
                if (!InStasis) return burned + statusSeparator + freeText;

                float burnedIntoHerb = health.GetMaxHealth - health.GetHealth - HerbsBurned * SliceSize;
                int percent = Mathf.Clamp(Mathf.FloorToInt(burnedIntoHerb / SliceSize * 100f), 0, 100);
                return burned + statusSeparator + string.Format(heldFormat, percent);
            }
        }

        private void Reset() => SetPriorityDefault(ModulePriority.Override);

        private void Awake()
        {
            health = GetComponent<HealthComponent>();
            if (joltPivot == null)
            {
                Animator animator = GetComponentInChildren<Animator>(true);
                joltPivot = animator != null ? animator.transform : null;
            }

            if (joltPivot != null) joltRest = joltPivot.localScale;
        }

        private void OnEnable()
        {
            health.AddFilter(this);

            // Ensure rather than Of: the AgentController makes the receiver in its own Awake, which
            // is not ordered against this one; whichever runs first makes it and the other finds it.
            receiver = StatusReceiver.Ensure(gameObject);
            receiver.StatusChanged += OnStatusChanged;
        }

        private void OnDisable()
        {
            health.RemoveFilter(this);
            if (receiver != null) receiver.StatusChanged -= OnStatusChanged;
        }

        public override string ModuleDescription =>
            "Life bound to sacred herbs: only DamageKind.Rue landed while she is in stasis hurts her, " +
            "and one stasis burns at most one herb's slice. Any other hit staggers her briefly.\n\n" +
            "• herbs / herbVisuals — slices and the hat herbs that show them\n" +
            "• staggerSeconds / staggerRecoverySeconds";

        public override MoveIntent? Tick(in AgentContext context, float deltaTime) =>
            Time.time < staggeredUntil ? MoveIntent.Idle() : null;

        /// <summary>A stasis opens a window that ends at the floor of the herb she is on now.</summary>
        private void OnStatusChanged(StatusKind kind, bool started)
        {
            if (kind != StatusKind.Stasis || !started) return;

            int herbsLeftAfterThis = herbs - 1 - HerbsBurned;
            windowFloor = Mathf.Max(lastHerbPlucked ? PluckFloor : 0, Mathf.FloorToInt(herbsLeftAfterThis * SliceSize));
        }

        /// <summary>
        /// SERVER: the keepers have pulled the last herb out of her hat. The one blow that gets past
        /// every rule above, because it is the end of the fight rather than a hit in it.
        /// </summary>
        public void PluckLastHerb(Transform by)
        {
            if (!health.Alive) return;

            plucking = true;
            try
            {
                health.Damage(health.GetHealth, by, DamageKind.Rue);
            }
            finally
            {
                plucking = false;
            }
        }

        public void Filter(HealthComponent victim, ref DamageHit hit)
        {
            if (plucking) return;

            if (hit.Kind == DamageKind.Rue && InStasis)
            {
                int room = victim.GetHealth - windowFloor;
                if (hit.Amount > room) hit.Amount = Mathf.Max(0, room);
                if (hit.Amount == 0) hit.Defense = DamageDefense.Blocked;
                return;
            }

            if (hit.Amount <= 0) return;

            hit.Amount = 0;
            hit.Defense = DamageDefense.Blocked;

            if (Time.time < staggerReadyAt) return;
            staggeredUntil = Time.time + staggerSeconds;
            staggerReadyAt = staggeredUntil + staggerRecoverySeconds;
        }

        private void Update()
        {
            int burned = HerbsBurned;

            // The first frame only learns the count: a late joiner or a reload arriving with herbs
            // already gone must not see them all burn again.
            if (herbsShown >= 0 && burned > herbsShown) Burned(herbsShown, burned);
            herbsShown = burned;

            for (int i = 0; i < herbVisuals.Length; i++)
            {
                bool shown = i >= burned;
                if (herbVisuals[i] != null && herbVisuals[i].activeSelf != shown)
                    herbVisuals[i].SetActive(shown);
            }

            Jolt();
        }

        private void Jolt()
        {
            if (joltLeft <= 0f || joltPivot == null) return;

            joltLeft = Mathf.Max(0f, joltLeft - Time.deltaTime);
            float t = joltLeft / joltSeconds;
            joltPivot.localScale = joltRest * (1f + joltAmount * t * t);
        }

        /// <summary>Herbs <paramref name="from"/> … <paramref name="to"/> − 1 just burned. Every machine.</summary>
        private void Burned(int from, int to)
        {
            for (int i = from; i < to && i < herbVisuals.Length; i++)
                if (herbBurnFx != null && herbVisuals[i] != null)
                    Destroy(Instantiate(herbBurnFx, herbVisuals[i].transform.position, Quaternion.identity), 4f);

            joltLeft = joltSeconds;
            Sfx.Play(burnSound, transform.position + Vector3.up * 2f);

            Camera view = Camera.main;
            if (burnShake != null && view != null &&
                (view.transform.position - transform.position).sqrMagnitude < burnShakeRadius * burnShakeRadius)
                CameraShakerHandler.Shake(burnShake);
        }

        protected override void OnValidate()
        {
            SetMinPriority(ModulePriority.Override);
            if (herbVisuals.Length > 0 && herbVisuals.Length != herbs)
                Debug.LogWarning($"[HerbBoundLife] {name} has {herbs} herbs but {herbVisuals.Length} herb visuals.", this);
        }
    }
}
