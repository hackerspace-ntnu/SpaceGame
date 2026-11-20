using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Characters;
using SpaceGame.Core;
using SpaceGame.Items;

namespace SpaceGame.Gameplay.Arena
{
    /// <summary>
    /// One arena match: two teams, rounds of last-team-standing, first to
    /// <see cref="roundsToWin"/> takes it.
    ///
    /// <para>
    /// <b>Server</b>: runs the phases (<see cref="ArenaPhase"/>), resets the floor and revives
    /// everyone at their team's spawn between rounds, arms the weapon pads when a round goes live,
    /// drops a dead player's weapons where they fell, and puts back anyone found outside the walls.
    /// It is also every contestant's <see cref="IDamageFilter"/>: no damage outside a live round,
    /// none between teammates, and every hit scaled and capped by <see cref="ArenaDamageRules"/>.
    /// </para>
    ///
    /// <para>
    /// <b>Every peer</b>: reads <see cref="State"/> for the scoreboard, and puts its own player into
    /// the spectator camera when they die — a death in a round is an elimination, so there is no
    /// respawn button (<c>PlayerRespawn</c> refuses one while a match exists) until the next round
    /// revives everyone.
    /// </para>
    ///
    /// <para>
    /// <see cref="Instance"/> is set in <c>Awake</c>, before the network spawn, so anything asking
    /// "is this an arena" — the spawn flow, the death screen, the respawn gate — gets the same
    /// answer on every machine from the moment the scene loads.
    /// </para>
    ///
    /// Nothing here is saved: a match is a single session, and the arena scene has no save system.
    /// </summary>
    [DisallowMultipleComponent]
    public class ArenaMatch : NetworkBehaviour, IDamageFilter
    {
        public static ArenaMatch Instance { get; private set; }

        [Header("Rounds")]
        [SerializeField, Min(1)] private int roundsToWin = 3;

        [Tooltip("Seconds everyone stands at their spawn before a round goes live.")]
        [SerializeField, Min(0f)] private float countdownSeconds = 5f;

        [Tooltip("Seconds the round's result stays up before the next countdown.")]
        [SerializeField, Min(0f)] private float roundOverSeconds = 4f;

        [Header("Damage")]
        [Tooltip("Every hit on a contestant is multiplied by this. Stretches a firefight out without " +
                 "retuning the weapons, which are balanced for the story world.")]
        [SerializeField, Min(0f)] private float damageScale = 0.6f;

        [Tooltip("No single hit takes more than this. Against 100 health, 34 means nobody dies in " +
                 "fewer than three hits.")]
        [SerializeField, Min(1)] private int maxHitDamage = 34;

        [Header("Movement")]
        [Tooltip("Every contestant's jump height is multiplied by this. Height, not speed: 2 is a " +
                 "jump twice as high.")]
        [SerializeField, Min(0f)] private float jumpHeightScale = 2f;

        [Tooltip("Seconds a jump pressed just before landing is kept and spent on touchdown. The " +
                 "arena's jump is ready the moment a player lands — no timed cooldown.")]
        [SerializeField, Min(0f)] private float jumpBufferSeconds = 0.15f;

        [Header("Arena")]
        [SerializeField] private ArenaBounds bounds;
        [SerializeField] private ArenaTeamSpawn[] teamSpawns = System.Array.Empty<ArenaTeamSpawn>();
        [SerializeField] private ArenaWeaponPad[] pads = System.Array.Empty<ArenaWeaponPad>();

        [Tooltip("How far apart two players' spawn positions should be, when the spawn has room.")]
        [SerializeField, Min(0f)] private float spawnSeparation = 2.5f;

