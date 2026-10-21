// The crew's bounty: one at a time, taken from a wanted poster and handed in where it was posted (Bounties.md).
//
// The server decides, every machine presents. Where Network.Decides this takes the bounty, notes the first tip a
// resident gives, watches for the target to be caught, and runs the hand-in: a guard or elder comes to whoever brought
// it in, thanks them in the settlement's own voice, spreads the deed, and drops the reward at their feet. The hunt
// itself — its cast at the site, their spots, its clock and bounds, taking its people away — is the BountyHuntRun this
// owns. A hunt that fails (the target killed when wanted alive or fallen beneath the ground, out of time, out of
// bounds) starts
// over in place: still open, its cast stood afresh at their spots. Only a claimed hunt is over for good. Everything any
// machine needs to show it — the visor, the poster's prompt and stamp, which tip a resident says — is read from one
// BountyProgress and the list of claimed hunts, which BountyNetwork replicates and BountySaveable saves.
//
// Lives in persistentScene beside the ObjectiveDirector. Story worlds only.
using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using SpaceGame.Agents;
using SpaceGame.Agents.Residents;
using SpaceGame.Core;
using SpaceGame.Gameplay;
using SpaceGame.Gameplay.Objectives;
using SpaceGame.Presentation;
using SpaceGame.World.Posters;
using SpaceGame.World.Safety;

namespace SpaceGame.World.Bounties
{
    [DisallowMultipleComponent]
    public sealed class BountyBoard : MonoBehaviour
    {
        /// <summary>How a target behaves once the hunt is on. Handed to each <see cref="BountyTarget"/>.</summary>
        [Serializable]
        public sealed class CaptiveTuning
        {
            [Tooltip("The faction a target joins when it gives up: one that fights nobody, so it never takes up the fight again.")]
            public FactionDefinition faction;

            [Tooltip("A target gives up when knocked down, netted, held, or hurt to this share of its health or less.")]
            [Range(0f, 1f)] public float surrenderBelow = 0.25f;

            [Tooltip("A captive follows the nearest player within this, in metres; with nobody that close it waits.")]
            [Min(1f)] public float followRadius = 25f;

            [Tooltip("How close behind a player a captive walks, in metres.")]
            [Min(0.5f)] public float followDistance = 3f;

            [Tooltip("Seconds between a target's decisions.")]
            [Min(0.05f)] public float thinkSeconds = 0.5f;
        }

        /// <summary>The stamp laid across a claimed hunt's posters.</summary>
        [Serializable]
        public sealed class StampLook
        {
            public string text = "CLAIMED";
            public Color colour = new(0.62f, 0.1f, 0.08f, 0.9f);
            [Tooltip("Degrees the stamp is turned across the sheet.")]
            public float angle = 14f;
            [Tooltip("How much of the sheet's width the stamp spans.")]
            [Range(0.1f, 1f)] public float widthShare = 0.85f;
            [Tooltip("The stamp's height as a share of its width.")]
            [Range(0.1f, 1f)] public float heightShare = 0.3f;
            [Tooltip("How far in front of the sheet the stamp lies, in metres, so it never sinks into it.")]
            [Min(0f)] public float lift = 0.004f;
        }

        public static BountyBoard Instance { get; private set; }

        // The visor message saying why the hunt started over: one id, so a second failure replaces the first.
        private const string FailedMessageId = "bounty.failed";

        [SerializeField] private CaptiveTuning captive = new();

        [SerializeField] private StampLook stamp = new();

        [Header("Taking a bounty")]
        [Tooltip("How close to a poster of the bounty a player must stand for the server to let them take it, in metres.")]
        [SerializeField, Min(1f)] private float takeRange = 8f;

        [Tooltip("The hand-in radius round an owning settlement with no generated extent (a hand-built outpost), in metres.")]
        [SerializeField, Min(5f)] private float handInRadius = 60f;

        [Header("Hunting")]
        [Tooltip("The visor's search area is centred up to this far off the hideout, in metres.")]
        [SerializeField, Min(0f)] private float searchOffset = 90f;

        [Tooltip("Once the target exists and the local player is this close to it, the visor points at the target itself, in metres.")]
        [SerializeField, Min(1f)] private float revealRange = 40f;

