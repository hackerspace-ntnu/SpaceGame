using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using Random = UnityEngine.Random;
using SpaceGame.Agents;
using SpaceGame.Characters;
using SpaceGame.Presentation;

namespace SpaceGame.Gameplay
{
    // Server-authoritative match orchestrator for the minigame arena. Owns bot
    // spawning, faction assignment, kill/lives tracking, and win detection. Lives
    // in MinigameArena.unity, separate from SpawnManager/SpawnPoint
    // (Assets/Game/Scripts/Game/), which remain untouched — this does its own
    // independent spawn-point collection for match entities per design spec §3.
    //
    // All three gamemodes run through the same machinery: entities are grouped by a
    // team index, and the only difference between Team Deathmatch and the solo
    // modes is how many teams there are and which faction asset each one draws.
    // Team Deathmatch puts everyone into 2 teams sharing 2 of the 4 team factions;
    // Free-For-All and Battle Royale give every entity its own team index and its
    // own Solo faction, which the relationship table already marks hostile to all
    // the others.
    public class MatchManager : NetworkBehaviour
    {
        private const int AllyTeam = 0;
        private const int EnemyTeam = 1;
        private const int DrawTeam = -1;

        [Header("Team factions — Team Deathmatch uses the first two")]
        [SerializeField] private FactionDefinition[] teamFactions;

        [Header("Solo factions — one per entity in Free-For-All / Battle Royale")]
        [SerializeField] private FactionDefinition[] soloFactions;

        [SerializeField] private FactionRelationshipTable relationshipTable;

        [Header("Bots")]
        [SerializeField] private GameObject deathmatchBotPrefab;

        [Tooltip("Targeting tuning applied to every match entity, so the same prefab that wanders " +
                 "the open world fights like an arena bot here. Leave empty to use the built-in " +
                 "arena defaults (see BuildDefaultArenaTargeting).")]
        [SerializeField] private TargetingProfile arenaTargetingProfile;

        [Header("Respawn")]
        [SerializeField] private float respawnDelay = 3f;

        [Header("Spawn placement")]
        [Tooltip("How far a spawn point may be from the NavMesh and still be snapped onto it. " +
                 "Points further out than this are dropped from the match.")]
        [SerializeField] private float navMeshSnapDistance = 12f;

        [Tooltip("Random offset applied around a spawn point when placing an entity. A full match has " +
                 "more entities than usable spawn points, so without this they spawn inside each other.")]
        [SerializeField] private float spawnScatterRadius = 7f;

        private readonly List<GameObject> spawnedEntities = new();
        private readonly Dictionary<GameObject, int> entityTeam = new();

        // Per-entity scoring, for the leaderboard. Separate from killsByTeam, which the win
        // conditions use and which cannot distinguish who on a team did the killing.
        private readonly Dictionary<GameObject, int> killsByEntity = new();
        private readonly Dictionary<GameObject, int> deathsByEntity = new();
        private readonly Dictionary<GameObject, string> nameByEntity = new();
        private int botsNamed;

        private readonly Dictionary<int, int> killsByTeam = new();
        private readonly Dictionary<int, int> livesByEntity = new();
        private readonly Dictionary<int, int> livesByTeam = new();
        private readonly Dictionary<int, int> membersByTeam = new();

        private List<Vector3> allSpawnPositions = new();
        private List<List<Vector3>> teamSpawnBlocks;
        private int spawnCursor;
        private int nextSoloSlot;
        private int humansRegistered;

        // Read once at match start rather than per-death: MatchSettings is a static
        // the menu writes, and a match should not change shape underneath itself if
        // anything else touches it later.
        private MatchGameMode mode;
        private WinCondition condition;
        private int livesPerEntity;
        private bool matchEnded;
        private bool suppressScorePublish;

        public event System.Action<int> OnMatchEnded; // winning team index, or -1 for draw

        // The team the local peer is playing for, so the result screen can tell
        // victory from defeat without knowing anything about the gamemode. -1 until
        // the server has assigned one (and for a pure spectator that never spawns).
        public static int LocalTeamIndex { get; private set; } = DrawTeam;

