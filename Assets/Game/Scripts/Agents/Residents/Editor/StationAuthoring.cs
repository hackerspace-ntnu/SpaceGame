// The stations' data, authored in one place: which clips answer each station cue, which hand tools those clips are made for,
// and which cue each kind of spot holds.
//
// A station is a spot whose prop calls for one kind of work (docs/AI/systems/Stations.md). Two things used to make a cook mime a
// farmer's job: a cue is a POOL of every action tagged with it and `cook` / `work` / `craft` were each a pool of a dozen unrelated
// loops, and a spot named a pool where its prop called for one clip. This tool makes each station cue's LOOPS exactly the listed
// actions (one-shots are left alone: a Hold never plays them), gives the cue the tools its clips assume, and points each kind of
// spot at one cue. Re-runnable: it only writes what differs.
//
// Run from: Tools > SpaceGame > Residents > Author Station Cues
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SpaceGame.Items;
using SpaceGame.Presentation;
using SpaceGame.World;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.Agents.Residents.EditorTools
{
    public static class StationAuthoring
    {
        /// <summary>One station cue: what it means, what its clips are made for, and exactly which looping actions answer it.</summary>
        public readonly struct CueRow
        {
            public readonly string Name;
            public readonly string Meaning;
            public readonly string[] Tools;
            public readonly bool BareHands;
            public readonly string[] Actions;

            public CueRow(string name, string meaning, string[] tools, bool bareHands, string[] actions)
            {
                Name = name;
                Meaning = meaning;
                Tools = tools;
                BareHands = bareHands;
                Actions = actions;
            }
        }

        private const string CueFolder = "Assets/Game/ScriptableObjects/Animation/Cues";
        private const string ActionFolder = "Assets/Game/ScriptableObjects/Animation/Actions";
        private const string SpotFolder = "Assets/Game/ScriptableObjects/Settlements/Spots";
        private const string ToolFolder = "Assets/Game/Resources/Items/Tools";
        private static readonly string[] None = new string[0];

        /// <summary>
        /// Every station cue. A null meaning keeps the one the cue has; null actions keep its membership (only the tools change).
        /// A station cue never falls back to another: a loop that does not fit leaves the body standing instead of miming the wider job.
        /// </summary>
        public static readonly CueRow[] Cues =
        {
            // The kitchen: one word per thing done at a stove, a board, a sink.
            new CueRow("stir", "Stirring a pot, a bowl or a drink: a ladle through something thick.",
                       new[] { "Tool_Ladle", "Tool_PoulticeBowl", "Tool_MortarPestle" }, false, new[] { "Stir", "Stir Pot", "Stir Drink" }),
            new CueRow("cookpan", "Working a pan over a fire: tossing and turning by its handle.",
                       new[] { "Tool_Ladle" }, false, new[] { "Cook Pan" }),
            new CueRow("cookwok", "Tossing a wok over a flame.", new[] { "Tool_Ladle" }, false, new[] { "Cook Wok" }),
            new CueRow("grill", "Turning meat on a grill with tongs or a spatula.", new[] { "Tool_Ladle" }, false, new[] { "Grill Meat" }),
            new CueRow("chop", "Chopping on a board: vegetables, meat, herbs.",
                       new[] { "Tool_Cleaver", "Tool_HerbKnife", "Tool_SkinningKnife", "Tool_HuntingKnife" }, false,
                       new[] { "Chop Vegetables", "Chop Vegetables Once", "Chop Food" }),
            new CueRow("wash", "Washing food or hands in a basin or under a flow.", None, true, new[] { "Wash Vegetables", "Wash Vegetables Long" }),
            new CueRow("blend", "Feeding a blender.", None, true, new[] { "Blend Fruit" }),
            new CueRow("plate", "Laying food onto a plate to serve it. A one-shot: asked for with Express, never held by a spot.", None, true, new[] { "Plate Food" }),
            new CueRow("season", "A pinch of seasoning over a dish. A one-shot: asked for with Express, never held by a spot.", None, true, new[] { "Season Food" }),

            // The rest of the workshop, farm and mine.
            new CueRow("wipe", "Wiping, brushing or smoothing a surface with the hand: a table, a hive, clay, a hide.", None, true, new[] { "Wipe Surface" }),
            new CueRow("weave", "Winding thread or cord in the hands: a loom, a spindle.", new[] { "Tool_Spindle" }, false, new[] { "Coil Rope" }),
            new CueRow("rummage", "Reaching into shelves and boxes and sorting through what is in them.", None, true, new[] { "Rummage" }),
            new CueRow("mine", null, new[] { "Tool_Pickaxe", "Tool_Pickaxe_Rust" }, false, new[] { "Mine Ground", "Mine Wall" }),
            new CueRow("dig", null, new[] { "Tool_Shovel" }, false, new[] { "Dig" }),
            new CueRow("farm", null, new[] { "Tool_Hoe" }, false, new[] { "Farm Plough" }),
            new CueRow("hammer", null, new[] { "Tool_Hammer", "Tool_Mallet", "Tool_RockHammer" }, false, new[] { "Hammer", "Hammer Ground" }),
            new CueRow("repair", null, new[] { "Tool_Wrench_Ring", "Tool_Wrench_Open", "Tool_PipeWrench", "Tool_Pliers" }, false,
                       new[] { "Screwdriver", "Wrench Loosen", "Wrench Tighten" }),
            new CueRow("tend", null, None, true, new[] { "Gather Plants", "Gather Crouch Hold", "Kneel Work" }),
            new CueRow("craft", null, None, false, new[] { "Coil Rope", "Screwdriver", "Wrench Tighten", "Wipe Surface" }),
            new CueRow("serve", null, new[] { "Tool_Flask" }, false, null),

            // Words a station used to borrow a clip from.
            new CueRow("cook", "The kitchen as a whole: every cooking action carries it besides its station. Asked for by chat and Express; no spot holds it.",
                       None, false, new[] { "Blend Fruit", "Chop Food", "Chop Vegetables", "Chop Vegetables Once", "Cook Pan", "Cook Wok", "Grill Meat",
                                            "Plate Food", "Season Food", "Stir", "Stir Drink", "Stir Pot", "Wash Vegetables", "Wash Vegetables Long" }),
            new CueRow("operate", null, None, false, new[] { "Fax Hold", "Radio Call" }),
            new CueRow("eat", null, None, false, new[] { "Eat Bowl", "Eat Bowl Hand" }),
            new CueRow("listen", null, None, false, new[] { "Lean On Counter" }),
            new CueRow("pickup", null, None, false, new string[0]),
        };

        /// <summary>
        /// The cue each kind of spot holds, by SpotUse asset name; null = the resident stands there (no clip fits the prop yet, or the
        /// spot is a stop an errand makes with its arms full). A prop whose work differs from its use's is overridden on the spot itself.
        /// </summary>
        public static readonly (string Use, string Cue)[] Spots =
        {
            ("Kitchen", "stir"), ("Bar", "serve"), ("Butchery", "chop"), ("Farm", "farm"), ("Mine", "mine"),
            ("Forge", "hammer"), ("Smeltery", "hammer"), ("Mill", "hammer"), ("Workshop", "repair"), ("Loom", "weave"),
            ("Pottery", "wipe"), ("Apiary", "wipe"), ("Tannery", "wipe"), ("Infirmary", "tend"), ("Garden", "tend"),
            ("Stall", "explain"), ("Pen", null), ("Shrine", null), ("Gate", null), ("TowerWatch", null),
            ("Well", null), ("Plant", null), ("OrePile", null), ("Smelter", null), ("GoodsPile", null),
            ("Fodder", null), ("Trough", null), ("Woodpile", null), ("Firebox", null),
        };

        [MenuItem("Tools/SpaceGame/Residents/Author Station Cues")]
        private static void AuthorMenu() => Debug.Log(Author());

        /// <summary>Writes the cues, their tags and the spot uses' hold cues; the report says what changed.</summary>
        public static string Author()
        {
            var report = new List<string>();
            var actions = AssetDatabase.FindAssets("t:CharacterAction", new[] { ActionFolder })
                .Select(g => AssetDatabase.LoadAssetAtPath<CharacterAction>(AssetDatabase.GUIDToAssetPath(g))).ToList();

            foreach (CueRow row in Cues) AuthorCue(row, actions, report);
            foreach ((string use, string cue) in Spots) AuthorSpot(use, cue, report);
            return report.Count == 0 ? "[Stations] already up to date." : "[Stations]\n" + string.Join("\n", report);
        }

        private static void AuthorCue(CueRow row, List<CharacterAction> actions, List<string> report)
        {
            string path = CueFolder + "/" + row.Name + ".asset";
            var cue = AssetDatabase.LoadAssetAtPath<CharacterCue>(path);
            if (cue == null)
            {
                cue = ScriptableObject.CreateInstance<CharacterCue>();
                AssetDatabase.CreateAsset(cue, path);
                report.Add("created cue " + row.Name);
            }

            var so = new SerializedObject(cue);
            if (row.Meaning != null) so.FindProperty("meaning").stringValue = row.Meaning;
            so.FindProperty("fallback").objectReferenceValue = null;
            so.FindProperty("needsFreeHands").boolValue = false;
            so.FindProperty("bareHands").boolValue = row.BareHands;
            SerializedProperty tools = so.FindProperty("tools");
            tools.arraySize = row.Tools.Length;
            for (int i = 0; i < row.Tools.Length; i++)
                tools.GetArrayElementAtIndex(i).objectReferenceValue = Require<InventoryItem>(ToolFolder + "/" + row.Tools[i] + ".asset");
            if (so.ApplyModifiedPropertiesWithoutUndo()) report.Add("updated cue " + row.Name);
            EditorUtility.SetDirty(cue);
            AssetDatabase.SaveAssetIfDirty(cue);

            if (row.Actions == null) return;

            foreach (CharacterAction action in actions)
            {
                bool listed = row.Actions.Contains(action.name);
                bool tagged = action.Cues.Contains(cue);
                // A one-shot not listed keeps its tag: a Hold never plays it, and a moment may still ask for it.
                if (listed == tagged || (!listed && !action.Loops)) continue;

                SetTag(action, cue, listed);
                report.Add((listed ? "tagged " : "untagged ") + action.name + " " + (listed ? "with " : "from ") + row.Name);
            }

            foreach (string name in row.Actions)
                if (!actions.Any(a => a.name == name)) report.Add("NO ACTION named '" + name + "' for " + row.Name);
        }

        private static void SetTag(CharacterAction action, CharacterCue cue, bool tagged)
        {
            var so = new SerializedObject(action);
            SerializedProperty cues = so.FindProperty("cues");
            if (tagged)
            {
                cues.InsertArrayElementAtIndex(cues.arraySize);
                cues.GetArrayElementAtIndex(cues.arraySize - 1).objectReferenceValue = cue;
            }
            else
            {
                for (int i = cues.arraySize - 1; i >= 0; i--)
                    if (cues.GetArrayElementAtIndex(i).objectReferenceValue == cue)
                    {
                        cues.GetArrayElementAtIndex(i).objectReferenceValue = null;
                        cues.DeleteArrayElementAtIndex(i);
                    }
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(action);
            AssetDatabase.SaveAssetIfDirty(action);
        }

        private static void AuthorSpot(string useName, string cueName, List<string> report)
        {
            var use = Require<SpotUse>(SpotFolder + "/" + useName + ".asset");
            CharacterCue cue = cueName != null ? Require<CharacterCue>(CueFolder + "/" + cueName + ".asset") : null;
            if (use.holdCue == cue) return;

            var so = new SerializedObject(use);
            so.FindProperty("holdCue").objectReferenceValue = cue;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(use);
            AssetDatabase.SaveAssetIfDirty(use);
            report.Add("spot " + useName + " holds " + (cueName ?? "nothing"));
        }

        private static T Require<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) throw new FileNotFoundException("[Stations] " + path + " is missing.");
            return asset;
        }
    }
}
