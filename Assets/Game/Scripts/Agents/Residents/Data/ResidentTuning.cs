// Every number the resident system is tuned by, in one asset (Resources/Residents/ResidentTuning).
// Durations are GAME minutes unless the name says otherwise, because a day plan is laid out on the
// clock — one game hour is cycleDuration / 24 real seconds. Per-archetype numbers do not live here:
// they are derived from nerve and temper (Resident.ApplyDerivedTuning), so a retune never needs a
// re-Assign. The defaults below are the shipped tuning, so a fresh CreateInstance plays correctly.
using System;
using UnityEngine;
using SpaceGame.Items;
using SpaceGame.Presentation;
using SpaceGame.World;

namespace SpaceGame.Agents.Residents
{
    /// <summary>One stretch of a lifestyle's template day, in hours 0–24.</summary>
    [Serializable]
    public struct DayBlock
    {
        public Activity activity;
        public float startHour;
        public float endHour;

        public DayBlock(Activity activity, float startHour, float endHour)
        {
            this.activity = activity;
            this.startHour = startHour;
            this.endHour = endHour;
        }
    }

    /// <summary>The shape of a day for one lifestyle. The planner staggers and seats it per resident.</summary>
    [Serializable]
    public class DayTemplate
    {
        public Lifestyle lifestyle;
        public DayBlock[] blocks;
    }

    /// <summary>What one kind of trip looks like: where it goes, how far, and what is carried.</summary>
    [Serializable]
    public struct TripKindRow
    {
        public TripKind kind;
        [Tooltip("Comma-separated SiteKind names a trip point may come from, e.g. \"ScrapField,Ruin\".")]
        public string siteKinds;
        [Tooltip("Metres from the settlement a trip point may lie.")]
        public float maxRadius;
        [Tooltip("Carries its held item drawn on the way out.")]
        public bool armed;
        public CharacterCue cue;
        public GameObject prop;
    }

    [CreateAssetMenu(menuName = "SpaceGame/Residents/Tuning", fileName = "ResidentTuning")]
    public sealed class ResidentTuning : ScriptableObject
    {
        private const string ResourcePath = "Residents/ResidentTuning";
        private const float MinutesPerHour = 60f;
        private const float MinutesPerDay = 24f * MinutesPerHour;

        // Real seconds per game day when no DayNightCycle is live — the shipped Sun.prefab's.
        private const float FallbackDaySeconds = 1600f;

        [Header("Day (game minutes)")]
        public Vector2 workSession = new Vector2(180f, 360f);
        public Vector2 breakLength = new Vector2(30f, 90f);
        [Tooltip("REAL seconds. A segment shorter than this is merged into its neighbour.")]
        public float minDwellRealSeconds = 20f;
        public float windowJitter = 30f;
        public Vector2 bedtimeSpread = new Vector2(21f * MinutesPerHour, 24f * MinutesPerHour);
        public Vector2 wake = new Vector2(5f * MinutesPerHour, 6.5f * MinutesPerHour);
        [Tooltip("One per lifestyle, at most six blocks each.")]
        public DayTemplate[] templates = DefaultTemplates();

        [Header("Trips (game minutes)")]
        public Vector2 departWindow = new Vector2(7f * MinutesPerHour, 14f * MinutesPerHour);
        public float returnBy = 15.5f * MinutesPerHour;
        [Tooltip("Still away this long past bedtime and unobserved → moved home.")]
        public float backstopDelay = 60f;
        public TripKindRow[] tripKinds = DefaultTripKinds();
        [Tooltip("How many trip points a settlement picks around itself, seeded off its position.")]
        [Min(0)] public int tripPoints = 8;
        [Tooltip("Metres from the settlement a trip point lies: past its edge, inside a morning's walk.")]
        public Vector2 tripDistance = new Vector2(60f, 150f);

