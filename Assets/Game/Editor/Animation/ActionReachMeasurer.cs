// Measures, once and offline, where a work action puts its work, and writes it onto the action as its Reach.
//
// A work clip was captured with its own prop in its own hands, so nothing in the clip says where a Raxy's tool ends up. This
// plays each work action through the real humanoid controller on a real Raxy, samples a cycle, finds the moment the work lands
// (the Contact mark) and records where it lands then, relative to the body root: x right, y up, z forward, metres. A station
// stands that far from what it works at (StationStand).
//
// What is tracked is the HAND, not the tool. A tool's grip is fitted to the Carry hold pose, not to a work clip, so the tool
// held on the real rig points wherever its carry grip points (a hoe's tip came out 1.7 m in the air while the hand was on the
// ground), and a measurement through it measures the grip, not the work. The hand's grip point is the one thing every clip
// states for certain. For a strike at the ground (plough, dig, mine, hammer, kneel) the working end is then ESTIMATED on the
// ground: the tool's length beyond the grip, laid from the hand down to the floor, gives how far past the hand it lands; a wall swing
// holds the tool level. A bench strike points the tool down, so its end is over the hand. The continuous work reports the hand,
// which understates what a tool in it reaches.
//
// Contact is found by a rule per action, from the clip's own evidence:
//   GroundStrike  the lowest the hand gets in the cycle; the tool's end is put on the floor from there.
//   LowestHand    a strike at a bench (hammer, chop) or a reach to the ground (gathering): the lowest the hand gets; the reach is the hand.
//   FarthestHand  a swing at a wall: the farthest the hand gets in front of the body; the tool's end is its length ahead of the hand.
//   CycleMean     continuous work in one place (saw, stir, sweep, wrench): no single moment, so the mean over the cycle, and no mark.
// Variants differ (male and female rigs, left and right handed): the action stores the mean, each variant its own contact time,
// and the report the spread.
//
// Re-runnable: it reads the actions, the controller and the tools' lengths as they are now and rewrites the same fields. Run it
// again after any of them changes. Output: the log and Temp/ActionReach.csv.
//
// Run from: Tools > SpaceGame > Animation > Measure Action Reach
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using SpaceGame.Items;
using SpaceGame.Presentation;

namespace SpaceGame.EditorTools
{
    public static class ActionReachMeasurer
    {
        public enum ContactRule { GroundStrike, LowestHand, FarthestHand, CycleMean }

        /// <summary>One action to measure, how its contact is found, and (GroundStrike only) the tool whose length puts its end on the floor.</summary>
        public readonly struct Job
        {
            public readonly string Action;
            public readonly string Tool;
            public readonly ContactRule Rule;

            public Job(string action, string tool, ContactRule rule)
            {
                Action = action;
                Tool = tool;
                Rule = rule;
            }
        }

        /// <summary>One variant's measurement: when the work lands in its clip and where, and how fast the hand moves then.</summary>
        public readonly struct VariantReach
        {
            public readonly int Variant;
            public readonly float ContactAt;
            public readonly Vector3 Work;
            public readonly Vector3 Hand;
            public readonly float HandSpeedAtContact;
            public readonly float HandSpeedMedian;

            public VariantReach(int variant, float contactAt, Vector3 work, Vector3 hand, float speedAtContact, float speedMedian)
            {
                Variant = variant;
                ContactAt = contactAt;
                Work = work;
                Hand = hand;
                HandSpeedAtContact = speedAtContact;
                HandSpeedMedian = speedMedian;
            }
        }

        public sealed class Result
        {
            public Job Job;
            public CharacterAction Action;
            public float ToolBeyondGrip;
            public List<VariantReach> Variants = new List<VariantReach>();
            public Vector3 Mean;
            public string Skipped;
        }

        /// <summary>Samples over one cycle of a clip.</summary>
        private const int Samples = 60;

        /// <summary>A contact is not written closer to either end of the clip than this: the first frame is "unset".</summary>
        private const float ContactMargin = 0.01f;

        private const string RaxyPath = "Assets/Game/Prefabs/Agents/Characters/Raxy/Raxy_handyman.prefab";
        private const string ToolFolder = "Assets/Game/Resources/Items/Tools";
        private const string CsvPath = "Temp/ActionReach.csv";
        private static readonly Vector3 StagePosition = new Vector3(0f, 700f, 0f);

