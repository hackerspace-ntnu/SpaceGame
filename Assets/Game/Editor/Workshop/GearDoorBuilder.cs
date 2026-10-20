// The workshop's gear doors, built into Workshop.prefab by WorkshopPrefabBuilder after the machinery is wired: the front
// gate (GearDoorLayout.json) and the vault's round door (VaultDoorLayout.json).
//
// The leaves come from workshop_export.py as hinged nodes in their modelled (open) pose, each naming its door. This
// shuts them, gives them a solid hull, puts the pegs, the drive gear and the lock gear on them from the door's layout,
// lays the pile of loose gears on the ground in front of the door (out of the gate's leaves' sweep), and makes the gear
// items the piles are made of. It is part of Rebuild: hand edits to any of it are lost on the next rebuild.
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using SpaceGame.Items;
using SpaceGame.World;

namespace SpaceGame.EditorTools
{
    public static class GearDoorBuilder
    {

        // The gear kit is modelled at module 0.04; the gate's gears are drawn half as big again, at the player's scale.
        internal const float ModelScale = 1.5f;
        // Where a gear's back face sits in front of the face its peg is bolted to (the kit peg's seat, scaled).
        private const float SeatOut = 0.05f * ModelScale;
        private const float PlaceRange = 4.5f;
        private const string PegModel = "gear_peg";
        private const string CollisionPrefix = "COLT_";

        // Which door a leaf is (the model's `gear_door` on it), and the vault's markers.
        private const string GateDoor = "";
        private const string VaultDoor = "Vault";
        private const string FaceMark = "VaultDoor_Face";
        // The vault's loose cogs lie in a rock tunnel that hangs low in places: the pile looks for its ground this far
        // above each spot, so it lands on the floor and not on an overhang.
        private const float VaultPileProbeHeight = 0.5f;

        /// <summary>A gear the pile is made of: what the layout calls it, its model, its teeth, and what the player sees.</summary>
        private readonly struct GearItem
        {
            public readonly string Kind, Model, Label;
            public readonly int Teeth;

            public GearItem(string kind, string model, int teeth, string label)
            {
                Kind = kind; Model = model; Teeth = teeth; Label = label;
            }
        }

        private static readonly GearItem[] Items =
        {
            new("XS", "gear_xs", 10, "Gear (XS)"),
            new("S", "gear_s", 14, "Gear (S)"),
            new("M", "gear_m", 20, "Gear (M)"),
            new("L", "gear_l", 28, "Gear (L)"),
            new("XL", "gear_xl", 40, "Gear (XL)"),
        };

        // The fixed gears, by the name a layout gives them: the gate's big pair and the vault's small iron and rust ones.
        private static readonly Dictionary<string, string> FixedModels = new()
        {
            { "Skeleton", "gear_skeleton" },
            { "Flywheel", "gear_flywheel" },
            { "Fixed_S", "gear_fixed_s" },
            { "Fixed_M", "gear_fixed_m" },
        };

        /// <summary>
        /// Build the gate's puzzle into <paramref name="root"/>, and hand back the door it made. Returns what it built,
        /// for the rebuild's log line.
        /// </summary>
        public static string Attach(GameObject root, WorkshopLayout layout, Dictionary<string, Transform> nodes, out GearDoor gate)
        {
            List<ShutLeaf> leaves = ShutLeaves(layout, nodes, GateDoor);
            if (leaves.Count == 0) throw new InvalidDataException("workshop_layout.json has no gate hinges. Re-run workshop_export.py.");

            // The shut gate's outer face, which faces the lane (+X): every gear on it is seated in that plane.
            float face = float.MinValue;
            foreach (ShutLeaf leaf in leaves) face = Mathf.Max(face, leaf.Shut.max.x);
            var plane = new Pose(new Vector3(face, 0f, 0f), Quaternion.identity);

            gate = Build(root, "GearDoor", GateDoor, GearDoorLayout.Load(), leaves, plane, groundProbe: null);
            return Describe("gear door", gate, leaves.Count);
        }

