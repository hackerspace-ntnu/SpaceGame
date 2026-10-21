// The open hunt as the server runs it (Bounties.md): stands its cast at the site, keeps each of them to their spot,
// counts its clock and how long the crew have been out of its bounds, and takes its people away when it ends. The
// BountyBoard owns one, built afresh whenever a hunt is taken, starts over, ends or is restored; the board keeps the taking, the
// tips, the hand-in and what every machine is told.
//
// What is worth keeping lives in BountyHuntRunState, which BountySaveable saves: the cast by identity (a body
// outlives its chunk, so it is found again by SaveRef as its chunk streams in), the clock, and the people of ended
// or restarted hunts still waiting to be taken away. What is held here besides is re-derived: which stop a patrol walks to next,
// when a wanderer moves on.
//
// Server only: a SaveRef resolves only on the server.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using SpaceGame.Agents;
using SpaceGame.Core;
using SpaceGame.Core.Persistence;
using SpaceGame.Gameplay;
using SpaceGame.Gameplay.Ragdoll;
using SpaceGame.Persistence;

namespace SpaceGame.World.Bounties
{
    public sealed class BountyHuntRun
    {
        /// <summary>How the cast is stood at its site and kept to its spots. Set on the BountyBoard.</summary>
        [Serializable]
        public sealed class CastTuning
        {
            [Tooltip("How far from a spot the NavMesh may be found to stand someone on, in metres.")]
            [Min(0.5f)] public float navSampleDistance = 4f;

            [Tooltip("People beyond the spots of their tag stand within this of the last one (of the site, with none), in metres.")]
            [Min(0f)] public float scatterRadius = 5f;

            [Tooltip("How close to a post counts as standing on it, in metres.")]
            [Min(0.2f)] public float postArriveRadius = 1f;

            [Tooltip("How close to a patrol stop or a wander point counts as reached, in metres.")]
            [Min(0.5f)] public float walkArriveRadius = 2f;

            [Tooltip("Seconds a wanderer stands where it got to before it moves on.")]
            [Min(0f)] public float wanderPauseSeconds = 6f;
        }

        // A goal's reason is how the run tells its own goal from one an errand left. A fight never writes the goal: it
        // only outranks it, so the goal is still there to walk back to once the fight is over.
        private const string PostReason = "bounty post";
        private const string PatrolReason = "bounty patrol";
        private const string WanderReason = "bounty wander";

        // A post's facing point lies this far ahead of the spot. Only its direction matters.
        private const float FacingReach = 5f;

        /// <summary>One of the cast as found in the world this session, beside its saved piece.</summary>
        private sealed class Live
        {
            public GameObject body;
            public AgentGoal goal;
            public AgentTargeting targeting;
            public HealthComponent health;
            public RagdollController ragdoll;
            public int patrolStop = -1;
            public float wanderAt = -1f;

            public void Bind(GameObject found)
            {
                body = found;
                goal = found.GetComponent<AgentGoal>();
                targeting = found.GetComponent<AgentTargeting>();
                health = found.GetComponent<HealthComponent>();
                ragdoll = found.GetComponent<RagdollController>();
                patrolStop = -1;
                wanderAt = -1f;
            }

            /// <summary>Alive, on its feet, unheld and not fighting: free to keep to its spot.</summary>
            public bool Free =>
                goal != null && (health == null || health.Alive) && (ragdoll == null || !ragdoll.IsHeldOrDown) &&
                (targeting == null || !targeting.HasTarget);
        }

        private readonly BountyHunt hunt;
        private readonly BountyHuntRunState state;
        private readonly CastTuning tuning;
        private readonly BountyBoard.CaptiveTuning captive;
        private readonly List<Live> live = new();
        private readonly List<BountyHuntSpot> spots = new();
        private readonly Dictionary<(FactionRoster roster, RosterRole role), int> dealt = new();
        private readonly Dictionary<string, int> spotsTaken = new();

        private float lastTick = -1f;

        /// <param name="open">The open hunt, or null with none open (the run then only takes away what is pending).</param>
        public BountyHuntRun(BountyHunt open, BountyHuntRunState state, CastTuning tuning, BountyBoard.CaptiveTuning captive)
        {
            hunt = open;
            this.state = state;
            this.tuning = tuning;
            this.captive = captive;
            for (int i = 0; i < state.pieces.Count; i++) live.Add(new Live());
        }

