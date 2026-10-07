using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SpaceGame.Presentation;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// The humanoid animation vocabulary in one place: every <see cref="CharacterAction"/>, every
    /// <see cref="CharacterCue"/> and what answers it, and the <see cref="MomentReactions"/> table
    /// that turns gameplay moments into cues.
    ///
    /// <para>
    /// It exists because the vocabulary is spread over a few hundred small assets whose links
    /// point one way only — an action lists its cues, a cue knows nothing of its actions — so
    /// "what answers <c>greet</c>?" or "which cue does nothing yet?" cannot be answered from the
    /// Inspector. Actions come from <see cref="HumanoidControllerBuilder.CollectActions"/>, not the
    /// runtime catalog: the catalog is only as fresh as the last rebuild, and this window is where
    /// a new, not-yet-built action should already show.
    /// </para>
    /// <para>
    /// In play mode each action and cue can be played on the selected humanoid, through the same
    /// <see cref="CharacterActions.Play"/> and <see cref="BodyLanguage.Express"/> gameplay uses.
    /// </para>
    /// </summary>
    public sealed class AnimationLibraryWindow : EditorWindow
    {
        private const string AnyOption = "(any)";
        private const string UntaggedOption = "(untagged)";
        private const string TopLevelGroup = "(top level)";

        // Cue popup layout: the two fixed options come before the cue names.
        private const int AnyIndex = 0;
        private const int UntaggedIndex = 1;
        private const int FirstCueIndex = 2;

        private const float NameWidth = 220f;
        private const float EnumWidth = 90f;
        private const float NumberWidth = 60f;
        private const float WideWidth = 200f;
        private const float ButtonWidth = 60f;

        private static readonly string[] Tabs = { "Actions", "Cues", "Moments" };
        private static readonly string[] SlotOptions = WithAny(Enum.GetNames(typeof(CharacterAction.Slot)));
        private static readonly string[] PlaybackOptions = WithAny(Enum.GetNames(typeof(CharacterAction.Playback)));
        private static readonly Color UnansweredTint = new Color(1f, 0.6f, 0.45f);

        private sealed class Entry
        {
            public CharacterAction Action;
            public string Group;
            public string Search;
            public string CueNames;
            public bool Untagged;
            public float Seconds;
        }

        private readonly List<Entry> entries = new List<Entry>();
        private readonly List<CharacterCue> cues = new List<CharacterCue>();
        private readonly Dictionary<CharacterCue, List<CharacterAction>> tagged = new Dictionary<CharacterCue, List<CharacterAction>>();
        private readonly HashSet<string> collapsed = new HashSet<string>();
        private string[] cueOptions = { AnyOption, UntaggedOption };
        private MomentReactions moments;
        private int untaggedCount;

        private int tab;
        private string search = string.Empty;
        private int cueFilter = AnyIndex;
        private int slotFilter;
        private int playbackFilter;
        private Vector2 scroll;

        [MenuItem("Tools/SpaceGame/Animation/Animation Library")]
        private static void Open() => GetWindow<AnimationLibraryWindow>("Animation Library");

        private void OnEnable() => Refresh();

        private void OnProjectChange()
        {
            Refresh();
            Repaint();
        }

        // The play buttons and the "select a humanoid" note follow the selection.
        private void OnSelectionChange() => Repaint();

        private void Refresh()
        {
            entries.Clear();
            cues.Clear();
            tagged.Clear();
            untaggedCount = 0;

            cues.AddRange(AssetDatabase.FindAssets("t:" + nameof(CharacterCue))
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<CharacterCue>)
                .Where(cue => cue != null)
                .OrderBy(cue => cue.name, StringComparer.Ordinal));
            foreach (CharacterCue cue in cues) tagged[cue] = new List<CharacterAction>();

            foreach (CharacterAction action in HumanoidControllerBuilder.CollectActions())
            {
                List<CharacterCue> actionCues = action.Cues.Where(cue => cue != null).Distinct().ToList();
                foreach (CharacterCue cue in actionCues)
                {
                    if (!tagged.TryGetValue(cue, out List<CharacterAction> list)) tagged[cue] = list = new List<CharacterAction>();
                    list.Add(action);
                }
                if (actionCues.Count == 0) untaggedCount++;

                string cueNames = string.Join(", ", actionCues.Select(cue => cue.name));
                IEnumerable<string> clipNames = Enumerable.Range(0, action.VariantCount)
                    .Select(action.GetVariant)
                    .SelectMany(v => new[] { v.clip, v.aimDown, v.aimUp, v.enter, v.exit })
                    .Where(clip => clip != null)
                    .Select(clip => clip.name);

                entries.Add(new Entry
                {
                    Action = action,
                    Group = GroupOf(action),
                    Search = action.name + " " + cueNames + " " + string.Join(" ", clipNames),
                    CueNames = cueNames,
                    Untagged = actionCues.Count == 0,
                    Seconds = FirstVariantSeconds(action)
                });
            }

            cueOptions = new[] { AnyOption, UntaggedOption }.Concat(cues.Select(cue => cue.name)).ToArray();
            cueFilter = Mathf.Min(cueFilter, cueOptions.Length - 1);
            moments = AssetDatabase.LoadAssetAtPath<MomentReactions>(HumanoidControllerBuilder.ReactionsPath);
        }

        private void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label($"{entries.Count} actions · {cues.Count} cues · {untaggedCount} untagged actions",
                                EditorStyles.miniLabel);
                search = EditorGUILayout.TextField(search, EditorStyles.toolbarSearchField);
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton)) Refresh();

                // Not in play mode: the rebuild replaces the controller under every running Animator.
                using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                {
                    // Deferred out of OnGUI so a rebuild that refuses the content throws into the
                    // console rather than through this layout; the assets it writes refresh the
                    // window via OnProjectChange.
                    if (GUILayout.Button("Rebuild Controller", EditorStyles.toolbarButton))
                        EditorApplication.delayCall += () => HumanoidControllerBuilder.Rebuild();
                }
            }

            tab = GUILayout.Toolbar(tab, Tabs);
            using (var view = new EditorGUILayout.ScrollViewScope(scroll))
            {
                scroll = view.scrollPosition;
                if (tab == 0) DrawActions();
                else if (tab == 1) DrawCues();
                else DrawMoments();
            }
        }

        private void DrawActions()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Cue", GUILayout.ExpandWidth(false));
                cueFilter = EditorGUILayout.Popup(cueFilter, cueOptions);
                GUILayout.Label("Slot", GUILayout.ExpandWidth(false));
                slotFilter = EditorGUILayout.Popup(slotFilter, SlotOptions);
                GUILayout.Label("Playback", GUILayout.ExpandWidth(false));
                playbackFilter = EditorGUILayout.Popup(playbackFilter, PlaybackOptions);
            }

            CharacterActions body = PlayTarget<CharacterActions>("CharacterActions");
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Action", EditorStyles.boldLabel, GUILayout.Width(NameWidth));
                GUILayout.Label("Slot", EditorStyles.boldLabel, GUILayout.Width(EnumWidth));
                GUILayout.Label("Playback", EditorStyles.boldLabel, GUILayout.Width(EnumWidth));
                GUILayout.Label("Variants", EditorStyles.boldLabel, GUILayout.Width(NumberWidth));
                GUILayout.Label("Length", EditorStyles.boldLabel, GUILayout.Width(NumberWidth));
                GUILayout.Label("Cues", EditorStyles.boldLabel, GUILayout.Width(WideWidth));
                GUILayout.Label("Postures", EditorStyles.boldLabel, GUILayout.Width(WideWidth));
            }

            foreach (IGrouping<string, Entry> group in entries.Where(Passes).GroupBy(e => e.Group).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                bool open = EditorGUILayout.Foldout(!collapsed.Contains(group.Key), $"{group.Key} ({group.Count()})", true);
                if (open) collapsed.Remove(group.Key);
                else collapsed.Add(group.Key);
                if (!open) continue;

                foreach (Entry entry in group) DrawActionRow(entry, body);
            }
        }

        private void DrawActionRow(Entry entry, CharacterActions body)
        {
            CharacterAction action = entry.Action;
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(action.name, EditorStyles.linkLabel, GUILayout.Width(NameWidth))) Reveal(action);
                GUILayout.Label(action.BodySlot.ToString(), GUILayout.Width(EnumWidth));
                GUILayout.Label(action.Mode.ToString(), GUILayout.Width(EnumWidth));
                GUILayout.Label(action.VariantCount.ToString(), GUILayout.Width(NumberWidth));
                GUILayout.Label($"{entry.Seconds:0.00} s", GUILayout.Width(NumberWidth));
                GUILayout.Label(entry.CueNames, GUILayout.Width(WideWidth));
                GUILayout.Label(action.Postures == BodyPostures.Any ? "Any" : action.Postures.ToString(), GUILayout.Width(WideWidth));
                if (body == null) return;

                if (GUILayout.Button("Play", GUILayout.Width(ButtonWidth)) && !body.Play(action))
                    ShowNotification(new GUIContent($"{action.name} did not start: this machine does not write the " +
                                                    "body, or the controller needs a rebuild (see the console)."));
                if (action.Loops && GUILayout.Button("Stop", GUILayout.Width(ButtonWidth))) body.Stop(action.BodySlot);
            }
        }

        private void DrawCues()
        {
            BodyLanguage body = PlayTarget<BodyLanguage>("BodyLanguage");
            foreach (CharacterCue cue in cues)
            {
                if (!Matches(cue.name + " " + cue.Meaning)) continue;

                List<CharacterAction> answers = tagged[cue];
                Color previous = GUI.backgroundColor;
                if (answers.Count == 0) GUI.backgroundColor = UnansweredTint;
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    GUI.backgroundColor = previous;
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button(cue.name, EditorStyles.linkLabel, GUILayout.Width(NameWidth))) Reveal(cue);
                        GUILayout.Label(cue.Fallback != null ? "falls back to " + cue.Fallback.name : "no fallback",
                                        EditorStyles.miniLabel);
                        GUILayout.FlexibleSpace();
                        if (body != null && GUILayout.Button("Express", GUILayout.Width(ButtonWidth)))
                        {
                            CharacterAction picked = body.Express(cue);
                            ShowNotification(new GUIContent(picked != null
                                ? $"{cue.name}: {picked.name}"
                                : $"{cue.name}: nothing fits this body's posture, or this machine does not write it."));
                        }
                    }

                    if (!string.IsNullOrEmpty(cue.Meaning))
                        EditorGUILayout.LabelField(cue.Meaning, EditorStyles.wordWrappedLabel);
                    EditorGUILayout.LabelField(answers.Count == 0
                        ? "No action is tagged with this cue: it is vocabulary nothing answers yet."
                        : $"{answers.Count} actions: {string.Join(", ", answers.Select(a => a.name))}",
                        EditorStyles.wordWrappedMiniLabel);
                }
            }
        }

        private void DrawMoments()
        {
            if (moments == null)
            {
                EditorGUILayout.HelpBox($"No reaction table at {HumanoidControllerBuilder.ReactionsPath}.", MessageType.Warning);
                return;
            }
            if (GUILayout.Button(HumanoidControllerBuilder.ReactionsPath, EditorStyles.linkLabel)) Reveal(moments);

            foreach (CharacterMoment moment in Enum.GetValues(typeof(CharacterMoment)))
            {
                if (moment == CharacterMoment.None) continue;

                MomentReactions.Row row = moments.Find(moment);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(moment.ToString(), GUILayout.Width(NameWidth));
                    if (row == null || row.Silent)
                    {
                        GUILayout.Label(row == null ? "no row: the body shows nothing" : "silent", EditorStyles.miniLabel);
                        continue;
                    }

                    ScriptableObject answer = row.action != null ? (ScriptableObject)row.action : row.cue;
                    string label = (row.action != null ? "action " : "cue ") + answer.name;
                    if (GUILayout.Button(label, EditorStyles.linkLabel, GUILayout.Width(WideWidth))) Reveal(answer);
                    GUILayout.Label($"{row.chance:P0}", GUILayout.Width(NumberWidth));
                    GUILayout.Label($"{row.cooldown:0.##} s cooldown", GUILayout.Width(WideWidth));
                }

                if (row != null && row.action == null && row.cue != null && !Answered(row.cue))
                    EditorGUILayout.HelpBox($"Nothing answers '{row.cue.name}': no one-shot action is tagged with it " +
                                            "or any of its fallbacks, so this moment never shows.", MessageType.Warning);
            }
        }

        // A moment plays a one-shot (BodyLanguage resolves it with loops off), so a cue answered
        // only by loops still shows nothing when a moment asks for it.
        private bool Answered(CharacterCue cue)
        {
            var seen = new HashSet<CharacterCue>();
            for (; cue != null && seen.Add(cue); cue = cue.Fallback)
            {
                if (tagged.TryGetValue(cue, out List<CharacterAction> list) && list.Any(a => !a.Loops && a.VariantCount > 0))
                    return true;
            }
            return false;
        }

        private bool Passes(Entry entry)
        {
            CharacterAction action = entry.Action;
            if (slotFilter > 0 && action.BodySlot != (CharacterAction.Slot)(slotFilter - 1)) return false;
            if (playbackFilter > 0 && action.Mode != (CharacterAction.Playback)(playbackFilter - 1)) return false;
            if (cueFilter == UntaggedIndex && !entry.Untagged) return false;
            if (cueFilter >= FirstCueIndex && !action.Cues.Contains(cues[cueFilter - FirstCueIndex])) return false;
            return Matches(entry.Search);
        }

        private bool Matches(string text) =>
            string.IsNullOrEmpty(search) || text.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>
        /// The component to play on, from the selection or its parents — null, with a note saying
        /// why, outside play mode or when the selection has none.
        /// </summary>
        private static T PlayTarget<T>(string componentName) where T : Component
        {
            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter play mode and select a humanoid to play these on it.", MessageType.None);
                return null;
            }

            GameObject selected = Selection.activeGameObject;
            T target = selected != null ? selected.GetComponentInParent<T>() : null;
            if (target == null)
                EditorGUILayout.HelpBox($"Select a humanoid with {componentName} on it or a parent to play these on it.",
                                        MessageType.Info);
            return target;
        }

        // Ping only in play mode: selecting the asset would take the selection off the body being played on.
        private static void Reveal(UnityEngine.Object asset)
        {
            EditorGUIUtility.PingObject(asset);
            if (!EditorApplication.isPlaying) Selection.activeObject = asset;
        }

        private static string GroupOf(CharacterAction action)
        {
            string folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(action)).Replace('\\', '/');
            return folder.Length > HumanoidControllerBuilder.ActionsFolder.Length
                ? folder.Substring(HumanoidControllerBuilder.ActionsFolder.Length + 1)
                : TopLevelGroup;
        }

        // The aim clips blend with the level clip rather than playing after it, so only the
        // enter → main → exit sequence adds up to the time the action holds the body.
        private static float FirstVariantSeconds(CharacterAction action)
        {
            if (action.VariantCount == 0) return 0f;

            CharacterAction.Variant variant = action.GetVariant(0);
            float seconds = Length(variant.clip);
            if (action.Mode == CharacterAction.Playback.EnterLoopExit) seconds += Length(variant.enter) + Length(variant.exit);
            return seconds;
        }

        private static float Length(AnimationClip clip) => clip != null ? clip.length : 0f;

        private static string[] WithAny(string[] names) => new[] { AnyOption }.Concat(names).ToArray();
    }
}