        [Header("Errands, ambles and pairs")]
        [Tooltip("What errands carry in the hand, beyond the trip rows' props: hand tools (a bucket, a crate) whose grip and " +
                 "hold pose the equipment already solves. The index a machine receives is this list's position after the " +
                 "trip rows, so APPEND — never reorder.")]
        public InventoryItem[] carryItems = System.Array.Empty<InventoryItem>();
        [Tooltip("GAME minutes a chore round-trip run lasts before a short rest.")]
        public Vector2 choreSession = new Vector2(60f, 120f);
        [Tooltip("Of a roamer's free-time slots, the share spent wandering with no booked seat.")]
        [Range(0f, 1f)] public float ambleChance = 0.6f;
        [Tooltip("REAL seconds an amble stands at each stop before moving on.")]
        public Vector2 ambleStopSeconds = new Vector2(5f, 14f);
        [Tooltip("Metres from the heart an amble may roam, as a share of the settlement's extent.")]
        [Range(0.1f, 1f)] public float ambleReach = 0.7f;
        [Tooltip("Metres a follower keeps beside its partner.")]
        [Min(0.5f)] public float pairGap = 1.4f;
        [Tooltip("A leader this far ahead of its partner stops and waits.")]
        [Min(2f)] public float pairWaitDistance = 7f;
        [Tooltip("Two residents walking this close together may talk without stopping.")]
        [Min(1f)] public float walkTalkRange = 4f;
        [Tooltip("Seconds a pair walking and talking rest before the next exchange — shorter than a standing talk's rest, " +
                 "because a pair has a whole walk to fill.")]
        [Min(0f)] public float walkTalkRestSeconds = 30f;
        [Tooltip("Metres outside the outermost buildings the patrol ring runs.")]
        [Min(1f)] public float perimeterOffset = 5f;
        [Tooltip("Points the patrol ring is sampled at before the unwalkable ones are dropped.")]
        [Range(6, 64)] public int perimeterPoints = 24;
        [Tooltip("Walking speed multiplier on patrol: unhurried, watchful.")]
        [Range(0.3f, 1.5f)] public float patrolSpeed = 0.8f;

        [Header("Walking (metres / seconds)")]
        [Tooltip("The speed plans assume a resident walks at, to know when it must set off.")]
        [Min(0.1f)] public float walkSpeed = 1.5f;
        [Tooltip("A walk with no NavMesh path is estimated as the straight line times this.")]
        [Min(1f)] public float detourFactor = 1.3f;
        [Tooltip("How far out from a doorway a resident may stand to be 'at the door', nearest first.")]
        public float[] doorStandDistances = { 1.5f, 2.5f, 4f, 6f };
        [Tooltip("Held by a resident who sleeps in the open, with no dwelling to go into.")]
        public CharacterCue campSleepCue;

        [Header("Perception (metres / seconds)")]
        public float noticeRadius = 14f;
        public float earshot = 20f;
        public float avoidRadius = 10f;
        public int gunshotsToWake = 1;
        public float racketSecondsToWake = 6f;
        [Tooltip("Seconds each aggression band holds before cooling a step; also how long a woken resident looks.")]
        public float settleSeconds = 20f;

        [Header("Talk (real seconds)")]
        public float remarkGapPerPlayer = 9f;
        public float remarkGapPerResident = 45f;
        public int maxBubbles = 4;
        [Tooltip("Seconds between looks for two residents in one circle to start talking.")]
        [Min(0.1f)] public float pairInterval = 2f;
        [Tooltip("Most lines in one conversation between residents, the opener included.")]
        [Min(1)] public int maxTurns = 4;
        [Tooltip("Seconds after a conversation before either resident starts another.")]
        [Min(0f)] public float conversationRestSeconds = 90f;
        [Tooltip("Seconds a talking pair keep facing each other after the current line ends.")]
        [Min(0f)] public float focusAfterLine = 2f;

        [Header("Attitude")]
        public float familiarityPerTalk = 2f;
        public int dailyTalkCap = 10;
        [Tooltip("Regard (familiarity + favor) at or above this is Warm.")]
        public float warmAt = 20f;
        [Tooltip("Regard at or below minus this is Cold (bold) or Afraid (timid), grudge or not.")]
        public float coldAt = 20f;
        public float forgiveDaysBase = 3f;

        [Header("Favor (points, signed)")]
        [Tooltip("What each deed is worth to the resident it was done to. Everyone else who sees or hears of it " +
                 "gets this times their share below; each resident counts a deed once.")]
        public float favorForHit = -25f;
        public float favorForThreat = -10f;
        public float favorForKilling = -100f;
        public float favorForDefending = 25f;
        [Tooltip("The share of a deed's favor a resident takes when it was done to their family, a friend, a coworker, " +
                 "or anyone else in the settlement. The resident it was done to takes all of it.")]
        [Range(0f, 1f)] public float familyShare = 0.75f;
        [Range(0f, 1f)] public float friendShare = 0.5f;
        [Range(0f, 1f)] public float coworkerShare = 0.35f;
        [Range(0f, 1f)] public float neighbourShare = 0.2f;
        [Tooltip("Favor never goes past this either way.")]
        public float favorLimit = 100f;

        [Header("Aggression (real seconds)")]
        public float irritationStepSeconds = 8f;

        [Header("Rumours (real seconds)")]
        [Tooltip("How long a resident who just heard of an attack takes to pass it on to everyone within earshot — " +
                 "one hop of the news. Whoever saw it tells at once.")]
        public float rumourTellSeconds = 1.5f;
        [Tooltip("Seconds between passes over who is within earshot of whom, for the news.")]
        [Min(0.1f)] public float rumourSweepSeconds = 0.5f;

        private static ResidentTuning instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => instance = null;

