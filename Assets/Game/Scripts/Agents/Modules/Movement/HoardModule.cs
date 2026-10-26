// A creature that hoards what glints. It sits on its nest and guards it, then goes foraging round
// its territory; any loose glinting item it notices it fetches home in its jaws and drops in the
// nest. Everything it takes can be won back from the nest.
//
// That one habit is the whole puzzle of a hoarder, because every way past it falls out of it:
//   sneak    — it only guards what it can SEE: take from the nest while it is out foraging
//   lure     — drop something shiny far off and it goes to fetch it
//   befriend — an item it takes while a player stands close and it is calm is a gift, and
//              CreatureTrust counts them; trust scales down the trespass it feels at its nest
//   fight    — the ordinary ProvocationModule fight, fed by the trespass below
//
// The carry is the creature's inventory, not a parented item: a NetworkObject cannot hang off a
// bone. The item goes into `carrySlot`; EntityEquipmentController shows a copy of it on the jaw
// socket, and NpcRandomLoadout (no candidates) replicates the slot so every machine shows the same
// copy. At the nest the world item is dropped back out through the item drop service.
//
// Priority sits just above AggressionTelegraphModule's. The trespass has to keep feeding the meter
// while the telegraph holds the creature planted in the drawn band, and a module the controller
// never reaches feeds nothing — so this one is ticked first and hands the frame down once drawn.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using SpaceGame.Core;
using SpaceGame.Items;

namespace SpaceGame.Agents
{
    public class HoardModule : BehaviourModuleBase
    {
        private enum Phase { Home, Forage, Fetch }

        [Header("Nest")]
        [Tooltip("The middle of the hoard. The creature rests here and drops what it brings here.")]
        [SerializeField] private Transform nest;

        [Tooltip("Items lying this close to the nest are the hoard: guarded, never fetched.")]
        [SerializeField] private float nestRadius = 4f;

        [Tooltip("It never fetches anything further than this from its nest.")]
        [SerializeField] private float territoryRadius = 80f;

        [Header("Rhythm")]
        [Tooltip("Seconds it sits on the nest before it goes foraging (random in this range).")]
        [SerializeField] private Vector2 homeSeconds = new(25f, 45f);

        [Tooltip("Seconds it forages before it heads home empty-jawed (random in this range).")]
        [SerializeField] private Vector2 forageSeconds = new(40f, 70f);

        [Header("Fetching")]
        [Tooltip("How far from itself it notices a loose glinting item.")]
        [SerializeField] private float noticeRadius = 30f;

        [Tooltip("How close it has to get to an item to take it.")]
        [SerializeField] private float grabDistance = 2.5f;

        [Tooltip("Seconds between looks for loot and intruders. Not per frame: every agent pays it.")]
        [SerializeField] private float rescanInterval = 0.5f;

        [Tooltip("The inventory slot its jaws are. EntityEquipmentController shows this slot.")]
        [SerializeField] private int carrySlot;

        [Header("Guarding")]
        [Tooltip("A player it can see this close to the nest is trespassing, and the meter climbs.")]
        [SerializeField] private float guardRadius = 12f;

        [Header("Gifts")]
        [Tooltip("An item it takes while calm, with a player standing this close, is a gift.")]
        [SerializeField] private float giftRadius = 10f;

        private readonly List<ScanContact> contacts = new(16);

        private EntityInventoryComponent inventory;
        private EntityEquipmentController equipment;
        private ProvocationModule provocation;
        private PerceptionModule perception;
        private CreatureTrust trust;

        private Phase phase;
        private float phaseEnds;
        private float scanTimer;
        private PickupableItem fetching;
        private Transform intruder;

        private void Reset() => SetPriorityDefault(ModulePriority.Ambient + 3);

        private void Awake()
        {
            inventory = GetComponent<EntityInventoryComponent>();
            equipment = GetComponent<EntityEquipmentController>();
            provocation = GetComponent<ProvocationModule>();
            perception = GetComponent<PerceptionModule>();
            trust = GetComponent<CreatureTrust>();
        }

        private void OnEnable()
        {
            BeginPhase(Phase.Home, homeSeconds);
            scanTimer = 0f;
            fetching = null;
            intruder = null;
        }

        public override string ModuleDescription =>
            "Rests on its nest and guards it, then forages its territory and fetches loose " +
            "glinting items home in its jaws. Feeds Trespass to ProvocationModule; counts gifts " +
            "into CreatureTrust.\n\n" +
            "• nest / nestRadius / territoryRadius — where it lives\n" +
            "• homeSeconds / forageSeconds — its rhythm\n" +
            "• guardRadius / giftRadius — what counts as trespass, and as a gift";

        private bool Carrying
        {
            get
            {
                InventorySlot slot = inventory.GetSlot(carrySlot);
                return slot != null && !slot.IsEmpty;
            }
        }

        private bool AtNest(Vector3 position) => Flat(position - nest.position) <= nestRadius;