        [Tooltip("How the hunt's people are stood at its site and kept to their spots.")]
        [SerializeField] private BountyHuntRun.CastTuning cast = new();

        [Header("Handing in")]
        [Tooltip("A player this close to the caught target inside the settlement is the one who brought it in, in metres.")]
        [SerializeField, Min(1f)] private float deliverRadius = 25f;

        [Tooltip("Who comes to take the target, by preference. With none of these living there, the nearest resident comes.")]
        [SerializeField] private ResidentArchetype[] claimantArchetypes = Array.Empty<ResidentArchetype>();

        [Tooltip("Seconds the claimant is given to walk over before the thanks are said wherever they stand.")]
        [SerializeField, Min(1f)] private float claimantPatience = 20f;

        [Tooltip("The claimant says thanks once this close to whoever brought the target in, in metres.")]
        [SerializeField, Min(1f)] private float thanksRange = 4f;

        [Tooltip("Seconds a hunt's people stay once it has been handed in, before they are taken away once nobody watches (Remains).")]
        [SerializeField, Min(0f)] private float takenAwaySeconds = 30f;

        [Header("Failing")]
        [Tooltip("Seconds of server play the cast may stand before the hunt fails and starts over.")]
        [SerializeField, Min(1f)] private float maxOpenSeconds = 3600f;

        [Tooltip("Seconds every player may stay outside the hunt's bounds (round the owning settlement) before it fails and starts over.")]
        [SerializeField, Min(1f)] private float outOfBoundsSeconds = 120f;

        [Tooltip("How far beneath the terrain surface the target may be before it counts as fallen through the world and the hunt " +
                 "starts over, in metres. Well past a body sunk in a dip or a ragdoll clipping the ground, and past the depth an " +
                 "UnderTerrainGuard lifts at, so a guarded body is rescued rather than failed.")]
        [SerializeField, Min(0.5f)] private float lostBelowGround = 2f;

        [Tooltip("With no terrain under it to measure, a target below this height has fallen out of the world.")]
        [SerializeField] private float lostBelowY = -500f;

        [Tooltip("How far under the target to look for a built floor, in metres. Standing on one (a cave, a hull's deck) it is " +
                 "supported, however far below the heightmap that floor lies.")]
        [SerializeField, Min(0f)] private float builtFloorReach = 1.7f;

        [Tooltip("Seconds between the server's decisions.")]
        [SerializeField, Min(0.05f)] private float thinkSeconds = 0.5f;

        private BountyProgress progress;
        private BountyObjective objective;
        private float nextThink;
        private readonly List<Transform> players = new();

        // Every machine: the catalog places of the hunts handed in, in the order they were.
        private readonly List<int> claimed = new();

        // Server only: the open hunt as it runs, and the people of ended or restarted hunts still to be taken away.
        private BountyHuntRun running;

        // Server only, and not saved: a hand-in under way. A load mid-walk simply starts it again.
        private Resident claimant;
        private Transform deliverer;
        private float claimDeadline = -1f;

        /// <summary>
        /// The bounty state changed on this machine. True when it happened in play — taken, a tip, caught, handed in —
        /// and false when it was restored from a save or read off the wire on joining.
        /// </summary>
        public event Action<bool> Changed;

        /// <summary>A client's local player asked to take the bounty at this catalog place; BountyNetwork carries it.</summary>
        public event Action<int> TakeRequested;

        /// <summary>The claimed hunts changed on this machine: one was handed in, or the list was restored or adopted.</summary>
        public event Action<IReadOnlyList<int>> ClaimedChanged;

        public BountyProgress Progress => progress;

        /// <summary>The catalog places of every hunt handed in. A claimed hunt is never offered again.</summary>
        public IReadOnlyList<int> Claimed => claimed;

        /// <summary>Server only: the open hunt's cast and clock, and the people of ended or restarted hunts still to be taken away.</summary>
        public BountyHuntRunState Run => Running.State;

        private BountyHuntRun Running => running ??= NewRun(null, new BountyHuntRunState());

        /// <summary>A story world: bounties exist.</summary>
        public static bool IsRunning => Instance != null && !VersusSession.IsActive;

        /// <summary>The bounty the crew have taken, or null.</summary>
        public BountyHunt Open => progress.HasOpen ? BountyCatalog.At(progress.OpenIndex) : null;

