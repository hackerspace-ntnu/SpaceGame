// A ranch at the edge of a settlement: a corral (a SettlementPen behind a DoorInteraction gate), a tame animal in the
// yard to be won over, a range out in the desert where a wild herd is turned loose, and the wrangler who works it all.
// Its animals are found by RanchStock kind and distance, never by scene reference: they follow the streaming grid.
// Its quest (an ObjectiveChain of RanchSteps) says what the crew must do; the ranch says where the wrangler stands for
// each of those steps (its beats) and lays out what a step hands the crew — a saddle, lassos, the herd.
//
// The SERVER decides: it spawns the wrangler (NpcSpawn: saved by nobody, rebuilt where the quest has him whenever the
// chunk loads), walks him from beat to beat — stopping to wait whenever he leads and the crew fall behind — and lets
// penned stock graze its pen. EVERY machine answers what the steps ask — the herd's count, the gate, the tame animal's
// trust — from state that already replicates (positions, the gate's latch, CreatureTrust, SaddleSocket).
//
// What the ranch lays is saved by itself: the gear as ordinary pickups (ObjectiveWorld.Spawn), the herd as ordinary
// saved creatures. A step's saved Begun flag is what stops either being laid twice; the ranch remembers nothing.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using SpaceGame.Agents;
using SpaceGame.Core;
using SpaceGame.Gameplay.Objectives;
using SpaceGame.Items;

namespace SpaceGame.World
{
    /// <summary>What a ranch step hands the crew as it begins (<see cref="Ranch.Lay"/>).</summary>
    public enum RanchCue
    {
        None,
        /// <summary>One saddle, laid on the yard's gear rest.</summary>
        Saddle,
        /// <summary>A lasso for every player, dropped at the wrangler's feet.</summary>
        Lassos,
        /// <summary>The wild herd, turned loose on the range.</summary>
        Herd,
    }

    /// <summary>Where the wrangler is for one step of the ranch's quest, and what he says if asked.</summary>
    [Serializable]
    public struct RanchBeat
    {
        [Tooltip("The quest step this beat is for.")]
        public ObjectiveStep step;

        [Tooltip("Where the wrangler stands during the step, facing along it.")]
        public Transform mark;

        [Tooltip("He leads the crew there: he stops and waits for them whenever nobody is within the ranch's lead radius.")]
        public bool lead;

        [Tooltip("He runs there rather than walking.")]
        public bool run;

        [Tooltip("What he says, in turn, when talked to during the step.")]
        [TextArea(1, 3)] public string[] hints;
    }

    [DisallowMultipleComponent]
    public sealed class Ranch : MonoBehaviour
    {
        [Header("Identity")]
        [Tooltip("The asset the ranch's steps and wrangler find it by.")]
        [SerializeField] private RanchSite site;

        [Tooltip("The ranch's quest. Must be in the ObjectiveDirector's catalog.")]
        [SerializeField] private ObjectiveChain quest;

        [Header("Places")]
        [Tooltip("The corral the herd is driven into. Its gate is what shuts them in.")]
        [SerializeField] private SettlementPen corral;

        [Tooltip("Where the wrangler stands before anybody has taken the quest on.")]
        [SerializeField] private Transform wranglerHome;

        [Tooltip("Where he stands once the quest is done. Empty: back at his home.")]
        [SerializeField] private Transform doneMark;

        [Tooltip("Where gear for the yard (the saddle) is laid out.")]
        [SerializeField] private Transform gearRest;

        [Tooltip("The middle of the range the wild herd is turned loose on.")]
        [SerializeField] private Transform herdRange;

        [Header("Stock")]
        [Tooltip("A wild one of the herd: a saved creature prefab carrying RanchStock (Wild).")]
        [SerializeField] private GameObject wildPrefab;

        [Tooltip("How many wild ones are turned loose.")]
        [SerializeField, Min(1)] private int herdCount = 3;

        [Tooltip("How far from the range's middle they are turned loose, metres.")]
        [SerializeField, Min(1f)] private float herdSpread = 20f;

        [Tooltip("How far from a chosen point the NavMesh may be found to stand an animal on, metres.")]
        [SerializeField, Min(0.5f)] private float navSampleDistance = 6f;