        public BountyHuntRunState State => state;

        /// <summary>The target, once it is in the world here: spawned, or found again after a load.</summary>
        public BountyTarget Target { get; private set; }

        /// <summary>Whether the cast has stood at the site: before that a hunt has nothing to lose, so its clock waits.</summary>
        public bool HasStood => state.spawned;

        /// <summary>Whether some of the cast is due at the site and not yet stood there.</summary>
        public bool WantsCast(BountyPhase phase) => hunt != null && (!state.spawned || TipCastDue(phase));

        /// <summary>
        /// Stands everyone due at <paramref name="site"/>: the OnTaken entries the first time, the OnFirstTip entries once
        /// a tip has been heard. Each entry's count and roster draw come from the world and the hunt, so a second wave,
        /// or the same hunt reached again in the same world, deals the same people.
        /// </summary>
        public void SpawnCast(BountyHuntSite site, BountyPhase phase)
        {
            bool onTaken = !state.spawned;
            bool onTip = phase >= BountyPhase.Located && !state.tipCastSpawned;

            // Before the first body, so a failure part-way never stands anyone twice.
            state.spawned = true;
            state.tipCastSpawned |= onTip;

            int seed = BountyRules.CastSeed(WorldSession.WorldId, hunt.Id);
            dealt.Clear();
            spotsTaken.Clear();

            // Every entry is walked, due or not, so the roster decks and the spots are dealt in the same order in both
            // waves: a later wave takes the places the earlier one left.
            BountyHunt.CastEntry[] cast = hunt.Cast;
            for (int entryIndex = 0; entryIndex < cast.Length; entryIndex++)
            {
                BountyHunt.CastEntry entry = cast[entryIndex];
                if (entry == null) continue;

                bool due = entry.spawnWhen == HuntSpawnWhen.OnTaken ? onTaken : onTip;
                string problem = Problem(entry, entryIndex);
                if (problem != null)
                {
                    if (due) Debug.LogError($"[Bounties] '{hunt.name}': {problem}", hunt);
                    continue;
                }

                string tag = entry.spotTag ?? string.Empty;
                site.SpotsTagged(tag, spots);
                int count = entry.who == CastWho.Target ? 1 : BountyRules.CastCount(seed, entryIndex, entry.minCount, entry.maxCount);
                for (int i = 0; i < count; i++)
                {
                    GameObject prefab = Draw(entry, seed, due);
                    spotsTaken.TryGetValue(tag, out int place);
                    spotsTaken[tag] = place + 1;
                    if (due && prefab != null) Stand(site, entry, prefab, tag, place);
                }
            }
        }

        /// <summary>
        /// One server step once the cast stands: the clock, the bounds round <paramref name="town"/>, finding the cast
        /// again as its chunks stream in, and keeping each of them to their spot.
        /// </summary>
        public void Tick(float now, Vector3 town, IReadOnlyList<Transform> players)
        {
            float delta = lastTick < 0f ? 0f : now - lastTick;
            lastTick = now;
            if (hunt == null || !HasStood) return;

            state.elapsed += delta;
            state.outsideFor = BountyRules.AllOutside(players, town, hunt.BoundsRadius) ? state.outsideFor + delta : 0f;
            KeepToSpots(now);
        }

        /// <summary>
        /// The hunt was handed in: the target is let go (led to <paramref name="targetLedTo"/> if it lives), everyone
        /// else here starts the usual countdown to being taken away, and anyone whose chunk is not loaded is taken away
        /// the moment it is.
        /// </summary>
        public void TakeAway(Vector3 targetLedTo, float takenAwaySeconds)
        {
            for (int i = 0; i < state.pieces.Count; i++)
            {
                BountyHuntRunState.HuntPiece piece = state.pieces[i];
                if (!TryFind(i, out GameObject body))
                {
                    if (piece.body.IsSet) state.pendingDespawn.Add(piece.body);
                    continue;
                }

                if (piece.target) BountyTarget.Attach(body, captive).Release(targetLedTo, takenAwaySeconds);
                else Remains.On(body).BeginUnlessCounting(takenAwaySeconds);
            }

            Target = null;
        }