        public float SearchOffset => searchOffset;
        public float RevealRange => revealRange;
        public StampLook Stamp => stamp;
        public float MaxOpenSeconds => maxOpenSeconds;
        public float OutOfBoundsSeconds => outOfBoundsSeconds;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogError("[Bounties] A second BountyBoard was destroyed; the crew have one bounty book.", this);
                Destroy(this);
                return;
            }

            Instance = this;
            if (captive.faction == null)
                Debug.LogError("[Bounties] No captive faction assigned, so a beaten target can never give up.", this);
        }

        private void OnDestroy()
        {
            if (objective != null) QuestLog.Close(objective);
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (!IsRunning || !Network.Decides || Time.time < nextThink) return;
            nextThink = Time.time + thinkSeconds;

            Running.DespawnPending();
            if (!progress.HasOpen) return;

            SessionPlayers.Collect(players);
            Running.Tick(Time.time, progress.Town, players);
            BountyFailure failure = CurrentFailure();
            if (failure != BountyFailure.None)
            {
                StartOver(failure);
                return;
            }

            Watch();
            if (progress.Phase == BountyPhase.Caught) HandIn();
        }

        // ── Every machine ────────────────────────────────────────────────────

        /// <summary>Takes on what the server says. <paramref name="live"/>: it just happened, rather than being caught up on.</summary>
        public void Adopt(BountyProgress adopted, bool live) => Set(adopted, live);

        /// <summary>Takes on the claimed hunts the server says.</summary>
        public void AdoptClaimed(IReadOnlyList<int> adopted)
        {
            claimed.Clear();
            claimed.AddRange(adopted);
            AnnounceClaimed();
        }

        /// <summary>Whether the hunt at catalog place <paramref name="place"/> has been handed in: it is over for good.</summary>
        public bool IsClaimed(int place) => claimed.Contains(place);

        /// <summary>This machine's player asks to take <paramref name="bounty"/>.</summary>
        public void RequestTake(BountyHunt bounty)
        {
            int index = BountyCatalog.IndexOf(bounty);
            if (!IsRunning || !BountyRules.CanTake(progress, index) || IsClaimed(index)) return;

            if (Network.Decides) Take(index, GameplayMenuScope.LocalPlayerTransform);
            else TakeRequested?.Invoke(index);
        }

        /// <summary>The open bounty's target's name, for <c>{target}</c> in a resident's line; null with none open.</summary>
        public string TargetName => Open != null ? Open.TargetName : null;

        /// <summary>The tip <paramref name="speaker"/> gives about the open bounty, for <c>{tip}</c>; null with none open.</summary>
        public string TipFor(Resident speaker)
        {
            BountyHunt bounty = Open;
            if (bounty == null || speaker == null || bounty.Tips.Length == 0) return null;
            return bounty.Tip(BountyRules.TipIndex(bounty.Id, speaker.index, bounty.Tips.Length));
        }

        /// <summary>Where the target is, on this machine, once it exists here.</summary>
        public bool TryGetTargetPosition(out Vector3 position)
        {
            position = default;
            BountyTarget target = running?.Target;
            if (target != null)
            {
                position = target.transform.position;
                return true;
            }

            NetworkManager net = NetworkManager.Singleton;
            if (progress.Target == 0 || net == null || net.SpawnManager == null ||
                !net.SpawnManager.SpawnedObjects.TryGetValue(progress.Target, out NetworkObject body) || body == null)
                return false;

            position = body.transform.position;
            return true;
        }

        // ── Server ───────────────────────────────────────────────────────────

        /// <summary>
        /// Server: <paramref name="taker"/> takes the hunt at catalog place <paramref name="index"/>, if its owner offers it
        /// to them, they stand by one of its posters, it was never claimed, and the crew have none open. The owner is
        /// where it is handed in.
        /// </summary>
        public void Take(int index, Transform taker)
        {
            BountyHunt bounty = BountyCatalog.At(index);
            if (!IsRunning || !Network.Decides || bounty == null || taker == null || !BountyRules.CanTake(progress, index) ||
                IsClaimed(index)) return;

            SettlementBounties owner = SettlementBounties.Owner(bounty);
            if (owner == null || !owner.Offers(bounty, this, taker))
            {
                Debug.LogWarning($"[Bounties] {taker.name} asked for '{bounty.name}', which no loaded settlement offers them.", taker);
                return;
            }

            WantedPoster poster = BountyPosters.Nearest(bounty, taker.position, takeRange);
            if (poster == null)
            {
                Debug.LogWarning($"[Bounties] {taker.name} asked for '{bounty.name}' with no poster of it within {takeRange} m.", taker);
                return;
            }

            Settlement town = owner.Settlement;
            running = FreshRun(bounty);
            EndClaim();

            BountyProgress next = progress;
            next.Open = index + 1;
            next.Phase = BountyPhase.Asking;
            next.Town = town.transform.position;
            next.TownRadius = town.GeneratedExtent > 0f ? town.GeneratedExtent : handInRadius;
            next.Site = bounty.SitePosition;
            next.Target = 0;
            next.TargetDead = false;
            next.OutsideFor = 0f;
            Set(next, live: true);
        }

        /// <summary>Server: does <paramref name="resident"/> live where the open bounty was taken, and is it still being asked about?</summary>
        public bool AsksAbout(Resident resident) =>
            IsRunning && progress.HasOpen && progress.Phase != BountyPhase.Caught && resident != null && IsTown(resident.Settlement);

        /// <summary>Server: a resident just passed on a tip. The first one puts the search area on the visor.</summary>
        public void TipHeard()
        {
            if (!progress.HasOpen || progress.Phase != BountyPhase.Asking) return;
            BountyProgress next = progress;
            next.Phase = BountyPhase.Located;
            Set(next, live: true);
        }

        /// <summary>Server: the site of the open hunt is loaded and some of its cast is due there and not yet stood.</summary>
        public bool WantsCastAt(BountyHuntSite site) =>
            IsRunning && Network.Decides && site != null && site.Hunt != null && site.Hunt == Open && Running.WantsCast(progress.Phase);

        /// <summary>Server: stands the open hunt's due cast at <paramref name="site"/>, as ordinary saved entities.</summary>
        public void SpawnCast(BountyHuntSite site)
        {
            if (!WantsCastAt(site)) return;
            Running.SpawnCast(site, progress.Phase);
            Watch();
        }

        /// <summary>Restores the bounty book from a save. Server and offline only; clients are told by BountyNetwork.</summary>
        public void Restore(BountyHunt open, BountyPhase phase, Vector3 town, float townRadius, Vector3 site,
                            BountyHuntRunState restoredRun, IReadOnlyList<int> restoredClaimed)
        {
            claimed.Clear();
            claimed.AddRange(restoredClaimed);
            restoredRun ??= new BountyHuntRunState();

            BountyProgress restored = default;
            int openIndex = BountyCatalog.IndexOf(open);
            bool stillOpen = openIndex >= 0 && !IsClaimed(openIndex);
            if (stillOpen)
            {
                restored.Open = openIndex + 1;
                restored.Phase = phase;
                restored.Town = town;
                restored.TownRadius = townRadius;
                restored.Site = site;
                restored.OutsideFor = Mathf.Floor(restoredRun.outsideFor);
            }
            else
            {
                // A cast whose hunt is no longer open (its asset is gone from the catalog) is taken away like an ended one's.
                restoredRun.WithdrawPieces();
            }

            running = NewRun(stillOpen ? open : null, restoredRun);
            EndClaim();
            Set(restored, live: false);
            AnnounceClaimed();
        }

        private BountyHuntRun NewRun(BountyHunt open, BountyHuntRunState state) => new(open, state, cast, captive);

        /// <summary>A run with nothing stood and its clock at zero, still taking away whoever an earlier one left.</summary>
        private BountyHuntRun FreshRun(BountyHunt open) => NewRun(open, new BountyHuntRunState { pendingDespawn = Run.pendingDespawn });

        /// <summary>
        /// Why the open hunt fails at this think, or None. Nothing fails before the cast stands: there is nothing yet to
        /// start over.
        /// </summary>
        private BountyFailure CurrentFailure()
        {
            if (!Running.HasStood) return BountyFailure.None;

            BountyTarget target = Running.Target;
            bool killed = target != null && BountyRules.KilledWhenWantedAlive(Open.Wanted, target.IsDead);
            bool lost = target != null && BeneathTheWorld(target.transform);
            return BountyRules.FailureOf(Run.elapsed, Run.outsideFor, maxOpenSeconds, outOfBoundsSeconds, killed, lost);
        }

        /// <summary>
        /// Whether <paramref name="body"/> has fallen through the world, by the under-terrain guard's own rule: deeper than
        /// <c>lostBelowGround</c> under the measured terrain, or below <c>lostBelowY</c> with none to measure. A body that
        /// is carried or riding, or stands on a built floor, is below the heightmap on purpose and is never lost.
        /// </summary>
        private bool BeneathTheWorld(Transform body)
        {
            if (body.parent != null || CarriedBody.IsHeld(body.gameObject)) return false;

            Vector3 at = body.position;
            bool hasTerrain = TerrainProbe.TryGetTerrainHeight(at, out float terrainY);
            UnderTerrainVerdict verdict = new UnderTerrainRule(lostBelowGround, 0f, lostBelowY).Evaluate(at.y, hasTerrain, terrainY);
            return verdict.Action != UnderTerrainAction.None && !SpawnClearance.StandsOnStructure(at, builtFloorReach);
        }

        /// <summary>
        /// Server: the open hunt failed, so it starts over where it stands. Its whole cast goes at once (the dead and the
        /// caught too; anyone not loaded goes as they load), and the site stands them afresh at their spots: the same
        /// people, as the same world deals them. It stays open: the tip the crew heard is kept, a caught target is lost,
        /// and the clock starts again.
        /// </summary>
        private void StartOver(BountyFailure failure)
        {
            Run.WithdrawPieces();
            running = FreshRun(Open);
            Running.DespawnPending();
            EndClaim();

            BountyProgress next = progress;
            if (next.Phase == BountyPhase.Caught) next.Phase = BountyPhase.Located;
            next.Target = 0;
            next.TargetDead = false;
            next.OutsideFor = 0f;
            next.Resets = unchecked((byte)(next.Resets + 1));
            next.Failure = failure;
            Set(next, live: true);
        }

        /// <summary>
        /// What every machine is shown about the open hunt: where the target is, whether it is dead or caught, and how
        /// long the crew have been out of bounds, in whole seconds so the wire carries one change a second at most.
        /// </summary>
        private void Watch()
        {
            BountyProgress next = progress;
            next.OutsideFor = Mathf.Floor(Run.outsideFor);

            bool caught = false;
            BountyTarget target = Running.Target;
            if (target != null)
            {
                next.Target = target.TryGetComponent(out NetworkObject net) && net.IsSpawned ? net.NetworkObjectId : 0;
                next.TargetDead = target.IsDead;
                caught = target.IsCaught && progress.Phase != BountyPhase.Caught;
                if (caught) next.Phase = BountyPhase.Caught;
            }

            Set(next, live: caught);
        }

        /// <summary>
        /// The caught target is inside the settlement with a player beside it: the claimant walks over, and once there
        /// (or out of patience) says thanks, the deed is told and the reward lies at their feet.
        /// </summary>
        private void HandIn()
        {
            BountyTarget target = Running.Target;
            if (target == null) return;

            if (claimDeadline < 0f)
            {
                Vector3 at = target.transform.position;
                if (!BountyRules.FlatWithin(at, progress.Town, progress.TownRadius)) return;

                deliverer = BountyRules.NearestWithin(players, at, deliverRadius);
                if (deliverer == null) return;

                claimant = PickClaimant(at);
                claimDeadline = Time.time + claimantPatience;
                if (claimant != null) claimant.ComeOnstage();
            }

            bool ready = claimant == null || claimant.IsDead || deliverer == null || Time.time >= claimDeadline ||
                         (claimant.transform.position - deliverer.position).sqrMagnitude <= thanksRange * thanksRange;
            if (!ready)
            {
                claimant.SetOverride(OverrideKind.Scripted, deliverer.position, claimDeadline - Time.time);
                return;
            }

            Finish(target);
        }

        private void Finish(BountyTarget target)
        {
            BountyHunt bounty = Open;
            bool dead = target.IsDead;
            Transform payee = claimant != null && !claimant.IsDead ? claimant.transform : deliverer;

            if (claimant != null && !claimant.IsDead && deliverer != null)
            {
                if (claimant.TryGetComponent(out ResidentVoice voice))
                    voice.Say(Topic.Remark, deliverer, dead ? Observation.BroughtInDead : Observation.BroughtInAlive);
                Gossip.Witness(claimant, deliverer, dead ? ActKind.BroughtInDead : ActKind.BroughtInAlive, ResidentTuning.Instance.noticeRadius);
            }

            if (payee != null) Pay(bounty, dead, payee);

            Settlement town = FindTown();
            Running.TakeAway(town != null ? town.WalkableHeart : progress.Town, takenAwaySeconds);
            Close();
        }

        /// <summary>Ends the open hunt for good, handed in: it joins the claimed list and the crew have none open.</summary>
        private void Close()
        {
            claimed.Add(progress.OpenIndex);
            running = FreshRun(null);
            EndClaim();
            Set(default, live: true);
            AnnounceClaimed();
        }

        private static void Pay(BountyHunt bounty, bool dead, Transform payee)
        {
            bool asWanted = BountyRules.BroughtAsWanted(bounty.Wanted, dead);
            foreach (BountyHunt.Reward reward in bounty.Rewards)
            {
                if (reward.item == null) continue;
                int count = BountyRules.Payout(reward.count, asWanted, bounty.OtherStateShare);
                for (int i = 0; i < count; i++) GameServices.ItemDropService.DropItem(payee, reward.item);
            }
        }

        private Resident PickClaimant(Vector3 near)
        {
            SettlementSociety society = FindTown()?.Society;
            if (society == null) return null;

            Resident preferred = null, anyone = null;
            float preferredDistance = float.MaxValue, anyDistance = float.MaxValue;
            foreach (Resident r in society.Residents)
            {
                if (r == null || r.IsDead || r.IsAway) continue;
                float d = (r.transform.position - near).sqrMagnitude;
                if (d < anyDistance) { anyDistance = d; anyone = r; }
                if (d < preferredDistance && Array.IndexOf(claimantArchetypes, r.archetype) >= 0) { preferredDistance = d; preferred = r; }
            }
            return preferred != null ? preferred : anyone;
        }

        private void AnnounceClaimed()
        {
            BountyPosters.RestampAll();
            ClaimedChanged?.Invoke(claimed);
        }

        private void EndClaim()
        {
            claimant = null;
            deliverer = null;
            claimDeadline = -1f;
        }

        /// <summary>Whether <paramref name="settlement"/> owns the open hunt: never merely the nearest one, which may be an Outpost.</summary>
        private bool IsTown(Settlement settlement)
        {
            BountyHunt open = Open;
            return settlement != null && open != null && !string.IsNullOrEmpty(open.OwnerSettlementId) &&
                   settlement.SettlementId == open.OwnerSettlementId;
        }

        private Settlement FindTown()
        {
            foreach (Settlement settlement in FindObjectsByType<Settlement>(FindObjectsSortMode.None))
                if (IsTown(settlement)) return settlement;
            return null;
        }

        private void Set(BountyProgress next, bool live)
        {
            if (next.Equals(progress)) return;

            // The out-of-bounds count ticking is not news: it must not re-announce the hunt every second it runs.
            if (live && OnlyOutsideForMoved(progress, next)) live = false;

            BountyProgress was = progress;
            progress = next;
            if (progress.Open != was.Open) Retrack(live);
            else if (live && objective != null)
            {
                if (progress.Resets != was.Resets)
                    SystemMessages.Post(FailedMessageId, objective.FailedLine(progress.Failure), MessageSeverity.Notice);
                QuestLog.Open(objective, acquired: true);
            }
            Changed?.Invoke(live);
        }

        private static bool OnlyOutsideForMoved(BountyProgress was, BountyProgress next)
        {
            was.OutsideFor = next.OutsideFor;
            return was.Equals(next);
        }

        /// <summary>
        /// A new open bounty is a new quest, so it is announced as one. <paramref name="live"/>: taken just now, in play,
        /// so it becomes the active quest; restored from a save or learned on joining, it is listed quietly.
        /// </summary>
        private void Retrack(bool live)
        {
            if (objective != null) QuestLog.Close(objective);
            objective = progress.HasOpen ? new BountyObjective(this, Open) : null;
            if (objective != null) QuestLog.Open(objective, acquired: live);
        }
    }
}