        [Tooltip("Stock this far from the corral is this ranch's: the tame animal in the yard (RanchStock, Tame, placed " +
                 "in the scene) and the wild herd, metres.")]
        [SerializeField, Min(10f)] private float stockReach = 500f;

        [Header("Gear the wrangler hands out")]
        [SerializeField] private InventoryItem saddle;
        [SerializeField] private InventoryItem lasso;

        [Tooltip("How far above its spot gear is dropped, so it settles rather than starting in the ground, metres.")]
        [SerializeField, Min(0f)] private float gearDropHeight = 0.4f;

        [Tooltip("Metres between two pieces of gear laid side by side.")]
        [SerializeField, Min(0.1f)] private float gearSpacing = 0.8f;

        [Tooltip("How far in front of the wrangler he drops the lassos, metres.")]
        [SerializeField, Min(0f)] private float handOutReach = 1.5f;

        [Header("The wrangler")]
        [Tooltip("The wrangler: an NPC prefab with RanchWrangler for this site. Spawned by the server, saved by nobody.")]
        [SerializeField] private GameObject wranglerPrefab;

        [Tooltip("How often the server moves the wrangler on, seconds.")]
        [SerializeField, Min(0.05f)] private float thinkSeconds = 0.5f;

        [Tooltip("Leading, he stops to wait once nobody of the crew is within this distance of him, metres.")]
        [SerializeField, Min(2f)] private float leadRadius = 25f;

        [Tooltip("Waiting, he sets off again once somebody is within this share of the lead radius. Below 1 so he " +
                 "does not stop and start at its edge.")]
        [SerializeField, Range(0.1f, 1f)] private float resumeShare = 0.6f;

        [Tooltip("Where he stands for each step of the quest. A step with no beat leaves him where the last one put him.")]
        [SerializeField] private RanchBeat[] beats = Array.Empty<RanchBeat>();

        private static readonly List<Ranch> Loaded = new();

        private RanchWrangler wrangler;
        private Transform lastMark;
        private float nextThink;
        private bool waiting;
        private bool reportedNoWrangler;

        public RanchSite Site => site;
        public ObjectiveChain QuestChain => quest;
        public SettlementPen Corral => corral;
        /// <summary>EVERY machine: the ranch's tame animal, nearest the yard; null while none is loaded here.</summary>
        public CreatureTrust Tame
        {
            get
            {
                RanchStock nearest = null;
                float best = float.PositiveInfinity;
                foreach (RanchStock stock in RanchStock.All)
                {
                    if (stock.Of != RanchStock.Kind.Tame || !stock.IsAlive) continue;

                    float distance = FlatDistance(stock.transform.position, YardPosition);
                    if (distance > stockReach || distance >= best) continue;
                    best = distance;
                    nearest = stock;
                }

                return nearest != null ? nearest.GetComponent<CreatureTrust>() : null;
            }
        }
        public InventoryItem SaddleItem => saddle;

        /// <summary>EVERY machine: the corral's gate is shut (or it has none).</summary>
        public bool GateShut => corral == null || corral.Gate == null || !corral.Gate.IsOpen;

        public Vector3 GatePosition => corral != null && corral.Gate != null ? corral.Gate.transform.position : transform.position;
        public Vector3 YardPosition => gearRest != null ? gearRest.position : transform.position;
        public Vector3 RangePosition => herdRange != null ? herdRange.position : site != null ? site.Range : transform.position;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Loaded.Clear();

        /// <summary>EVERY machine: the ranch of <paramref name="ranchSite"/>, while it is loaded here; else null.</summary>
        public static Ranch Of(RanchSite ranchSite)
        {
            if (ranchSite == null) return null;

            foreach (Ranch ranch in Loaded)
                if (ranch.site == ranchSite) return ranch;

            return null;
        }

        private void OnEnable() => Loaded.Add(this);

        private void OnDisable()
        {
            Loaded.Remove(this);

            if (wrangler != null && Network.Decides) NpcSpawn.Remove(wrangler.gameObject);
            wrangler = null;
            lastMark = null;
        }

        // ── SERVER ──────────────────────────────────────────────────────────────

