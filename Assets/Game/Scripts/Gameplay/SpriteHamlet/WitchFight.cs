using System.Collections;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Characters;
using SpaceGame.Core;
using SpaceGame.Diagnostics;
using SpaceGame.Gameplay.Objectives;
using SpaceGame.Gameplay.Status;
using SpaceGame.Presentation;

namespace SpaceGame.Gameplay.SpriteHamlet
{
    /// <summary>
    /// The two ends of the witch fight, on the witch: losing it, and winning it.
    ///
    /// <para>
    /// <b>Lost</b> the moment any keeper falls to her poison (<see cref="PoisonTally.Fell"/>). Every
    /// player near shows the failure screen, and under its blackout the server puts the whole fight
    /// back as it began: every keeper set down, cleared of poison and put on its post, the witch
    /// healed of every herb, thawed and returned to her door. Nobody dies and nothing is saved about
    /// it — the quest step simply has not been met yet (GDC-L1-DESIGN-0002: a clear, fair fail state
    /// for a fight whose whole point is protecting the keepers).
    /// </para>
    /// <para>
    /// <b>Won</b> when rue has taken all it may (<see cref="HerbBoundLife.IsBroken"/>): she is held,
    /// every player near watches the finishing cutscene, and the keepers leap up onto her hat and pull
    /// the last herb out by hand (<see cref="HerbBoundLife.PluckLastHerb"/>) — the ending the fight
    /// has been building towards, delivered by the characters it was about (GDC-L1-NARR-0001).
    /// </para>
    /// <para>
    /// Scripted priority: while it resets or finishes she stands still, and her side-effect modules
    /// (the blink) are silenced, as <see cref="QuestHoldModule"/> does for the monologue.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(HerbBoundLife))]
    public class WitchFight : BehaviourModuleBase
    {
        [Tooltip("The keepers' QuestActor ids. A fall by any of them loses the fight; all of them go up her hat.")]
        [SerializeField] private string[] keeperIds = { "keeper-pip", "keeper-bramble", "keeper-wisteria" };

        [Tooltip("How far the clearing's WitchArena may be from her, metres.")]
        [SerializeField, Min(1f)] private float arenaSearchRadius = 60f;

        [Tooltip("Only players this close see the failure screen and the finish, metres.")]
        [SerializeField, Min(1f)] private float audienceRange = 80f;

        [Tooltip("Side-effect modules silenced while she is reset or finished — priority does not stop them.")]
        [SerializeField] private BehaviourModuleBase[] silenced = System.Array.Empty<BehaviourModuleBase>();

        [Header("Lost")]
        [SerializeField] private QuestFailedCutscene failedCutscene;

        [Tooltip("{0} is the fallen keeper's name.")]
        [SerializeField] private string failedReasonFormat = "{0} breathed the witch's poison";

        [Tooltip("Seconds from the fall to the reset — the screen must be black by then.")]
        [SerializeField, Min(0f)] private float resetAfter = 1.4f;

        [Tooltip("Seconds she stands at her door after the reset before the fight resumes.")]
        [SerializeField, Min(0f)] private float graceAfterReset = 3f;

        [Header("Won")]
        [SerializeField] private Cutscene finisherCutscene;

        [Tooltip("The bone the keepers cling to.")]
        [SerializeField] private HumanBodyBones perchBone = HumanBodyBones.Head;

        [Tooltip("Where each keeper clings, in the bone's space: round the crown of her hat.")]
        [SerializeField] private Vector3[] perchOffsets =
        {
            new(0f, 0.35f, 0.05f), new(-0.18f, 0.25f, -0.05f), new(0.18f, 0.25f, -0.05f),
        };

        [Tooltip("Seconds from the start of the finish to the first keeper leaving the ground.")]
        [SerializeField, Min(0f)] private float leapDelay = 0.8f;

        [Tooltip("Seconds between one keeper leaping and the next.")]
        [SerializeField, Min(0f)] private float leapStagger = 0.25f;