        private readonly NetworkVariable<ArenaMatchState> state = new(
            new ArenaMatchState { RoundWinner = ArenaRules.Undecided },
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        /// <summary>A player in the match, with the parts of them the server reaches for.</summary>
        private sealed class Contestant
        {
            public PlayerIdentity Identity;
            public HealthComponent Health;
            public PlayerInventoryNetwork Inventory;
            public PlayerRespawn Respawn;
            public System.Action Died;

            public int Team => Identity != null ? Identity.Team : ArenaDamageRules.NoTeam;

            public bool InMatch => Team >= 0 && Team < ArenaRules.TeamCount;
        }

        private readonly List<Contestant> contestants = new();
        private readonly List<Vector3> occupied = new();

        private PlayerController localPlayer;

        // Who is in the arena per side as of the last server tick, living or dead.
        private int playersTeam0;
        private int playersTeam1;

        public ArenaMatchState State => state.Value;

        /// <summary>Seconds left in the current timed phase, as this peer reckons server time; 0 when untimed.</summary>
        public float SecondsLeft
        {
            get
            {
                if (!IsSpawned || state.Value.PhaseEndsAt <= 0d) return 0f;
                return Mathf.Max(0f, (float)(state.Value.PhaseEndsAt - NetworkManager.ServerTime.Time));
            }
        }

        public int RoundsToWin => roundsToWin;

        /// <summary>
        /// Whether a player may be given a body right now. Not while a real round is live: someone
        /// who joins mid-round would be a fresh player dropped into an elimination, so they wait for
        /// the next one. A practice round lets them straight in — their arrival on the empty side is
        /// what starts the real match.
        /// </summary>
        public bool AcceptsSpawns => state.Value.Phase != ArenaPhase.Fighting || state.Value.Practice;

        /// <summary>
        /// Server. Whether <see cref="TogglePractice"/> would do anything: the match is waiting with
        /// someone in it but not on both sides, or a practice round is already running.
        /// </summary>
        public bool CanTogglePractice =>
            IsServer && (state.Value.Practice
                ? state.Value.Phase == ArenaPhase.Countdown || state.Value.Phase == ArenaPhase.Fighting
                : state.Value.Phase == ArenaPhase.Waiting && ArenaRules.CanStartPractice(playersTeam0, playersTeam1));

        /// <summary>
        /// Server, the host's call. Starts a practice round when the match is waiting for an
        /// opponent — so the arena can be tried alone — or ends the practice round that is running.
        /// A practice round is reset like any other round, is never scored, and gives way to a real
        /// round as soon as both sides have a player.
        /// </summary>
        public void TogglePractice()
        {
            if (!CanTogglePractice) return;

            ArenaMatchState next = state.Value;
            double now = NetworkManager.ServerTime.Time;

            if (next.Practice) EndRound(ref next, ArenaRules.Draw, now);
            else BeginRound(ref next, now, practice: true);

            state.Value = next;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogError("[Arena] A second ArenaMatch in the scene — only one runs a match.", this);
                enabled = false;
                return;
            }

            Instance = this;
        }

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            PlayerIdentity.RosterChanged += OnRosterChanged;
            OnRosterChanged();
        }

        public override void OnNetworkDespawn()
        {
            PlayerIdentity.RosterChanged -= OnRosterChanged;

            foreach (Contestant contestant in contestants) Release(contestant);
            contestants.Clear();

            if (localPlayer != null) localPlayer.OnPlayerDeath -= Spectate;
            localPlayer = null;
        }

        private void OnRosterChanged()
        {
            BindLocalPlayer();
            TakeEliminatedBodiesOffField();

            if (IsServer) SyncContestants();
        }

        // ──────────────────────────────────────────────────────────────── every peer

        /// <summary>
        /// Finds the player this peer drives, so its death can be met with the spectator camera, and
        /// gives it the arena's jump. Only the owner hears <see cref="PlayerController.OnPlayerDeath"/>,
        /// which is exactly the machine whose camera should change — and movement is the owner's
        /// too, so the jump rules only need setting here.
        /// </summary>
        private void BindLocalPlayer()
        {
            if (localPlayer != null) return;

            foreach (PlayerIdentity identity in PlayerIdentity.All)
            {
                if (identity == null || !identity.IsOwner) continue;

                localPlayer = identity.GetComponentInChildren<PlayerController>();
                if (localPlayer == null) return;

                localPlayer.OnPlayerDeath += Spectate;

                PlayerMovement movement = localPlayer.GetComponent<PlayerMovement>();
                if (movement != null) movement.SetJumpRules(jumpHeightScale, readyOnLanding: true, jumpBufferSeconds);
                return;
            }
        }