        // The leaderboard, as every peer sees it. Static for the same reason LocalTeamIndex is: the
        // UI is built at runtime on clients that have no reference to this server-side component.
        // The server writes it directly; clients receive it through BroadcastScoresRpc.
        public static IReadOnlyList<MatchScoreEntry> Scores => scores;
        public static event System.Action OnScoresChanged;

        private static readonly List<MatchScoreEntry> scores = new();

        public override void OnNetworkSpawn()
        {
            // Before the IsServer gate: these are local overlays every peer needs, and clients reach
            // them through BroadcastMatchEndedClientRpc / BroadcastScoresRpc.
            LocalTeamIndex = DrawTeam;
            scores.Clear();
            OnScoresChanged?.Invoke();
            MatchResultUI.Ensure();
            MatchLeaderboardUI.Ensure();

            if (!IsServer) return;

            mode = MatchSettings.Mode;
            condition = MatchSettings.ResolvedCondition;
            livesPerEntity = MatchSettings.ResolvedLives;

            allSpawnPositions = CollectSpawnPositions();
            teamSpawnBlocks = TeamAssignment.SplitEvenly(allSpawnPositions, teamCount: 2);

            // Every RegisterEntity would otherwise broadcast the whole table, so a 16-bot match
            // opens with 16 RPCs carrying near-identical data. Publish once when the roster is set.
            suppressScorePublish = true;
            SpawnBots();
            RegisterAlreadySpawnedPlayers();
            suppressScorePublish = false;
            PublishScores();

            Debug.Log($"[MatchManager] {MatchSettings.Describe()} (condition={condition}, lives={livesPerEntity}, " +
                      $"spawnPoints={allSpawnPositions.Count})");
        }

        private List<Vector3> CollectSpawnPositions()
        {
            var points = FindObjectsByType<SpawnPoint>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

            // Scoped to this manager's own scene. persistentScene is loaded underneath
            // the arena and carries the main game's own SpawnPoint ~17km away, so an
            // unfiltered sweep hands roughly one entity per match a position outside
            // the arena entirely — it spawns in the main world and never joins the fight.
            // A spawn point that cannot vouch for a position is skipped rather than substituted
            // for: the arena is a single loaded scene, so a failure here means that point genuinely
            // has no ground under it, and the surviving points are all better answers.
            var positions = new List<Vector3>(points.Length);
            foreach (var p in points)
            {
                if (p.gameObject.scene == gameObject.scene && p.TryGetSpawnPoint(out var position))
                    positions.Add(position);
            }

            // Fallback for a MatchManager placed somewhere without its own spawn
            // points — better to spawn everyone somewhere than nowhere.
            if (positions.Count == 0)
            {
                foreach (var p in points)
                {
                    if (p.TryGetSpawnPoint(out var position))
                        positions.Add(position);
                }
            }

            positions = KeepMutuallyReachable(positions);

            for (int i = positions.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (positions[i], positions[j]) = (positions[j], positions[i]);
            }

            return positions;
        }

        // Snaps spawn positions onto the NavMesh and keeps only the largest group that
        // can path to each other.
        //
        // The arena's NavMesh is not one connected surface — steep terrain splits it
        // into islands. Bots spawned on a minority island walk a few metres, hit the
        // edge of their island, and stop: they never reach anyone, so nobody dies and
        // a last-standing match runs forever. Losing a few spawn points is a much
        // better trade than losing the match.
        private List<Vector3> KeepMutuallyReachable(List<Vector3> positions)
        {
            var onMesh = new List<Vector3>(positions.Count);
            foreach (var position in positions)
            {
                if (NavMesh.SamplePosition(position, out NavMeshHit hit, navMeshSnapDistance, NavMesh.AllAreas))
                    onMesh.Add(hit.position);
            }

            // No NavMesh at all (or none near the points) — leave the authored
            // positions alone rather than returning nothing.
            if (onMesh.Count == 0)
                return positions;

            var reachable = SpawnReachability.LargestConnectedGroup(onMesh.Count,
                (a, b) => CanPathBetween(onMesh[a], onMesh[b]));

            var kept = new List<Vector3>(reachable.Count);
            foreach (int index in reachable)
                kept.Add(onMesh[index]);

            if (kept.Count < onMesh.Count)
            {
                Debug.LogWarning($"[MatchManager] {onMesh.Count - kept.Count} of {positions.Count} spawn points sit on " +
                                 "NavMesh islands cut off from the rest of the arena and were dropped. Rebake the arena " +
                                 "NavMesh or move those points if the match feels cramped.");
            }

            return kept;
        }