        /// <summary>
        /// Build the vault's door into <paramref name="root"/>: one round leaf, its face where the model marks it
        /// (VaultDoor_Face), and its loose cogs on the floor in front of it.
        /// </summary>
        public static string AttachVault(GameObject root, WorkshopLayout layout, Dictionary<string, Transform> nodes, out GearDoor vault)
        {
            List<ShutLeaf> leaves = ShutLeaves(layout, nodes, VaultDoor);
            if (leaves.Count == 0) throw new InvalidDataException("workshop_layout.json has no vault door leaf. Re-run workshop_export.py.");

            // The face marker's -Y is out of the door (the gear kit's front), so its forward points in. The door's frame
            // is laid as the gate's is: +X out, +Z along the door (the player's right facing it), at the marker.
            Pose marked = layout.PoseOf(FaceMark);
            Vector3 outward = -(marked.rotation * Vector3.forward);
            Vector3 along = Vector3.Cross(Vector3.up, -outward).normalized;
            var plane = new Pose(marked.position, Quaternion.LookRotation(along, Vector3.up));

            vault = Build(root, "VaultDoor", VaultDoor, GearDoorLayout.Load(GearDoorLayout.VaultPath), leaves, plane,
                          groundProbe: VaultPileProbeHeight);
            return Describe("vault door", vault, leaves.Count);
        }

        private static string Describe(string what, GearDoor door, int leaves)
        {
            var so = new SerializedObject(door);
            SerializedProperty pegs = so.FindProperty("pegs");
            int standing = 0;
            for (int i = 0; i < pegs.arraySize; i++)
                if (pegs.GetArrayElementAtIndex(i).FindPropertyRelative("startKind").intValue >= 0) standing++;
            return $"{what}: {pegs.arraySize} pegs, {standing} gears on them, {so.FindProperty("pile").arraySize} loose gears, " +
                   $"{leaves} leaves shut";
        }

        /// <summary>A leaf turned shut, its hull's bounds in the prefab's space, and how it opens.</summary>
        private readonly struct ShutLeaf
        {
            public readonly Transform Node;
            public readonly Bounds Shut;
            public readonly GearDoor.Leaf Row;

            public ShutLeaf(Transform node, Bounds shut, GearDoor.Leaf row)
            {
                Node = node; Shut = shut; Row = row;
            }
        }

        /// <summary>The leaves of one door, shut and solid. Their open pose is the one they are modelled in.</summary>
        private static List<ShutLeaf> ShutLeaves(WorkshopLayout layout, Dictionary<string, Transform> nodes, string door)
        {
            var leaves = new List<ShutLeaf>();
            foreach (WorkshopLayout.Hinge hinge in layout.hinges ?? new WorkshopLayout.Hinge[0])
            {
                if ((hinge.door ?? string.Empty) != door) continue;
                if (!nodes.TryGetValue(hinge.node, out Transform node))
                    throw new InvalidDataException($"workshop_moving.fbx has no door leaf '{hinge.node}'.");

                Vector3 axis = WorkshopLayout.V(hinge.axis).normalized;
                node.rotation = Quaternion.AngleAxis(hinge.shut, axis) * node.rotation;
                leaves.Add(new ShutLeaf(node, MakeLeafSolid(node), new GearDoor.Leaf
                {
                    node = node,
                    localAxis = node.InverseTransformDirection(axis).normalized,
                    openDegrees = -hinge.shut,
                }));
            }
            return leaves;
        }