        /// <summary>
        /// Every player, on every machine, vanishes on elimination instead of falling as a ragdoll.
        /// See <see cref="ArenaEliminatedBody"/>.
        /// </summary>
        private static void TakeEliminatedBodiesOffField()
        {
            foreach (PlayerIdentity identity in PlayerIdentity.All)
            {
                if (identity == null) continue;

                HealthComponent health = identity.GetComponentInChildren<HealthComponent>();
                if (health != null && !health.TryGetComponent(out ArenaEliminatedBody _))
                    health.gameObject.AddComponent<ArenaEliminatedBody>();
            }
        }

        // The revive at the next round takes them out again: PlayerController.OnRevive exits it.
        private void Spectate() => localPlayer.EnterSpectatorMode();

        // ──────────────────────────────────────────────────────────────── server

        private void Update()
        {
            if (!IsSpawned || !IsServer) return;

            ArenaMatchState next = state.Value;
            double now = NetworkManager.ServerTime.Time;

            Count(out int total0, out int total1, out int alive0, out int alive1);

            // Never in a real round — its sides are settled until it is decided. Whatever round
            // comes next stands the moved player at their new side's spawn.
            if ((next.Phase == ArenaPhase.Waiting || next.Practice) &&
                FillEmptySide(ArenaRules.SideToFill(total0, total1)))
                Count(out total0, out total1, out alive0, out alive1);

            playersTeam0 = total0;
            playersTeam1 = total1;
            next.AliveTeam0 = (byte)alive0;
            next.AliveTeam1 = (byte)alive1;

            switch (next.Phase)
            {
                case ArenaPhase.Waiting:
                    if (ArenaRules.CanStartRound(total0, total1)) BeginRound(ref next, now, practice: false);
                    break;

                case ArenaPhase.Countdown:
                    if (now >= next.PhaseEndsAt) GoLive(ref next);
                    break;

                case ArenaPhase.Fighting:
                    // Someone arrived on the empty side: the practice is over, the match begins.
                    if (next.Practice && ArenaRules.CanStartRound(total0, total1))
                    {
                        BeginRound(ref next, now, practice: false);
                        break;
                    }

                    int winner = ArenaRules.EvaluateRound(alive0, alive1, next.Practice);
                    if (winner != ArenaRules.Undecided) EndRound(ref next, winner, now);
                    else KeepInside();
                    break;

                case ArenaPhase.RoundOver:
                    if (now < next.PhaseEndsAt) break;

                    if (ArenaRules.CanStartRound(total0, total1))
                    {
                        BeginRound(ref next, now, practice: false);
                    }
                    else
                    {
                        next.Phase = ArenaPhase.Waiting;
                        next.Practice = false;
                    }
                    break;
            }

            if (!next.Equals(state.Value)) state.Value = next;
        }

        private void Count(out int total0, out int total1, out int alive0, out int alive1)
        {
            total0 = total1 = alive0 = alive1 = 0;

            foreach (Contestant contestant in contestants)
            {
                if (!contestant.InMatch) continue;

                bool alive = contestant.Health != null && contestant.Health.Alive;

                if (contestant.Team == 0) { total0++; if (alive) alive0++; }
                else { total1++; if (alive) alive1++; }
            }
        }

        /// <summary>
        /// Moves one player onto <paramref name="side"/>, which is empty while the other side has
        /// several (<see cref="ArenaRules.SideToFill"/>). The lobby puts anyone who never picked a
        /// team on team 0, so without this a host and friends who just pressed Ready would all
        /// land on one side and the match could never start.
        ///
        /// <para>
        /// The newest arrival goes rather than the host, who set the match up. The roster is
        /// told too, so a reconnect brings them back to the side they now play on.
        /// </para>
        ///
        /// <para>
        /// That client's own <c>VersusSession.LocalTeam</c> still names the side the lobby gave
        /// them; only the team voice grouping reads it. Everything in the arena reads
        /// <see cref="PlayerIdentity.Team"/>, which this does change.
        /// </para>
        /// </summary>
        private bool FillEmptySide(int side)
        {
            if (side < 0) return false;

            Contestant mover = null;
            int inMatch = 0;

            foreach (Contestant contestant in contestants)
            {
                if (!contestant.InMatch) continue;

                inMatch++;
                if (contestant.Team != side && (mover == null || !contestant.Identity.IsSessionHost))
                    mover = contestant;
            }

            // Only once everyone connected is standing in the arena. Someone still loading may well
            // have picked the empty side, and moving a player for them would unbalance a 2v2 into
            // a 1v3 the moment they arrive.
            if (mover == null || inMatch < NetworkManager.ConnectedClientsIds.Count) return false;

            VersusTeamRoster.Claim(mover.Identity.OwnerClientId, side, ArenaRules.TeamCount);
            mover.Identity.SetTeam(side);

            Debug.Log($"[Arena] Everyone was on one side, so '{mover.Identity.DisplayName}' now plays " +
                      $"for {VersusRules.TeamName(side)}.", this);
            return true;
        }