        private static bool CanPathBetween(Vector3 from, Vector3 to)
        {
            var path = new NavMeshPath();
            return NavMesh.CalculatePath(from, to, NavMesh.AllAreas, path)
                   && path.status == NavMeshPathStatus.PathComplete;
        }

        private void SpawnBots()
        {
            if (deathmatchBotPrefab == null)
            {
                Debug.LogError("[MatchManager] No DeathmatchBot prefab assigned — spawning no bots.");
                return;
            }

            if (mode == MatchGameMode.TeamDeathmatch)
            {
                SpawnBotGroup(AllyTeam, MatchRules.ClampTeamBots(MatchSettings.AllyCount));
                SpawnBotGroup(EnemyTeam, MatchRules.ClampTeamBots(MatchSettings.EnemyCount));
                return;
            }

            // Solo modes: every bot is its own team, so the group is one bot wide.
            int soloBots = MatchRules.ClampSoloBots(MatchSettings.SoloBotCount);
            for (int i = 0; i < soloBots; i++)
                SpawnBotGroup(AllocateSoloTeam(), count: 1);
        }

        private void SpawnBotGroup(int teamIndex, int count)
        {
            for (int i = 0; i < count; i++)
            {
                GameObject bot = Instantiate(deathmatchBotPrefab, NextSpawnPosition(teamIndex), Quaternion.identity);
                RegisterEntity(bot, teamIndex, FactionFor(teamIndex));

                var netObj = bot.GetComponent<NetworkObject>();
                if (netObj != null)
                    netObj.Spawn();
            }
        }

        // Team Deathmatch draws from its team's half of the shuffled points so the
        // two sides start apart. Solo modes walk the whole shuffled list instead,
        // handing out a different point to each entity until they run out.
        private Vector3 NextSpawnPosition(int teamIndex)
        {
            if (mode == MatchGameMode.TeamDeathmatch)
                return RandomSpawnPosition(teamIndex);

            if (allSpawnPositions.Count == 0) return transform.position;
            return Scatter(allSpawnPositions[spawnCursor++ % allSpawnPositions.Count]);
        }

        private Vector3 RandomSpawnPosition(int teamIndex)
        {
            List<Vector3> pool = allSpawnPositions;
            if (mode == MatchGameMode.TeamDeathmatch && teamSpawnBlocks != null
                && teamIndex >= 0 && teamIndex < teamSpawnBlocks.Count && teamSpawnBlocks[teamIndex].Count > 0)
            {
                pool = teamSpawnBlocks[teamIndex];
            }

            if (pool == null || pool.Count == 0) return transform.position;
            return Scatter(pool[Random.Range(0, pool.Count)]);
        }

        // Offsets within the spawn point's own neighbourhood and re-samples, so a
        // reused point still puts entities beside each other rather than inside each
        // other. Falls back to the exact point when nothing nearby is walkable.
        private Vector3 Scatter(Vector3 origin)
        {
            if (spawnScatterRadius <= 0f) return origin;

            for (int attempt = 0; attempt < 6; attempt++)
            {
                Vector2 offset = Random.insideUnitCircle * spawnScatterRadius;
                Vector3 candidate = origin + new Vector3(offset.x, 0f, offset.y);

                if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, spawnScatterRadius, NavMesh.AllAreas))
                    return hit.position;
            }