        /// <summary>
        /// One gear door: its drive and lock, its pegs and the gears standing on them, on the leaves, in
        /// <paramref name="plane"/> (prefab space; +X out of the door, +Z along it, origin on the face at floor level
        /// below the door's middle), and its pile on the ground in front of it (+X `out`, +Z `u`).
        /// </summary>
        private static GearDoor Build(GameObject root, string name, string saveId, GearDoorLayout puzzle, List<ShutLeaf> leaves,
                                      Pose plane, float? groundProbe)
        {
            Transform doorRoot = new GameObject(name).transform;
            doorRoot.SetParent(root.transform, false);
            doorRoot.SetLocalPositionAndRotation(plane.position, plane.rotation);
            Vector3 outward = doorRoot.right;
            // A gear model faces out of its seat, the way PlaceModel turns it: the seat's Z points into the door.
            Quaternion seatFrame = Quaternion.LookRotation(-outward, Vector3.up);

            List<InventoryItem> items = EnsureItems(Geometry(puzzle));
            var kinds = new GearDoor.Kind[Items.Length];
            for (int k = 0; k < Items.Length; k++)
            {
                GameObject model = LoadModel(Items[k].Model);
                kinds[k] = new GearDoor.Kind
                {
                    item = items[k], teeth = Items[k].Teeth, model = model,
                    toothDegrees = ToothDegrees(model, Items[k].Teeth),
                };
            }

            GameObject peg = LoadModel(PegModel);
            Transform LeafAt(float u)
            {
                foreach (ShutLeaf leaf in leaves)
                {
                    (float least, float most) = Across(doorRoot, root.transform, leaf.Shut);
                    if (u >= least && u <= most) return leaf.Node;
                }
                string door = saveId == GateDoor ? "gate" : "vault door";
                throw new InvalidDataException($"no {door} leaf stands at u = {u:0.00}: its layout is off the door.");
            }

            // Made at metre scale and then hung on the leaf keeping that: the leaf is an FBX node at scale 100.
            Transform OnLeaf(Transform leaf, string seatName, Vector3 at)
            {
                Transform made = new GameObject(seatName).transform;
                made.SetPositionAndRotation(at, seatFrame);
                made.SetParent(leaf, true);
                return made;
            }

            Transform Seat(string seatName, Vector2 at, out Transform post)
            {
                Transform leaf = LeafAt(at.x);
                Vector3 bolted = doorRoot.TransformPoint(new Vector3(0f, at.y, at.x));
                post = OnLeaf(leaf, seatName + "_Peg", bolted);
                GearDoor.PlaceModel(peg, post, ModelScale);

                Transform seat = OnLeaf(leaf, seatName, bolted + outward * SeatOut);
                Transform spinner = new GameObject("Spinner").transform;
                spinner.SetParent(seat, false);
                return seat;
            }

            GearDoor.FixedGear Fixed(string seatName, GearDoorLayout.Fixed spec)
            {
                Transform seat = Seat(seatName, GearDoorLayout.V(spec.at), out _);
                Transform spinner = seat.Find("Spinner");
                GameObject model = LoadModel(FixedModels[spec.kind]);
                GearDoor.PlaceModel(model, spinner, ModelScale);
                return new GearDoor.FixedGear
                {
                    spinner = spinner, plane = GearDoorLayout.V(spec.at), teeth = spec.teeth,
                    toothDegrees = ToothDegrees(model, spec.teeth),
                };
            }

            GearDoor.FixedGear drive = Fixed("Drive", puzzle.driver);
            GearDoor.FixedGear lockGear = Fixed("Lock", puzzle.output);

            var pegs = new GearDoor.Peg[puzzle.pegs.Length];
            for (int i = 0; i < pegs.Length; i++)
            {
                Vector2 at = GearDoorLayout.V(puzzle.pegs[i]);
                pegs[i] = new GearDoor.Peg { seat = Seat($"Seat_{i:00}", at, out Transform post), plane = at, post = post };
            }

            // The part of the train still standing: drawn already, so the door looks right in the editor too.
            foreach (GearDoorLayout.Placed standing in puzzle.preinstalled)
            {
                GearDoor.Peg holding = pegs[standing.peg];
                holding.startKind = KindIndex(standing.kind);
                GearDoor.PlaceModel(kinds[holding.startKind].model, holding.seat.Find("Spinner"), ModelScale);
            }

            // The pile, lying front up on the floor in front of the door (out of the gate's leaves' sweep).
            Transform pileRoot = new GameObject("Pile").transform;
            pileRoot.SetParent(doorRoot, false);
            var pile = new GearDoor.PileSpot[puzzle.pile.Length];
            for (int i = 0; i < pile.Length; i++)
            {
                GearDoorLayout.Loose loose = puzzle.pile[i];
                int kind = KindIndex(loose.kind);
                Transform seat = new GameObject($"Spot_{i:00}").transform;
                seat.SetParent(pileRoot, false);
                seat.localPosition = new Vector3(loose.@out, 0f, loose.u);
                seat.localRotation = Quaternion.LookRotation(Vector3.down, Quaternion.Euler(0f, loose.yaw, 0f) * Vector3.forward);
                Transform spinner = new GameObject("Spinner").transform;
                spinner.SetParent(seat, false);
                GearDoor.PlaceModel(kinds[kind].model, spinner, ModelScale);
                pile[i] = new GearDoor.PileSpot { seat = seat, kind = kind };
            }

            var rows = new GearDoor.Leaf[leaves.Count];
            for (int i = 0; i < rows.Length; i++) rows[i] = leaves[i].Row;

            GearDoor door = doorRoot.gameObject.AddComponent<GearDoor>();
            door.Author(saveId, Geometry(puzzle), ModelScale, kinds, drive, lockGear, pegs, pile, rows);
            if (groundProbe.HasValue)
            {
                var so = new SerializedObject(door);
                so.FindProperty("groundProbeHeight").floatValue = groundProbe.Value;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            EditorUtility.SetDirty(door);
            return door;
        }

        /// <summary>How far along the door (its frame's +Z) a shut leaf reaches, least and most.</summary>
        private static (float least, float most) Across(Transform door, Transform root, Bounds shut)
        {
            float least = float.MaxValue, most = float.MinValue;
            for (int corner = 0; corner < 8; corner++)
            {
                var local = new Vector3((corner & 1) == 0 ? shut.min.x : shut.max.x, (corner & 2) == 0 ? shut.min.y : shut.max.y,
                                        (corner & 4) == 0 ? shut.min.z : shut.max.z);
                float u = door.InverseTransformPoint(root.TransformPoint(local)).z;
                least = Mathf.Min(least, u);
                most = Mathf.Max(most, u);
            }
            return (least, most);
        }

        // ── The leaves ───────────────────────────────────────────────────────

        /// <summary>The leaf's hull becomes what is bumped into, the leaf a kinematic body, and the hull carves a NavMesh.
        /// Returns the shut leaf's bounds in the prefab's space.</summary>
        private static Bounds MakeLeafSolid(Transform node)
        {
            Bounds? shut = null;
            foreach (MeshFilter filter in node.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!filter.name.StartsWith(CollisionPrefix)) continue;

                if (filter.TryGetComponent(out MeshRenderer renderer)) renderer.enabled = false;
                MeshCollider hull = filter.gameObject.AddComponent<MeshCollider>();
                hull.sharedMesh = filter.sharedMesh;
                hull.convex = true;

                // A shut leaf is a wall across the lane; an open one stands beside it. Carving, as a turning room's wall.
                NavMeshObstacle obstacle = filter.gameObject.AddComponent<NavMeshObstacle>();
                obstacle.shape = NavMeshObstacleShape.Box;
                obstacle.center = filter.sharedMesh.bounds.center;
                obstacle.size = filter.sharedMesh.bounds.size;
                obstacle.carving = true;

                // From the mesh: the renderer was just switched off, and a switched-off renderer has no bounds.
                Bounds local = filter.sharedMesh.bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = filter.transform.TransformPoint(new Vector3(
                        (corner & 1) == 0 ? local.min.x : local.max.x, (corner & 2) == 0 ? local.min.y : local.max.y,
                        (corner & 4) == 0 ? local.min.z : local.max.z));
                    Bounds grown = shut ?? new Bounds(point, Vector3.zero);
                    grown.Encapsulate(point);
                    shut = grown;
                }
            }
            if (!shut.HasValue) throw new InvalidDataException($"door leaf '{node.name}' has no {CollisionPrefix} hull. Re-run workshop_export.py.");