        [Tooltip("Seconds a leap takes.")]
        [SerializeField, Min(0.05f)] private float leapSeconds = 0.7f;

        [Tooltip("Seconds they rummage in her hat before the last herb comes out.")]
        [SerializeField, Min(0f)] private float pluckSeconds = 2f;

        private HerbBoundLife herbs;
        private HealthComponent health;
        private StatusReceiver receiver;
        private QuestHoldModule hold;
        private PoisonSpiralModule spiral;
        private Animator animator;

        private bool resetting;
        private bool finishing;
        private bool silencedNow;

        /// <summary>Every machine: the finish is under way.</summary>
        public bool IsFinishing => finishing;

        private void Reset() => SetPriorityDefault(ModulePriority.Scripted);

        private void Awake()
        {
            herbs = GetComponent<HerbBoundLife>();
            health = GetComponent<HealthComponent>();
            hold = GetComponent<QuestHoldModule>();
            spiral = GetComponent<PoisonSpiralModule>();
            animator = GetComponentInChildren<Animator>(true);
        }

        private void OnEnable()
        {
            receiver = StatusReceiver.Ensure(gameObject);
            PoisonTally.Fell += OnKeeperFell;
            this.NetOn(NetMsg.WitchFightFailed, OnFailedElsewhere);
            this.NetOn(NetMsg.WitchFinisher, OnFinisherElsewhere);
        }

        private void OnDisable()
        {
            PoisonTally.Fell -= OnKeeperFell;
            this.NetOff(NetMsg.WitchFightFailed, OnFailedElsewhere);
            this.NetOff(NetMsg.WitchFinisher, OnFinisherElsewhere);
            Silence(false);
        }

        public override string ModuleDescription =>
            "The ends of the witch fight. A keeper falling to poison shows the failure screen and " +
            "resets the fight under it; rue breaking her starts the finish, where the keepers pluck " +
            "the last herb. Holds her still through both.\n\n" +
            "• keeperIds — the keepers whose fall loses it and who climb her hat\n" +
            "• resetAfter / graceAfterReset / leapDelay / leapSeconds / pluckSeconds";

        public override MoveIntent? Tick(in AgentContext context, float deltaTime)
        {
            if (!finishing && !resetting && herbs.IsBroken && Network.Simulates(this))
                this.NetToAll(NetMsg.WitchFinisher);

            bool holding = resetting || finishing;
            Silence(holding);
            return holding ? MoveIntent.Idle() : null;
        }

        private void Silence(bool value)
        {
            if (silencedNow == value) return;
            silencedNow = value;
            foreach (BehaviourModuleBase module in silenced)
                if (module != null) module.Active = !value;
        }

        // ─────────── Lost ───────────

        private bool FightIsOn =>
            health.Alive && !finishing && !resetting && (hold == null || !hold.IsHolding);

        private void OnKeeperFell(PoisonTally keeper)
        {
            if (!Network.Simulates(this) || !FightIsOn) return;

            int index = KeeperIndex(keeper.gameObject);
            if (index < 0) return;

            this.NetToAll(NetMsg.WitchFightFailed, new NetArg { A = index });
            StartCoroutine(Fault.Coroutine(this, nameof(ResetUnderBlackout), ResetUnderBlackout(), () => resetting = false));
        }

        private void OnFailedElsewhere(in NetArg arg, ulong sender)
        {
            if (spiral != null) spiral.Interrupt();

            if (failedCutscene == null || !AudienceIsNear(out PlayerController player)) return;

            string name = KeeperName(arg.A);
            failedCutscene.Reason = string.Format(failedReasonFormat, name);
            CutsceneDirector.Instance.Play(failedCutscene, player.gameObject);
        }