        private void BeginRound(ref ArenaMatchState next, double now, bool practice)
        {
            ClearFloor();
            ReviveEveryone();

            // A practice round is not one of the match's rounds, so it does not advance the count.
            if (!practice) next.Round++;
            next.Practice = practice;
            next.RoundWinner = ArenaRules.Undecided;
            next.Phase = ArenaPhase.Countdown;
            next.PhaseEndsAt = now + countdownSeconds;
        }

        private void GoLive(ref ArenaMatchState next)
        {
            next.Phase = ArenaPhase.Fighting;
            next.PhaseEndsAt = 0d;

            foreach (ArenaWeaponPad pad in pads)
                if (pad != null) pad.Arm();
        }

        private void EndRound(ref ArenaMatchState next, int winner, double now)
        {
            foreach (ArenaWeaponPad pad in pads)
                if (pad != null) pad.Disarm();

            if (winner == 0) next.ScoreTeam0++;
            else if (winner == 1) next.ScoreTeam1++;

            int matchWinner = ArenaRules.MatchWinner(next.ScoreTeam0, next.ScoreTeam1, roundsToWin);

            if (matchWinner >= 0)
            {
                next.Phase = ArenaPhase.MatchOver;
                next.RoundWinner = (sbyte)matchWinner;
                next.PhaseEndsAt = 0d;
                return;
            }

            next.Phase = ArenaPhase.RoundOver;
            next.RoundWinner = (sbyte)winner;
            next.PhaseEndsAt = now + roundOverSeconds;
        }

        /// <summary>
        /// Takes every loose weapon off the floor: whatever the pads put down and nobody took, and
        /// whatever the dead dropped. Only runtime spawns — anything authored into the scene stays.
        /// </summary>
        private void ClearFloor()
        {
            foreach (ArenaWeaponPad pad in pads)
                if (pad != null) pad.Disarm();

            foreach (PickupableItem pickup in FindObjectsByType<PickupableItem>(FindObjectsSortMode.None))
            {
                NetworkObject networkObject = pickup.NetworkObject;
                if (networkObject == null || !networkObject.IsSpawned) continue;
                if (networkObject.IsSceneObject.GetValueOrDefault()) continue;

                GameServices.World.Despawn(pickup.gameObject);
            }
        }

        /// <summary>Empty hands, full health, back at their team's spawn — the living and the dead alike.</summary>
        private void ReviveEveryone()
        {
            occupied.Clear();

            foreach (Contestant contestant in contestants)
            {
                if (!contestant.InMatch || contestant.Respawn == null) continue;

                if (contestant.Inventory != null)
                    for (int slot = 0; slot < contestant.Inventory.GetInventorySize(); slot++)
                        contestant.Inventory.TryRemoveItem(slot);

                if (!TryGetSpawnPose(contestant.Team, occupied, out Vector3 position, out Quaternion rotation))
                {
                    Debug.LogError($"[Arena] No spawn for {VersusRules.TeamName(contestant.Team)} could " +
                                   $"take '{contestant.Identity.DisplayName}' — is an ArenaTeamSpawn for " +
                                   "that team authored, with ground under it?", this);
                    continue;
                }

                // Remembered here, not read back from the body: the move is performed by the owner a
                // round trip from now, so every body is still where it was when the next one is placed.
                occupied.Add(position);
                contestant.Respawn.ReviveAt(position, rotation);
            }
        }

        /// <summary>Puts anyone who got past the walls back at their team's spawn.</summary>
        private void KeepInside()
        {
            if (bounds == null) return;

            foreach (Contestant contestant in contestants)
            {
                if (!contestant.InMatch || contestant.Health == null || !contestant.Health.Alive) continue;

                Transform body = contestant.Identity.transform;
                if (bounds.Contains(body.position)) continue;

                if (TryGetSpawnPose(contestant.Team, out Vector3 position, out Quaternion rotation))
                    NetworkedTeleport.Move(body.gameObject, position, rotation);
            }
        }