        /// <summary>The project's tuning asset; the code defaults when the asset is missing. Never null.</summary>
        public static ResidentTuning Instance
        {
            get
            {
                if (instance) return instance;

                instance = Resources.Load<ResidentTuning>(ResourcePath);
                if (instance) return instance;

                Debug.LogWarning($"ResidentTuning: no asset at Resources/{ResourcePath} — playing on code defaults.");
                instance = CreateInstance<ResidentTuning>();
                return instance;
            }
        }

        /// <summary>Real seconds a span of game minutes lasts on the live day/night cycle.</summary>
        public float GameMinutesToSeconds(float minutes)
        {
            DayNightCycle cycle = DayNightCycle.Main;
            float daySeconds = cycle ? cycle.cycleDuration : FallbackDaySeconds;
            return minutes * daySeconds / MinutesPerDay;
        }

        public DayTemplate TemplateFor(Lifestyle lifestyle) =>
            templates == null ? null : Array.Find(templates, t => t != null && t.lifestyle == lifestyle);

        public bool TryGetTrip(TripKind kind, out TripKindRow row)
        {
            int at = tripKinds == null ? -1 : Array.FindIndex(tripKinds, r => r.kind == kind);
            row = at >= 0 ? tripKinds[at] : default;
            return at >= 0;
        }

        /// <summary>
        /// The prop byte presence replicates for a carried item: 1-based, the trip rows first, then <see cref="carryItems"/>.
        /// 0 (and nothing carried) when it is not in the list — the validator names such a chore.
        /// </summary>
        public byte PropIndexOf(InventoryItem item)
        {
            if (item == null) return 0;

            int trips = tripKinds != null ? tripKinds.Length : 0;
            int at = carryItems != null ? Array.IndexOf(carryItems, item) : -1;
            return at >= 0 ? (byte)(trips + 1 + at) : (byte)0;
        }

        /// <summary>The trip row's prop a replicated index stands for; null for 0, for a carried item and for a row that carries nothing.</summary>
        public GameObject PropAt(byte index)
        {
            int trips = tripKinds != null ? tripKinds.Length : 0;
            return index > 0 && index <= trips ? tripKinds[index - 1].prop : null;
        }

        /// <summary>The hand item a replicated index stands for; null for 0 and for a trip row.</summary>
        public InventoryItem CarryItemAt(byte index)
        {
            int trips = tripKinds != null ? tripKinds.Length : 0;
            int at = index - trips - 1;
            return index > trips && carryItems != null && at < carryItems.Length ? carryItems[at] : null;
        }

        private static DayTemplate[] DefaultTemplates() => new[]
        {
            new DayTemplate
            {
                lifestyle = Lifestyle.Stationed,
                blocks = new[]
                {
                    new DayBlock(Activity.Sleep, 0f, 6f), new DayBlock(Activity.Work, 6f, 18f),
                    new DayBlock(Activity.Stroll, 18f, 19f), new DayBlock(Activity.Hearth, 19f, 22f),
                    new DayBlock(Activity.Sleep, 22f, 24f),
                },
            },
            new DayTemplate
            {
                lifestyle = Lifestyle.Roamer,
                blocks = new[]
                {
                    new DayBlock(Activity.Sleep, 0f, 6f), new DayBlock(Activity.Stroll, 6f, 12f),
                    new DayBlock(Activity.Break, 12f, 13f), new DayBlock(Activity.Stroll, 13f, 18f),
                    new DayBlock(Activity.Hearth, 18f, 22f), new DayBlock(Activity.Sleep, 22f, 24f),
                },
            },
            new DayTemplate
            {
                lifestyle = Lifestyle.Outrider,
                blocks = new[]
                {
                    new DayBlock(Activity.Sleep, 0f, 6f), new DayBlock(Activity.Trip, 6f, 15.5f),
                    new DayBlock(Activity.Stroll, 15.5f, 18f), new DayBlock(Activity.Hearth, 18f, 22f),
                    new DayBlock(Activity.Sleep, 22f, 24f),
                },
            },
        };

        private static TripKindRow[] DefaultTripKinds() => new[]
        {
            new TripKindRow { kind = TripKind.Hunt, siteKinds = "AnimalGround", maxRadius = 400f, armed = true },
            new TripKindRow { kind = TripKind.Scout, siteKinds = "Landmark,Ruin,Camp", maxRadius = 600f, armed = true },
            new TripKindRow { kind = TripKind.Forage, siteKinds = "Landmark,WaterHole", maxRadius = 250f },
            new TripKindRow { kind = TripKind.Salvage, siteKinds = "ScrapField,Ruin", maxRadius = 500f },
            new TripKindRow { kind = TripKind.Water, siteKinds = "WaterHole", maxRadius = 400f },
        };
    }
}