        /// <summary>SERVER: put the fight back as it began, while nobody can see it happen.</summary>
        private IEnumerator ResetUnderBlackout()
        {
            resetting = true;
            yield return new WaitForSeconds(resetAfter);

            KeeperCarry.SetDownEverywhere();
            DestroyLoose<PoisonBall>();
            DestroyLoose<RueBomb>();

            receiver.ClearAll();
            health.ResetToFull();

            WitchArena.TryFindNear(transform.position, arenaSearchRadius, out WitchArena arena);
            if (arena != null)
                NetworkedTeleport.Move(gameObject, arena.WitchHome.position, arena.WitchHome.rotation);

            foreach (string id in keeperIds)
            {
                if (!QuestActor.TryFind(id, out QuestActor keeper)) continue;

                keeper.GetComponent<PoisonTally>()?.Clear();
                keeper.GetComponent<RueStrikeModule>()?.ReturnToPost();
                if (arena != null && arena.TryGetPost(id, out Vector3 post))
                    NetworkedTeleport.Move(keeper.gameObject, post, keeper.transform.rotation);
            }

            yield return new WaitForSeconds(graceAfterReset);
            resetting = false;
        }

        /// <summary>
        /// Every machine's copies of anything still in flight — a ball thrown before the fall must not
        /// reach a keeper who has just been given its breath back.
        /// </summary>
        private static void DestroyLoose<T>() where T : Component
        {
            foreach (T loose in FindObjectsByType<T>(FindObjectsSortMode.None))
                Destroy(loose.gameObject);
        }

        // ─────────── Won ───────────

        private void OnFinisherElsewhere(in NetArg arg, ulong sender)
        {
            if (finishing) return;
            finishing = true;

            if (spiral != null) spiral.Interrupt();
            if (Network.Simulates(this)) StartCoroutine(Fault.Coroutine(this, nameof(Finish), Finish()));

            if (finisherCutscene != null && AudienceIsNear(out PlayerController player))
                CutsceneDirector.Instance.Play(finisherCutscene, player.gameObject);
        }

        /// <summary>SERVER: the keepers go up her hat, rummage, and pull the last herb out.</summary>
        private IEnumerator Finish()
        {
            DestroyLoose<PoisonBall>();
            KeeperCarry.SetDownEverywhere();

            Transform perch = animator != null && animator.isHuman ? animator.GetBoneTransform(perchBone) : null;
            if (perch == null) perch = transform;

            yield return new WaitForSeconds(leapDelay);

            KeeperCarry first = null;
            for (int i = 0; i < keeperIds.Length; i++)
            {
                if (!QuestActor.TryFind(keeperIds[i], out QuestActor keeper)) continue;

                KeeperCarry carry = keeper.GetComponent<KeeperCarry>();
                if (carry == null) continue;

                first ??= carry;
                carry.Perch(perch, perchOffsets.Length > 0 ? perchOffsets[i % perchOffsets.Length] : Vector3.up, leapSeconds);
                yield return new WaitForSeconds(leapStagger);
            }

            yield return new WaitForSeconds(leapSeconds + pluckSeconds);

            herbs.PluckLastHerb(first != null ? first.transform : null);

            foreach (string id in keeperIds)
                if (QuestActor.TryFind(id, out QuestActor keeper))
                    keeper.GetComponent<KeeperCarry>()?.Unperch();
        }

        // ─────────── Shared ───────────

        private bool AudienceIsNear(out PlayerController player)
        {
            player = GameplayMenuScope.FindLocalPlayer();
            return player != null && CutsceneDirector.Instance != null &&
                   (player.transform.position - transform.position).sqrMagnitude <= audienceRange * audienceRange;
        }

        private int KeeperIndex(GameObject keeper)
        {
            for (int i = 0; i < keeperIds.Length; i++)
                if (QuestActor.TryFind(keeperIds[i], out QuestActor actor) && actor.gameObject == keeper)
                    return i;
            return -1;
        }

        private string KeeperName(int index)
        {
            if (index < 0 || index >= keeperIds.Length) return string.Empty;
            return QuestActor.TryFind(keeperIds[index], out QuestActor actor) && actor.GetComponent<KeeperCarry>() is { } carry
                ? carry.KeeperName
                : keeperIds[index];
        }

        protected override void OnValidate() => SetMinPriority(ModulePriority.Scripted);
    }
}
