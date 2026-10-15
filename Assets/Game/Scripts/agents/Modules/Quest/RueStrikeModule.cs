// A sprite-hamlet keeper's part in the witch fight: it holds a post at the edge of the clearing and
// lobs little glass bombs of rue at her, over and over, ducking whenever she spins.
//
// The bombs are slow and aimed at where she stood when they left the keeper's hand, so a free witch
// is never where they land -- the keepers are visibly trying and missing, and that is the player's
// cue that their job is to hold her still (GDC-L1-NARR-0001). One that lands on her while she is in
// stasis burns a herb; how much a stasis can burn is HerbBoundLife's rule, not this one's.
//
// The post moves with the player's hands. A keeper set down somewhere new throws from there -- the
// player who carries a keeper out of a spiral is also choosing where it fights from. A reset sends it
// back to the post the clearing gives it.
//
// Override priority: during the fight this beats following the player, which is what keeps the
// keepers from trailing after them like ducklings. A stun (Scripted) stops it. Held in someone's arms,
// fallen to the poison or leaping up the witch's hat, it stands still and throws nothing. Outside the
// fight it passes, and the keeper goes back to following.
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Gameplay;
using SpaceGame.Gameplay.Objectives;
using SpaceGame.Gameplay.SpriteHamlet;

namespace SpaceGame.Agents
{
    public class RueStrikeModule : BehaviourModuleBase
    {
        [Tooltip("The QuestActor id of whom the rue is for.")]
        [SerializeField] private string targetActorId = "witch-morwen";

        [Tooltip("How far the clearing's WitchArena may be from the witch, metres.")]
        [SerializeField, Min(1f)] private float arenaSearchRadius = 60f;

        [Tooltip("Close enough to its post to stop walking and start throwing, metres.")]
        [SerializeField, Min(0.2f)] private float postTolerance = 1.2f;

        [Header("Throwing")]
        [SerializeField] private RueBomb bombPrefab;

        [Tooltip("A witch further than this is out of throwing range, metres.")]
        [SerializeField, Min(1f)] private float throwRange = 24f;

        [Tooltip("Seconds between throws.")]
        [SerializeField, Min(0.1f)] private float throwInterval = 2.4f;

        [Tooltip("Up to this many seconds added to each wait, so three keepers do not throw in step.")]
        [SerializeField, Min(0f)] private float throwJitter = 0.8f;

        [Tooltip("Flight time of a bomb: this much plus flightPerMetre for every metre it travels.")]
        [SerializeField, Min(0.05f)] private float flightBase = 0.45f;

        [SerializeField, Min(0f)] private float flightPerMetre = 0.05f;

        [Tooltip("Height above its feet the bomb leaves the keeper's hand, metres.")]
        [SerializeField, Min(0f)] private float handHeight = 0.7f;

        [Header("Ducking")]
        [Tooltip("A spinning witch within this many metres sends the keeper to the ground.")]
        [SerializeField, Min(1f)] private float duckRadius = 40f;

        private QuestActor self;
        private KeeperCarry carry;
        private PoisonTally tally;

        private bool hasPost;
        private Vector3 post;
        private float nextThrowAt;

        /// <summary>Every machine: a bomb just left this keeper's hand.</summary>
        public event System.Action Threw;

        private void Reset() => SetPriorityDefault(ModulePriority.Override);

        private void Awake()
        {
            self = GetComponent<QuestActor>();
            carry = GetComponent<KeeperCarry>();
            tally = GetComponent<PoisonTally>();
        }

        private void OnEnable()
        {
            this.NetOn(NetMsg.RueBombThrown, OnThrownElsewhere);
            if (carry != null) carry.SetDownAt += TakePost;
            hasPost = false;
            nextThrowAt = Time.time + Random.Range(0f, throwJitter);
        }

        private void OnDisable()
        {
            this.NetOff(NetMsg.RueBombThrown, OnThrownElsewhere);
            if (carry != null) carry.SetDownAt -= TakePost;
        }

        public override string ModuleDescription =>
            "During the witch fight: holds a post at the edge of the clearing (its WitchArena entry, or " +
            "wherever it was last set down) and lobs a RueBomb at the witch every throwInterval. Ducks " +
            "while she spins; still while carried, fallen or leaping. Passes outside the fight.\n\n" +
            "• targetActorId — the QuestActor id it throws at\n" +
            "• throwRange / throwInterval / throwJitter / flightBase / flightPerMetre";

        /// <summary>SERVER: go back to the post the clearing gives it — the fight is starting over.</summary>
        public void ReturnToPost() => hasPost = false;

        private void TakePost(Vector3 at)
        {
            post = at;
            hasPost = true;
        }

        public override MoveIntent? Tick(in AgentContext context, float deltaTime)
        {
            if (!QuestActor.TryFind(targetActorId, out QuestActor witch)) return null;

            HealthComponent witchHealth = witch.GetComponent<HealthComponent>();
            if (witchHealth != null && !witchHealth.Alive) return null;

            QuestHoldModule hold = witch.GetComponent<QuestHoldModule>();
            if (hold != null && hold.IsHolding) return null;

            if ((tally != null && tally.Fallen) || (carry != null && (carry.IsCarried || carry.IsPerched)))
                return MoveIntent.Idle();

            Vector3 at = witch.transform.position;

            if (PoisonSpiralModule.SpinningNear(context.Position, duckRadius))
                return MoveIntent.StopAndFace(at);

            if (!hasPost) FindPost(at, context.Position);

            Vector3 toPost = post - context.Position;
            toPost.y = 0f;
            if (toPost.magnitude > postTolerance)
                return MoveIntent.MoveTo(post, postTolerance * 0.5f, 1f, isRunning: true);

            if (Time.time >= nextThrowAt && (at - context.Position).sqrMagnitude <= throwRange * throwRange)
                Throw(at, context.Position);

            return MoveIntent.StopAndFace(at);
        }

        /// <summary>Its entry in the clearing's WitchArena, or where it stands when the clearing has none for it.</summary>
        private void FindPost(Vector3 witchAt, Vector3 standing)
        {
            hasPost = true;
            post = standing;

            if (self != null && WitchArena.TryFindNear(witchAt, arenaSearchRadius, out WitchArena arena))
                arena.TryGetPost(self.ActorId, out post);
        }

        private void Throw(Vector3 at, Vector3 from)
        {
            nextThrowAt = Time.time + throwInterval + Random.Range(0f, throwJitter);

            float seconds = flightBase + flightPerMetre * Vector3.Distance(from, at);
            this.NetToAll(NetMsg.RueBombThrown, new NetArg { P = at, A = Mathf.RoundToInt(seconds * 1000f) });
        }

        /// <summary>The throw, on every machine; only the server's bomb bills anything.</summary>
        private void OnThrownElsewhere(in NetArg arg, ulong sender)
        {
            Threw?.Invoke();
            if (bombPrefab == null) return;

            QuestActor.TryFind(targetActorId, out QuestActor witch);
            RueBomb bomb = Instantiate(bombPrefab, transform.position + Vector3.up * handHeight, Quaternion.identity);
            bomb.Launch(arg.P, arg.A / 1000f, transform, witch != null ? witch.gameObject : null, !Network.Simulates(this));
        }

        protected override void OnValidate()
        {
            SetMinPriority(ModulePriority.Override);
        }
    }
}