            Rigidbody body = node.gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            return shut.Value;
        }

        // ── Gears ────────────────────────────────────────────────────────────

        public static GearGeometry Geometry(GearDoorLayout puzzle) => new()
        {
            perTooth = puzzle.perTooth, addendum = puzzle.addendum,
            clearance = puzzle.clearance, tolerance = puzzle.tolerance,
        };

        private static int KindIndex(string kind)
        {
            for (int k = 0; k < Items.Length; k++)
                if (Items[k].Kind == kind) return k;
            throw new InvalidDataException($"A gear door layout names a gear kind the builder does not make: '{kind}'.");
        }

        private static GameObject LoadModel(string file) => CarriedGearItems.LoadModel(file);

        /// <summary>
        /// Where the model has a tooth tip, in degrees about its axis, in the frame it is drawn in on a spinner: the
        /// furthest vertex from the axis of its biggest mesh, which is the rim.
        /// </summary>
        private static float ToothDegrees(GameObject model, int teeth)
        {
            var spinner = new GameObject("ToothProbe").transform;
            try
            {
                GameObject drawn = GearDoor.PlaceModel(model, spinner, 1f);
                MeshFilter rim = null;
                foreach (MeshFilter filter in drawn.GetComponentsInChildren<MeshFilter>(true))
                    if (rim == null || filter.sharedMesh.vertexCount > rim.sharedMesh.vertexCount) rim = filter;

                Vector3[] vertices = rim.sharedMesh.vertices;
                Vector2 tip = Vector2.zero;
                foreach (Vector3 vertex in vertices)
                {
                    Vector3 local = spinner.InverseTransformPoint(rim.transform.TransformPoint(vertex));
                    var flat = new Vector2(local.x, local.y);
                    if (flat.sqrMagnitude > tip.sqrMagnitude) tip = flat;
                }
                return Mathf.Repeat(Mathf.Atan2(tip.y, tip.x) * Mathf.Rad2Deg, 360f / teeth);
            }
            finally
            {
                Object.DestroyImmediate(spinner.gameObject);
            }
        }