        private void Update()
        {
            if (!Network.Decides || Time.time < nextThink) return;
            nextThink = Time.time + thinkSeconds;

            ObjectiveDirector director = ObjectiveDirector.Instance;
            if (director == null || !director.IsRunning) return;

            Quest taken = director.Find(quest);
            bool hasBeat = TryGetCurrentBeat(taken, out RanchBeat beat);
            Transform mark = hasBeat && beat.mark != null ? beat.mark : MarkOutsideBeats(taken);

            if (wrangler == null) SpawnWrangler(mark);
            if (wrangler != null && wrangler.IsAlive) Direct(mark, hasBeat && beat.lead, hasBeat && beat.run);

            KeepPennedStock();
        }

        /// <summary>SERVER: the wrangler stands at the mark of <paramref name="step"/>'s beat.</summary>
        public bool GuideStandsFor(ObjectiveStep step) =>
            wrangler != null && TryGetBeat(step, out RanchBeat beat) && beat.mark != null && wrangler.IsAt(beat.mark.position);

        /// <summary>
        /// SERVER, once, as a ranch step begins: hand the crew what <paramref name="cue"/> names. Saved by what it lays —
        /// the step's own saved Begun flag is what keeps a reload from laying it again.
        /// </summary>
        public void Lay(RanchCue cue, ObjectiveWorld world)
        {
            switch (cue)
            {
                case RanchCue.Saddle:
                    LayGear(saddle, 1, YardPosition, gearRest != null ? gearRest.rotation : transform.rotation, world);
                    break;
                case RanchCue.Lassos:
                    Transform giver = wrangler != null ? wrangler.transform : herdRange;
                    Vector3 at = giver != null ? giver.position + giver.forward * handOutReach : RangePosition;
                    LayGear(lasso, Mathf.Max(1, world.Crew.Count), at, giver != null ? giver.rotation : Quaternion.identity, world);
                    break;
                case RanchCue.Herd:
                    LayHerd();
                    break;
            }
        }

        private Transform MarkOutsideBeats(Quest taken)
        {
            if (taken == null) return wranglerHome;
            if (taken.IsComplete) return doneMark != null ? doneMark : wranglerHome;

            // A step with no beat: stay with the last one.
            return lastMark != null ? lastMark : wranglerHome;
        }

        private void SpawnWrangler(Transform mark)
        {
            if (wranglerPrefab == null || mark == null) return;

            GameObject body = NpcSpawn.Create(wranglerPrefab, mark.position, mark.rotation, this);
            if (body == null) return;

            // He follows the crew between chunks; they hold the ground loaded, he does not.
            if (body.TryGetComponent(out SceneTracked tracked)) tracked.SetKeepChunksLoaded(false);

            if (!body.TryGetComponent(out wrangler))
            {
                if (!reportedNoWrangler)
                    Debug.LogError($"[Ranch] '{wranglerPrefab.name}' has no RanchWrangler, so nobody works the ranch.", this);
                reportedNoWrangler = true;
                NpcSpawn.Remove(body);
                return;
            }

            wrangler.PutAt(new Pose(mark.position, mark.rotation));
            lastMark = mark;
        }

        /// <summary>To the mark — unless he is leading and the crew have fallen behind, when he waits for them.</summary>
        private void Direct(Transform mark, bool lead, bool run)
        {
            if (mark == null) return;
            lastMark = mark;

            Transform nearest = NearestCrew(wrangler.transform.position, out float distance);
            if (lead && !wrangler.IsAt(mark.position))
            {
                float limit = waiting ? leadRadius * resumeShare : leadRadius;
                waiting = distance > limit;
                if (waiting)
                {
                    wrangler.Wait(nearest);
                    return;
                }
            }
            else
            {
                waiting = false;
            }

            wrangler.Stand(new Pose(mark.position, mark.rotation), nearest, run);
        }

        /// <summary>A wild one shut in the corral grazes it from then on; one that gets out again is let go of.</summary>
        private void KeepPennedStock()
        {
            if (corral == null) return;

            bool shut = GateShut;
            Vector3 centre = corral.WorldBounds.center;
            foreach (RanchStock stock in RanchStock.All)
            {
                if (!IsHerd(stock)) continue;

                bool inside = corral.Contains(stock.transform.position);
                if (inside && shut) stock.KeepIn(centre);
                else if (!inside) stock.LetOut();
            }
        }