        /// <summary>
        /// Server. A standing position at one of a team's spawns, kept clear of every living
        /// player, facing into the arena. False when no spawn for that team can vouch for ground.
        /// </summary>
        public bool TryGetSpawnPose(int team, out Vector3 position, out Quaternion rotation)
        {
            occupied.Clear();

            foreach (Contestant contestant in contestants)
                if (contestant.Health != null && contestant.Health.Alive)
                    occupied.Add(contestant.Identity.transform.position);

            return TryGetSpawnPose(team, occupied, out position, out rotation);
        }

        private bool TryGetSpawnPose(int team, IReadOnlyList<Vector3> taken,
                                     out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;

            bool found = false;
            float best = float.NegativeInfinity;

            foreach (ArenaTeamSpawn spawn in teamSpawns)
            {
                if (spawn == null || spawn.Team != team) continue;

                if (!spawn.Point.TryGetSpawnPoint(taken, spawnSeparation, out Vector3 candidate, out float clearance))
                    continue;

                if (found && clearance <= best) continue;

                found = true;
                best = clearance;
                position = candidate;
            }

            if (!found) return false;

            Vector3 centre = bounds != null ? bounds.Center : transform.position;
            Vector3 inward = centre - position;
            inward.y = 0f;
            rotation = inward.sqrMagnitude > 0.01f ? Quaternion.LookRotation(inward) : Quaternion.identity;
            return true;
        }

        // ──────────────────────────────────────────────────────── contestants (server)

        /// <summary>Follows the roster: every spawned player is a contestant, filtered and watched for death.</summary>
        private void SyncContestants()
        {
            for (int i = contestants.Count - 1; i >= 0; i--)
            {
                Contestant contestant = contestants[i];
                if (contestant.Identity != null && contestant.Identity.IsSpawned) continue;

                Release(contestant);
                contestants.RemoveAt(i);
            }

            foreach (PlayerIdentity identity in PlayerIdentity.All)
            {
                if (identity == null || contestants.Exists(c => c.Identity == identity)) continue;

                var contestant = new Contestant
                {
                    Identity = identity,
                    Health = identity.GetComponentInChildren<HealthComponent>(),
                    Inventory = identity.GetComponentInChildren<PlayerInventoryNetwork>(),
                    Respawn = identity.GetComponentInChildren<PlayerRespawn>()
                };

                contestant.Died = () => OnContestantDied(contestant);

                if (contestant.Health != null)
                {
                    contestant.Health.AddFilter(this);
                    contestant.Health.OnDeath += contestant.Died;
                }

                contestants.Add(contestant);
            }
        }

        private void Release(Contestant contestant)
        {
            if (contestant.Health == null) return;

            contestant.Health.RemoveFilter(this);
            contestant.Health.OnDeath -= contestant.Died;
        }

        /// <summary>A death in a live round drops everything the player was carrying where they fell.</summary>
        private void OnContestantDied(Contestant contestant)
        {
            if (state.Value.Phase != ArenaPhase.Fighting || contestant.Inventory == null) return;

            for (int slot = 0; slot < contestant.Inventory.GetInventorySize(); slot++)
                contestant.Inventory.DropSlot(slot);
        }

        /// <summary>Every hit on a contestant, on the server, before it lands. See <see cref="ArenaDamageRules"/>.</summary>
        public void Filter(HealthComponent victim, ref DamageHit hit)
        {
            Contestant target = contestants.Find(c => c.Health == victim);
            if (target == null) return;

            PlayerIdentity attacker = hit.Source != null ? hit.Source.GetComponentInParent<PlayerIdentity>() : null;
            int attackerTeam = attacker != null ? attacker.Team : ArenaDamageRules.NoTeam;

            hit.Amount = ArenaDamageRules.Resolve(hit.Amount, attackerTeam, target.Team,
                                                  attacker == target.Identity,
                                                  state.Value.Phase == ArenaPhase.Fighting,
                                                  damageScale, maxHitDamage);
        }
    }
}