        /// <summary>
        /// One item per gear kind: picked up off the ground, carried in the hands, put on a peg with the use button.
        /// </summary>
        private static List<InventoryItem> EnsureItems(GearGeometry geometry)
        {
            var made = new List<InventoryItem>();
            foreach (GearItem gear in Items)
                made.Add(CarriedGearItems.Ensure("Gear_" + gear.Kind, gear.Label, gear.Model, ModelScale,
                                                 2f * geometry.OuterRadius(gear.Teeth), AddPegPlacement));
            AssetDatabase.SaveAssets();
            return made;
        }

        /// <summary>A door gear is used by putting it on the peg it is aimed at.</summary>
        private static void AddPegPlacement(GameObject root, InventoryItem item)
        {
            PlaceableItem placeable = root.AddComponent<PlaceableItem>();
            GearPegPlacement rule = root.AddComponent<GearPegPlacement>();

            var so = new SerializedObject(placeable);
            SerializedFields.Set(so, "rule", rule);
            so.FindProperty("range").floatValue = PlaceRange;
            so.FindProperty("placeSound").intValue = (int)SpaceGame.Audio.SfxId.ImpactMetal;
            so.ApplyModifiedPropertiesWithoutUndo();

            so = new SerializedObject(rule);
            SerializedFields.Set(so, "gear", item);
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
