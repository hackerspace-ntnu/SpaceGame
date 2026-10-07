// Grades how every carried tool sits in a Raxy's hand, from the built prefabs and the real pose.
//
// Build All fits each tool's rotation so its two reference axes point where the stance says, and checks
// the along axis. That says nothing about whether the HAND is on the tool: a root authored at the end of a
// haft, a haft thicker than the root's offset, a tool whose business end was modelled the wrong way up. This
// seats every built prefab the way the game does and measures what a person looking at the picture sees:
// how far the tool is turned from its stance along and about its length, where along its length the hand
// closes, whether the hand closes on geometry at all, and whether the tool's far end goes through the floor.
//
// Output: the log, and Temp/HandToolPreview/grip_audit.csv, one row per tool in roster order.
//
// Run from: Tools ▸ SpaceGame ▸ Items ▸ Hand Tools ▸ Audit Grips
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    /// <summary>What <see cref="HandToolAudit"/> measured for one tool, in metres and degrees at the size it is held.</summary>
    public readonly struct GripAudit
    {
        public readonly string Id;
        public readonly CarryStance Stance;
        public readonly ItemGrip.HoldStyle Pose;
        public readonly GripResidual Residual;

        /// <summary>The root to the nearest point of the tool's surface: how far the hand is from anything to hold.</summary>
        public readonly float RootToSurface;

        /// <summary>Across the tool, how far the handle's centre line is from the root (vertices within a hand's width of it).</summary>
        public readonly float HandleOffAxis;

        /// <summary>Radius of the tool around that centre line at the root.</summary>
        public readonly float HandleRadius;

        /// <summary>The tool's length along its own axis, and how much of it lies on the business end's side of the root.</summary>
        public readonly float Length;
        public readonly float GripFraction;

        /// <summary>The lowest point of the tool above the holder's soles. Negative: through the floor.</summary>
        public readonly float GroundClearance;

        /// <summary>The grip frame's origin (the palm) to the root. Not zero: the fit moves the root to the fist.</summary>
        public readonly float PalmToRoot;

        /// <summary>
        /// How far the root is from where the roster put it relative to the middle of the closed fist (the fist itself,
        /// moved by the row's nudge and grip shift): how far the hand is from closing on the grip. Zero when it does.
        /// </summary>
        public readonly float FistToRoot;

        /// <summary>
        /// Degrees between the tool's axis and the line a fist closes on: across the knuckles for a tool gripped like
        /// a shaft, along the fingers for one aimed like a gun. A hand cannot close on a shaft at 90 degrees to that
        /// line, so a big number is a wrist bent into a pose no person takes. Independent of the body's pose.
        /// </summary>
        public readonly float FistAngle;

        public GripAudit(string id, CarryStance stance, ItemGrip.HoldStyle pose, GripResidual residual, float rootToSurface,
                         float handleOffAxis, float handleRadius, float length, float gripFraction, float groundClearance,
                         float palmToRoot, float fistToRoot, float fistAngle)
        {
            Id = id;
            Stance = stance;
            Pose = pose;
            Residual = residual;
            RootToSurface = rootToSurface;
            HandleOffAxis = handleOffAxis;
            HandleRadius = handleRadius;
            Length = length;
            GripFraction = gripFraction;
            GroundClearance = groundClearance;
            PalmToRoot = palmToRoot;
            FistToRoot = fistToRoot;
            FistAngle = fistAngle;
        }

        /// <summary>What is off about this tool, in words; empty when nothing is.</summary>
        public string Flags
        {
            get
            {
                var flags = new List<string>();
                if (Residual.Worst > HandToolAudit.MaxResidual) flags.Add("TURNED");
                if (FistToRoot > HandToolAudit.MaxFistToRoot) flags.Add("NOT_IN_FIST");
                if (RootToSurface > HandToolAudit.MaxRootToSurface) flags.Add("HAND_IN_AIR");
                else if (HandleOffAxis > HandToolAudit.MaxHandleOffAxis) flags.Add("OFF_HANDLE");
                bool shaft = Stance == CarryStance.Wield || Stance == CarryStance.Staff;
                if (shaft && (GripFraction < HandToolAudit.RootAtEnd || GripFraction > 1f - HandToolAudit.RootAtEnd))
                    flags.Add("ROOT_AT_END");
                if (Stance != CarryStance.Push && GroundClearance < 0f) flags.Add("THROUGH_FLOOR");
                if (shaft && FistAngle > HandToolAudit.MaxFistAngle) flags.Add("WRIST_TWISTED");
                return string.Join(" ", flags);
            }
        }
    }

    public static class HandToolAudit
    {
        /// <summary>Degrees a seated axis may be off its stance.</summary>
        public const float MaxResidual = 1f;

        /// <summary>
        /// Metres from the root to the nearest vertex beyond which the hand closes on air. A shaft's own radius is about
        /// 0.02, and a mesh has vertices only at its rings, so the nearest one to a root in mid-shaft is further still.
        /// </summary>
        public const float MaxRootToSurface = 0.045f;

        /// <summary>Metres the handle's centre line may sit from the root before the fist is beside the handle.</summary>
        public const float MaxHandleOffAxis = 0.02f;

        /// <summary>Metres from the middle of the fist to the root beyond which the tool is not in the hand.</summary>
        public const float MaxFistToRoot = 0.03f;

        /// <summary>Degrees a tool's axis may lie from the line a fist closes on before the wrist is bent unnaturally.</summary>
        public const float MaxFistAngle = 50f;

        /// <summary>The fraction of a tool's length from either end inside which the root is "at the end".</summary>
        public const float RootAtEnd = 0.05f;

        /// <summary>Vertices within this distance of the root along the tool count as "the handle at the grip".</summary>
        private const float HandWidth = 0.05f;

        private const string OutputPath = "Temp/HandToolPreview/grip_audit.csv";

        [MenuItem("Tools/SpaceGame/Items/Hand Tools/Audit Grips")]
        private static void AuditMenu()
        {
            using HandToolRig rig = HandToolRig.Create();
            if (rig == null) return;

            List<GripAudit> rows = Measure(rig);
            Directory.CreateDirectory(Path.GetDirectoryName(OutputPath) ?? ".");
            File.WriteAllText(OutputPath, Csv(rows));
            Debug.Log($"[HandToolAudit] {rows.Count} tools, {rows.Count(r => r.Flags.Length > 0)} flagged. {Path.GetFullPath(OutputPath)}\n{Table(rows)}");
        }

        /// <summary>Every built tool in roster order. A tool that is not built yet is skipped.</summary>
        public static List<GripAudit> Measure(HandToolRig rig)
        {
            rig.Pose(ItemGrip.HoldStyle.Carry);
            float soles = Lowest(rig.Body);
            var rows = new List<GripAudit>();
            foreach (HandToolSpec spec in HandToolRoster.All)
            {
                var item = AssetDatabase.LoadAssetAtPath<InventoryItem>(spec.ItemPath);
                if (item == null || item.itemPrefab == null) continue;
                rows.Add(Measure(rig, spec, item.itemPrefab, soles));
            }

            return rows;
        }

        public static GripAudit Measure(HandToolRig rig, HandToolSpec spec, GameObject prefab, float soles)
        {
            StanceDefinition stance = CarryStances.Of(spec.Stance);
            ItemGrip.HoldStyle pose = prefab.GetComponent<ItemGrip>().Style;
            rig.Pose(pose);

            var socket = new EquipItemSocket(rig.Hand, rig.Frame, 1f);
            GameObject held = socket.Equip(prefab);
            try
            {
                GripResidual residual = GripFitter.ResidualOfSeated(rig, spec, held);
                Vector3 root = held.GetComponent<ItemGrip>().GripPoint.position;
                Vector3 along = held.transform.TransformDirection(spec.ItemAlong ?? stance.ItemAlong).normalized;
                Vector3 face = held.transform.TransformDirection(spec.ItemFace ?? stance.ItemFace).normalized;
                Vector3 side = Vector3.Cross(along, face).normalized;

                // Where the row asked the root to sit relative to the fist, in the world.
                Vector3 intended = rig.Facing * (spec.Nudge - stance.BodyAlong * spec.GripShift);

                List<Vector3> points = Vertices(held);
                float below = 0f, above = 0f, nearest = float.MaxValue, lowest = float.MaxValue;
                var handle = new List<Vector2>();
                foreach (Vector3 p in points)
                {
                    Vector3 d = p - root;
                    float a = Vector3.Dot(d, along);
                    below = Mathf.Min(below, a);
                    above = Mathf.Max(above, a);
                    nearest = Mathf.Min(nearest, d.magnitude);
                    lowest = Mathf.Min(lowest, p.y);
                    if (Mathf.Abs(a) < HandWidth) handle.Add(new Vector2(Vector3.Dot(d, face), Vector3.Dot(d, side)));
                }

                Vector2 centre = handle.Count == 0 ? Vector2.zero : handle.Aggregate(Vector2.zero, (sum, v) => sum + v) / handle.Count;
                float radius = handle.Count == 0 ? 0f : handle.Average(v => Vector2.Distance(v, centre));
                float length = above - below;

                return new GripAudit(spec.Id, spec.Stance, pose, residual,
                                     points.Count == 0 ? float.MaxValue : nearest,
                                     handle.Count == 0 ? float.MaxValue : centre.magnitude, radius,
                                     length, length > 1e-4f ? -below / length : 0f, lowest - soles,
                                     Vector3.Distance(socket.GripPosition, root), Vector3.Distance(rig.FistCentre() + intended, root),
                                     FistAngle(socket, spec, along));
            }
            finally
            {
                Object.DestroyImmediate(held);
            }
        }

        /// <summary>
        /// The angle between the tool's axis and the line the fist closes on. A tool gripped like a shaft (every stance
        /// but <see cref="CarryStance.Aim"/> and <see cref="CarryStance.Push"/>) lies across the knuckles, which is the
        /// grip frame's +Y (out the thumb side); an aimed one lies along the fingers, the frame's +Z.
        /// </summary>
        private static float FistAngle(EquipItemSocket socket, HandToolSpec spec, Vector3 along)
        {
            bool aimed = spec.Stance == CarryStance.Aim || spec.Stance == CarryStance.Push;
            Vector3 line = socket.GripRotation * (aimed ? Vector3.forward : Vector3.up);
            float angle = Vector3.Angle(along, line);
            return aimed ? angle : Mathf.Min(angle, 180f - angle);
        }

        public static string Csv(IEnumerable<GripAudit> rows)
        {
            var text = new StringBuilder("id,stance,pose,alongDeg,faceDeg,palmToRoot,fistToRoot,rootToSurface,handleOffAxis,handleRadius,length,gripFraction,groundClearance,fistAngle,flags\n");
            foreach (GripAudit r in rows)
                text.AppendLine(string.Join(",", r.Id, r.Stance, r.Pose, F(r.Residual.Along, 1), F(r.Residual.Face, 1),
                                            F(r.PalmToRoot, 3), F(r.FistToRoot, 3), F(r.RootToSurface, 3), F(r.HandleOffAxis, 3), F(r.HandleRadius, 3),
                                            F(r.Length, 2), F(r.GripFraction, 2), F(r.GroundClearance, 2), F(r.FistAngle, 0), r.Flags));
            return text.ToString();
        }

        public static string Table(IEnumerable<GripAudit> rows)
        {
            var text = new StringBuilder();
            foreach (GripAudit r in rows)
                text.AppendLine($"{r.Id,-24} {r.Stance,-5} {r.Pose,-9} along {F(r.Residual.Along, 1),5} face {F(r.Residual.Face, 1),5} " +
                                $"surf {F(r.RootToSurface, 3)} off {F(r.HandleOffAxis, 3)} len {F(r.Length, 2)} grip {F(r.GripFraction, 2)} " +
                                $"floor {F(r.GroundClearance, 2)} fist {F(r.FistAngle, 0)} {r.Flags}");
            return text.ToString();
        }

        private static string F(float value, int digits) =>
            value == float.MaxValue ? "none" : value.ToString("F" + digits, System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>Every vertex of every enabled mesh under <paramref name="root"/>, in world space.</summary>
        private static List<Vector3> Vertices(GameObject root)
        {
            var points = new List<Vector3>();
            var scratch = new List<Vector3>();
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(false))
            {
                var renderer = filter.GetComponent<MeshRenderer>();
                if (filter.sharedMesh == null || renderer == null || !renderer.enabled) continue;

                scratch.Clear();
                filter.sharedMesh.GetVertices(scratch);
                points.AddRange(scratch.Select(filter.transform.TransformPoint));
            }

            return points;
        }

        private static float Lowest(GameObject body) =>
            body.GetComponentsInChildren<Renderer>(false).Where(r => r.enabled).Min(r => r.bounds.min.y);
    }
}
