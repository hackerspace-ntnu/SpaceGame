using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// Plays every creature clip in the review scene so each can be judged and marked Good, Fix or
    /// Broken, with a note, into <see cref="CreatureAnimationReviewLog"/>.
    ///
    /// <para>
    /// Two ways to look. <b>One creature</b> lists all its clips and plays the selected one.
    /// <b>Lineup</b> picks a clip name (Walk, Idle, Death…) and plays it on every creature that
    /// has one, side by side, for comparing feel across the cast.
    /// </para>
    /// <para>
    /// Runs in edit mode: Play in a scene outside the build is redirected to the main menu by the
    /// Bootstrapper, and edit mode keeps the agents' AI and netcode out of the picture. Clips are
    /// sampled onto model instances from <see cref="EditorApplication.update"/>.
    /// </para>
    /// </summary>
    public sealed class CreatureAnimationReviewWindow : EditorWindow
    {
        private const string ScenePath = "Assets/Game/Scenes/Tests/CreatureAnimationReview.unity";
        private const float RowHeight = 18f;
        private const float RoleColumnWidth = 150f;
        // A frame that jumps this many median frames, and at least PopMinimumDegrees, reads as a pop.
        // Strikes and falls routinely reach 3-7x, so lower values flag half the cast.
        private const float PopRatioWarning = 8f;
        private const float PopMinimumDegrees = 5f;
        // The treadmill slider reaches this far past the creature's run speed.
        private const float SpeedSliderHeadroom = 1.5f;
        private const float MinimumSpeedSlider = 2f;
        private const float FrameMargin = 1.3f;

        private static readonly float[] Rates = { 0.1f, 0.25f, 0.5f, 1f, 2f };
        private static readonly string[] RateLabels = { "0.1×", "0.25×", "0.5×", "1×", "2×" };
        private static readonly string[] Modes = { "One creature", "Lineup" };
        private static readonly Color SelectedRow = new Color(0.24f, 0.48f, 0.9f, 0.35f);
        private static readonly Dictionary<ClipVerdict, Color> VerdictColours = new Dictionary<ClipVerdict, Color>
        {
            { ClipVerdict.Unreviewed, new Color(0.5f, 0.5f, 0.5f) },
            { ClipVerdict.Good, new Color(0.35f, 0.8f, 0.4f) },
            { ClipVerdict.Fix, new Color(0.95f, 0.7f, 0.2f) },
            { ClipVerdict.Broken, new Color(0.9f, 0.3f, 0.3f) },
        };

        private enum View { Side, Front, Top }

        [SerializeField] private int mode;
        [SerializeField] private int creatureIndex;
        [SerializeField] private int lineupIndex;
        [SerializeField] private int row;
        [SerializeField] private int rateIndex = 3;
        [SerializeField] private bool treadmill = true;
        [SerializeField] private bool orthographic;

        private List<ReviewCreature> catalog;
        private CreatureAnimationReviewLog log;
        private CreatureAnimationStage stage;
        private readonly List<(ReviewCreature creature, ReviewClip clip)> rows = new List<(ReviewCreature, ReviewClip)>();
        private string[] lineupNames = new string[0];
        private string[] lineupLabels = new string[0];
        private readonly Dictionary<ReviewClip, float> speedOverrides = new Dictionary<ReviewClip, float>();
        private bool playing = true;
        private bool frameOnBuild = true;
        private double time;
        private double lastTick;
        private Vector2 scroll;
        private float listHeight;
        private GUIStyle labelStyle;

        [MenuItem("Tools/SpaceGame/Creatures/Animation Review")]
        private static void Open() => GetWindow<CreatureAnimationReviewWindow>("Creature Animation Review").minSize = new Vector2(380f, 480f);

        private void OnEnable()
        {
            catalog = CreatureAnimationCatalog.Collect();
            log = CreatureAnimationReviewLog.Load();
            stage = new CreatureAnimationStage();
            var counts = catalog.SelectMany(c => c.Clips.Select(clip => clip.Name))
                .GroupBy(n => n).Where(g => g.Count() > 1)
                .OrderByDescending(g => g.Count()).ThenBy(g => g.Key).ToList();
            lineupNames = counts.Select(g => g.Key).ToArray();
            lineupLabels = counts.Select(g => $"{g.Key} ({g.Count()})").ToArray();
            creatureIndex = Mathf.Clamp(creatureIndex, 0, Mathf.Max(0, catalog.Count - 1));
            lineupIndex = Mathf.Clamp(lineupIndex, 0, Mathf.Max(0, lineupNames.Length - 1));
            RebuildRows();

            lastTick = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
            SceneView.duringSceneGui += OnSceneGUI;
            EditorSceneManager.sceneClosing += OnSceneClosing;
        }

        private void OnDisable()
        {
            EditorApplication.update -= Tick;
            SceneView.duringSceneGui -= OnSceneGUI;
            EditorSceneManager.sceneClosing -= OnSceneClosing;
            stage.Dispose();
        }

        private void OnSceneClosing(Scene scene, bool removingScene) => stage.Clear();

        private static bool InReviewScene => SceneManager.GetActiveScene().path == ScenePath;

        private (ReviewCreature creature, ReviewClip clip) Selected => rows[row];

        private CreatureAnimationStage.Lane SelectedLane => stage.Lanes[mode == 0 ? 0 : row];

        // ------------------------------------------------------------------ playback

        private void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            float dt = (float)(now - lastTick);
            lastTick = now;
            if (!InReviewScene || rows.Count == 0) return;
            bool built = UpdateStage();
            if (!playing && !built) return;
            if (playing) time += dt * Rates[rateIndex];
            stage.Sample(time, treadmill);
            SceneView.RepaintAll();
            Repaint();
        }

        // Builds the stage if needed and frames a freshly built one. True if it built.
        private bool UpdateStage()
        {
            bool built = EnsureStage();
            if (built && frameOnBuild) Frame(View.Side, stage.AllBounds);
            return built;
        }

        // Builds the lanes if the scene lost them or the creatures on show changed, and gives each
        // lane its clip and speed. True if it built.
        private bool EnsureStage()
        {
            var creatures = mode == 0 ? new[] { Selected.creature } : rows.Select(r => r.creature).ToArray();
            bool built = !stage.Shows(creatures);
            if (built) stage.Build(creatures);
            for (int i = 0; i < stage.Lanes.Count; i++)
            {
                var lane = stage.Lanes[i];
                lane.Clip = mode == 0 ? Selected.clip : rows[i].clip;
                lane.Speed = SpeedOf(lane.Clip);
                CreatureAnimationCatalog.Measure(lane.Clip, lane.Body);
            }
            if (built && mode == 0)
                foreach (ReviewClip clip in Selected.creature.Clips)
                    CreatureAnimationCatalog.Measure(clip, stage.Lanes[0].Body);
            return built;
        }

        private float SpeedOf(ReviewClip clip) => speedOverrides.TryGetValue(clip, out float s) ? s : clip.GameSpeed;

        private void RebuildRows()
        {
            rows.Clear();
            if (mode == 0 && catalog.Count > 0)
                rows.AddRange(catalog[creatureIndex].Clips.Select(clip => (catalog[creatureIndex], clip)));
            else if (mode == 1 && lineupNames.Length > 0)
                foreach (ReviewCreature creature in catalog)
                {
                    ReviewClip clip = creature.Clips.FirstOrDefault(c => c.Name == lineupNames[lineupIndex]);
                    if (clip != null) rows.Add((creature, clip));
                }
            row = Mathf.Clamp(row, 0, Mathf.Max(0, rows.Count - 1));
            frameOnBuild = true;
        }

        private void Restart()
        {
            time = 0;
            if (rows.Count == 0 || !InReviewScene) return;
            UpdateStage();
            stage.Sample(time, treadmill);
            SceneView.RepaintAll();
        }

        private void Step(int frames)
        {
            playing = false;
            AnimationClip clip = Selected.clip.Clip;
            time = Mathf.Max(0f, (float)time + frames / clip.frameRate);
            stage.Sample(time, treadmill);
            SceneView.RepaintAll();
        }

        private void Frame(View view, Bounds bounds)
        {
            frameOnBuild = false;
            var sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null) return;
            Quaternion rotation = view == View.Side ? Quaternion.LookRotation(Vector3.forward)
                : view == View.Front ? Quaternion.LookRotation(Vector3.left)
                : Quaternion.LookRotation(Vector3.down, Vector3.right);
            sceneView.LookAt(bounds.center, rotation, bounds.extents.magnitude * FrameMargin, orthographic, false);
        }

        // ------------------------------------------------------------------ GUI

        private void OnGUI()
        {
            HandleKeys();
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                int newMode = GUILayout.Toolbar(mode, Modes, EditorStyles.toolbarButton);
                if (newMode != mode)
                {
                    mode = newMode;
                    row = 0;
                    RebuildRows();
                    Restart();
                }
                GUILayout.FlexibleSpace();
                var all = catalog.SelectMany(c => c.Clips.Select(clip => (c, clip))).ToList();
                GUILayout.Label($"{log.Reviewed(all)}/{all.Count} reviewed", EditorStyles.miniLabel);
                if (GUILayout.Button("Copy report", EditorStyles.toolbarButton))
                {
                    EditorGUIUtility.systemCopyBuffer = log.Report(catalog);
                    ShowNotification(new GUIContent("Report copied as Markdown"));
                }
            }

            if (!InReviewScene)
            {
                EditorGUILayout.HelpBox("The review plays in its own scene, so nothing is added to the scene you work in.", MessageType.Info);
                if (GUILayout.Button("Open review scene")) OpenReviewScene();
                return;
            }
            if (rows.Count == 0)
            {
                EditorGUILayout.HelpBox("No creature with clips under Assets/Game/Prefabs/Agents/creatures.", MessageType.Warning);
                return;
            }
            UpdateStage();

            DrawPicker();
            DrawTransport();
            DrawView();
            DrawSelected();
            DrawList();
        }

        private void DrawPicker()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("◀", GUILayout.Width(28f))) Cycle(-1);
                if (mode == 0)
                {
                    var names = catalog.Select(c => c.Name).ToArray();
                    int pick = EditorGUILayout.Popup(creatureIndex, names);
                    if (pick != creatureIndex) PickList(pick);
                    GUILayout.Label($"{log.Reviewed(rows)}/{rows.Count}", GUILayout.Width(50f));
                }
                else
                {
                    int pick = EditorGUILayout.Popup(lineupIndex, lineupLabels);
                    if (pick != lineupIndex) PickList(pick);
                }
                if (GUILayout.Button("▶", GUILayout.Width(28f))) Cycle(1);
            }
        }

        private void DrawTransport()
        {
            var lane = SelectedLane;
            AnimationClip clip = lane.Clip.Clip;
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(playing ? "❚❚" : "▶", GUILayout.Width(32f))) playing = !playing;
                if (GUILayout.Button("|◀", GUILayout.Width(28f))) Step(-1);
                if (GUILayout.Button("▶|", GUILayout.Width(28f))) Step(1);
                rateIndex = GUILayout.Toolbar(rateIndex, RateLabels, EditorStyles.miniButton);
            }
            float local = CreatureAnimationStage.LocalTime(lane, time);
            float scrub = EditorGUILayout.Slider(clip.length > 0f ? local / clip.length : 0f, 0f, 1f);
            if (clip.length > 0f && !Mathf.Approximately(scrub, local / clip.length))
            {
                playing = false;
                time = scrub * clip.length;
                stage.Sample(time, treadmill);
                SceneView.RepaintAll();
            }
            int frames = Mathf.RoundToInt(clip.length * clip.frameRate);
            EditorGUILayout.LabelField($"frame {Mathf.RoundToInt(local * clip.frameRate)} / {frames}   " +
                                       $"{local:0.00} / {clip.length:0.00} s   {(clip.isLooping ? "loops" : "one-shot")}",
                EditorStyles.miniLabel);
        }

        private void DrawView()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                Bounds bounds = mode == 0 ? stage.AllBounds : SelectedLane.Bounds;
                if (GUILayout.Button("Side")) Frame(View.Side, bounds);
                if (GUILayout.Button("Front")) Frame(View.Front, bounds);
                if (GUILayout.Button("Top")) Frame(View.Top, bounds);
                if (mode == 1 && GUILayout.Button("All")) Frame(View.Side, stage.AllBounds);
                bool ortho = GUILayout.Toggle(orthographic, "Ortho", EditorStyles.miniButton, GUILayout.Width(48f));
                if (ortho != orthographic && SceneView.lastActiveSceneView != null)
                    SceneView.lastActiveSceneView.orthographic = ortho;
                orthographic = ortho;
            }

            var (creature, clip) = Selected;
            using (new EditorGUILayout.HorizontalScope())
            {
                treadmill = EditorGUILayout.ToggleLeft("Treadmill m/s", treadmill, GUILayout.Width(100f));
                float max = Mathf.Max(MinimumSpeedSlider, creature.RunSpeed * SpeedSliderHeadroom);
                float speed = EditorGUILayout.Slider(SpeedOf(clip), 0f, max);
                if (!Mathf.Approximately(speed, SpeedOf(clip))) SetSpeed(clip, speed);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("game speed:", EditorStyles.miniLabel, GUILayout.Width(70f));
                if (GUILayout.Button("stand", EditorStyles.miniButtonLeft)) SetSpeed(clip, 0f);
                if (GUILayout.Button($"walk {creature.WalkSpeed:0.00}", EditorStyles.miniButtonMid)) SetSpeed(clip, creature.WalkSpeed);
                if (GUILayout.Button($"run {creature.RunSpeed:0.00}", EditorStyles.miniButtonRight)) SetSpeed(clip, creature.RunSpeed);
            }
        }

        private void SetSpeed(ReviewClip clip, float speed)
        {
            speedOverrides[clip] = speed;
            SelectedLane.Speed = speed;
            stage.Sample(time, treadmill);
            SceneView.RepaintAll();
        }

        private void DrawSelected()
        {
            var (creature, clip) = Selected;
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField($"{creature.Name} · {clip.Name}", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(clip.Roles.Count > 0 ? $"Plays in game as {string.Join(", ", clip.Roles)}" : "Not played by the game",
                EditorStyles.miniLabel);
            string warning = Warning(clip);
            if (warning != null) EditorGUILayout.HelpBox(warning, MessageType.Warning);

            ClipVerdict verdict = log.Verdict(creature.Name, clip.Name);
            using (new EditorGUILayout.HorizontalScope())
            {
                VerdictButton("1  Good", ClipVerdict.Good, verdict);
                VerdictButton("2  Fix", ClipVerdict.Fix, verdict);
                VerdictButton("3  Broken", ClipVerdict.Broken, verdict);
                VerdictButton("0  Clear", ClipVerdict.Unreviewed, verdict);
            }
            string note = EditorGUILayout.TextField("Note", log.Note(creature.Name, clip.Name));
            if (note != log.Note(creature.Name, clip.Name)) log.SetNote(creature.Name, clip.Name, note);
            EditorGUILayout.LabelField("↑↓ clip · ←→ list · space play · , . frame · 1 2 3 0 verdict · esc leaves the note",
                EditorStyles.centeredGreyMiniLabel);
        }

        private void VerdictButton(string label, ClipVerdict value, ClipVerdict current)
        {
            Color previous = GUI.backgroundColor;
            if (value == current && value != ClipVerdict.Unreviewed) GUI.backgroundColor = VerdictColours[value];
            if (GUILayout.Button(label)) Judge(value);
            GUI.backgroundColor = previous;
        }

        private static string Warning(ReviewClip clip)
        {
            if (!clip.Measured) return null;
            if (clip.HeldPose)
                return clip.Roles.Count > 0
                    ? "Moves nothing: a held pose, or a clip bound to another skeleton — the game plays it as " + string.Join(", ", clip.Roles) + "."
                    : "Moves nothing: a held pose.";
            if (clip.PopRatio > PopRatioWarning && clip.PopDegrees > PopMinimumDegrees)
                return clip.PopFrame == 0
                    ? $"The loop jumps {clip.PopDegrees:0}° ({clip.PopRatio:0}× a normal frame) from its last frame back to its first: watch the seam."
                    : $"Frame {clip.PopFrame} jumps {clip.PopDegrees:0}° ({clip.PopRatio:0}× a normal frame): step there with , and . to see the pop.";
            return null;
        }

        private void DrawList()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            for (int i = 0; i < rows.Count; i++)
            {
                var (creature, clip) = rows[i];
                Rect rect = GUILayoutUtility.GetRect(0f, RowHeight, GUILayout.ExpandWidth(true));
                if (i == row) EditorGUI.DrawRect(rect, SelectedRow);
                EditorGUI.DrawRect(new Rect(rect.x + 2f, rect.y + 4f, 10f, 10f), VerdictColours[log.Verdict(creature.Name, clip.Name)]);
                string name = mode == 0 ? clip.Name : creature.Name;
                if (Warning(clip) != null) name += "  ⚠";
                GUI.Label(new Rect(rect.x + 18f, rect.y, rect.width - RoleColumnWidth - 18f, rect.height), name);
                GUI.Label(new Rect(rect.xMax - RoleColumnWidth, rect.y, RoleColumnWidth, rect.height),
                    string.Join(", ", clip.Roles), EditorStyles.miniLabel);
                if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
                {
                    Select(i);
                    Event.current.Use();
                }
            }
            EditorGUILayout.EndScrollView();
            if (Event.current.type == EventType.Repaint) listHeight = GUILayoutUtility.GetLastRect().height;
        }

        // ------------------------------------------------------------------ selection + keys

        private void HandleKeys()
        {
            Event e = Event.current;
            if (e.type != EventType.KeyDown || rows.Count == 0) return;
            if (GUIUtility.keyboardControl != 0)
            {
                if (e.keyCode == KeyCode.Escape)
                {
                    GUI.FocusControl(null);
                    e.Use();
                }
                return;
            }
            switch (e.keyCode)
            {
                case KeyCode.UpArrow: Select(row - 1); break;
                case KeyCode.DownArrow: Select(row + 1); break;
                case KeyCode.LeftArrow: Cycle(-1); break;
                case KeyCode.RightArrow: Cycle(1); break;
                case KeyCode.Space: playing = !playing; break;
                case KeyCode.Comma: Step(-1); break;
                case KeyCode.Period: Step(1); break;
                case KeyCode.Alpha1: case KeyCode.Keypad1: Judge(ClipVerdict.Good); break;
                case KeyCode.Alpha2: case KeyCode.Keypad2: Judge(ClipVerdict.Fix); break;
                case KeyCode.Alpha3: case KeyCode.Keypad3: Judge(ClipVerdict.Broken); break;
                case KeyCode.Alpha0: case KeyCode.Keypad0: Judge(ClipVerdict.Unreviewed); break;
                default: return;
            }
            e.Use();
            Repaint();
        }

        // Good moves straight on to the next clip; Fix and Broken stay so a note can be typed.
        private void Judge(ClipVerdict verdict)
        {
            var (creature, clip) = Selected;
            log.SetVerdict(creature.Name, clip.Name, verdict);
            if (verdict == ClipVerdict.Good && row < rows.Count - 1) Select(row + 1);
        }

        private void Select(int index)
        {
            if (rows.Count == 0) return;
            // The note saves on every keystroke; dropping focus keeps an open edit from carrying over to the next clip.
            GUIUtility.keyboardControl = 0;
            row = Mathf.Clamp(index, 0, rows.Count - 1);
            float top = row * RowHeight;
            scroll.y = Mathf.Clamp(scroll.y, top + RowHeight - listHeight, top);
            Restart();
            playing = true;
        }

        private void Cycle(int direction)
        {
            int count = mode == 0 ? catalog.Count : lineupNames.Length;
            if (count > 0) PickList(((mode == 0 ? creatureIndex : lineupIndex) + direction + count) % count);
        }

        private void PickList(int index)
        {
            GUIUtility.keyboardControl = 0;
            if (mode == 0) creatureIndex = index;
            else lineupIndex = index;
            row = 0;
            RebuildRows();
            Restart();
        }

        private static void OpenReviewScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (File.Exists(ScenePath))
            {
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                return;
            }
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        // ------------------------------------------------------------------ scene labels

        private void OnSceneGUI(SceneView view)
        {
            if (!InReviewScene || stage.Lanes.Count == 0) return;
            if (labelStyle == null)
                labelStyle = new GUIStyle(EditorStyles.whiteBoldLabel) { alignment = TextAnchor.LowerCenter };
            for (int i = 0; i < stage.Lanes.Count; i++)
            {
                var lane = stage.Lanes[i];
                if (lane.Clip == null) continue;
                bool selected = mode == 0 || i == row;
                ClipVerdict verdict = log.Verdict(lane.Creature.Name, lane.Clip.Name);
                labelStyle.normal.textColor = verdict == ClipVerdict.Unreviewed ? Color.white : VerdictColours[verdict];
                Vector3 top = new Vector3(lane.Bounds.center.x, lane.Bounds.max.y, lane.Bounds.center.z);
                Handles.Label(top, $"{lane.Creature.Name}\n{lane.Clip.Name} · {lane.Speed:0.00} m/s", labelStyle);
                if (!selected) continue;
                Handles.color = SelectedRow;
                Handles.DrawWireCube(lane.Bounds.center, lane.Bounds.size);
            }
        }
    }
}
