// The main room's hoist once somebody takes its control console on the third floor. Until then it runs its round by
// itself (WorkshopHoistRound, on the shared clock). The first hand on the console takes it over for good: from then on
// it stands wherever it was last driven to, which is saved, and it can carry one load on its hook — the sealed reactor
// core from the Shellbacks' vault — down onto the ship parked in the pit, where letting go of it fits it.
//
// The server decides where the hoist is and what hangs on it, and announces both (WorkshopCraneState). The operator's
// own machine drives its copy at once from the keys and takes the server's word when they let go, the way a hand cog
// turns its room (WorkshopWheel). Every other machine eases after the announcements.
using System;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Audio;
using SpaceGame.Core;
using SpaceGame.Diagnostics;
using SpaceGame.Gameplay;
using SpaceGame.Items;
using SpaceGame.Core.Persistence;
using SpaceGame.Vehicles;

namespace SpaceGame.World
{
    /// <summary>
    /// The hoist under a driver's hand: where it stands, what hangs on its hook, and the rules for both. Built by
    /// <c>WorkshopCraneBuilder</c>; the console that drives it is <see cref="WorkshopCraneConsole"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WorkshopCrane : MonoBehaviour
    {
        private const int AskVerb = -1;
        private const int LatchVerb = 1;
        private const int HangVerb = 2;

        private const int DrivenBit = 1 << 0;
        private const int LoadedBit = 1 << 1;
        private const int InstantBit = 1 << 2;
        private const int FittedBit = 1 << 3;

        [Header("Reach")]
        [Tooltip("Where the hoist can go. Written by the builder from the export's measurements.")]
        [SerializeField] private WorkshopCraneReach reach;

        [Tooltip("The bridge's run, the trolley's run, in the prefab's space: a metre of each moves the hook this way.")]
        [SerializeField] private Vector3 bridgeAxis = Vector3.right;
        [SerializeField] private Vector3 trolleyAxis = Vector3.forward;

        [Tooltip("The point of the hook a load hangs from, as modelled, in the prefab's space.")]
        [SerializeField] private Vector3 hookPoint;

        [Tooltip("The hook block's box about the hook point, in metres: what it bumps into the world with.")]
        [SerializeField] private Bounds hookBox = new(Vector3.up, new Vector3(1f, 3f, 1f));

        [Tooltip("Everything that moves with the hoist (the lift node): never something it bumps into.")]
        [SerializeField] private Transform hoistParts;

        [Tooltip("Where a load is drawn: a metre-scale child of the hook block at the hook point.")]
        [SerializeField] private Transform loadPoint;

        [SerializeField] private WorkshopMachinery machinery;

        [Header("Feel")]
        [Tooltip("Metres a second the bridge and the trolley travel at full lever.")]
        [SerializeField, Min(0.1f)] private float travelSpeed = 2.4f;

        [Tooltip("Metres a second the hook winds and the gantry climbs at full lever.")]
        [SerializeField, Min(0.1f)] private float liftSpeed = 1.6f;

        [Tooltip("Seconds from still to full speed, and back.")]
        [SerializeField, Min(0.01f)] private float rampSeconds = 0.35f;

        [Tooltip("Metres a second a machine that only hears about the hoist eases it towards where the server has it.")]
        [SerializeField, Min(0.1f)] private float followSpeed = 6f;

        [Tooltip("Seconds between the server's announcements of a hoist on the move.")]
        [SerializeField, Min(0.02f)] private float publishSeconds = 0.1f;

        [Tooltip("What the hook and its load bump into. Players never stop it: it stops short of nothing but the building and the ship.")]
        [SerializeField] private LayerMask blockers = Physics.DefaultRaycastLayers;

        [Header("Load")]
        [Tooltip("The one thing the hook carries: the Shellbacks' sealed reactor core.")]
        [SerializeField] private InventoryItem load;

        [Tooltip("How the load is drawn on the hook: the model alone, no pickup.")]
        [SerializeField] private GameObject loadModel;

        [Tooltip("The ship module letting go of the load fits.")]
        [SerializeField] private ShipPartKind installs = ShipPartKind.ReactorCore;

        [Tooltip("How close the load's middle must come to an empty mount of that kind, in metres, for it to swing onto " +
                 "the mount; let go of there, it is fitted.")]
        [SerializeField, Min(0.5f)] private float snapRadius = 3f;

        [Tooltip("Seconds the load takes to swing onto a mount in reach, or back under the hook out of it.")]
        [SerializeField, Min(0.01f)] private float snapSeconds = 0.25f;

        [Tooltip("How far from the hook a loose load is looked for when the console grabs, in metres.")]
        [SerializeField, Min(0.5f)] private float grabRadius = 3.5f;

        [Tooltip("How far from the hook a player may stand to hang the load on it, in metres.")]
        [SerializeField, Min(0.5f)] private float hangReach = 5.5f;

        [Header("Sound")]
        [SerializeField] private SfxId latchSound = SfxId.ImpactMetal;
        [SerializeField] private SfxId installSound = SfxId.InteractWorkstationRepair;

        private static readonly Collider[] Overlaps = new Collider[32];

        private WorkshopCranePose pose;
        private WorkshopCranePose heard;
        private Vector3 velocity;
        private bool driven;
        private bool loaded;
        private bool heardOnce;
        private GameObject drawnLoad;
        private Bounds loadBox;
        // How the load hangs under the hook (in the hook point's frame), and where its drawn part sits on its own root.
        private Vector3 hangPosition;
        private Quaternion hangRotation;
        private Transform drawnPart;
        private Coroutine askRoutine;
        private WorkshopCranePose announced;
        private float nextPublish;

        /// <summary>Somebody has driven it: it no longer runs its round, and stands where it was left.</summary>
        public bool IsDriven => driven;

        /// <summary>The console is being worked from this machine, which then drives its own copy ahead of the server.</summary>
        public bool DrivenHere { get; set; }

        /// <summary>The reactor core hangs on the hook.</summary>
        public bool IsLoaded => loaded;

        public WorkshopCranePose Pose => pose;

        public InventoryItem Load => load;

        public ShipPartKind Installs => installs;

        /// <summary>The bridge's and the trolley's runs in the world: which way a metre of each takes the hook.</summary>
        public Vector3 BridgeDirection => transform.TransformDirection(bridgeAxis).normalized;

        public Vector3 TrolleyDirection => transform.TransformDirection(trolleyAxis).normalized;

        /// <summary>Where the hook point is now, in the world.</summary>
        public Vector3 HookPosition => HookAt(pose);

        /// <summary>The middle of the load hanging on the hook, in the world; the hook point with nothing on it.</summary>
        public Vector3 LoadCentre => loaded ? HookPosition + loadBox.center : HookPosition;

        /// <summary>Server: the load was let go of over the ship and fitted into it.</summary>
        public event Action<ShipPartRack> Installed;

        /// <summary>Any machine: what hangs on the hook, or whether it is driven, changed.</summary>
        public event Action Changed;

#if UNITY_EDITOR
        /// <summary>Editor, for the builder: what the hoist is, in one call.</summary>
        public void Author(WorkshopCraneReach hoistReach, Vector3 bridge, Vector3 trolley, Vector3 hook, Bounds hookBlock,
                           Transform parts, Transform loadAt, WorkshopMachinery hoistMachinery, InventoryItem carried,
                           GameObject carriedModel)
        {
            reach = hoistReach;
            bridgeAxis = bridge;
            trolleyAxis = trolley;
            hookPoint = hook;
            hookBox = hookBlock;
            hoistParts = parts;
            loadPoint = loadAt;
            machinery = hoistMachinery;
            load = carried;
            loadModel = carriedModel;
        }
#endif

        // ── Lifecycle ────────────────────────────────────────────────────────

        private void Awake()
        {
            if (!Application.isPlaying) return;
            if (machinery == null || loadPoint == null || load == null || loadModel == null)
                Debug.LogError($"[WorkshopCrane] '{name}' is missing its machinery, hook, load or load model. Rebuild it " +
                               "with Tools/SpaceGame/Workshop/Rebuild Workshop Prefab.", this);
        }

        private void OnEnable()
        {
            if (!Application.isPlaying) return;

            this.NetOn(NetMsg.WorkshopCraneOperate, OnOperateRequested);
            this.NetOn(NetMsg.WorkshopCraneState, OnAnnounced);

            // A late joiner, or a client streaming the workshop in, asks where the hoist is and what it carries.
            if (Network.IsNetworked && !Network.Server)
                askRoutine = StartCoroutine(Fault.Coroutine(this, "WorkshopCrane.Ask",
                    this.NetToServerWhenSpawned(NetMsg.WorkshopCraneOperate, new NetArg { A = AskVerb })));
        }

        private void OnDisable()
        {
            if (!Application.isPlaying) return;

            this.NetOff(NetMsg.WorkshopCraneOperate, OnOperateRequested);
            this.NetOff(NetMsg.WorkshopCraneState, OnAnnounced);
            if (askRoutine != null) StopCoroutine(askRoutine);
            askRoutine = null;
        }

        private void Update()
        {
            SnapLoad(Time.deltaTime);

            // Machines that only hear about the hoist ease after it; the driver's machine and the server drive it.
            if (!driven || !heardOnce) return;
            pose = new WorkshopCranePose(
                Mathf.MoveTowards(pose.bridge, heard.bridge, followSpeed * Time.deltaTime),
                Mathf.MoveTowards(pose.trolley, heard.trolley, followSpeed * Time.deltaTime),
                Mathf.MoveTowards(pose.height, heard.height, followSpeed * Time.deltaTime));
        }

        // ── Driving (the server, and the driver's own machine ahead of it) ──

        /// <summary>
        /// Server, or the driver's own machine: the first hand on the console. The round stops for good where it is, so
        /// the hoist does not jump.
        /// </summary>
        public void TakeOver()
        {
            if (driven) return;

            driven = true;
            Vector4 round = machinery != null ? machinery.RoundPose : Vector4.zero;
            pose = reach.Clamp(new WorkshopCranePose(round.x, round.y, WorkshopCraneReach.HeightOf(round.z, round.w)));
            heard = pose;
            velocity = Vector3.zero;
            Changed?.Invoke();
            if (Network.Simulates(this)) Announce(instant: true);
        }

        /// <summary>
        /// Move by <paramref name="command"/> for <paramref name="deltaTime"/>: each axis ramps towards its lever and stops
        /// short of whatever the hook or its load would run into. On the server it is the truth; on the driver's machine
        /// it is the guess the server's word corrects when they let go.
        /// </summary>
        public void Drive(WorkshopCraneCommand command, float deltaTime)
        {
            if (!driven || deltaTime <= 0f) return;

            float ramp = deltaTime / rampSeconds;
            velocity.x = Mathf.MoveTowards(velocity.x, command.Bridge * travelSpeed, ramp * travelSpeed);
            velocity.y = Mathf.MoveTowards(velocity.y, command.Trolley * travelSpeed, ramp * travelSpeed);
            velocity.z = Mathf.MoveTowards(velocity.z, command.Height * liftSpeed, ramp * liftSpeed);
            if (velocity == Vector3.zero) return;

            // One axis at a time, so a load against a wall still slides along it.
            WorkshopCranePose at = pose;
            for (int axis = 0; axis < 3; axis++)
            {
                if (velocity[axis] == 0f) continue;

                WorkshopCranePose next = at;
                float by = velocity[axis] * deltaTime;
                if (axis == 0) next.bridge += by;
                else if (axis == 1) next.trolley += by;
                else next.height += by;
                next = reach.Clamp(next);

                bool stuckAlready = axis == 2 && by > 0f && Blocked(at);
                if (!stuckAlready && Blocked(next))
                {
                    velocity[axis] = 0f;
                    continue;
                }
                at = next;
            }

            pose = at;
            heard = at;
            heardOnce = true;

            // Moved since it was last told: everyone else hears about it ten times a second.
            if (!Network.IsNetworked || !Network.Simulates(this) || Time.time < nextPublish || pose.Equals(announced)) return;
            nextPublish = Time.time + publishSeconds;
            Announce(instant: false);
        }

        /// <summary>Where the hook point would be at <paramref name="at"/>, in the world.</summary>
        public Vector3 HookAt(WorkshopCranePose at) => transform.TransformPoint(
            hookPoint + bridgeAxis.normalized * at.bridge + trolleyAxis.normalized * at.trolley + Vector3.up * at.height);

        /// <summary>Whether the hook block, or the load on it, would stand in anything but the hoist itself and people.</summary>
        private bool Blocked(WorkshopCranePose at)
        {
            Vector3 hook = HookAt(at);
            Bounds box = new(hook + hookBox.center, hookBox.size);
            if (loaded) box.Encapsulate(new Bounds(hook + loadBox.center, loadBox.size));

            int count = Physics.OverlapBoxNonAlloc(box.center, box.extents, Overlaps, Quaternion.identity, blockers,
                                                   QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider hit = Overlaps[i];
                if (hoistParts != null && hit.transform.IsChildOf(hoistParts)) continue;
                if (drawnLoad != null && hit.transform.IsChildOf(drawnLoad.transform)) continue;
                if (hit.GetComponentInParent<AgentController>() != null) continue;
                return true;
            }
            return false;
        }

        // ── The hook's load ──────────────────────────────────────────────────

        /// <summary>The console's grab lever: take hold of a loose core under the hook, or let go of the one on it.</summary>
        public void RequestLatch(GameObject driver)
        {
            if (driver != null) this.NetToServer(NetMsg.WorkshopCraneOperate, new NetArg { A = LatchVerb }.With(driver));
        }

        /// <summary>A player with the core in their hands hangs it on the hook.</summary>
        public void RequestHang(Interactor interactor)
        {
            if (interactor != null)
                this.NetToServer(NetMsg.WorkshopCraneOperate, new NetArg { A = HangVerb }.With(interactor.gameObject));
        }

        /// <summary>Whether <paramref name="player"/> stands near enough the hook to hang something on it.</summary>
        public bool WithinHangReach(Transform player) =>
            player != null && (player.position - HookPosition).sqrMagnitude <= hangReach * hangReach;

        private void OnOperateRequested(in NetArg arg, ulong sender)
        {
            if (!Network.Simulates(this)) return;

            if (arg.A == AskVerb)
            {
                Announce(instant: true);
                return;
            }

            GameObject actor = arg.Resolve();
            if (actor == null || !Network.MayActFor(actor, sender)) return;

            if (arg.A == LatchVerb)
            {
                if (loaded) LetGo();
                else Grab();
            }
            else if (arg.A == HangVerb) Hang(actor);
        }

        /// <summary>Any machine: a loose core lies close enough under the hook for the grab lever to take hold of it.</summary>
        public bool TryFindLooseLoad(out PickupableItem lying) =>
            LooseItems.TryFind(load, HookPosition, grabRadius, out lying, out _);

        /// <summary>Server: a loose core under the hook goes onto it.</summary>
        private void Grab()
        {
            if (!driven || !TryFindLooseLoad(out PickupableItem lying)) return;

            GameServices.World.Despawn(lying.gameObject);
            SetLoaded(true);
        }

        /// <summary>Server: the core in this player's hands goes onto the hook.</summary>
        private void Hang(GameObject actor)
        {
            if (!driven || loaded || !WithinHangReach(actor.transform)) return;

            IPlayerInventory inventory = actor.GetComponentInParent<IPlayerInventory>();
            int slot = inventory != null ? SlotHolding(inventory, load) : -1;
            if (slot < 0 || !inventory.TryRemoveItem(slot)) return;

            SetLoaded(true);
        }

        /// <summary>Server: over an empty mount it fits it; anywhere else the core drops off the hook.</summary>
        private void LetGo()
        {
            if (TryFindMount(LoadCentre, out ShipPartRack rack, out int socket) && rack.TryInstall(socket, installs))
            {
                SetLoaded(false, fitted: true);
                Installed?.Invoke(rack);
                return;
            }

            Vector3 at = LoadCentre;
            Quaternion facing = drawnLoad != null ? drawnLoad.transform.rotation : Quaternion.identity;
            SetLoaded(false, fitted: false);
            GameObject dropped = GameServices.World.Spawn(load.itemPrefab, at, facing);
            if (dropped != null) SaveableEntity.EnsureRuntime(dropped, load.ID);
        }

        /// <summary>The empty mount of the kind this hoist fits that lies nearest <paramref name="point"/>, within reach.</summary>
        public bool TryFindMount(Vector3 point, out ShipPartRack rack, out int socket)
        {
            rack = null;
            socket = -1;
            float best = snapRadius * snapRadius;
            foreach (ShipPartRack candidate in ShipPartRack.Active)
            {
                for (int i = 0; i < candidate.Sockets.Count; i++)
                {
                    ShipPartSocket mount = candidate.Sockets[i];
                    if (mount == null || !candidate.Accepts(i, installs)) continue;

                    float distance = (mount.Centre - point).sqrMagnitude;
                    if (distance >= best) continue;
                    best = distance;
                    rack = candidate;
                    socket = i;
                }
            }
            return rack != null;
        }

        private static int SlotHolding(IPlayerInventory inventory, InventoryItem item)
        {
            for (int slot = 0; slot < inventory.GetInventorySize(); slot++)
            {
                InventorySlot held = inventory.GetSlot(slot);
                if (held != null && held.Item == item) return slot;
            }
            return -1;
        }

        /// <summary>Server: the hook took hold or let go. Heard here at once, and by everyone else with the announcement.</summary>
        private void SetLoaded(bool value, bool fitted = false)
        {
            Vector3 heardAt = LoadCentre;
            ShowLoad(value);
            Clunk(fitted, heardAt);
            Changed?.Invoke();
            Announce(instant: false, fitted);
        }

        private void Clunk(bool fitted, Vector3 at) => Sfx.Play(fitted ? installSound : latchSound, at, GetInstanceID());

        // ── Every machine ────────────────────────────────────────────────────

        /// <summary>Server: tell everyone where the hoist is and what it carries.</summary>
        public void Announce(bool instant, bool fitted = false)
        {
            announced = pose;
            int flags = (driven ? DrivenBit : 0) | (loaded ? LoadedBit : 0) | (instant ? InstantBit : 0) |
                        (fitted ? FittedBit : 0);
            this.NetToAll(NetMsg.WorkshopCraneState, new NetArg { B = flags, P = pose.ToWire() });
        }

        /// <summary>
        /// The server's word. Idempotent: the host hears its own announcement back. The driver's own machine keeps its
        /// pose: it is a round trip ahead.
        /// </summary>
        private void OnAnnounced(in NetArg arg, ulong sender)
        {
            bool nowDriven = (arg.B & DrivenBit) != 0;
            bool nowLoaded = (arg.B & LoadedBit) != 0;
            bool instant = (arg.B & InstantBit) != 0;
            bool changed = nowDriven != driven || nowLoaded != loaded;

            driven = nowDriven;
            if (nowLoaded != loaded)
            {
                Vector3 heardAt = LoadCentre;
                ShowLoad(nowLoaded);
                if (!instant) Clunk((arg.B & FittedBit) != 0, heardAt);
            }
            if (changed) Changed?.Invoke();

            if (Network.Simulates(this) || DrivenHere) return;
            heard = WorkshopCranePose.FromWire(arg.P);
            if (instant || !heardOnce) pose = heard;
            heardOnce = true;
        }

        private void ShowLoad(bool value)
        {
            loaded = value;
            if (drawnLoad != null) Destroy(drawnLoad);
            drawnLoad = null;
            loadBox = default;
            if (!value || loadModel == null || loadPoint == null) return;

            // Hung by its middle with its top at the hook: measured off the model rather than authored, so another
            // load is drawn right without a number changing.
            drawnLoad = Instantiate(loadModel, loadPoint, false);
            Transform drawnAt = drawnLoad.transform;
            drawnAt.localPosition = Vector3.zero;
            Bounds drawn = WorldBounds(drawnLoad);
            drawnAt.position += loadPoint.position - new Vector3(drawn.center.x, drawn.max.y, drawn.center.z);
            loadBox = WorldBounds(drawnLoad);
            loadBox.center -= loadPoint.position;
            drawnPart = BiggestPart(drawnLoad);
            hangPosition = drawnAt.localPosition;
            hangRotation = drawnAt.localRotation;
        }

        /// <summary>
        /// Every machine, drawing only: a load within <see cref="snapRadius"/> of an empty mount swings onto it, its part
        /// laid exactly over the mount's own (the module's mesh is the hull's, exported again on its own), so the driver
        /// sees it sit where it will be fitted; out of reach it swings back under the hook. Where the load IS for the hoist (what it bumps into, what a let-go fits)
        /// stays under the hook: this moves the picture, never the rules.
        /// </summary>
        private void SnapLoad(float deltaTime)
        {
            if (drawnLoad == null) return;

            Transform drawnAt = drawnLoad.transform;
            Vector3 position = loadPoint.TransformPoint(hangPosition);
            Quaternion rotation = loadPoint.rotation * hangRotation;
            if (drawnPart != null && TryFindMount(LoadCentre, out ShipPartRack rack, out int socket))
            {
                // The root's pose that puts the drawn part where the mount's part is.
                Transform part = rack.Sockets[socket].Part;
                rotation = part.rotation * Quaternion.Inverse(Quaternion.Inverse(drawnAt.rotation) * drawnPart.rotation);
                position = part.position - rotation * (Quaternion.Inverse(drawnAt.rotation) * (drawnPart.position - drawnAt.position));
            }

            float ease = 1f - Mathf.Exp(-deltaTime / snapSeconds);
            drawnAt.SetPositionAndRotation(Vector3.Lerp(drawnAt.position, position, ease),
                                           Quaternion.Slerp(drawnAt.rotation, rotation, ease));
        }

        /// <summary>The drawn load's biggest mesh: the part that lines up with a mount's.</summary>
        private static Transform BiggestPart(GameObject drawn)
        {
            MeshFilter biggest = null;
            foreach (MeshFilter filter in drawn.GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh != null && (biggest == null || filter.sharedMesh.vertexCount > biggest.sharedMesh.vertexCount))
                    biggest = filter;
            return biggest != null ? biggest.transform : drawn.transform;
        }

        private static Bounds WorldBounds(GameObject drawn)
        {
            Bounds? all = null;
            foreach (Renderer renderer in drawn.GetComponentsInChildren<Renderer>(true))
            {
                if (all == null) all = renderer.bounds;
                else
                {
                    Bounds grown = all.Value;
                    grown.Encapsulate(renderer.bounds);
                    all = grown;
                }
            }
            return all ?? new Bounds(drawn.transform.position, Vector3.one);
        }

        // ── Saving ───────────────────────────────────────────────────────────

        /// <summary>Server, or the only machine: land the hoist as a save left it.</summary>
        public void Restore(bool wasDriven, WorkshopCranePose at, bool wasLoaded)
        {
            driven = wasDriven;
            pose = reach.Clamp(at);
            heard = pose;
            heardOnce = driven;
            velocity = Vector3.zero;
            if (wasLoaded != loaded) ShowLoad(wasLoaded);
            Changed?.Invoke();
            if (Network.Simulates(this)) Announce(instant: true);
        }
    }
}