        /// <summary>Every work action that puts a tool or a hand to something, and what it is done with.</summary>
        public static readonly Job[] Jobs =
        {
            new Job("Farm Plough", "Tool_Hoe", ContactRule.GroundStrike),
            new Job("Dig", "Tool_Shovel", ContactRule.GroundStrike),
            new Job("Mine Ground", "Tool_Pickaxe", ContactRule.GroundStrike),
            new Job("Hammer Ground", "Tool_Hammer", ContactRule.GroundStrike),
            new Job("Kneel Work", "Tool_Trowel", ContactRule.GroundStrike),
            new Job("Gather Plants", null, ContactRule.LowestHand),
            new Job("Gather Crouch Hold", null, ContactRule.LowestHand),
            new Job("Mine Wall", "Tool_Pickaxe", ContactRule.FarthestHand),
            new Job("Hammer", null, ContactRule.LowestHand),
            new Job("Chop Vegetables", null, ContactRule.LowestHand),
            new Job("Chop Food", null, ContactRule.LowestHand),
            new Job("Drill Low", null, ContactRule.CycleMean),
            new Job("Saw", null, ContactRule.CycleMean),
            new Job("Fish Rod", null, ContactRule.CycleMean),
            new Job("Fish Wait Hold", null, ContactRule.CycleMean),
            new Job("Fish Reel Fight", null, ContactRule.CycleMean),
            new Job("Wrench Loosen", null, ContactRule.CycleMean),
            new Job("Wrench Tighten", null, ContactRule.CycleMean),
            new Job("Screwdriver", null, ContactRule.CycleMean),
            new Job("Sweep", null, ContactRule.CycleMean),
            new Job("Stir", null, ContactRule.CycleMean),
            new Job("Stir Pot", null, ContactRule.CycleMean),
            new Job("Cook Pan", null, ContactRule.CycleMean),
            new Job("Cook Wok", null, ContactRule.CycleMean),
            new Job("Grill Meat", null, ContactRule.CycleMean),
            new Job("Wash Vegetables", null, ContactRule.CycleMean),
            new Job("Blend Fruit", null, ContactRule.CycleMean),
            new Job("Wipe Surface", null, ContactRule.CycleMean),
            new Job("Coil Rope", null, ContactRule.CycleMean),
            new Job("Rummage", null, ContactRule.CycleMean),
            new Job("Bartend Hold", null, ContactRule.CycleMean),
        };

        [MenuItem("Tools/SpaceGame/Animation/Measure Action Reach")]
        private static void MeasureMenu() => Debug.Log(Run(true));

        /// <summary>Measures every job and, when <paramref name="write"/>, stores the result on the actions. The report, also written to a CSV.</summary>
        public static string Run(bool write)
        {
            List<Result> results = MeasureAll();
            if (write)
                foreach (Result result in results)
                    if (result.Action != null && result.Variants.Count > 0) Write(result);

            Directory.CreateDirectory(Path.GetDirectoryName(CsvPath) ?? ".");
            File.WriteAllText(CsvPath, Csv(results));
            return "[ActionReach] " + results.Count(r => r.Variants.Count > 0) + " of " + results.Count + " actions measured" + (write ? " and written" : "") + ".\n" + Table(results);
        }

        public static List<Result> MeasureAll()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(HumanoidControllerBuilder.ControllerPath);
            GameObject raxy = AssetDatabase.LoadAllAssetsAtPath(RaxyPath).OfType<GameObject>().FirstOrDefault(g => g.transform.parent == null);
            if (controller == null || raxy == null)
                throw new FileNotFoundException("[ActionReach] needs " + HumanoidControllerBuilder.ControllerPath + " and " + RaxyPath);

            var results = new List<Result>();
            var body = (GameObject)PrefabUtility.InstantiatePrefab(raxy);
            body.hideFlags = HideFlags.DontSave;
            body.transform.SetPositionAndRotation(StagePosition, Quaternion.identity);
            PrefabUtility.UnpackPrefabInstance(body, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            try
            {
                Animator animator = body.GetComponentInChildren<Animator>(true);
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.Rebind();

                Transform hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
                HandGripFrame frame = HandGripFrame.Derive(animator, hand, true);
                var socket = new EquipItemSocket(hand, frame, 1f);

                foreach (Job job in Jobs) results.Add(MeasureJob(job, animator, body.transform, hand, frame, socket));
            }
            finally
            {
                Object.DestroyImmediate(body);
            }
            return results;
        }