        /// <summary>
        /// Takes away the people of ended or restarted hunts as their chunks stream in, and forgets those already gone from the
        /// world. Runs whether or not a hunt is open: they outlive the hunt that stood them.
        /// </summary>
        public void DespawnPending()
        {
            List<SaveRef> pending = state.pendingDespawn;
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                if (pending[i].TryResolve(out GameObject body) && body != null) GameServices.World.Despawn(body);
                else if (StillInWorld(pending[i])) continue;
                pending.RemoveAt(i);
            }
        }

        private bool TipCastDue(BountyPhase phase)
        {
            if (phase < BountyPhase.Located || state.tipCastSpawned) return false;
            foreach (BountyHunt.CastEntry entry in hunt.Cast)
                if (entry != null && entry.spawnWhen == HuntSpawnWhen.OnFirstTip) return true;
            return false;
        }

        /// <summary>Why <paramref name="entry"/> cannot be stood, or null when it can.</summary>
        private string Problem(BountyHunt.CastEntry entry, int entryIndex)
        {
            switch (entry.who)
            {
                case CastWho.Target:
                    return hunt.Target == null ? "it has no target: its poster prints no agent." : null;
                case CastWho.Prefab:
                    return entry.prefab == null ? $"cast entry {entryIndex} names no prefab." : null;
                case CastWho.RosterRole:
                    if (entry.roster == null) return $"cast entry {entryIndex} names no roster.";
                    return entry.role == RosterRole.Rider
                        ? $"cast entry {entryIndex} asks for a Rider, which a roster deals as a mount, not a person. Use another role."
                        : null;
                default:
                    return $"cast entry {entryIndex} has an unknown kind '{entry.who}'.";
            }
        }

        /// <summary>The prefab for the next of <paramref name="entry"/>. Always takes its card off the roster deck, due or not.</summary>
        private GameObject Draw(BountyHunt.CastEntry entry, int seed, bool due)
        {
            switch (entry.who)
            {
                case CastWho.Target:
                    return hunt.Target;
                case CastWho.Prefab:
                    return entry.prefab;
                default:
                    var deck = (entry.roster, entry.role);
                    dealt.TryGetValue(deck, out int drawn);
                    dealt[deck] = drawn + 1;
                    return due ? entry.roster.Deal(entry.role, seed, drawn) : null;
            }
        }

        /// <summary>
        /// Spawns one of the cast on the spot at <paramref name="place"/> among those tagged <paramref name="tag"/>, or
        /// scattered round the last of them (round the site, with none) once they are all taken.
        /// </summary>
        private void Stand(BountyHuntSite site, BountyHunt.CastEntry entry, GameObject prefab, string tag, int place)
        {
            bool onSpot = place < spots.Count;
            Transform anchor = onSpot ? spots[place].transform : spots.Count > 0 ? spots[spots.Count - 1].transform : site.transform;
            Vector3 wanted = onSpot ? anchor.position : anchor.position + FlatScatter(tuning.scatterRadius);
            Vector3 at = NavMesh.SamplePosition(wanted, out NavMeshHit hit, tuning.navSampleDistance, NavMesh.AllAreas) ? hit.position : wanted;

            GameObject body = GameServices.World.Spawn(prefab, at, anchor.rotation);
            if (body == null) return;

            // Standing at its site, it must not hold nine chunks loaded round itself.
            if (body.TryGetComponent(out SceneTracked tracked)) tracked.SetKeepChunksLoaded(false);

            bool isTarget = entry.who == CastWho.Target;
            if (entry.faction != null)
            {
                // Never the town's own faction, so a fight with them is never a fight with the town; and for the target,
                // never the captive faction, or it would count as surrendered the moment it stood (EntityFactionSaveable keeps it).
                if (body.TryGetComponent(out EntityFaction faction)) faction.SetFaction(entry.faction);
                else Debug.LogWarning($"[Bounties] '{prefab.name}' has no EntityFaction, so it cannot fight for {entry.faction.name}.", body);
            }

            if (isTarget && body.TryGetComponent(out EntityFaction standing) && standing.Faction == captive.faction)
                Debug.LogError($"[Bounties] '{prefab.name}' stands in the captive faction {captive.faction.name}, so it counts as " +
                               "surrendered at once. Give its Target cast entry another faction.", body);

            SaveRef bodyRef = SaveRef.From(body);
            if (!bodyRef.IsSet)
                Debug.LogError($"[Bounties] '{prefab.name}' spawned without a save identity; the hunt loses it on reload. " +
                               "Is it a registered network prefab with a SaveableEntity?", body);

            state.pieces.Add(new BountyHuntRunState.HuntPiece
            {
                body = bodyRef,
                target = isTarget,
                spot = onSpot ? BountyRules.SpotKey(tag, place) : string.Empty,
            });
            var found = new Live();
            found.Bind(body);
            live.Add(found);
            if (isTarget) Target = BountyTarget.Attach(body, captive);

            if (onSpot) Keep(found, place, Time.time);
            else if (found.goal != null)
                found.goal.TrySetSampled(at, tuning.postArriveRadius, PostReason, hold: true, facePoint: at + anchor.forward * FacingReach);
        }