        public override MoveIntent? Tick(in AgentContext context, float deltaTime)
        {
            if (nest == null || inventory == null) return null;

            scanTimer -= deltaTime;
            bool scan = scanTimer <= 0f;
            if (scan)
            {
                scanTimer = rescanInterval;
                intruder = FindIntruder();
            }

            Guard(deltaTime);

            // A fight, a flight or a last warning owns the body. Null hands the frame down to them.
            if (context.Targeting != null && context.Targeting.HasTarget) return null;
            if (provocation != null && provocation.Band >= AggressionBand.Drawn) return null;

            if (Carrying)
                return AtNest(context.Position) ? DropInNest() : Home(hurry: false);

            // Someone at the nest it can see: it comes straight back, whatever it was doing.
            if (intruder != null && phase != Phase.Home) BeginPhase(Phase.Home, homeSeconds);

            // A glint anywhere in view pulls it off the nest as well as off its rounds — which is
            // what makes a lure work. Not while somebody is at the nest: guarding comes first.
            if (phase != Phase.Fetch && intruder == null && scan && TryNotice(context.Position, out fetching))
                phase = Phase.Fetch;

            switch (phase)
            {
                case Phase.Fetch:
                    return Fetch(context.Position);

                case Phase.Forage:
                    if (Time.time >= phaseEnds) BeginPhase(Phase.Home, homeSeconds);
                    return null;   // roaming is WanderModule's

                default:
                    if (!AtNest(context.Position)) return Home(hurry: intruder != null);
                    if (Time.time >= phaseEnds && intruder == null) BeginPhase(Phase.Forage, forageSeconds);
                    // Sitting on the nest IS the behaviour, so it claims the frame; the telegraph
                    // turns its head toward whoever it is watching.
                    return MoveIntent.Idle();
            }
        }

        private void BeginPhase(Phase next, Vector2 seconds)
        {
            phase = next;
            phaseEnds = Time.time + Random.Range(seconds.x, seconds.y);
            if (next != Phase.Fetch) fetching = null;
        }

        private MoveIntent Home(bool hurry) =>
            MoveIntent.MoveTo(nest.position, stopDistance: nestRadius * 0.5f, isRunning: hurry);

        // ─────────── Loot ───────────

        private bool TryNotice(Vector3 from, out PickupableItem found)
        {
            found = null;
            ScannerRegistry.Collect(from, noticeRadius, contacts, contacts.Capacity);

            for (int i = 0; i < contacts.Count; i++)
            {
                if (contacts[i].Target is not PickupableItem pickup) continue;
                if (pickup.Item == null || !pickup.Item.glints) continue;

                float fromNest = Flat(pickup.transform.position - nest.position);
                if (fromNest <= nestRadius || fromNest > territoryRadius) continue;
                if (!NavMesh.SamplePosition(pickup.transform.position, out _, grabDistance, NavMesh.AllAreas)) continue;

                found = pickup;   // Collect sorts nearest first
                return true;
            }
            return false;
        }

        private MoveIntent? Fetch(Vector3 position)
        {
            // Gone: somebody picked it up first, or it rolled into the nest.
            if (fetching == null || !fetching.isActiveAndEnabled || AtNest(fetching.transform.position))
            {
                BeginPhase(Phase.Forage, forageSeconds);
                return null;
            }

            Vector3 at = fetching.transform.position;
            if ((at - position).sqrMagnitude > grabDistance * grabDistance)
                return MoveIntent.MoveTo(at, stopDistance: grabDistance * 0.6f);

            Take(fetching);
            BeginPhase(Phase.Home, homeSeconds);
            return Home(hurry: false);
        }

        /// <summary>Into the jaws: the world item goes, its state rides the slot, the copy shows.</summary>
        private void Take(PickupableItem pickup)
        {
            ItemState state = pickup.ReadState();
            inventory.RestoreSlot(carrySlot, pickup.Item);
            inventory.GetSlot(carrySlot).State = state;
            if (equipment != null) equipment.EquipSlot(carrySlot);

            Transform giver = NearestPlayer(transform.position, giftRadius);
            if (trust != null && giver != null && (provocation == null || provocation.Band == AggressionBand.Calm))
                trust.Gift();

            GameServices.World.Despawn(pickup.gameObject);
        }

        private MoveIntent DropInNest()
        {
            InventorySlot slot = inventory.GetSlot(carrySlot);
            InventoryItem item = slot.Item;
            ItemState state = slot.State;

            inventory.RestoreSlot(carrySlot, null);
            if (equipment != null) equipment.EquipSlot(carrySlot);
            GameServices.ItemDropService.DropItem(nest, item, state);

            BeginPhase(Phase.Home, homeSeconds);
            return MoveIntent.Idle();
        }

        // ─────────── Guarding ───────────

        private float Welcome => trust != null ? trust.Trust : 0f;

        private void Guard(float deltaTime)
        {
            if (intruder == null || provocation == null) return;
            provocation.AddAggression(AggressionInput.Trespass, deltaTime * (1f - Welcome), intruder);
        }

        /// <summary>
        /// The nearest player at the nest that this creature can actually see there. Nobody, to a
        /// tame creature: a friend at the nest is not an intruder.
        /// </summary>
        private Transform FindIntruder()
        {
            if (Welcome >= 1f) return null;

            Transform player = NearestPlayer(nest.position, guardRadius);
            if (player == null) return null;
            return perception == null || perception.IsVisible(player) ? player : null;
        }

        private static Transform NearestPlayer(Vector3 around, float radius)
        {
            Transform best = null;
            float bestSqr = radius * radius;
            IReadOnlyList<EntityFaction> all = EntityTargetRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                EntityFaction candidate = all[i];
                if (candidate == null) continue;

                float sqr = (candidate.transform.position - around).sqrMagnitude;
                if (sqr > bestSqr) continue;

                // The player's hand is what makes an entity a player — the rule MenaceSensor uses.
                if (candidate.GetComponentInChildren<EquipmentController>() == null) continue;

                best = candidate.transform;
                bestSqr = sqr;
            }
            return best;
        }

        private static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;

        protected override void OnValidate()
        {
            nestRadius = Mathf.Max(0.5f, nestRadius);
            territoryRadius = Mathf.Max(nestRadius, territoryRadius);
            noticeRadius = Mathf.Max(1f, noticeRadius);
            grabDistance = Mathf.Max(0.5f, grabDistance);
            rescanInterval = Mathf.Max(0.1f, rescanInterval);
            guardRadius = Mathf.Max(0f, guardRadius);
            giftRadius = Mathf.Max(0f, giftRadius);
        }
    }
}