        private static Result MeasureJob(Job job, Animator animator, Transform root, Transform hand, HandGripFrame frame, EquipItemSocket socket)
        {
            var result = new Result { Job = job, Action = FindAction(job.Action) };
            if (result.Action == null)
            {
                result.Skipped = "no such action";
                return result;
            }

            if (job.Tool != null)
            {
                result.ToolBeyondGrip = ToolBeyondGrip(job.Tool, animator, hand, socket);
                if (result.ToolBeyondGrip <= 0f)
                {
                    result.Skipped = "no tool " + job.Tool;
                    return result;
                }
            }

            CharacterAction action = result.Action;
            for (int v = 0; v < action.VariantCount; v++)
            {
                if (action.GetVariant(v).clip == null) continue;

                string state = HumanoidLayers.StateName(action, v, HumanoidLayers.Stage.Main);
                result.Variants.Add(MeasureVariant(job.Rule, result.ToolBeyondGrip, v, state, HumanoidLayers.ForSlot(action.BodySlot), animator, root, hand, frame));
            }

            if (result.Variants.Count > 0)
                result.Mean = result.Variants.Aggregate(Vector3.zero, (sum, r) => sum + r.Work) / result.Variants.Count;
            return result;
        }

        private static VariantReach MeasureVariant(ContactRule rule, float toolBeyondGrip, int variant, string state, string layerName, Animator animator,
                                                   Transform root, Transform hand, HandGripFrame frame)
        {
            int layer = animator.GetLayerIndex(layerName);
            for (int i = 0; i < animator.layerCount; i++)
                animator.SetLayerWeight(i, i == 0 || i == layer ? 1f : 0f);

            Quaternion toRoot = Quaternion.Inverse(root.rotation);
            var hands = new Vector3[Samples];
            for (int i = 0; i < Samples; i++)
            {
                animator.Play(state, layer, (float)i / Samples);
                animator.Update(0f);
                animator.Update(0f);
                hands[i] = toRoot * (hand.TransformPoint(frame.LocalPosition) - root.position);
            }

            int contact = -1;
            switch (rule)
            {
                case ContactRule.GroundStrike:
                case ContactRule.LowestHand:
                    contact = IndexOfMin(hands, h => h.y);
                    break;
                case ContactRule.FarthestHand:
                    contact = IndexOfMin(hands, h => -h.z);
                    break;
            }

            Vector3 at = contact >= 0 ? hands[contact] : hands.Aggregate(Vector3.zero, (sum, h) => sum + h) / Samples;
            Vector3 work = at;
            if (rule == ContactRule.GroundStrike)
            {
                // The tool's end rests on the floor: from a hand this high, its length beyond the grip reaches this far past it.
                float past = Mathf.Sqrt(Mathf.Max(0f, toolBeyondGrip * toolBeyondGrip - at.y * at.y));
                work = new Vector3(at.x, 0f, at.z + past);
            }
            else if (rule == ContactRule.FarthestHand)
            {
                // A swing at a wall holds the tool level: its end is its length ahead of the hand.
                work = new Vector3(at.x, at.y, at.z + toolBeyondGrip);
            }

            float[] speed = Enumerable.Range(0, Samples)
                .Select(i => (hands[(i + 1) % Samples] - hands[(i + Samples - 1) % Samples]).magnitude / (2f / Samples)).ToArray();
            float median = speed.OrderBy(s => s).ElementAt(Samples / 2);
            float contactAt = contact >= 0 ? Mathf.Clamp((float)contact / Samples, ContactMargin, 1f - ContactMargin) : 0f;
            return new VariantReach(variant, contactAt, work, at, contact >= 0 ? speed[contact] : 0f, median);
        }

        private static int IndexOfMin(Vector3[] points, System.Func<Vector3, float> key)
        {
            int best = 0;
            for (int i = 1; i < points.Length; i++)
                if (key(points[i]) < key(points[best])) best = i;
            return best;
        }

