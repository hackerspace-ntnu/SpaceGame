// The workshop's story. Raiders broke in, smashed the gate's gear train on their way out and took the Heart Gear. One
// Shellback got out before the gate jammed; he waits in front of it, frantic, and begs whoever comes by to open it.
// When the gate opens he walks in, his family (or the workshop's residents) meet the player in the middle of the main
// room, and he asks them to get the gear back. That lays the trail the thieves left from the gate to their camp, an
// outpost far out in the dunes, where they and the gear appear once the camp's chunk streams in. The gear brought back
// to the middle of the main room opens the last chapter: in thanks the Shellbacks offer the sealed reactor core in their
// vault, deep behind the turning rooms. The player opens the vault's gear lock, carries the core up to the hoist's
// console on the third floor, hangs it on the hook and lowers it onto their ship, which fits it and ends the story.
//
// The server decides every step (Network.Decides) and announces the state on the building's channel; the Shellbacks'
// lines, the briefing, the trail, the thanks and the visor's objective all follow from that state on every machine.
// The gate itself is the GearDoor's.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using SpaceGame.Agents;
using SpaceGame.Core;
using SpaceGame.Diagnostics;
using SpaceGame.Gameplay;
using SpaceGame.Gameplay.Objectives;
using SpaceGame.Items;
using SpaceGame.Core.Persistence;
using SpaceGame.Vehicles;

