// Asks where a tool's business end goes while a worker works with it.
//
// A grip is fitted against an idle hold pose, but a worker draws the tool to PLAY a work clip, and the
// clip puts the hand somewhere else. Nothing in the clips says where the tool was: a Kevin Iglesias
// plough, a CMU hammering take and a UAL sweep were each captured with their own prop. So the rule used
// here is the clip's own evidence:
//
//   * A clip that works the ground (plough, dig, mine, hammer ground) must bring the business end (the
//     farthest vertex along the tool's +Y, which every model puts at the head or tip) to within a few
//     centimetres of the soles at the lowest point of its cycle. Lower than that is through the floor,
//     higher is a swing in the air.
//   * A two-handed clip says where the shaft was: the line between the two hands. The tool is held in one
//     hand, but its axis should lie along that line, or the work looks like it is done with the wrong end.
//
// Neither is a contact point (the work clips carry no Contact mark: a looped chore has none), so this is
// the nearest honest approximation, and the table says which rule each number is.
//
// Output: the log and Temp/HandToolPreview/work_audit.csv.
//
// Run from: Tools ▸ SpaceGame ▸ Items ▸ Hand Tools ▸ Audit Work Clips
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using SpaceGame.Items;
using SpaceGame.Presentation;

namespace SpaceGame.EditorTools
{
    /// <summary>One tool in one work clip, over one cycle.</summary>
    public readonly struct WorkAudit
    {
        public readonly string Tool;
        public readonly string Action;
        public readonly int Variant;

        /// <summary>The lowest the business end gets above the soles, metres. Negative: through the floor.</summary>
        public readonly float LowestTip;

        /// <summary>The highest it gets, metres above the soles.</summary>
        public readonly float HighestTip;

        /// <summary>At the lowest moment, how far in front of the hips the tip is, metres.</summary>
        public readonly float ForwardAtLowest;

        /// <summary>At the lowest moment, the acute angle between the tool's axis and the line between the hands.</summary>
        public readonly float ShaftAngle;

        /// <summary>At the lowest moment, the hands' distance apart. A clip with one hand on the tool has them far apart.</summary>
        public readonly float HandsApart;

        public WorkAudit(string tool, string action, int variant, float lowestTip, float highestTip, float forwardAtLowest,
                         float shaftAngle, float handsApart)
        {
            Tool = tool;
            Action = action;
            Variant = variant;
            LowestTip = lowestTip;
            HighestTip = highestTip;
            ForwardAtLowest = forwardAtLowest;
            ShaftAngle = shaftAngle;
            HandsApart = handsApart;
        }
    }

    public static class HandToolWorkAudit
    {
        /// <summary>Samples taken over one cycle of a clip.</summary>
        private const int Samples = 24;

        /// <summary>
        /// What a worker holding each tool plays, from the job cues (dig, hammer, repair, craft, tend, cook) and the
        /// actions tagged with them, and from each tool's own use action. A tool with no row has no work clip.
        /// </summary>
        public static readonly (string Tool, string[] Actions)[] Pairings =
        {
            ("Tool_Hoe", new[] { "Farm Plough", "Dig" }),
            ("Tool_Shovel", new[] { "Dig" }),
            ("Tool_Pickaxe", new[] { "Mine Ground", "Mine Wall", "Dig" }),
            ("Tool_Pickaxe_Rust", new[] { "Mine Ground", "Mine Wall" }),
            ("Tool_RockHammer", new[] { "Mine Ground", "Hammer" }),
            ("Tool_ScrapCutter", new[] { "Mine Wall" }),
            ("Tool_Hammer", new[] { "Hammer Ground", "Hammer" }),
            ("Tool_Mallet", new[] { "Hammer Ground", "Hammer" }),
            ("Tool_HandSaw", new[] { "Saw" }),
            ("Tool_BoneSaw", new[] { "Saw" }),
            ("Tool_FishingRod", new[] { "Fish Rod" }),
            ("Tool_Broom", new[] { "Sweep" }),
            ("Tool_SandBrush", new[] { "Sweep" }),
            ("Tool_Wrench_Ring", new[] { "Wrench Tighten" }),
            ("Tool_Wrench_Open", new[] { "Wrench Tighten" }),
            ("Tool_PipeWrench", new[] { "Wrench Tighten" }),
            ("Tool_Ladle", new[] { "Stir Pot" }),
            ("Tool_Cleaver", new[] { "Chop Food" }),
            ("Tool_Dibber", new[] { "Plant Seedling" }),
            ("Tool_Trowel", new[] { "Poke Ground" }),
            ("Tool_Spear_Stone", new[] { "Stab" }),
            ("Tool_Spear_Bone", new[] { "Stab" }),
            ("Tool_HuntingKnife", new[] { "Stab" }),
            ("Tool_SkinningKnife", new[] { "Stab" }),
            ("Tool_Club_Knotted", new[] { "Sword Strike" }),
            ("Tool_Club_Studded", new[] { "Sword Strike" }),
            ("Tool_HandAxe", new[] { "Sword Strike" }),
            ("Tool_Harpoon_Iron", new[] { "Spear Throw" }),
            ("Tool_Lasso", new[] { "Lasso Throw" }),
        };

        private const string OutputPath = "Temp/HandToolPreview/work_audit.csv";

        [MenuItem("Tools/SpaceGame/Items/Hand Tools/Audit Work Clips")]
        private static void AuditMenu()
        {
            using HandToolRig rig = HandToolRig.Create();
            if (rig == null) return;

            List<WorkAudit> rows = Measure(rig);
            Directory.CreateDirectory(Path.GetDirectoryName(OutputPath) ?? ".");
            File.WriteAllText(OutputPath, Csv(rows));
            Debug.Log($"[HandToolWorkAudit] {rows.Count} tool/clip pairs. {Path.GetFullPath(OutputPath)}\n{Table(rows)}");
        }