        private void LayGear(InventoryItem item, int count, Vector3 origin, Quaternion facing, ObjectiveWorld world)
        {
            if (item == null)
            {
                Debug.LogError("[Ranch] A step asked for gear this ranch has no item for. Set it on the Ranch.", this);
                return;
            }

            Vector3 side = facing * Vector3.right;
            for (int i = 0; i < count; i++)
            {
                Vector3 at = origin + side * ((i - (count - 1) * 0.5f) * gearSpacing) + Vector3.up * gearDropHeight;
                world.Spawn(item, at, facing);
            }
        }

        private void LayHerd()
        {
            if (wildPrefab == null)
            {
                Debug.LogError("[Ranch] No wild herd prefab on the Ranch, so there is nothing to round up.", this);
                return;
            }

            Vector3 middle = RangePosition;
            for (int i = 0; i < herdCount; i++)
            {
                Vector2 offset = UnityEngine.Random.insideUnitCircle * herdSpread;
                Vector3 wanted = middle + new Vector3(offset.x, 0f, offset.y);
                Vector3 at = NavMesh.SamplePosition(wanted, out NavMeshHit hit, navSampleDistance, NavMesh.AllAreas)
                    ? hit.position : wanted;

                GameObject animal = GameServices.World.Spawn(wildPrefab, at,
                                                             Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f));

                // A herd grazing out on the range must not hold nine chunks loaded round each of its members.
                if (animal != null && animal.TryGetComponent(out SceneTracked tracked)) tracked.SetKeepChunksLoaded(false);
            }
        }

        // ── EVERY machine ───────────────────────────────────────────────────────

        /// <summary>
        /// The herd as this machine sees it: how many of the ranch's living wild stock there are, how many stand in the
        /// corral, and the stray nearest <paramref name="from"/> (null when every one is penned).
        /// </summary>
        public void CountHerd(Vector3 from, out int penned, out int total, out RanchStock nearestStray)
        {
            penned = 0;
            total = 0;
            nearestStray = null;
            float best = float.PositiveInfinity;

            foreach (RanchStock stock in RanchStock.All)
            {
                if (!stock.IsAlive || !IsHerd(stock)) continue;

                total++;
                if (corral != null && corral.Contains(stock.transform.position))
                {
                    penned++;
                    continue;
                }

                float distance = (stock.transform.position - from).sqrMagnitude;
                if (distance >= best) continue;
                best = distance;
                nearestStray = stock;
            }
        }

        /// <summary>What the wrangler says when talked to during <paramref name="step"/>; empty when it has no beat.</summary>
        public IReadOnlyList<string> HintsFor(ObjectiveStep step) =>
            TryGetBeat(step, out RanchBeat beat) && beat.hints != null ? beat.hints : Array.Empty<string>();

        private bool TryGetCurrentBeat(Quest taken, out RanchBeat beat)
        {
            beat = default;
            return taken != null && !taken.IsComplete && TryGetBeat(taken.Current, out beat);
        }

        private bool TryGetBeat(ObjectiveStep step, out RanchBeat beat)
        {
            foreach (RanchBeat candidate in beats)
            {
                if (candidate.step != step || step == null) continue;
                beat = candidate;
                return true;
            }

            beat = default;
            return false;
        }

        private bool IsHerd(RanchStock stock) =>
            stock.Of == RanchStock.Kind.Wild &&
            FlatDistance(stock.transform.position, corral != null ? corral.transform.position : transform.position) <= stockReach;

        private static float FlatDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private static Transform NearestCrew(Vector3 point, out float distance)
        {
            Transform nearest = null;
            distance = float.PositiveInfinity;

            foreach (PlayerIdentity member in PlayerIdentity.All)
            {
                if (member == null) continue;

                float flat = FlatDistance(member.transform.position, point);
                if (flat >= distance) continue;

                distance = flat;
                nearest = member.transform;
            }

            return nearest;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.95f, 0.7f, 0.3f);
            foreach (RanchBeat beat in beats)
                if (beat.mark != null) Gizmos.DrawWireSphere(beat.mark.position, 1f);

            if (herdRange == null) return;
            Gizmos.color = new Color(0.6f, 0.85f, 0.4f);
            Gizmos.DrawWireSphere(herdRange.position, herdSpread);
        }
    }
}