namespace SpaceGame.World
{
    /// <summary>
    /// The gate's Shellback, the meeting in the main room and the hunt for the stolen gear. Built by
    /// <c>WorkshopMissionBuilder</c> into the workshop prefab; saved by <c>WorkshopMissionSaveable</c>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WorkshopMission : MonoBehaviour
    {
        [Header("Places")]
        [Tooltip("The gate whose opening starts the meeting.")]
        [SerializeField] private GearDoor gate;

        [Tooltip("Where the Shellback paces while the gate is shut, outside it, clear of the leaves' swing. +Z faces the gate.")]
        [SerializeField] private Transform gateStand;

        [Tooltip("The middle of the main room, where everyone meets and the gear is brought back. +Z faces the lane in.")]
        [SerializeField] private Transform meeting;

        [Header("People")]
        [Tooltip("The Shellback who got out: a WorkshopGatekeeper. Spawned by the server, saved by nobody: rebuilt from this state.")]
        [SerializeField] private GameObject gatekeeperPrefab;

        [Tooltip("The workshop's own residents, for a workshop that has them (WorkshopOutpost).")]
        [SerializeField] private WorkshopGathering gathering = new();

        [Tooltip("The family shut inside, for a workshop that has no residents (the bare prefab placed in a chunk).")]
        [SerializeField] private WorkshopFamily family = new();

        [Header("Thieves")]
        [Tooltip("What they stole: the item the player brings back.")]
        [SerializeField] private InventoryItem stolenGear;

        [SerializeField] private GearThiefBand thieves = new();

        [Tooltip("The trail the thieves left from the gate to their camp.")]
        [SerializeField] private WorkshopClueTrail trail = new();

        [Tooltip("Where the thieves' camp stands, known before its chunk is loaded. Written by Place Gear Thief Camp.")]
        [SerializeField] private GearThiefCampSite campSite;

        [Header("Vault")]
        [Tooltip("The main room's hand cog: the room turned from the lane to a hallway opens the way to the vault.")]
        [SerializeField] private WorkshopWheel mainWheel;

        [Tooltip("Degrees either side of facing the lane the main room still counts as shut to the hidden hallways.")]
        [SerializeField, Range(1f, 45f)] private float laneSlack = 20f;

        [Tooltip("The vault's door, whose gear lock the player opens to reach the core.")]
        [SerializeField] private GearDoor vault;

        [Tooltip("What the Shellbacks give: the sealed reactor core, lying in the vault from the start.")]
        [SerializeField] private InventoryItem vaultCore;

        [Tooltip("Where the core rests in the vault, on its cradle. +Z along the core.")]
        [SerializeField] private Transform coreRest;

        [Tooltip("How far above its cradle the core is laid, in metres, so it settles onto it.")]
        [SerializeField, Min(0f)] private float coreDropHeight = 0.3f;

        [Tooltip("The hoist that lowers the core onto the ship: its fitting the core ends the story.")]
        [SerializeField] private WorkshopCrane crane;

        [Tooltip("The hoist's console on the third floor, where the core is taken.")]
        [SerializeField] private Transform craneConsole;

        [Tooltip("How far from the site the loaded camp (its GearThiefCamp) may stand and still be it, in metres.")]
        [SerializeField, Min(1f)] private float campMatchRadius = 30f;

        [Header("Pacing")]
        [Tooltip("How close to the middle of the main room a player must come for the meeting to start, in metres.")]
        [SerializeField, Min(1f)] private float meetRadius = 9f;

        [Tooltip("Seconds the Shellback is given to walk in from the gate before he is put in the main room.")]
        [SerializeField, Min(0f)] private float walkInPatience = 90f;

        [Tooltip("Seconds after the mission is given before the residents go back to their day.")]
        [SerializeField, Min(0f)] private float dismissAfterSeconds = 25f;

        [Tooltip("How close to the middle of the main room the stolen gear must be brought, in metres.")]
        [SerializeField, Min(1f)] private float homeRadius = 9f;

        [Tooltip("How far from the thieves' camp a dropped gear is looked for, for the waypoint, in metres.")]
        [SerializeField, Min(1f)] private float lostGearSearchRadius = 400f;

        [Tooltip("Seconds between the server's looks at the people, the thieves and the gear.")]
        [SerializeField, Min(0.05f)] private float thinkSeconds = 0.5f;

        [Tooltip("How far from his spot the NavMesh may be found to stand the Shellback on, in metres.")]
        [SerializeField, Min(0.5f)] private float standSampleDistance = 4f;

        [Tooltip("Seconds without any NavMesh at his spot before the console is told he cannot be stood there.")]
        [SerializeField, Min(1f)] private float noGroundWarnSeconds = 15f;

        [Tooltip("Slack on the server's check that a player examining a clue is standing at it, in metres.")]
        [SerializeField, Min(0f)] private float examineSlack = 1.5f;

        private static readonly List<WorkshopMission> Live = new();

        private readonly List<Transform> players = new();

        private WorkshopMissionState state;

        private WorkshopGatekeeper gatekeeper;
        private bool gatekeeperPlaced;
        private float gatekeeperWaitSince = -1f;
        private bool warnedNoGround;
        private float gatheringSince;
        private float dismissAt = -1f;
        private float nextThink;
        private Settlement settlement;
        private Coroutine askRoutine;

        private WorkshopMissionObjective objective;
        private bool pleaHeard;

        /// <summary>
        /// The state changed on this machine. True when the story just moved to a new phase in play (a briefing or a thanks
        /// may be spoken); false for a restore, a join, or a change within a phase.
        /// </summary>
        public event Action<bool> Changed;

        public WorkshopMissionPhase Phase => state.phase;

        /// <summary>A player has the stolen gear in their hotbar while hunting, or the reactor core in the vault chapter.</summary>
        public bool GearCarried => state.carried;

        /// <summary>The reactor core has been laid in the vault (and is a record of its own from then on).</summary>
        public bool CoreSpawned => state.coreSpawned;

        /// <summary>The hoist has fitted the core to a ship. Before the Shellbacks offered it, too: then their offer ends the story.</summary>
        public bool CoreFitted => state.coreFitted;

        /// <summary>The thieves' carrier snatched the gear up and is running with it.</summary>
        public bool CarrierHasGear => state.carrierHasGear;

        /// <summary>How many clues of the thieves' trail have been examined.</summary>
        public int CluesFound => state.clues;

        /// <summary>Where the thieves' camp is. False only when no camp has been placed in the world.</summary>
        public bool TryGetCamp(out Vector3 position)
        {
            bool placed = campSite != null && campSite.IsPlaced;
            position = placed ? campSite.Position : default;
            return placed;
        }

        /// <summary>The thieves and the gear exist at the camp.</summary>
        public bool BandSpawned => state.bandSpawned;

        public GearDoor Gate => gate;
        public GearDoor VaultDoor => vault;
        public WorkshopWheel MainWheel => mainWheel;

        /// <summary>The main room still faces the lane, as modelled: no hidden hallway is open from it yet.</summary>
        public bool MainRoomFacesLane => mainWheel != null && Mathf.Abs(Mathf.DeltaAngle(mainWheel.Angle, 0f)) <= laneSlack;
        public InventoryItem StolenGear => stolenGear;
        public InventoryItem VaultCore => vaultCore;
        public WorkshopCrane Crane => crane;
        public Pose CoreRest => PoseOf(coreRest);
        public Pose CraneConsole => PoseOf(craneConsole);
        public WorkshopClueTrail Trail => trail;
        public Pose GateStand => PoseOf(gateStand);
        public Pose Meeting => PoseOf(meeting);
        public float LostGearSearchRadius => lostGearSearchRadius;

        /// <summary>This machine's player has heard the Shellback's plea, so the gate is their objective now.</summary>
        public bool PleaHeard => pleaHeard;

        /// <summary>The live mission nearest <paramref name="position"/>, or null when none is loaded.</summary>
        public static WorkshopMission Nearest(Vector3 position)
        {
            WorkshopMission nearest = null;
            float best = float.MaxValue;
            foreach (WorkshopMission mission in Live)
            {
                float distance = (mission.transform.position - position).sqrMagnitude;
                if (distance >= best) continue;
                best = distance;
                nearest = mission;
            }
            return nearest;
        }

        private void Awake()
        {
            // The builder adds this in the editor, where nothing below may run: OnEnable's handlers would bake a
            // NetChannel into the workshop prefab.
            if (!Application.isPlaying) return;

            objective = new WorkshopMissionObjective(this);
            if (gate == null || gateStand == null || meeting == null || gatekeeperPrefab == null || stolenGear == null ||
                vault == null || vaultCore == null || coreRest == null || crane == null || craneConsole == null || mainWheel == null)
                Debug.LogError($"[WorkshopMission] '{name}' is missing its gate, places, Shellback, stolen gear, vault, core, " +
                               "crane or main hand cog. Rebuild it with Tools/SpaceGame/Workshop/Rebuild Workshop Prefab.", this);
        }

        private void OnEnable()
        {
            if (!Application.isPlaying) return;

            Live.Add(this);
            settlement = GetComponentInParent<Settlement>();
            this.NetOn(NetMsg.WorkshopMissionAsk, OnAsked);
            this.NetOn(NetMsg.WorkshopMissionState, OnAnnounced);
            this.NetOn(NetMsg.WorkshopMissionExamine, OnExamineRequested);
            if (crane != null) crane.Installed += OnCoreInstalled;

            // A late joiner, or a client streaming the workshop in, asks where the story stands.
            if (Network.IsNetworked && !Network.Server)
                askRoutine = StartCoroutine(Fault.Coroutine(this, "WorkshopMission.Ask",
                    this.NetToServerWhenSpawned(NetMsg.WorkshopMissionAsk, default)));
            Track(acquired: false);
            ShowTrail();
        }

        private void OnDisable()
        {
            if (!Application.isPlaying) return;

            Live.Remove(this);
            this.NetOff(NetMsg.WorkshopMissionAsk, OnAsked);
            this.NetOff(NetMsg.WorkshopMissionState, OnAnnounced);
            this.NetOff(NetMsg.WorkshopMissionExamine, OnExamineRequested);
            if (crane != null) crane.Installed -= OnCoreInstalled;
            if (askRoutine != null) StopCoroutine(askRoutine);
            askRoutine = null;
            QuestLog.Close(objective);
            trail.Hide();

            // The Shellbacks are this state's to rebuild, so they go with the workshop when its chunk streams out.
            if (Network.Decides && gatekeeper != null) GameServices.World.Despawn(gatekeeper.gameObject);
            gatekeeper = null;
            if (Network.Decides) family.Leave();
        }

        private void Update()
        {
            if (!Network.Decides || Time.time < nextThink) return;
            nextThink = Time.time + thinkSeconds;

            SessionPlayers.Collect(players);
            EnsureGatekeeper();
            LayCore();
            if (state.phase != WorkshopMissionPhase.Locked && !HasResidents) family.Appear(Meeting, InRow, this);
            switch (state.phase)
            {
                case WorkshopMissionPhase.Locked:
                    Plead();
                    break;
                case WorkshopMissionPhase.Gathering:
                    Gather();
                    break;
                case WorkshopMissionPhase.Hunting:
                    Hunt();
                    break;
                case WorkshopMissionPhase.Vault:
                    Fetch();
                    break;
                case WorkshopMissionPhase.Repaired:
                    Settle();
                    break;
            }
        }

        /// <summary>The workshop has residents of its own to gather; otherwise its family is spawned.</summary>
        private bool HasResidents => settlement != null && settlement.Society != null && settlement.Society.Residents.Count > 0;

        /// <summary>Everyone stands in the row while the meeting is on: gathering, and while the mission is being given.</summary>
        private bool InRow => state.phase == WorkshopMissionPhase.Gathering || dismissAt >= 0f && Time.time < dismissAt;

        // ── Server ───────────────────────────────────────────────────────────

        private void Plead()
        {
            if (gatekeeper != null) gatekeeper.Plead(GateStand, NearestPlayer(GateStand.position));
            if (gate != null && gate.IsSolved) Become(WorkshopMissionPhase.Gathering);
        }

        private void Gather()
        {
            Pose meet = Meeting;
            Transform greet = NearestPlayer(meet.position);
            gathering.Hold(settlement, meet, greet);
            family.Hold(meet, inRow: true, greet);

            bool ready = gatekeeper == null || !gatekeeper.IsAlive;
            if (!ready && gate != null && gate.StandsOpen)
            {
                if (Time.time - gatheringSince > walkInPatience && !gatekeeper.HasArrived) gatekeeper.PutAt(meet);
                // He runs in: his family is shut in there and the gate has only just let go.
                gatekeeper.Stand(meet, greet, run: true);
                ready = gatekeeper.HasArrived;
            }

            if (ready && WithinReach(greet, meet.position, meetRadius))
            {
                dismissAt = Time.time + dismissAfterSeconds;
                Become(WorkshopMissionPhase.Hunting);
            }
        }

        private void Hunt()
        {
            Pose meet = Meeting;
            Transform greet = NearestPlayer(meet.position);
            if (gatekeeper != null) gatekeeper.Stand(meet, greet);
            family.Hold(meet, InRow, greet);

            // The residents stay to hear the Shellback out, then go back to their day. A restored hunt holds nobody.
            if (InRow) gathering.Hold(settlement, meet, greet);
            else if (gathering.IsHolding)
            {
                dismissAt = -1f;
                gathering.Dismiss();
            }
            if (settlement != null) gathering.Return(settlement.WalkableHeart);

            // The camp is a long way off, in a chunk that streams in only as a player nears it: the band waits for it.
            if (!state.bandSpawned)
            {
                GearThiefCamp camp = TryGetCamp(out Vector3 site) ? GearThiefCamp.Nearest(site, campMatchRadius) : null;
                if (camp != null && thieves.TrySpawn(camp, stolenGear))
                {
                    WorkshopMissionState next = state;
                    next.bandSpawned = true;
                    Announce(next, live: false);
                }
                return;
            }

            WorkshopMissionState now = state;
            now.carrierHasGear = thieves.Tick(stolenGear, players);
            now.carried = TryFindCarrier(stolenGear, out IPlayerInventory carrier, out int slot, out Transform carrierBody);
            if (now.carried && WithinReach(carrierBody, meet.position, homeRadius) && carrier.TryRemoveItem(slot))
            {
                Become(WorkshopMissionPhase.Vault);
                return;
            }

            if (LooseItems.TryFind(stolenGear, meet.position, homeRadius, out PickupableItem lyingHome, out _))
            {
                GameServices.World.Despawn(lyingHome.gameObject);
                Become(WorkshopMissionPhase.Vault);
                return;
            }

            if (!now.Equals(state)) Announce(now, live: false);
        }

        /// <summary>
        /// The vault chapter: the Shellbacks wait in the hall and the server watches who carries the core. The hoist
        /// fitting it to a ship ends the story (<see cref="OnCoreInstalled"/>); a core fitted before they offered it
        /// ends it at once.
        /// </summary>
        private void Fetch()
        {
            Settle();
            if (state.coreFitted)
            {
                Become(WorkshopMissionPhase.Repaired);
                return;
            }

            WorkshopMissionState now = state;
            now.carried = TryFindCarrier(vaultCore, out _, out _, out _);
            if (!now.Equals(state)) Announce(now, live: false);
        }

        /// <summary>Server, once per world: the core on its cradle in the vault, a saved pickup like any other.</summary>
        private void LayCore()
        {
            if (state.coreSpawned || vaultCore == null || vaultCore.itemPrefab == null || coreRest == null) return;

            Pose rest = CoreRest;
            GameObject core = GameServices.World.Spawn(vaultCore.itemPrefab, rest.position + Vector3.up * coreDropHeight, rest.rotation);
            if (core == null) return;
            SaveableEntity.EnsureRuntime(core, vaultCore.ID);

            WorkshopMissionState next = state;
            next.coreSpawned = true;
            Announce(next, live: false);
        }

        /// <summary>Server: the hoist let the core go over an empty mount and the ship took it.</summary>
        private void OnCoreInstalled(ShipPartRack rack)
        {
            if (!Network.Decides) return;
            if (state.phase == WorkshopMissionPhase.Vault)
            {
                Become(WorkshopMissionPhase.Repaired);
                return;
            }

            // Fitted before the Shellbacks offered it: remembered, and their offer ends the story.
            WorkshopMissionState next = state;
            next.coreFitted = true;
            Announce(next, live: false);
        }

        /// <summary>The Shellback keeps to the middle of the hall and his family to their spots round it.</summary>
        private void Settle()
        {
            Pose meet = Meeting;
            Transform greet = NearestPlayer(meet.position);
            if (gatekeeper != null) gatekeeper.Stand(meet, greet);
            family.Hold(meet, inRow: false, greet);
        }

        /// <summary>A player whose hotbar holds <paramref name="item"/>, and the slot it is in.</summary>
        private bool TryFindCarrier(InventoryItem item, out IPlayerInventory inventory, out int slot, out Transform body)
        {
            foreach (Transform player in players)
            {
                if (player == null) continue;
                inventory = player.GetComponentInParent<IPlayerInventory>() ?? player.GetComponentInChildren<IPlayerInventory>();
                if (inventory == null) continue;

                for (slot = 0; slot < inventory.GetInventorySize(); slot++)
                {
                    InventorySlot held = inventory.GetSlot(slot);
                    if (held == null || held.Item != item) continue;
                    body = player;
                    return true;
                }
            }

            inventory = null;
            slot = -1;
            body = null;
            return false;
        }

        /// <summary>The Shellback, spawned where the story has him the first time the ground under him is ready.</summary>
        private void EnsureGatekeeper()
        {
            if (gatekeeper != null)
            {
                if (!gatekeeperPlaced) PlaceGatekeeper();
                return;
            }
            if (gatekeeperPrefab == null) return;

            Pose stand = StandFor(state.phase);
            if (!NavMesh.SamplePosition(stand.position, out NavMeshHit hit, standSampleDistance, NavMesh.AllAreas))
            {
                WarnIfNeverGrounded(stand.position);
                return;
            }

            GameObject spawned = NpcSpawn.Create(gatekeeperPrefab, hit.position, stand.rotation, this);
            if (spawned == null) return;
            if (spawned.TryGetComponent(out SceneTracked tracked)) tracked.SetKeepChunksLoaded(false);
            gatekeeper = spawned.GetComponent<WorkshopGatekeeper>();
            gatekeeperPlaced = true;
            if (gatekeeper == null)
                Debug.LogError($"[WorkshopMission] '{gatekeeperPrefab.name}' has no WorkshopGatekeeper.", gatekeeperPrefab);
        }

        // A Shellback who never appears looks exactly like one who is waiting for his ground to stream in; this is the
        // only thing that tells the two apart.
        private void WarnIfNeverGrounded(Vector3 stand)
        {
            if (gatekeeperWaitSince < 0f) gatekeeperWaitSince = Time.time;
            if (warnedNoGround || Time.time - gatekeeperWaitSince < noGroundWarnSeconds) return;

            warnedNoGround = true;
            Debug.LogWarning($"[WorkshopMission] no NavMesh within {standSampleDistance} m of the Shellback's spot at {stand} " +
                             $"after {noGroundWarnSeconds} s, so he has not appeared. Is the workshop under the baked NavMesh?", this);
        }

        /// <summary>After a restore moved the story on, the Shellback is put where it has him now.</summary>
        private void PlaceGatekeeper()
        {
            gatekeeperPlaced = true;
            if (gatekeeper.IsAlive) gatekeeper.PutAt(StandFor(state.phase));
        }

        private Pose StandFor(WorkshopMissionPhase at) => at == WorkshopMissionPhase.Locked ? GateStand : Meeting;

        /// <summary>Server: the story moves on, in play.</summary>
        private void Become(WorkshopMissionPhase next)
        {
            if (next == WorkshopMissionPhase.Gathering) gatheringSince = Time.time;
            WorkshopMissionState moved = state;
            moved.phase = next;
            moved.carried = false;
            moved.carrierHasGear = false;
            Announce(moved, live: true);
        }

        private void Announce(WorkshopMissionState next, bool live)
        {
            Adopt(next, live);
            this.NetToAll(NetMsg.WorkshopMissionState, new NetArg { A = next.WithLive(live) });
        }

        private void OnAsked(in NetArg arg, ulong sender)
        {
            if (!Network.Decides) return;
            this.NetToAll(NetMsg.WorkshopMissionState, new NetArg { A = state.Bits });
        }

        /// <summary>A player examined clue A of the trail. Counted if they stand at it; clues may be found out of order.</summary>
        private void OnExamineRequested(in NetArg arg, ulong sender)
        {
            if (!Network.Decides || state.phase != WorkshopMissionPhase.Hunting || !TryGetCamp(out Vector3 camp)) return;
            int index = arg.A;
            if (index < 0 || index >= trail.Count || index < state.clues) return;

            GameObject body = arg.Resolve();
            if (body == null || !Network.MayActFor(body, sender)) return;
            Vector3 clue = trail.Position(index, GateStand.position, camp);
            float reach = trail.ExamineRange + examineSlack;
            if ((body.transform.position - clue).sqrMagnitude > reach * reach) return;

            WorkshopMissionState next = state;
            next.clues = index + 1;
            Announce(next, live: false);
        }

        // ── Every machine ────────────────────────────────────────────────────

        /// <summary>The server's word. Idempotent: the host hears its own announcement back.</summary>
        private void OnAnnounced(in NetArg arg, ulong sender) =>
            Adopt(WorkshopMissionState.FromWire(arg.A), WorkshopMissionState.IsLive(arg.A));

        private void Adopt(WorkshopMissionState next, bool live)
        {
            if (next.Equals(state)) return;

            state = next;
            Track(acquired: live);
            ShowTrail();
            Changed?.Invoke(live);
        }

        /// <summary>This machine's player examined clue <paramref name="index"/>: ask the server to count it.</summary>
        public void RequestExamine(int index, Interactor interactor)
        {
            if (interactor == null) return;
            this.NetToServer(NetMsg.WorkshopMissionExamine, new NetArg { A = index }.With(interactor.gameObject));
        }

        /// <summary>This machine's player has heard the plea: the gate becomes their objective. Local only.</summary>
        public void HearPlea()
        {
            if (pleaHeard) return;
            pleaHeard = true;
            Track(acquired: true);
        }

        /// <summary>
        /// Opens the mission on this machine's quest log while it has something to ask, and closes it once it has not.
        /// <paramref name="acquired"/>: it opens because of something that happened just now, in play — the plea heard,
        /// the gate solved — so it becomes the active quest; streaming back in or a late join lists it quietly.
        /// </summary>
        private void Track(bool acquired)
        {
            if (objective == null) return;
            bool open = state.phase == WorkshopMissionPhase.Gathering || state.phase == WorkshopMissionPhase.Hunting ||
                        state.phase == WorkshopMissionPhase.Vault || state.phase == WorkshopMissionPhase.Locked && pleaHeard;
            if (open == QuestLog.IsOpen(objective)) return;
            if (open) QuestLog.Open(objective, acquired);
            else QuestLog.Close(objective);
        }

        /// <summary>The thieves' trail lies in the sand while they are being hunted, on every machine.</summary>
        private void ShowTrail()
        {
            if (!Application.isPlaying) return;
            if (state.phase == WorkshopMissionPhase.Hunting && TryGetCamp(out Vector3 camp))
                trail.Show(this, GateStand.position, camp, state.clues);
            else trail.Hide();
        }

        // ── Saving ───────────────────────────────────────────────────────────

        /// <summary>
        /// Server, or the only machine: land the story where a save left it. The thieves, the gear and the core are their
        /// own records; a band that was spawned, or a core that was laid, is never spawned again. A save from before the
        /// vault that had the gear home goes on to the vault.
        /// </summary>
        public void Restore(WorkshopMissionPhase restored, bool bandSpawned, int clues, bool coreSpawned, bool coreFitted = false)
        {
            if (!Network.Decides) return;
            if (restored == WorkshopMissionPhase.Done) restored = WorkshopMissionPhase.Vault;
            if (restored == WorkshopMissionPhase.Gathering) gatheringSince = Time.time;
            dismissAt = -1f;
            gatekeeperPlaced = gatekeeper == null;
            Announce(new WorkshopMissionState
            {
                phase = restored, bandSpawned = bandSpawned, clues = clues, coreSpawned = coreSpawned, coreFitted = coreFitted,
            }, live: false);
        }

        private Transform NearestPlayer(Vector3 point)
        {
            Transform nearest = null;
            float best = float.MaxValue;
            foreach (Transform player in players)
            {
                if (player == null) continue;
                float distance = (player.position - point).sqrMagnitude;
                if (distance >= best) continue;
                best = distance;
                nearest = player;
            }
            return nearest;
        }

        private static bool WithinReach(Transform body, Vector3 point, float radius) =>
            body != null && (body.position - point).sqrMagnitude <= radius * radius;

        private static Pose PoseOf(Transform marker) =>
            marker != null ? new Pose(marker.position, marker.rotation) : default;
    }
}