            return origin;
        }

        private FactionDefinition FactionFor(int teamIndex)
        {
            FactionDefinition[] pool = mode == MatchGameMode.TeamDeathmatch ? teamFactions : soloFactions;
            if (pool == null || pool.Length == 0)
            {
                Debug.LogError($"[MatchManager] No faction assets assigned for {mode} — entities will be unfactioned " +
                               "and invisible to targeting.");
                return null;
            }

            return pool[teamIndex % pool.Length];
        }

        // Solo slots are handed out in order to bots first and then to each human as
        // they register, so a 15-bot match still leaves the 16th faction for the host.
        private int AllocateSoloTeam()
        {
            int slot = nextSoloSlot;
            nextSoloSlot++;

            int cap = soloFactions != null && soloFactions.Length > 0 ? soloFactions.Length : MatchRules.SoloFactionCount;
            if (slot >= cap)
            {
                Debug.LogWarning($"[MatchManager] Ran out of solo factions at slot {slot}; reusing the last one — " +
                                 "the entities sharing it will be allied.");
                slot = cap - 1;
                nextSoloSlot = cap;
            }

            return slot;
        }

        // Called for the bots spawned above, and by RegisterPlayerEntity for humans.
        public void RegisterEntity(GameObject entity, int teamIndex, FactionDefinition faction)
        {
            if (entity == null || entityTeam.ContainsKey(entity)) return;

            // Ensure, not a null-guarded skip: an entity with no EntityFaction is invisible to every
            // targeting module, so silently skipping it removes it from the match with no error.
            EntityFaction.Ensure(entity, faction, relationshipTable);

            // Retune targeting for the arena. Open-world settings make an entity wait to be seen
            // before it will commit to anything, which in a last-standing match means nobody ever
            // finds anybody and the match runs forever.
            var targeting = entity.GetComponent<AgentTargeting>();
            if (targeting != null)
                targeting.ApplyProfile(ArenaTargeting);

            // Herd membership is baked into the prefab, so every bot ships with the same id. Left
            // alone, a Free-For-All puts sixteen mutually hostile entities in one herd sharing
            // movement broadcasts. One herd per team is the only reading that makes sense.
            var herd = entity.GetComponent<HerdModule>();
            if (herd != null)
                herd.SetHerdId($"match-team-{teamIndex}");

            spawnedEntities.Add(entity);
            entityTeam[entity] = teamIndex;
            killsByEntity[entity] = 0;
            deathsByEntity[entity] = 0;
            nameByEntity[entity] = BuildDisplayName(entity);
            membersByTeam[teamIndex] = membersByTeam.GetValueOrDefault(teamIndex) + 1;
            killsByTeam.TryAdd(teamIndex, 0);
            livesByEntity[entity.GetInstanceID()] = livesPerEntity;
            livesByTeam[teamIndex] = livesByTeam.GetValueOrDefault(teamIndex) + livesPerEntity;

            var health = entity.GetComponent<HealthComponent>();
            if (health != null)
                health.OnDeath += () => HandleDeath(entity, teamIndex);

            PublishScores();
        }

        // Humans get their client id so each peer can find its own row; bots are numbered in spawn
        // order. Names are fixed at spawn so the leaderboard doesn't reshuffle its labels mid-match.
        private string BuildDisplayName(GameObject entity)
        {
            var netObj = entity.GetComponent<NetworkObject>();
            if (netObj != null && netObj.IsPlayerObject)
                return $"Player {netObj.OwnerClientId + 1}";

            botsNamed++;
            return $"Bot {botsNamed:00}";
        }

        // Rebuilds the table and pushes it to clients. Called only when something died or joined,
        // so this is a handful of times a match, not per frame.
        private void PublishScores()
        {
            if (!IsServer || suppressScorePublish) return;

            var table = new MatchScoreEntry[spawnedEntities.Count];
            for (int i = 0; i < spawnedEntities.Count; i++)
            {
                GameObject entity = spawnedEntities[i];
                var netObj = entity != null ? entity.GetComponent<NetworkObject>() : null;

                table[i] = new MatchScoreEntry
                {
                    Name = nameByEntity.GetValueOrDefault(entity, "—"),
                    Kills = killsByEntity.GetValueOrDefault(entity),
                    Deaths = deathsByEntity.GetValueOrDefault(entity),
                    Team = entityTeam.GetValueOrDefault(entity, DrawTeam),
                    ClientId = netObj != null && netObj.IsPlayerObject
                        ? netObj.OwnerClientId
                        : MatchScoreEntry.NoClient,
                };
            }

            ApplyScores(table);
            BroadcastScoresRpc(table);
        }

        private static void ApplyScores(MatchScoreEntry[] table)
        {
            scores.Clear();
            scores.AddRange(table);
            OnScoresChanged?.Invoke();
        }

        // NotServer for the same reason BroadcastMatchEndedClientRpc is: the server already applied
        // the table locally, and a host would otherwise process it twice.
        [Rpc(SendTo.NotServer)]
        private void BroadcastScoresRpc(MatchScoreEntry[] table) => ApplyScores(table);

        private TargetingProfile runtimeArenaTargeting;

        private TargetingProfile ArenaTargeting =>
            arenaTargetingProfile != null ? arenaTargetingProfile : BuildDefaultArenaTargeting();

        // Arena defaults, used when no profile asset is assigned. The differences from the open
        // world are all one idea: in a deathmatch every entity is assumed to know the fight is on.
        //   * no line-of-sight requirement — bots converge through walls instead of milling about
        //   * long acquisition range — the arena is bigger than an ambient aggro radius
        //   * fast re-evaluation and a strong retaliation bias — fights stay reactive
        private TargetingProfile BuildDefaultArenaTargeting()
        {
            if (runtimeArenaTargeting != null)
                return runtimeArenaTargeting;

            runtimeArenaTargeting = ScriptableObject.CreateInstance<TargetingProfile>();
            runtimeArenaTargeting.hideFlags = HideFlags.HideAndDontSave;
            runtimeArenaTargeting.relationship = FactionRelationship.Hostile;
            runtimeArenaTargeting.acquisitionRange = 250f;
            runtimeArenaTargeting.loseRange = 300f;
            runtimeArenaTargeting.reevaluateInterval = 0.4f;
            runtimeArenaTargeting.requireLineOfSightToAcquire = false;
            runtimeArenaTargeting.memoryDuration = 8f;
            runtimeArenaTargeting.currentTargetBias = 0.3f;
            runtimeArenaTargeting.lastAttackerBias = 0.5f;
            runtimeArenaTargeting.occludedPenalty = 0.5f;
            return runtimeArenaTargeting;
        }

        // Entry point for the player spawn flow (SpawnManager.SpawnPlayerForClient).
        // Safe to call for the main game too: it no-ops when no match is running.
        public void RegisterPlayerEntity(GameObject player)
        {
            if (!IsServer || player == null || entityTeam.ContainsKey(player)) return;

            int teamIndex = mode == MatchGameMode.TeamDeathmatch ? PickTeamForHuman() : AllocateSoloTeam();
            RegisterEntity(player, teamIndex, FactionFor(teamIndex));
            humansRegistered++;

            // SpawnManager drops every player at the first spawn point it finds, which
            // has no idea about teams — put them where their side actually starts.
            MoveTo(player, RandomSpawnPosition(teamIndex));

            var netObj = player.GetComponent<NetworkObject>();
            if (netObj != null && netObj.IsSpawned)
                AssignTeamRpc(teamIndex, RpcTarget.Single(netObj.OwnerClientId, RpcTargetUse.Temp));
        }

        // The host always plays for the ally side — that is the side its own
        // "allied bots" slider configured. Anyone joining after that fills whichever
        // side is currently thinner, which keeps a drop-in match balanced without a
        // per-client team-select prompt.
        private int PickTeamForHuman()
        {
            if (humansRegistered == 0) return AllyTeam;

            int allies = membersByTeam.GetValueOrDefault(AllyTeam);
            int enemies = membersByTeam.GetValueOrDefault(EnemyTeam);
            return enemies < allies ? EnemyTeam : AllyTeam;
        }

        // A client whose player object spawned before this scene's MatchManager did
        // never got a RegisterPlayerEntity call, so sweep for them once at start.
        private void RegisterAlreadySpawnedPlayers()
        {
            if (NetworkManager.Singleton == null) return;

            foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
            {
                if (client.PlayerObject != null)
                    RegisterPlayerEntity(client.PlayerObject.gameObject);
            }
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void AssignTeamRpc(int teamIndex, RpcParams rpcParams)
        {
            LocalTeamIndex = teamIndex;
        }

        private void HandleDeath(GameObject entity, int teamIndex)
        {
            if (matchEnded) return;

            deathsByEntity[entity] = deathsByEntity.GetValueOrDefault(entity) + 1;
            CreditKill(entity, teamIndex);
            PublishScores();
            SetTargetable(entity, false);

            bool eliminated = true;

            if (MatchRules.RespawnsEnabled(condition))
            {
                int entityId = entity.GetInstanceID();
                livesByEntity[entityId] = livesByEntity.GetValueOrDefault(entityId) - 1;

                if (condition == WinCondition.LivesPerPlayer)
                    livesByTeam[teamIndex] = Mathf.Max(0, livesByTeam.GetValueOrDefault(teamIndex) - 1);

                bool outOfLives = condition == WinCondition.LivesPerPlayer && livesByEntity[entityId] <= 0;

                if (!outOfLives)
                {
                    eliminated = false;
                    StartCoroutine(RespawnEntityAfterDelay(entity, teamIndex));
                }
            }

            if (eliminated)
                NotifyEliminated(entity);

            CheckWinCondition();
        }

        // HealthComponent records who last damaged it, so a kill is credited to the
        // team the shooter belongs to rather than assumed. Friendly fire and deaths
        // with no attacker (falls, self-inflicted) score for nobody.
        private void CreditKill(GameObject victim, int victimTeam)
        {
            var health = victim.GetComponent<HealthComponent>();
            Transform source = health != null ? health.LastDamageSource : null;

            if (TryResolveEntity(source, out GameObject killer))
            {
                int killerTeam = entityTeam[killer];

                // Friendly fire and suicides score for nobody, on either the team tally or the
                // personal one — otherwise the leaderboard rewards shooting your own side.
                if (killerTeam != victimTeam)
                {
                    killsByTeam[killerTeam] = killsByTeam.GetValueOrDefault(killerTeam) + 1;
                    killsByEntity[killer] = killsByEntity.GetValueOrDefault(killer) + 1;
                }
                return;
            }

            // Two-team fallback: with exactly one opponent, an unattributed death can
            // only have come from the other side, and Kill Target is a Team
            // Deathmatch-only condition anyway.
            if (membersByTeam.Count == 2)
            {
                int otherTeam = victimTeam == AllyTeam ? EnemyTeam : AllyTeam;
                killsByTeam[otherTeam] = killsByTeam.GetValueOrDefault(otherTeam) + 1;
            }
        }

        // Entities are only visible to targeting while their EntityFaction is enabled —
        // that component is what registers them with EntityTargetRegistry.
        //
        // A corpse has to be taken out of the registry explicitly. Bots handle it by
        // accident (HealthReactionModule deactivates the GameObject after its despawn
        // delay) but an eliminated player object stays active forever, so every
        // surviving hostile keeps aiming at it: they stand still facing a body, stop
        // hunting each other, and a last-standing match never reaches a winner.
        private static void SetTargetable(GameObject entity, bool targetable)
        {
            if (entity == null) return;

            var faction = entity.GetComponent<EntityFaction>();
            if (faction != null)
                faction.enabled = targetable;
        }

        // Walks up from whichever collider or child transform carried the damage reference to the
        // registered match entity it belongs to.
        private bool TryResolveEntity(Transform t, out GameObject entity)
        {
            while (t != null)
            {
                if (entityTeam.ContainsKey(t.gameObject))
                {
                    entity = t.gameObject;
                    return true;
                }
                t = t.parent;
            }

            entity = null;
            return false;
        }

        // Drops an eliminated human into the free-fly spectator camera. Only for a
        // death they will not come back from — the camera swap is one-way until the
        // player is revived or the result screen appears.
        private void NotifyEliminated(GameObject entity)
        {
            var netObj = entity.GetComponent<NetworkObject>();
            if (netObj == null || !netObj.IsPlayerObject) return;

            EnterSpectatorRpc(RpcTarget.Single(netObj.OwnerClientId, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void EnterSpectatorRpc(RpcParams rpcParams)
        {
            NetworkObject localPlayer = NetworkManager.Singleton != null
                ? NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject()
                : null;
            if (localPlayer == null) return;

            var controller = localPlayer.GetComponent<PlayerController>();
            if (controller != null)
                controller.EnterSpectatorMode();
        }

        private IEnumerator RespawnEntityAfterDelay(GameObject entity, int teamIndex)
        {
            yield return new WaitForSeconds(respawnDelay);
            if (entity == null || matchEnded) yield break;

            entity.SetActive(true);
            MoveTo(entity, RandomSpawnPosition(teamIndex));

            // ResetToFull, not Heal(maxHealth): overkill damage leaves currentHealth
            // below zero, and Heal caps the restore at the amount passed, so a heavily
            // overkilled entity would respawn damaged — or still dead.
            var health = entity.GetComponent<HealthComponent>();
            if (health != null)
                health.ResetToFull();

            // Back into EntityTargetRegistry — HandleDeath took them out of it, and a
            // respawned entity that nobody can target would be invulnerable in practice.
            SetTargetable(entity, true);

            var agentController = entity.GetComponent<AgentController>();
            if (agentController != null)
                agentController.enabled = true;
        }

        // The player prefab's NetworkTransform is owner-authoritative (AuthorityMode:
        // Owner), so a server-side transform write is overwritten by the owner's next
        // state update and the respawn teleport silently does nothing for remote
        // clients. Route it through the owner instead. Bots aren't networked at all
        // and are moved directly.
        private void MoveTo(GameObject entity, Vector3 position)
        {
            var netObj = entity.GetComponent<NetworkObject>();
            if (netObj != null && netObj.IsSpawned)
            {
                TeleportRpc(new NetworkObjectReference(netObj), position,
                    RpcTarget.Single(netObj.OwnerClientId, RpcTargetUse.Temp));
                return;
            }

            entity.transform.position = position;
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void TeleportRpc(NetworkObjectReference target, Vector3 position, RpcParams rpcParams)
        {
            if (!target.TryGet(out NetworkObject netObj)) return;

            // Clear momentum too, or the velocity carried in from the death (a fall,
            // an explosion) resumes the instant the body is placed at the spawn point.
            var body = netObj.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.position = position;
            }

            netObj.transform.position = position;
        }

        private void CheckWinCondition()
        {
            if (matchEnded) return;

            int? winner = condition switch
            {
                WinCondition.KillTarget => MatchWinEvaluator.EvaluateKillTarget(killsByTeam, MatchSettings.KillTargetCount),
                WinCondition.LivesPerPlayer => MatchWinEvaluator.EvaluateLivesExhausted(livesByTeam),
                WinCondition.LastStanding => MatchWinEvaluator.EvaluateLastStanding(CountLivingByTeam()),
                _ => null,
            };

            if (winner.HasValue)
            {
                EndMatch(winner.Value);
                return;
            }

            // The evaluators return null both for "carry on" and for "nobody is left",
            // which would otherwise hang a match whose last two entities killed each
            // other in the same exchange.
            if (spawnedEntities.Count > 0 && NobodyLeft())
                EndMatch(DrawTeam);
        }

        private bool NobodyLeft()
        {
            // Kill Target never eliminates anyone — everyone respawns forever, so the
            // only way out is reaching the target.
            if (condition == WinCondition.KillTarget) return false;

            IEnumerable<int> remaining = condition == WinCondition.LivesPerPlayer
                ? livesByTeam.Values
                : CountLivingByTeam().Values;

            foreach (int value in remaining)
                if (value > 0) return false;

            return true;
        }

        private Dictionary<int, int> CountLivingByTeam()
        {
            var counts = new Dictionary<int, int>();
            foreach (var team in membersByTeam.Keys)
                counts[team] = 0;

            foreach (var entity in spawnedEntities)
            {
                if (entity == null || !entity.activeInHierarchy) continue;
                var health = entity.GetComponent<HealthComponent>();
                if (health != null && !health.Alive) continue;

                int team = entityTeam[entity];
                counts[team] = counts.GetValueOrDefault(team) + 1;
            }

            return counts;
        }

        private void EndMatch(int winningTeam)
        {
            matchEnded = true;
            Debug.Log($"[MatchManager] Match over — winning team {winningTeam}");

            OnMatchEnded?.Invoke(winningTeam);
            BroadcastMatchEndedClientRpc(winningTeam);
        }

        // NotServer, not ClientsAndHost: EndMatch only ever runs server-side and has
        // already raised the event locally, so a host (server + client in one process)
        // would otherwise show its result screen twice.
        [Rpc(SendTo.NotServer)]
        private void BroadcastMatchEndedClientRpc(int winningTeam)
        {
            OnMatchEnded?.Invoke(winningTeam);
        }
    }
}