        /// <summary>Finds the cast again as their chunks stream in, and sends each one who is free back to their spot.</summary>
        private void KeepToSpots(float now)
        {
            BountyHuntSite site = BountyHuntSite.For(hunt);
            for (int i = 0; i < state.pieces.Count; i++)
            {
                if (!TryFind(i, out GameObject body)) continue;

                BountyHuntRunState.HuntPiece piece = state.pieces[i];
                if (piece.target && Target == null) Target = BountyTarget.Attach(body, captive);

                // A caught target's goal is its own (BountyTarget); a scattered piece keeps the post it was given.
                if (piece.target && Target != null && Target.IsCaught) continue;
                if (site == null || !live[i].Free || !BountyRules.TryReadSpotKey(piece.spot, out string tag, out int place)) continue;

                site.SpotsTagged(tag, spots);
                if (place < spots.Count) Keep(live[i], place, now);
            }
        }

        /// <summary>Sends <paramref name="member"/> where its spot says, unless it is already on its way there.</summary>
        private void Keep(Live member, int place, float now)
        {
            AgentGoal goal = member.goal;
            if (goal == null) return;

            BountyHuntSpot spot = spots[place];
            Vector3 at = spot.transform.position;
            switch (spot.Mode)
            {
                case SpotMode.Post:
                    if (goal.HasGoal && goal.Reason == PostReason) return;
                    goal.TrySetSampled(at, tuning.postArriveRadius, PostReason, hold: true, facePoint: at + spot.transform.forward * FacingReach);
                    return;

                case SpotMode.Patrol:
                {
                    bool walking = goal.HasGoal && goal.Reason == PatrolReason;
                    if (walking && !goal.HasArrived) return;

                    int stop = member.patrolStop < 0 ? place : walking ? member.patrolStop + 1 : member.patrolStop;
                    member.patrolStop = stop % spots.Count;
                    goal.TrySetSampled(spots[member.patrolStop].transform.position, tuning.walkArriveRadius, PatrolReason, hold: false, facePoint: null);
                    return;
                }

                case SpotMode.Wander:
                {
                    bool roaming = goal.HasGoal && goal.Reason == WanderReason;
                    if (roaming && !goal.HasArrived) return;
                    if (roaming && member.wanderAt < 0f) member.wanderAt = now + tuning.wanderPauseSeconds;
                    if (roaming && now < member.wanderAt) return;

                    member.wanderAt = -1f;
                    goal.TrySetSampled(at + FlatScatter(spot.WanderRadius), tuning.walkArriveRadius, WanderReason, hold: false, facePoint: null);
                    return;
                }
            }
        }

        /// <summary>The live body of piece <paramref name="index"/>, found by its save identity once its chunk is loaded.</summary>
        private bool TryFind(int index, out GameObject body)
        {
            while (live.Count < state.pieces.Count) live.Add(new Live());

            Live member = live[index];
            if (member.body == null)
            {
                if (!state.pieces[index].body.TryResolve(out GameObject found) || found == null)
                {
                    body = null;
                    return false;
                }
                member.Bind(found);
            }

            body = member.body;
            return true;
        }

        /// <summary>
        /// Whether a pending body still has a record in the world, so is merely not loaded. Without a save session
        /// there is no world record to ask, so it is kept.
        /// </summary>
        private static bool StillInWorld(SaveRef pending)
        {
            WorldSaveStore world = SaveManager.Instance != null ? SaveManager.Instance.World : null;
            return world == null || world.Record.TryGet(pending.Id, out _);
        }

        private static Vector3 FlatScatter(float radius)
        {
            Vector2 offset = UnityEngine.Random.insideUnitCircle * radius;
            return new Vector3(offset.x, 0f, offset.y);
        }
    }
}
