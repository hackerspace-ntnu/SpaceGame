// Plan §10.2: the settlement at a glance. Plan — every resident's day as a Gantt row (edit mode builds the
// plans exactly as the server would, on the baked world NavMesh), post coverage and outrider share. Live — who
// is doing what right now, and jump-to-hour through DayNightCycle.JumpToHour (it re-anchors the cycle and
// replicates; Time.timeScale cannot speed up a hosted session's network clock). Report — the reaction
// matrix: each resident against four players, with the stance Attitude gives and the line LineMatcher
// would pick, and how many of those queries match no line at all.
using System;
using System.Collections.Generic;
using System.Linq;
using SpaceGame.World;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.Agents.Residents.EditorTools
{
    public sealed class ResidentsWindow : EditorWindow
    {
        private enum Tab { Plan, Live, Report }

        private readonly struct Scenario
        {
            public readonly string label;
            public readonly Topic topic;
            public readonly Observation observation;
            public readonly AggressionBand band;
            public readonly bool armed, grudge;
            public readonly int hits;
            public Scenario(string label, Topic topic, Observation observation, AggressionBand band, bool armed, bool grudge, int hits) =>
                (this.label, this.topic, this.observation, this.band, this.armed, this.grudge, this.hits) = (label, topic, observation, band, armed, grudge, hits);
        }

        private static readonly Scenario[] Scenarios =
        {
            new("stranger", Topic.Greeting, Observation.Approaching, AggressionBand.Calm, false, false, 0),
            new("armed", Topic.Remark, Observation.ArmedHeld, AggressionBand.Calm, true, false, 0),
            new("after a punch", Topic.Warning, Observation.Hitting, AggressionBand.Wary, false, true, 1),
            new("after kin harm", Topic.Warning, Observation.KinHarmed, AggressionBand.Drawn, false, true, 0),
        };
        private static readonly float[] JumpHours = { 0f, 6f, 9f, 12f, 15f, 18f, 21f };
        private const int HoursPerDay = 24;
        private const float MinutesPerHour = 60f;
        private const float NameWidth = 110f;
        private const float BarSaturation = 0.55f, BarValue = 0.85f;
        private static readonly Color BarBackground = new Color(0.15f, 0.15f, 0.15f);

        [SerializeField] private Settlement settlement;
        [SerializeField] private Tab tab;
        [SerializeField] private int day;
        private Vector2 scroll;
        [NonSerialized] private LineTable lineTable;
        [NonSerialized] private TextAsset lineSource;
        // Edit-mode plans per day, built once on the world NavMesh: a plan costs path queries.
        private static readonly Dictionary<(Settlement, int), DayPlan[]> previewPlans = new();

        [MenuItem("Tools/SpaceGame/Residents/Residents Window")]
        private static void OpenFromMenu() => Open(null);

        public static void Open(Settlement settlement)
        {
            var window = GetWindow<ResidentsWindow>("Residents");
            if (settlement != null) window.settlement = settlement;
        }

        /// <summary>
        /// The day's plans in roster order: the live ones in play, else built in edit mode exactly as the server
        /// would — on the baked world NavMesh, so regenerate with Generate + Bake World NavMesh for true times.
        /// </summary>
        public static DayPlan[] PlansFor(Settlement s, int day)
        {
            SettlementSociety society = s.Society;
            if (society == null) return Array.Empty<DayPlan>();
            if (Application.isPlaying) return society.Residents.Select(r => society.PlanFor(r.index, day)).ToArray();
            if (previewPlans.TryGetValue((s, day), out DayPlan[] cached)) return cached;

            // A fresh society, so every place it gathers is measured while the world NavMesh is there.
            using (new WorldNavMeshScope(s.transform.position))
            {
                var preview = new SettlementSociety(s, s.Culture);
                preview.RebuildPlans();
                DayPlan[] plans = preview.Residents.Select(r => preview.PlanFor(r.index, day)).ToArray();
                previewPlans[(s, day)] = plans;
                return plans;
            }
        }

        /// <summary>Forgets edit-mode plans, so the next look rebuilds them from the settlement as it is now.</summary>
        public static void ForgetPreview() => previewPlans.Clear();

        /// <summary>One plan as a 24 h bar, a colour per activity; travel shows as the gaps.</summary>
        public static void DrawDay(Rect bar, DayPlan plan, int day)
        {
            EditorGUI.DrawRect(bar, BarBackground);
            if (plan == null) return;
            float start = day * DayPlanner.MinutesPerDay;
            foreach (PlanSegment seg in plan.segments)
            {
                float from = Mathf.InverseLerp(start, start + DayPlanner.MinutesPerDay, seg.arrive), to = Mathf.InverseLerp(start, start + DayPlanner.MinutesPerDay, seg.leave);
                if (to > from) EditorGUI.DrawRect(new Rect(bar.x + bar.width * from, bar.y, bar.width * (to - from), bar.height), ColourOf(seg.activity));
            }
        }

        public static Color ColourOf(Activity activity) =>
            Color.HSVToRGB((float)activity / Enum.GetValues(typeof(Activity)).Length, BarSaturation, BarValue);

        public static string Clock(float minutes)
        {
            float m = Mathf.Repeat(minutes, DayPlanner.MinutesPerDay);
            return $"{(int)(m / MinutesPerHour):00}:{(int)(m % MinutesPerHour):00}";
        }

        private void OnInspectorUpdate()
        {
            if (Application.isPlaying) Repaint();
        }

        private void OnGUI()
        {
            settlement = (Settlement)EditorGUILayout.ObjectField("Settlement", settlement, typeof(Settlement), true);
            if (settlement == null)
                settlement = FindObjectsByType<Settlement>(FindObjectsSortMode.None).FirstOrDefault(s => s.HasResidents);
            if (settlement == null || settlement.Society == null || settlement.Society.Residents.Count == 0)
            {
                EditorGUILayout.HelpBox("No settlement with residents in the open scenes — give its config a culture and Generate.", MessageType.Info);
                return;
            }
            tab = (Tab)GUILayout.Toolbar((int)tab, Enum.GetNames(typeof(Tab)));
            scroll = EditorGUILayout.BeginScrollView(scroll);
            if (tab == Tab.Plan) DrawPlanTab();
            else if (tab == Tab.Live) DrawLiveTab();
            else DrawReportTab();
            EditorGUILayout.EndScrollView();
        }

        private void DrawPlanTab()
        {
            day = EditorGUILayout.IntField("Day", day);
            if (!Application.isPlaying && GUILayout.Button("Rebuild plans")) ForgetPreview();
            SettlementSociety society = settlement.Society;
            IReadOnlyList<Resident> roster = society.Residents;
            DayPlan[] plans = PlansFor(settlement, day);
            for (int i = 0; i < plans.Length; i++)
            {
                Rect row = EditorGUILayout.GetControlRect();
                GUI.Label(new Rect(row.x, row.y, NameWidth, row.height), roster[i] ? roster[i].DisplayName : "—");
                DrawDay(new Rect(row.x + NameWidth, row.y, row.width - NameWidth, row.height), plans[i], day);
            }
            EditorGUILayout.LabelField("Post coverage", EditorStyles.boldLabel);
            var worked = plans.Concat(PlansFor(settlement, day - 1)).Where(p => p != null).SelectMany(p => p.segments)
                .Where(seg => seg.activity == Activity.Work && society.Place(seg.place) != null).ToList();
            var posts = Enumerable.Range(0, society.PlaceCount).Select(society.Place)
                .Where(p => p.Kind == PlaceKind.Post && p.Use != null).Select(p => p.Use).Distinct();
            foreach (SpotUse post in posts)
            {
                int manned = Enumerable.Range(0, HoursPerDay).Count(h => worked.Any(seg => society.Place(seg.place).Use == post
                    && seg.arrive <= day * DayPlanner.MinutesPerDay + (h + 0.5f) * MinutesPerHour && seg.leave > day * DayPlanner.MinutesPerDay + (h + 0.5f) * MinutesPerHour));
                string gap = post.alwaysManned && manned < HoursPerDay ? " — always manned, HAS GAPS" : string.Empty;
                EditorGUILayout.LabelField(post.name, $"{manned}/{HoursPerDay} h{gap}");
            }
            int outriders = plans.Count(p => p != null && p.segments.Any(seg => seg.activity == Activity.Trip));
            EditorGUILayout.LabelField("Outrider share", $"{outriders}/{plans.Length} leave the settlement today");
        }

        private void DrawLiveTab()
        {
            DayNightCycle cycle = DayNightCycle.Main;
            if (!Application.isPlaying || cycle == null)
            {
                EditorGUILayout.HelpBox("Enter Play Mode (as host) to see residents live.", MessageType.Info);
                return;
            }
            EditorGUILayout.LabelField("Now", $"day {cycle.Day}, {Clock(cycle.HourOfDay * MinutesPerHour)}");
            using (new EditorGUILayout.HorizontalScope())
                foreach (float hour in JumpHours)
                    if (GUILayout.Button(Clock(hour * MinutesPerHour))) cycle.JumpToHour(hour);
            EditorGUILayout.LabelField("Name · activity · segment · band · offstage", EditorStyles.boldLabel);
            foreach (Resident r in settlement.Society.Residents.Where(r => r != null))
            {
                var routine = r.GetComponent<ResidentRoutine>();
                string segment = routine != null && routine.Current.HasValue ? routine.Current.Value.activity.ToString() : "—";
                string activity = r.Presence != null ? r.Presence.Activity.ToString() : "—";
                string band = r.Provocation != null ? r.Provocation.Band.ToString() : "—";
                EditorGUILayout.LabelField(r.DisplayName, $"{activity} · {segment} · {band} · {(r.IsOffstage ? "offstage" : "on")}");
            }
        }

        private void DrawReportTab()
        {
            TextAsset lines = settlement.Culture.lines;
            if (lines == null)
            {
                EditorGUILayout.HelpBox($"No line table on {settlement.Culture.name}.", MessageType.Warning);
                return;
            }
            bool reparse = GUILayout.Button("Reparse lines");
            if (reparse || lineSource != lines || lineTable == null)
                (lineTable, lineSource) = (LineTable.Parse(lines.text), lines);
            ResidentTuning tuning = ResidentTuning.Instance;
            int queries = 0, unmatched = 0;
            foreach (Resident r in settlement.Society.Residents.Where(r => r != null && r.archetype != null))
            {
                EditorGUILayout.LabelField($"{r.DisplayName} ({r.archetype.name}) — opens {r.OpeningStance}", EditorStyles.boldLabel);
                foreach (Scenario sc in Scenarios)
                {
                    var read = new PlayerRead { armed = sc.armed, recentHits = sc.hits };
                    Stance stance = Attitude.StanceFor(r.Nerve, r.Temper, 0f, sc.grudge, in read, sc.band, tuning.warmAt, tuning.coldAt);
                    var query = new LineQuery { person = r.displayName, archetype = r.archetype.name, topic = sc.topic, stance = stance, observation = sc.observation, seed = r.seed };
                    LineRow line = LineMatcher.Pick(lineTable, in query, Array.Empty<uint>(), out int score);
                    queries++;
                    if (line == null) unmatched++;
                    EditorGUILayout.LabelField(sc.label, line != null ? $"{stance} · \"{line.text}\" [{score}]" : $"{stance} · UNMATCHED", EditorStyles.wordWrappedLabel);
                }
            }
            EditorGUILayout.LabelField("Unmatched queries", $"{unmatched} of {queries}", EditorStyles.boldLabel);
        }
    }
}