        /// <summary>How far the tool's business end (its farthest point along +Y) is from where the hand grips it, metres; 0 for an unknown tool.</summary>
        private static float ToolBeyondGrip(string tool, Animator animator, Transform hand, EquipItemSocket socket)
        {
            var item = AssetDatabase.LoadAssetAtPath<InventoryItem>(ToolFolder + "/" + tool + ".asset");
            if (item == null || item.itemPrefab == null) return 0f;

            for (int i = 1; i < animator.layerCount; i++) animator.SetLayerWeight(i, 0f);
            animator.Update(0f);

            // Equipped from a plain copy: the socket strips the item for the hand, which a prefab instance may not have done to it.
            GameObject template = Object.Instantiate(item.itemPrefab);
            GameObject held = socket.Equip(template);
            Object.DestroyImmediate(template);
            try
            {
                return Vector3.Distance(held.transform.TransformPoint(FarthestAlongUp(held)), socket.GripPosition);
            }
            finally
            {
                Object.DestroyImmediate(held);
            }
        }

        /// <summary>The point of the held tool farthest along its own +Y, in the held item's local space.</summary>
        private static Vector3 FarthestAlongUp(GameObject held)
        {
            Vector3 best = Vector3.zero;
            float far = float.MinValue;
            var scratch = new List<Vector3>();
            foreach (MeshFilter filter in held.GetComponentsInChildren<MeshFilter>(false))
            {
                var renderer = filter.GetComponent<MeshRenderer>();
                if (filter.sharedMesh == null || renderer == null || !renderer.enabled) continue;

                scratch.Clear();
                filter.sharedMesh.GetVertices(scratch);
                foreach (Vector3 vertex in scratch)
                {
                    Vector3 local = held.transform.InverseTransformPoint(filter.transform.TransformPoint(vertex));
                    if (local.y <= far) continue;

                    far = local.y;
                    best = local;
                }
            }
            return best;
        }

        private static CharacterAction FindAction(string name)
        {
            string guid = AssetDatabase.FindAssets(name + " t:CharacterAction")
                .FirstOrDefault(g => Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(g)) == name);
            return guid == null ? null : AssetDatabase.LoadAssetAtPath<CharacterAction>(AssetDatabase.GUIDToAssetPath(guid));
        }

        // Stores the mean reach and each variant's contact time. A cycle-mean action has no contact, so its marks are left alone.
        private static void Write(Result result)
        {
            var so = new SerializedObject(result.Action);
            so.FindProperty("reach").vector3Value = result.Mean;
            if (result.Job.Rule != ContactRule.CycleMean)
            {
                SerializedProperty variants = so.FindProperty("variants");
                foreach (VariantReach r in result.Variants)
                    variants.GetArrayElementAtIndex(r.Variant).FindPropertyRelative("contactAt").floatValue = r.ContactAt;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(result.Action);
            AssetDatabase.SaveAssetIfDirty(result.Action);
        }

        public static string Csv(IEnumerable<Result> results)
        {
            var text = new StringBuilder("action,variant,rule,tool,toolBeyondGrip,contactAt,workRight,workUp,workForward,handRight,handUp,handForward,handSpeedAtContact,handSpeedMedian\n");
            foreach (Result r in results)
                foreach (VariantReach v in r.Variants)
                    text.AppendLine(string.Join(",", r.Job.Action, v.Variant, r.Job.Rule, r.Job.Tool ?? "hand", F(r.ToolBeyondGrip), F(v.ContactAt), F(v.Work.x),
                                                F(v.Work.y), F(v.Work.z), F(v.Hand.x), F(v.Hand.y), F(v.Hand.z), F(v.HandSpeedAtContact), F(v.HandSpeedMedian)));
            return text.ToString();
        }

        public static string Table(IEnumerable<Result> results)
        {
            var text = new StringBuilder();
            foreach (Result r in results)
            {
                if (r.Variants.Count == 0)
                {
                    text.AppendLine(r.Job.Action.PadRight(18) + " SKIPPED: " + r.Skipped);
                    continue;
                }

                float low = r.Variants.Min(v => v.Work.z), high = r.Variants.Max(v => v.Work.z);
                text.AppendLine(r.Job.Action.PadRight(18) + r.Job.Rule.ToString().PadRight(13) + "x" + r.Variants.Count + " reach (right " + F(r.Mean.x) + ", up " + F(r.Mean.y) +
                                ", forward " + F(r.Mean.z) + ") forward spread " + F(low) + ".." + F(high));
            }
            return text.ToString();
        }

        private static string F(float value) => value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
    }
}