        public static List<WorkAudit> Measure(HandToolRig rig)
        {
            rig.Pose(ItemGrip.HoldStyle.Carry);
            float restGround = Ground(rig);
            float restSoles = rig.Body.GetComponentsInChildren<Renderer>(false).Where(r => r.enabled).Min(r => r.bounds.min.y);
            float soleBelowFeet = restGround - restSoles;

            var rows = new List<WorkAudit>();
            foreach ((string tool, string[] actions) in Pairings)
            {
                HandToolSpec spec = HandToolRoster.All.First(s => s.Id == tool);
                var item = AssetDatabase.LoadAssetAtPath<InventoryItem>(spec.ItemPath);
                if (item == null || item.itemPrefab == null) continue;

                foreach (string actionName in actions)
                {
                    CharacterAction action = FindAction(actionName);
                    if (action == null)
                    {
                        Debug.LogWarning($"[HandToolWorkAudit] no action '{actionName}'.");
                        continue;
                    }

                    for (int v = 0; v < action.VariantCount; v++)
                    {
                        AnimationClip clip = action.GetVariant(v).clip;
                        if (clip != null) rows.Add(MeasureOne(rig, spec, item.itemPrefab, action, v, clip, soleBelowFeet));
                    }
                }
            }

            rig.Pose(ItemGrip.HoldStyle.Carry);
            return rows;
        }

        private static WorkAudit MeasureOne(HandToolRig rig, HandToolSpec spec, GameObject prefab, CharacterAction action,
                                            int variant, AnimationClip clip, float soleBelowFeet)
        {
            StanceDefinition stance = CarryStances.Of(spec.Stance);
            rig.Pose(prefab.GetComponent<ItemGrip>().Style);
            var socket = new EquipItemSocket(rig.Hand, rig.Frame, 1f);
            GameObject held = socket.Equip(prefab);
            try
            {
                Vector3 alongLocal = spec.ItemAlong ?? stance.ItemAlong;
                Vector3 tipLocal = FarthestAlong(held, alongLocal);
                Transform left = rig.Animator.GetBoneTransform(HumanBodyBones.LeftHand);

                float lowest = float.MaxValue, highest = float.MinValue, forward = 0f, angle = 0f, apart = 0f;
                for (int i = 0; i < Samples; i++)
                {
                    rig.Show(clip, clip.length * i / Samples);
                    Vector3 tip = held.transform.TransformPoint(tipLocal);
                    float height = tip.y - (Ground(rig) - soleBelowFeet);
                    highest = Mathf.Max(highest, height);
                    if (height >= lowest) continue;

                    lowest = height;
                    forward = Vector3.Dot(tip - rig.Hips.position, rig.Facing * Vector3.forward);
                    Vector3 hands = rig.Hand.position - left.position;
                    Vector3 axis = held.transform.TransformDirection(alongLocal);
                    angle = Vector3.Angle(axis, hands);
                    angle = Mathf.Min(angle, 180f - angle);
                    apart = hands.magnitude;
                }

                return new WorkAudit(spec.Id, action.name, variant, lowest, highest, forward, angle, apart);
            }
            finally
            {
                Object.DestroyImmediate(held);
            }
        }

        /// <summary>The point of the tool farthest along <paramref name="along"/>, in the held item's own space.</summary>
        private static Vector3 FarthestAlong(GameObject held, Vector3 along)
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
                    float d = Vector3.Dot(local, along);
                    if (d <= far) continue;

                    far = d;
                    best = local;
                }
            }

            return best;
        }

        /// <summary>The lower of the two feet, a height that follows the body wherever a clip puts the root.</summary>
        private static float Ground(HandToolRig rig) =>
            Mathf.Min(rig.Animator.GetBoneTransform(HumanBodyBones.LeftFoot).position.y,
                      rig.Animator.GetBoneTransform(HumanBodyBones.RightFoot).position.y);

        private static CharacterAction FindAction(string name)
        {
            string guid = AssetDatabase.FindAssets(name + " t:CharacterAction")
                .FirstOrDefault(g => Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(g)) == name);
            return guid == null ? null : AssetDatabase.LoadAssetAtPath<CharacterAction>(AssetDatabase.GUIDToAssetPath(guid));
        }

        public static string Csv(IEnumerable<WorkAudit> rows)
        {
            var text = new StringBuilder("tool,action,variant,lowestTip,highestTip,forwardAtLowest,shaftAngle,handsApart\n");
            foreach (WorkAudit r in rows)
                text.AppendLine(string.Join(",", r.Tool, r.Action, r.Variant, F(r.LowestTip), F(r.HighestTip), F(r.ForwardAtLowest),
                                            r.ShaftAngle.ToString("F0"), F(r.HandsApart)));
            return text.ToString();
        }

        public static string Table(IEnumerable<WorkAudit> rows)
        {
            var text = new StringBuilder();
            foreach (WorkAudit r in rows)
                text.AppendLine($"{r.Tool,-22} {r.Action,-15} v{r.Variant} tip low {F(r.LowestTip)} high {F(r.HighestTip)} " +
                                $"fwd {F(r.ForwardAtLowest)} shaftAngle {r.ShaftAngle:F0} hands {F(r.HandsApart)}");
            return text.ToString();
        }

        private static string F(float value) => value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
    }
}
