// Plan §10.1: one resident, everything a designer tunes or asks about. Edit mode — the five per-resident
// knobs, what the two axes derive (read-only: lifestyle, hits to fight, forgive days, opening stance),
// and today's plan as a list and a 24 h bar, built offline exactly as the server would. Play mode adds
// the live segment, override, focus, the routine's decision trace, the memory ledger and the last lines
// with their winning score.
using UnityEditor;
using UnityEngine;

namespace SpaceGame.Agents.Residents.EditorTools
{
    [CustomEditor(typeof(Resident))]
    public sealed class ResidentInspector : Editor
    {
        private static readonly string[] Tunables = { "archetype", "displayName", "nerveOverride", "temperOverride", "bedtimeOffset" };
        private static readonly string[] Baked = { "index", "home", "campPosition", "seed", "settlement", "bonds" };
        private const float BarHeight = 16f;

        public override bool RequiresConstantRepaint() => Application.isPlaying;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            foreach (string field in Tunables) DrawField(field);
            using (new EditorGUI.DisabledScope(true))
                foreach (string field in Baked) DrawField(field);
            serializedObject.ApplyModifiedProperties();

            var r = (Resident)target;
            if (r.archetype == null)
            {
                EditorGUILayout.HelpBox("No archetype — Generate the settlement (its config needs a culture).", MessageType.Warning);
                return;
            }
            EditorGUILayout.LabelField("Derived", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Lifestyle", r.Lifestyle.ToString());
            EditorGUILayout.LabelField("Jostles to fight", r.JostlesToFight.ToString());
            EditorGUILayout.LabelField("Forgive days", r.ForgiveDays.ToString("0.#"));
            EditorGUILayout.LabelField("Opening stance", r.OpeningStance.ToString());
            DrawPlan(r);
            if (Application.isPlaying) DrawLive(r);
        }

        private void DrawField(string field)
        {
            SerializedProperty property = serializedObject.FindProperty(field);
            if (property != null) EditorGUILayout.PropertyField(property, true);
        }

        private static void DrawPlan(Resident r)
        {
            SettlementSociety s = r.Society;
            if (s == null || r.Settlement == null || r.index < 0 || r.index >= s.Residents.Count) return;
            int day = Application.isPlaying ? s.Day : 0;
            DayPlan plan = Application.isPlaying ? s.PlanFor(r) : ResidentsWindow.PlansFor(r.Settlement, day)[r.index];
            EditorGUILayout.LabelField($"Plan — day {day}", EditorStyles.boldLabel);
            ResidentsWindow.DrawDay(EditorGUILayout.GetControlRect(false, BarHeight), plan, day);
            if (plan == null) return;
            foreach (PlanSegment seg in plan.segments)
            {
                SettlementPlace place = s.Place(seg.place);
                string where = place == null ? "?" : place.Use != null ? place.Use.name : place.Kind.ToString();
                EditorGUILayout.LabelField($"{ResidentsWindow.Clock(seg.arrive)}–{ResidentsWindow.Clock(seg.leave)}",
                                           $"{seg.activity} at {where} {seg.place} (leaves {ResidentsWindow.Clock(seg.depart)})");
            }
        }

        private static void DrawLive(Resident r)
        {
            EditorGUILayout.LabelField("Live", EditorStyles.boldLabel);
            var routine = r.GetComponent<ResidentRoutine>();
            EditorGUILayout.LabelField("Segment", routine != null && routine.Current.HasValue ? routine.Current.Value.activity.ToString() : "—");
            EditorGUILayout.LabelField("Override", r.Override == OverrideKind.None ? "—" : $"{r.Override} until {r.OverrideUntil:0.0} s");
            EditorGUILayout.LabelField("Focus", r.Focus != null && r.Focus.IsFocused ? "in conversation" : "—");
            EditorGUILayout.LabelField("Band", r.Provocation != null ? r.Provocation.Band.ToString() : "—");
            EditorGUILayout.LabelField("Offstage", r.IsOffstage.ToString());
            if (routine != null)
            {
                EditorGUILayout.LabelField("Decision trace", EditorStyles.boldLabel);
                foreach (string reason in routine.Trace) EditorGUILayout.LabelField(reason, EditorStyles.miniLabel);
            }
            EditorGUILayout.LabelField("Memory", EditorStyles.boldLabel);
            foreach (var standing in r.Memory.FavorByProfile)
                EditorGUILayout.LabelField(standing.Key, $"favor {standing.Value:+0.#;-0.#;0}, regard {r.Memory.Regard(standing.Key):0.#}");
            foreach (ResidentMemory.Deed d in r.Memory.Deeds)
                EditorGUILayout.LabelField(d.profile, $"{d.act} (resident {d.victim}){(d.heard ? " heard" : "")} on day {d.day}, expires {d.expires:0.#}");
            var voice = r.GetComponent<ResidentVoice>();
            if (voice == null) return;
            EditorGUILayout.LabelField("Last lines", EditorStyles.boldLabel);
            if (voice.LastLine != null) EditorGUILayout.LabelField("Winner", $"{voice.LastLine.key} [{voice.LastScore}]");
            foreach (string line in voice.LastLines) EditorGUILayout.LabelField(line, EditorStyles.wordWrappedMiniLabel);
        }
    }
}
