// Builds Assets/Game/Prefabs/agents/Vehicles/Spacecraft/PlayerShip.prefab from
// Assets/Game/Art/Models/Vehicles/PlayerShip/player_ship.fbx.
//
// The model is the user's hand-built lander (ship_lander_blockout.blend — never edited by
// tooling; player_ship_export.py exports it read-only). Unlike ship_rv it arrives with a real
// modelled interior: 140+ separate wall/floor/hull slabs, four telescoping side-door leaves, a
// ribbed back door, a stepped boarding stair down to the ground and a sill platform. This script
// turns that into a drivable vehicle the ShipRV way: pivots for everything that moves, colliders
// measured off the meshes, cockpit controls, and the full multiplayer + persistence component set.
//
// What moves, and how (all ArticulatedPart, networked by ArticulatedPartInteraction):
//   * BackDoor            — the ribbed aft panel slides DOWN along its own tilted plane.
//   * SlidingDoorLeaf1..4 — telescope: each leaf slides along the shared hull diagonal onto the
//                           lowest leaf, so one press collects them at the forward-lower side of
//                           the opening, arriving staggered because they travel equal speed over
//                           different distances.
//   * BoardingStair       — authored DEPLOYED (reaching the ground). The pivot is re-based so the
//                           stowed pose (tucked into the below-deck void) is the closed pose and
//                           the authored position is openDistance away. Opens with the side door.
//   * SillPlatform        — same re-basing; slides out from under the side-door sill.
//
// What comes OFF, and how (ShipPartSocket + ShipPartRack):
//   * Part_<Kind>_<Side>  — eleven bolt-on modules across seven kinds: the anti-gravity spine, two
//                           nuclear motors, two reactor cores, two belly motors, the nose intake,
//                           two flank turbines and the gun. Each becomes a socket that shows or
//                           hides geometry the prefab already carries, so a "missing" engine needs
//                           nothing spawned and its ghost is the real part, painted. The ship is
//                           authored WRECKED (mask 0) — filling it is the salvage loop.
//
// Everything geometric is measured from the meshes at build time, so a re-export with tweaked
// proportions still lands in the right place. The one thing it cannot survive is renamed meshes —
// VerifyParts refuses to build so a rename fails loudly (see PartNames in player_ship_export.py).
//
// Re-run from: Tools ▸ Vehicles ▸ Build PlayerShip Prefab
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Core;
using SpaceGame.Core.Persistence;
using SpaceGame.Vehicles;
using SpaceGame.World.Safety;
using SpaceGame.World.Weather;

namespace SpaceGame.EditorTools
{
    public static class PlayerShipBuilder
    {
        private const string ModelPath = "Assets/Game/Art/Models/Vehicles/PlayerShip/player_ship.fbx";
        private const string PrefabPath = "Assets/Game/Prefabs/agents/Vehicles/Spacecraft/PlayerShip.prefab";
        private const string TestScenePath = "Assets/Game/Scenes/Tests/Ferdinand_Test_world.unity";

        // Ground clearance the hover servo holds. Larger than ShipRV's 0.15: the boarding stair's
        // open pose reaches ~0.4 m below the hull skirts, and it must land beside the sand rather
        // than inside it.
        private const float HoverClearance = 0.5f;

        // How far a player's transform origin sits above the soles of their boots. Exactly one
        // metre: the capsule is 2 m tall with its centre at the pivot. A seat marker is a place to
        // put that ORIGIN, so a marker on the floor buries the feet a metre through it — which is
        // precisely what the first version of the arrival did.
        private const float PlayerPivotHeight = 1f;

        // How far a passenger seat's interaction volume stands proud of the chair it wraps. Enough
        // that the volume is crossed before the chair's own mesh collider from any angle a body
        // could look at it from, small enough that the volume does not reach out over the aisle and
        // answer for the hull behind it.
        private const float SeatVolumePadding = 0.25f;

        // Doors travel at roughly this speed; per-leaf durations come from distance/speed so the
        // telescoping leaves arrive one after another instead of in lockstep.
        private const float DoorSlideSpeed = 1.4f;

        // How far below horizontal the opened back door swings. The hinge sits ~2.7 m up; at a
        // shallow droop the free end floats in the air, unreachable from the sand. 40° puts the
        // tip at/under the ground so the lowered door is a walkable ramp from the dune up into
        // the bay (the walk collider is clipped at the ground line — see BuildBackDoor).
        private const float BackDoorDroopDegrees = 40f;

        // Slope the invisible boarding-ramp collider is laid at. The player is a Rigidbody capsule
        // with no step offset — it cannot climb the stair's 0.7 m treads (the DuneFoil lesson), so
        // a smooth ramp does the actual carrying and the treads stay visual.
        private const float BoardingRampAngle = 32f;

        // Radius of the player's capsule. The doorway threshold sweeps the floor a body-width in
        // from the plank, which is as far as a body standing in the doorway can reach.
        private const float PlayerBodyRadius = 0.5f;

        // How far above the walk-out plank a floor still counts as this doorway's threshold. The
        // bay floor just inside the side door stands 0.8 m over the plank; the cockpit dais a
        // metre further in is a different deck, and a ramp reaching for that would be a wall of
        // its own kind.
        private const float MaxThresholdRise = 1f;

        // Steepest the measured threshold may come out at before it is a wall again. Reported
        // rather than assumed: it depends entirely on where the artist put the floor.
        private const float MaxThresholdAngle = 45f;

        // Step the threshold sweeps the floor with, hunting the slab's edge and its width, and
        // how far the floor may vary across it and still count as the same floor.
        private const float ThresholdProbeStep = 0.05f;
        private const float ThresholdTolerance = 0.05f;

        // Thickness of the invisible threshold slab, and the overlap it carries past each end so
        // there is no seam where it meets the plank or the floor.
        private const float ThresholdThickness = 0.1f;
        private const float ThresholdOverlap = 0.15f;

        // Named meshes the build measures from. The export script guarantees these names; anything
        // else in the model is treated generically (structural collision by measurement).
        private static readonly string[] RequiredParts =
        {
            "back_door", "back_door_support", "back_door_support.001", "back_door_support.002",
            "sliding_door_1", "sliding_door_2", "sliding_door_3", "sliding_door_4",
            "Mesh_BoardingStair", "Mesh_BoardingStair_Foot", "Mesh_SillPlatform",
            "Mesh_CanopyDome", "Mesh_Deck_Fore", "Mesh_Deck_Main",
        };

        // Role meshes the collision pass leaves alone: the pivots give the moving parts their
        // own collider, and a convex hull of the canopy dome would fill the cockpit solid.
        // `player_ship_export.py` keeps the same list out of the collision bake.
        private static readonly HashSet<string> NoStructuralCollider = new HashSet<string>
        {
            "back_door", "back_door_support", "back_door_support.001", "back_door_support.002",
            "sliding_door_1", "sliding_door_2", "sliding_door_3", "sliding_door_4",
            "Mesh_BoardingStair", "Mesh_BoardingStair_Foot", "Mesh_SillPlatform",
            "Mesh_CanopyDome",
            // Adopted by BuildCockpit, which gives it its own interaction collider.
            "Cockpit_Steering_Wheel",
        };

        [MenuItem("Tools/Vehicles/Build PlayerShip Prefab")]
        public static void Build()
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (source == null)
            {
                Debug.LogError($"[PlayerShipBuilder] Model not found at {ModelPath} — run " +
                               "player_ship_export.py first.");
                return;
            }

            GameObject root = new GameObject("PlayerShip");

            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(source);
            // Reparenting meshes under hinge pivots is not possible on a prefab instance, so the
            // link to the FBX is deliberately broken here (same as ShipRVBuilder).
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            model.name = "Model";
            model.transform.SetParent(root.transform, false);
            model.transform.localPosition = Vector3.zero;

            if (!VerifyParts(model.transform))
            {
                Object.DestroyImmediate(root);
                return;
            }

            model.transform.localRotation = ResolveModelYaw(model.transform);

            // Seat the origin under the middle of the hull at ground level. The stair and its foot
            // are excluded from the measurement: they are authored deployed, reaching below the
            // hull, and an origin at the stair tip would hover the whole ship 0.4 m too high.
            Bounds whole = MeasureAll(model.transform, except: new[] { "Mesh_BoardingStair", "Mesh_BoardingStair_Foot" });
            model.transform.localPosition = new Vector3(-whole.center.x, -whole.min.y, -whole.center.z);

            var parts = new PartLookup(model.transform);

            if (!VerifyOrientation(parts))
            {
                Object.DestroyImmediate(root);
                return;
            }

            ArticulatedPart backDoor = BuildBackDoor(model.transform, parts);
            ArticulatedPart[] leaves = BuildSlidingLeaves(model.transform, parts);
            ArticulatedPart stair = BuildBoardingStair(model.transform, parts);

            // Double-sided materials: the hull is a surface, not a solid, so this is what makes
            // the interior visible from inside — and it is also what fixes the two belly tracks,
            // one of which is a mirrored copy (negative scale) whose flipped winding made it
            // invisible from one side.
            DoubleSidedMaterials.Apply(model.transform);
            MakeCanopyGlass(parts);

            if (!BuildStructuralCollision(root.transform, model.transform))
            {
                Object.DestroyImmediate(root);
                return;
            }

            // AFTER the collision pass, unlike the other three moving parts: the plank carries the
            // threshold that gets a body off it and onto the bay floor, and that threshold measures
            // the floor off the ship's own collision, which does not exist until the line above.
            ArticulatedPart platform = BuildSillPlatform(root.transform, model.transform, parts);

            // After the collision pass, because a socket adopts the fitting hull that pass gave the
            // module — it is the collider a socket switches off to make the hole a hole.
            if (!BuildPartSockets(root, model.transform))
            {
                Object.DestroyImmediate(root);
                return;
            }

            (Transform seat, Transform dismount, Transform cameraPivot) = BuildCockpit(root.transform, parts);
            BuildArrivalSeats(root.transform, parts);
            BuildCabinAlert(root.transform, parts);

            MountModule mount = BuildRootComponents(root, seat, dismount, cameraPivot);
            WireInteractions(leaves, stair, platform, backDoor);
            WireDeployment(root, mount, backDoor, leaves, stair, platform);

            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PrefabPath));
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            if (prefab == null)
            {
                Debug.LogError("[PlayerShipBuilder] Prefab save failed.");
                return;
            }

            // Both passes below find their work with AssetDatabase.FindAssets, which reads the
            // search index rather than the disk. A prefab saved in this same call is not in that
            // index yet, so without the import the sweeps run over every prefab in the project
            // except the one just built — and the way that fails is silent: the ship comes out
            // looking finished, minus whichever saver its components imply.
            AssetDatabase.ImportAsset(PrefabPath, ImportAssetOptions.ForceUpdate);

            NetworkPrefabRegistrar.Sync(out int added, out int total);
            Core.Persistence.EditorTools.SaveableWiring.WirePrefabs();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // A NetworkObject created by script ships GlobalObjectIdHash 0, and NGO silently drops
            // all but one prefab when several share a hash. The hash is filled in by the component's
            // own OnValidate, which only resolves against the saved ASSET — so the prefab has to be
            // re-imported and then reserialized, or the corrected value never reaches the YAML.
            AssetDatabase.ImportAsset(PrefabPath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.ForceReserializeAssets(new[] { PrefabPath });
            AssetDatabase.Refresh();

            if (!Verify())
                return;

            Debug.Log($"[PlayerShipBuilder] Built {PrefabPath} ({added} prefab(s) newly registered, " +
                      $"{total} networked prefabs total).");
        }

        /// <summary>
        /// Build + drop an instance into the test world. Batchmode entry point:
        /// -executeMethod SpaceGame.EditorTools.PlayerShipBuilder.BuildAll
        /// </summary>
        public static void BuildAll()
        {
            Build();
            PlaceInTestScene();
        }

        // ─────────── Model prep ───────────

        private static bool VerifyParts(Transform model)
        {
            List<string> missing = RequiredParts.Where(name => model.Find(name) == null).ToList();
            if (missing.Count == 0)
                return true;

            Debug.LogError("[PlayerShipBuilder] Aborting — the model is missing " +
                           $"{missing.Count} part(s): {string.Join(", ", missing)}. " +
                           "The export renames may need updating (player_ship_export.py).");
            return false;
        }

        /// <summary>
        /// Yaw that puts the nose (canopy end) on +Z, measured rather than assumed — the Blender
        /// axis conversion has bitten every vehicle in this project at least once.
        /// </summary>
        private static Quaternion ResolveModelYaw(Transform model)
        {
            Bounds canopy = RendererBounds(model.Find("Mesh_CanopyDome"));
            Bounds door = RendererBounds(model.Find("back_door"));
            Vector3 s = canopy.center - door.center;

            float yaw;
            if (Mathf.Abs(s.z) >= Mathf.Abs(s.x))
                yaw = s.z > 0f ? 0f : 180f;
            else
                yaw = s.x < 0f ? 90f : -90f;

            if (!Mathf.Approximately(yaw, 0f))
                Debug.Log($"[PlayerShipBuilder] Model yawed {yaw}° so the nose faces +Z.");
            return Quaternion.Euler(0f, yaw, 0f);
        }

        private static bool VerifyOrientation(PartLookup parts)
        {
            if (parts.B("Mesh_CanopyDome").center.z > parts.B("back_door").center.z)
                return true;
            Debug.LogError("[PlayerShipBuilder] Orientation check failed — canopy is not forward " +
                           "of the back door after yaw. Not building a backwards ship.");
            return false;
        }

        private static Bounds RendererBounds(Transform t)
        {
            Renderer r = t != null ? t.GetComponent<Renderer>() : null;
            return r != null ? r.bounds : new Bounds();
        }

        private static Bounds MeasureAll(Transform model, string[] except)
        {
            Bounds b = default;
            bool first = true;
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
            {
                if (except.Contains(r.name))
                    continue;
                if (first) { b = r.bounds; first = false; }
                else b.Encapsulate(r.bounds);
            }
            return b;
        }

        private class PartLookup
        {
            private readonly Transform model;
            public PartLookup(Transform model) => this.model = model;

            public Transform T(string name)
            {
                Transform t = model.Find(name);
                if (t == null)
                    Debug.LogError($"[PlayerShipBuilder] Missing part '{name}'");
                return t;
            }

            /// <summary>Like <see cref="T"/>, but for parts the build can do without.</summary>
            public Transform Find(string name) => model.Find(name);

            /// <summary>Every direct child whose name starts with <paramref name="prefix"/>.</summary>
            public List<Transform> StartingWith(string prefix)
            {
                var found = new List<Transform>();
                foreach (Transform child in model)
                    if (child.name.StartsWith(prefix))
                        found.Add(child);
                return found;
            }

            public Bounds B(string name) => RendererBounds(model.Find(name));
        }

        // ─────────── Pivots ───────────
        // ArticulatedPart animates its own transform, so every moving part hangs under a pivot
        // GameObject whose authored pose is the CLOSED pose.

        private static Transform MakePivot(Transform parent, string name, Vector3 position, params Transform[] children)
        {
            GameObject pivot = new GameObject(name);
            pivot.transform.SetParent(parent, false);
            pivot.transform.SetPositionAndRotation(position, Quaternion.identity);
            foreach (Transform child in children)
                if (child != null)
                    child.SetParent(pivot.transform, worldPositionStays: true);
            return pivot.transform;
        }

        private static ArticulatedPart AddSlide(Transform pivot, Vector3 axis, float distance,
                                                float openDuration, float closeDuration)
        {
            ArticulatedPart part = pivot.gameObject.AddComponent<ArticulatedPart>();
            Apply(part, so =>
            {
                SerializedFields.SetInt(so, "motion", (int)ArticulatedPart.MotionType.Slide);
                SerializedFields.SetVector3(so, "axis", axis);
                SerializedFields.SetFloat(so, "openDistance", distance);
                SerializedFields.SetFloat(so, "openDuration", openDuration);
                SerializedFields.SetFloat(so, "closeDuration", closeDuration);
            });
            return part;
        }

        private static ArticulatedPart AddRotate(Transform pivot, Vector3 axis, float angle,
                                                 float openDuration, float closeDuration)
        {
            ArticulatedPart part = pivot.gameObject.AddComponent<ArticulatedPart>();
            Apply(part, so =>
            {
                SerializedFields.SetInt(so, "motion", (int)ArticulatedPart.MotionType.Rotate);
                SerializedFields.SetVector3(so, "axis", axis);
                SerializedFields.SetFloat(so, "openAngle", angle);
                SerializedFields.SetFloat(so, "openDuration", openDuration);
                SerializedFields.SetFloat(so, "closeDuration", closeDuration);
            });
            return part;
        }

        private static void AddPanelCollider(Transform pivot, Bounds worldBounds)
        {
            BoxCollider box = pivot.gameObject.AddComponent<BoxCollider>();
            box.center = worldBounds.center - pivot.position;
            box.size = worldBounds.size;
        }

        // The ribbed aft panel, hinged along its BOTTOM edge like ShipRV's cargo ramp: opening
        // swings the whole panel down and aft until it lies just past horizontal, so the door IS
        // the boarding ramp into the aft bay, collider and all. The panel is authored leaning
        // ~35° in over the bay, so the swing is measured off the mesh rather than hardcoded: the
        // open angle is whatever rotation about the hinge carries the panel's own up-direction
        // onto a pose 10° below horizontal pointing aft.
        private static ArticulatedPart BuildBackDoor(Transform model, PartLookup parts)
        {
            Bounds leaf = parts.B("back_door");
            Bounds group = leaf;
            group.Encapsulate(parts.B("back_door_support"));
            group.Encapsulate(parts.B("back_door_support.001"));
            group.Encapsulate(parts.B("back_door_support.002"));

            // The panel OVERHANGS — its top edge leans aft over its bottom edge — so neither AABB
            // corner is the hinge line and guessing the lean's sign from the bounds puts the door
            // through 170° into the bay floor. Measure both edges off the leaf's own vertices:
            // hinge along the measured bottom edge, and the closed direction from bottom to top.
            Transform leafMesh = parts.T("back_door"); // grabbed before MakePivot reparents it
            float bottomZ = EdgeZ(leafMesh, nearBottom: true);
            float topZ = EdgeZ(leafMesh, nearBottom: false);

            Transform pivot = MakePivot(model, "BackDoor",
                new Vector3(group.center.x, group.min.y, bottomZ),
                leafMesh, parts.T("back_door_support"),
                parts.T("back_door_support.001"), parts.T("back_door_support.002"));

            Vector3 closedUp = new Vector3(0f, leaf.size.y, topZ - bottomZ).normalized;
            float outward = Mathf.Sign(group.center.z); // aft door opens aft, away from the hull
            float droop = BackDoorDroopDegrees * Mathf.Deg2Rad;
            Vector3 openDir = new Vector3(0f, -Mathf.Sin(droop), outward * Mathf.Cos(droop));
            float angle = Vector3.SignedAngle(closedUp, openDir, Vector3.right);

            ArticulatedPart part = AddRotate(pivot, Vector3.right, angle, 2.6f, 2.6f);

            // A thin box aligned to the panel's plane, NOT the world AABB of the leaning group —
            // that AABB is ~3.5 m deep, and once the door lies down the player stands on the
            // ghost volume well above the visible ramp. Length is clipped where the lowered
            // panel meets the ground (the hinge height over sin(droop)), so the buried tip has
            // no collider to lever the parked ship off the sand — and the collider's edge is
            // exactly the sand line the player steps up from. Placed while the pivot is still
            // at its authored pose, so it rides the rotation correctly.
            float panelLength = new Vector2(leaf.size.y, leaf.size.z).magnitude;
            float walkLength = Mathf.Min(panelLength, pivot.position.y / Mathf.Sin(droop) + 0.3f);

            GameObject surface = new GameObject("RampSurface");
            surface.transform.SetParent(pivot, false);
            surface.transform.position = pivot.position + closedUp * (walkLength * 0.5f);
            surface.transform.rotation = Quaternion.FromToRotation(Vector3.up, closedUp);
            BoxCollider walk = surface.AddComponent<BoxCollider>();
            walk.size = new Vector3(group.size.x, walkLength, 0.4f);

            pivot.gameObject.AddComponent<ArticulatedPartInteraction>();
            return part;
        }

        // The four-leaf telescoping side door. Authored pose is closed: the leaves fan down the
        // hull's curve, each showing about a metre. Opening slides every leaf up the shared
        // diagonal onto the TOP leaf's position (leaf 1 itself tucks half a step further), so the
        // parts collect in a stack at the aft-upper side — right to left as seen from outside —
        // and the cleared span is the forward-lower half, directly above the sill platform and
        // the boarding stair. Equal speed + unequal distance = the staggered cascade.
        private static ArticulatedPart[] BuildSlidingLeaves(Transform model, PartLookup parts)
        {
            string[] names = { "sliding_door_1", "sliding_door_2", "sliding_door_3", "sliding_door_4" };
            Bounds[] bounds = names.Select(n => parts.B(n)).ToArray();

            Vector3 firstCentre = bounds[0].center;
            Vector3 step = firstCentre - bounds[1].center;

            var result = new ArticulatedPart[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                // Grabbed BEFORE MakePivot reparents it — Transform.Find searches direct children
                // only, so a second lookup afterwards comes back null.
                Transform leaf = parts.T(names[i]);
                Transform pivot = MakePivot(model, "SlidingDoorLeaf" + (i + 1), bounds[i].center, leaf);

                Vector3 delta = i > 0 ? firstCentre - bounds[i].center : step * 0.55f;
                float distance = delta.magnitude;
                float duration = Mathf.Max(0.8f, distance / DoorSlideSpeed);

                result[i] = AddSlide(pivot, delta.normalized, distance, duration, duration);

                // The leaf's OWN hull, never the world AABB of a panel that leans ~20°: the four
                // boxes held 103 m³ around 17 m³ of door. Closed that is merely wasteful; opened,
                // all four stack at the aft-upper end and the phantom corners reach back down
                // across the sill plank and half the boarding ramp, so the way in is a 0.9 m slot
                // beside a 3 m drop. A door leaf is a modelled slab, so a convex hull of it is
                // exact — the same treatment the fittings get, for the same reason.
                AddConvexCollider(leaf.GetComponent<MeshFilter>());
            }

            return result;
        }

        // The stepped stair to the ground under the side door. Authored DEPLOYED: the pivot is
        // created at the authored pose, then shifted inboard so that shifted pose becomes the
        // closed baseline — stowed in the void under the bay floor, hidden behind the belly
        // skirts. Opening slides it back out to exactly where the artist put it.
        private static ArticulatedPart BuildBoardingStair(Transform model, PartLookup parts)
        {
            Bounds stair = parts.B("Mesh_BoardingStair");
            stair.Encapsulate(parts.B("Mesh_BoardingStair_Foot"));
            Bounds sill = parts.B("Mesh_SillPlatform");

            Transform pivot = MakePivot(model, "BoardingStair", stair.center,
                parts.T("Mesh_BoardingStair"), parts.T("Mesh_BoardingStair_Foot"));

            // The ramp collider is attached BEFORE the pivot is shifted to its stowed pose, so
            // it keeps the authored (deployed) pose relative to the stair. Attaching after the
            // shift left the collider a stow-offset outboard of the treads whenever the stair was
            // out — an invisible ramp floating 4 m beside the real one.
            BuildBoardingRamp(pivot, stair, sill);

            float inwardX = -Mathf.Sign(stair.center.x);
            Vector3 stow = new Vector3(inwardX * (stair.size.x + 0.4f), 0f, 0f);
            pivot.position += stow;

            ArticulatedPart part = AddSlide(pivot, (-stow).normalized, stow.magnitude, 2.2f, 2.2f);
            return part;
        }

        /// <summary>
        /// The invisible walking surface over the stair treads: a thin box from the ground past
        /// the sill, laid at <see cref="BoardingRampAngle"/>. It lives under the stair pivot so it
        /// deploys and stows with the stair. Longer than the stair itself when the treads are
        /// steeper than the target angle — a slightly early ramp start beats an unclimbable door.
        /// </summary>
        private static void BuildBoardingRamp(Transform stairPivot, Bounds stair, Bounds sill)
        {
            Vector3 outward = stair.center - sill.center;
            outward.y = 0f;
            outward = outward.sqrMagnitude > 1e-4f ? outward.normalized : Vector3.right;

            float height = sill.max.y - stair.min.y;
            float run = height / Mathf.Tan(BoardingRampAngle * Mathf.Deg2Rad);

            Vector3 top = new Vector3(sill.center.x, sill.max.y, sill.center.z)
                          + outward * (sill.size.magnitude * 0.25f);
            Vector3 bottom = top + outward * run + Vector3.down * height;

            GameObject ramp = new GameObject("BoardingRamp");
            ramp.transform.SetParent(stairPivot, false);
            ramp.transform.position = (top + bottom) * 0.5f;
            ramp.transform.rotation = Quaternion.LookRotation(bottom - top, Vector3.up);

            BoxCollider box = ramp.AddComponent<BoxCollider>();
            float width = Mathf.Min(stair.size.x, stair.size.z);
            box.size = new Vector3(width, 0.1f, Vector3.Distance(top, bottom) + 0.6f);
        }

        // The walk-out plate under the side-door sill, stowed inboard under the doorway floor.
        private static ArticulatedPart BuildSillPlatform(Transform root, Transform model, PartLookup parts)
        {
            Bounds plate = parts.B("Mesh_SillPlatform");
            // Grabbed once, BEFORE MakePivot reparents it — Transform.Find only searches direct
            // children, so a second lookup after the reparent comes back null.
            Transform plateMesh = parts.T("Mesh_SillPlatform");
            Transform pivot = MakePivot(model, "SillPlatform", plate.center, plateMesh);

            // Collider BEFORE the stow shift, same reason as the boarding stair: added after,
            // its offset is computed against the stowed pivot and the walkable box ends up a
            // plate-width outboard of the extended plank.
            AddPanelCollider(pivot, plate);
            BuildDoorThreshold(root, pivot, plate);

            float inwardX = -Mathf.Sign(plate.center.x);
            Vector3 stow = new Vector3(inwardX * (plate.size.x + 0.2f), 0f, 0f);
            pivot.position += stow;

            ArticulatedPart part = AddSlide(pivot, (-stow).normalized, stow.magnitude, 1.6f, 1.6f);

            // Invisible until it extends. The stowed plank hangs in the under-deck void, which is
            // open to the sky from below, so it would otherwise be seen floating inside the belly.
            // ShellVariantSwitcher already knows how to show a renderer only while a watched part
            // is off its closed pose — with no closed shell assigned, it is exactly a visibility
            // switch. The renderer starts disabled to match the authored (closed) state.
            Renderer plateRenderer = plateMesh.GetComponent<Renderer>();
            plateRenderer.enabled = false;
            ShellVariantSwitcher visibility = pivot.gameObject.AddComponent<ShellVariantSwitcher>();
            Apply(visibility, so =>
            {
                SerializedFields.Set(so, "openShell", plateRenderer);
                SetArray(so, "parts", new Object[] { part });
            });
            return part;
        }

        /// <summary>
        /// The invisible slope that carries a body off the walk-out plank and onto the bay floor.
        ///
        /// <para>
        /// The plank's top surface sits 0.82 m BELOW the floor immediately inside the doorway, in
        /// two lips — 0.29 m onto the deck plate and 0.54 m onto the raised slab behind it — with
        /// 0.43 m of run between them. The player is a Rigidbody capsule with no step offset, so
        /// both are walls, and a 0.5 m radius cannot stand on the ledge in between either. Opened,
        /// the side door therefore led onto the plank and stopped there. Same answer as the
        /// boarding stair's 0.7 m treads: a thin ramp does the carrying and the modelled geometry
        /// is left alone (GDC-L1-FEEL-0007 — tune for the sensation, not for the collision's
        /// literal truth; GDC-L1-FEEL-0003 on rejecting a clear intention at a corner).
        /// </para>
        ///
        /// <para>
        /// Both ends are MEASURED off the ship's own collision, not off the meshes: the floor here
        /// is three stacked slabs and none of them is the one a renderer-bounds guess would name.
        /// The ray is dropped from one rise above the plank rather than from the roof, so it cannot
        /// find the hull overhead, and moving parts are excluded so a leaf standing in its closed
        /// pose is not mistaken for the floor.
        /// </para>
        ///
        /// <para>
        /// The ramp is then cut to exactly the floor it climbs to, in both axes. Reaching past that
        /// slab's edge would leave a phantom slope standing on the open deck — and since the ramp
        /// rides the plank's pivot, the stowed pose would push that overhang up through the cabin
        /// floor as an invisible hump. Cut to the slab, the stowed ramp is buried under it.
        /// </para>
        /// </summary>
        private static void BuildDoorThreshold(Transform root, Transform pivot, Bounds plate)
        {
            // The collision colliders were added this frame; queries read the physics scene, which
            // does not know about them until their transforms are pushed across.
            Physics.SyncTransforms();

            float inward = -Mathf.Sign(plate.center.x);
            float innerEdge = plate.center.x + inward * plate.extents.x;
            float outerEdge = plate.center.x - inward * plate.extents.x;
            float rayTop = plate.max.y + MaxThresholdRise;
            float rayLength = MaxThresholdRise * 2f;

            // The floor to climb to is the HIGHEST one within a body's reach of the doorway, not
            // the one under a single probe point: the raised slab's edge is barely 0.15 m past
            // where a capsule first stands, and a probe falling short of it would ramp onto the
            // deck plate and leave the 0.54 m lip behind it untouched. The ray starts one rise up,
            // so anything higher than a threshold is simply never seen.
            float reach = PlayerBodyRadius * 2f;
            float floor = float.NegativeInfinity;
            float floorZ = plate.center.z;
            float floorX = innerEdge;
            for (float d = 0f; d <= reach; d += ThresholdProbeStep)
            {
                float x = innerEdge + inward * d;
                float measured = FloorAt(root, new Vector3(x, rayTop, floorZ), rayLength);
                if (measured <= floor) continue;
                floor = measured;
                floorX = x;
            }

            if (float.IsNegativeInfinity(floor) || floor <= plate.max.y + ThresholdTolerance)
                return; // the plank already meets the floor: nothing to climb, nothing to build

            // The outboard edge of that floor — the first place, coming in from the door, that
            // stands at it. That is where the ramp has to arrive.
            float edgeX = floorX;
            for (float d = 0f; d <= reach; d += ThresholdProbeStep)
            {
                float x = innerEdge + inward * d;
                if (!IsSameFloor(root, new Vector3(x, rayTop, floorZ), rayLength, floor)) continue;
                edgeX = x;
                break;
            }

            // ...and how wide it is, across the doorway, bounded by the plank itself.
            float near = floorZ;
            float far = floorZ;
            while (near - ThresholdProbeStep >= plate.min.z
                   && IsSameFloor(root, new Vector3(floorX, rayTop, near - ThresholdProbeStep),
                                  rayLength, floor))
                near -= ThresholdProbeStep;
            while (far + ThresholdProbeStep <= plate.max.z
                   && IsSameFloor(root, new Vector3(floorX, rayTop, far + ThresholdProbeStep),
                                  rayLength, floor))
                far += ThresholdProbeStep;

            Vector3 bottom = new Vector3(outerEdge, plate.max.y, (near + far) * 0.5f);
            Vector3 top = new Vector3(edgeX, floor, (near + far) * 0.5f);

            float rise = floor - plate.max.y;
            float run = Mathf.Abs(edgeX - outerEdge);
            float slope = Mathf.Atan2(rise, run) * Mathf.Rad2Deg;
            if (slope > MaxThresholdAngle)
            {
                // Built anyway — a steep ramp still beats a wall — but this is the shape of the
                // bug it exists to fix, so it must not pass silently.
                Debug.LogError($"[PlayerShipBuilder] The side door's threshold measures {slope:F0}° " +
                               $"({rise:F2} m over {run:F2} m), past the {MaxThresholdAngle:F0}° a " +
                               "body can walk. The floor inside the doorway has moved relative to " +
                               "the sill plank; the door is not enterable as built.");
            }

            GameObject ramp = new GameObject("DoorThreshold");
            ramp.transform.SetParent(pivot, false);
            ramp.transform.rotation = Quaternion.LookRotation(top - bottom, Vector3.up);
            // Sunk half its thickness, so the WALKING surface is the measured line itself: flush
            // with the plank at the bottom and with the floor at the top. That is also what keeps
            // the stowed ramp under the cabin floor rather than proud of it.
            ramp.transform.position = (bottom + top) * 0.5f
                                      - ramp.transform.up * (ThresholdThickness * 0.5f);

            BoxCollider box = ramp.AddComponent<BoxCollider>();
            box.size = new Vector3(far - near, ThresholdThickness,
                                   Vector3.Distance(bottom, top) + ThresholdOverlap * 2f);
        }

        /// <summary>
        /// The highest point of the ship's own STRUCTURE under <paramref name="from"/>, or
        /// <see cref="float.NegativeInfinity"/> if nothing is within <paramref name="distance"/>.
        ///
        /// <para>
        /// Restricted to colliders under <paramref name="root"/>: the ship is built in whatever
        /// scene the menu item was invoked from, and a ray that hit that scene's terrain would
        /// answer with a height no rebuild could reproduce. Moving parts are excluded as well —
        /// a door leaf or the plank itself standing in its authored pose is not a floor, and here
        /// the answer becomes geometry, so measuring one would bake the door's pose into the hull.
        /// </para>
        /// </summary>
        private static float FloorAt(Transform root, Vector3 from, float distance)
        {
            float best = float.NegativeInfinity;
            foreach (RaycastHit hit in Physics.RaycastAll(from, Vector3.down, distance, ~0,
                                                          QueryTriggerInteraction.Ignore))
            {
                if (!hit.collider.transform.IsChildOf(root)) continue;
                if (hit.collider.GetComponentInParent<ArticulatedPart>() != null) continue;
                if (hit.point.y > best) best = hit.point.y;
            }
            return best;
        }

        /// <summary>Whether the structure under <paramref name="from"/> is still the same floor.</summary>
        private static bool IsSameFloor(Transform root, Vector3 from, float distance, float height)
        {
            float measured = FloorAt(root, from, distance);
            return !float.IsNegativeInfinity(measured)
                   && Mathf.Abs(measured - height) <= ThresholdTolerance;
        }

        // ─────────── Collision ───────────
        // The hull comes from a baked collision proxy, not from a rule applied to the art.
        //
        // Unity will only put a *convex* MeshCollider on a Rigidbody, and no per-mesh rule
        // survives a hull the player walks around inside. Hulling each mesh fills the rooms with
        // its own skin — one of this lander's curved panels is 12.8 m³ of metal inside an 85 m³
        // hull. Shrink-wrapping the surface into grid cells (what this shipped with) is worse in
        // a way that is harder to see: a cell has to span every surface point in it, so wherever
        // the skin curves from floor to roof the cell becomes a pillar standing in the bay.
        //
        // So `player_ship_export.py` splits every closed structural mesh until each piece really
        // is nearly convex and bakes the pieces to `player_ship_collision.fbx` — 420 hulls that
        // hold 1.05x the ship's own volume, i.e. five percent of phantom solid instead of six
        // hundred. The bake asserts that ratio; this pass only has to mount it.
        //
        // Two things stay out of the bake because they need a collider of their own:
        //
        //   * ARTICULATED parts — the door leaves, stair and sill get theirs on their hinge
        //     pivot, so it travels with the panel.
        //   * FITTINGS — a `Part_*` module's socket switches its collider off to make the hole a
        //     hole, and a `Cockpit_Seat_Command` chair's collider is what a body stands against.
        //     Each gets one convex MeshCollider on its own mesh object: still the mesh's
        //     true hull rather than the fat local-bounds box these used to get.
        //
        // The canopy dome gets nothing at all, on purpose: a 3 m character's head occupies the
        // glass ball's lower half, and any honest collider there would brain the pilot.

        private const string CollisionModelPath =
            "Assets/Game/Art/Models/Vehicles/PlayerShip/player_ship_collision.fbx";

        /// <summary>Prefix `player_ship_export.py` stamps on every baked hull: <c>COL_&lt;source mesh&gt;_&lt;n&gt;</c>.</summary>
        private const string CollisionPrefix = "COL_";

        // Cockpit_ covers the hand-built console and command seats. The modelled steering wheel is
        // NOT here — BuildCockpit adopts it with its own padded interaction collider.
        // Part_ is the removable-module prefix the export script stamps.
        private static readonly string[] FittingPrefixes =
            { "Turbine_", "Thruster_", "Intake_", "RCS_", "Sensor_", "Cockpit_", PartPrefix };

        private static bool BuildStructuralCollision(Transform root, Transform model)
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(CollisionModelPath);
            if (source == null)
            {
                Debug.LogError($"[PlayerShipBuilder] No collision proxy at {CollisionModelPath} — " +
                               "re-run player_ship_export.py, which bakes it alongside the model.");
                return false;
            }

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely,
                                              InteractionMode.AutomatedAction);
            instance.name = "Collision";
            instance.transform.SetParent(root, false);

            // The proxy is baked in the model's own space, so it rides the model's placement —
            // the yaw onto +Z and the shift that seats the origin under the hull at ground level.
            instance.transform.SetLocalPositionAndRotation(model.localPosition, model.localRotation);
            instance.transform.localScale = model.localScale;

            MeshFilter[] baked = instance.GetComponentsInChildren<MeshFilter>(true);
            if (baked.Length == 0)
            {
                Debug.LogError("[PlayerShipBuilder] The collision proxy holds no meshes.");
                Object.DestroyImmediate(instance);
                return false;
            }

            if (!CollectHulls(instance.transform, baked, out var sources))
            {
                Object.DestroyImmediate(instance);
                return false;
            }

            int fittings = FitFittingColliders(model);
            if (!VerifyCollisionCoverage(model, sources))
            {
                Object.DestroyImmediate(instance);
                return false;
            }

            Debug.Log($"[PlayerShipBuilder] Collision: {baked.Length} baked hulls over " +
                      $"{sources.Count} meshes, {fittings} fitting hulls.");
            return true;
        }

        /// <summary>
        /// Moves every baked hull onto one holder as a MeshCollider and reports the source meshes
        /// they came from.
        /// </summary>
        /// <remarks>
        /// The bake writes each hull at the identity in the model's space, so all of them share
        /// one transform and a holder carrying that transform can hold the lot — hundreds of
        /// GameObjects for hundreds of colliders buys nothing. That is asserted rather than
        /// assumed: if the export ever starts placing hulls individually, silently collapsing
        /// them would scatter the ship's collision without moving a single visible triangle.
        /// </remarks>
        private static bool CollectHulls(Transform instance, MeshFilter[] baked,
                                         out HashSet<string> sources)
        {
            sources = new HashSet<string>();

            Matrix4x4 shared = instance.worldToLocalMatrix * baked[0].transform.localToWorldMatrix;
            foreach (MeshFilter filter in baked)
            {
                Matrix4x4 local = instance.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                if (local == shared)
                    continue;

                Debug.LogError($"[PlayerShipBuilder] Baked hull '{filter.name}' does not share the " +
                               "proxy's common transform. player_ship_export.py must link every " +
                               "hull at the identity — its points are already in model space.");
                return false;
            }

            GameObject holder = new GameObject("COL_Hulls");
            holder.transform.SetParent(instance, false);
            holder.transform.SetLocalPositionAndRotation(shared.GetPosition(), shared.rotation);
            holder.transform.localScale = shared.lossyScale;

            foreach (MeshFilter filter in baked)
            {
                MeshCollider collider = holder.AddComponent<MeshCollider>();
                collider.sharedMesh = filter.sharedMesh;
                collider.convex = true;
                sources.Add(SourceMeshOf(filter.name));
            }

            foreach (Transform child in instance.Cast<Transform>().ToList())
            {
                if (child != holder.transform)
                    Object.DestroyImmediate(child.gameObject);
            }
            return true;
        }

        /// <summary>
        /// Gives every fitting one convex MeshCollider on its own mesh object — the collider a
        /// socket switches off, and the one a body stands against when it sits in a chair.
        /// </summary>
        private static int FitFittingColliders(Transform model)
        {
            int fitted = 0;
            foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null || !IsFitting(filter.name)
                    || NoStructuralCollider.Contains(filter.name))
                    continue;

                AddConvexCollider(filter);
                fitted++;
            }
            return fitted;
        }

        /// <summary>
        /// One convex MeshCollider on a mesh's own object — the mesh's true hull rather than the
        /// fat local-bounds box. Exact for anything modelled as a slab or a block, which is what
        /// the fittings and the door leaves both are.
        /// </summary>
        private static void AddConvexCollider(MeshFilter filter)
        {
            MeshCollider collider = filter.gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = filter.sharedMesh;
            collider.convex = true;
        }

        /// <summary>
        /// Fails the build if a structural mesh ended up with no collision at all.
        /// </summary>
        /// <remarks>
        /// The export decides what to bake and this file decides what to fit by hand, and the two
        /// lists have to agree. Nothing enforces that across the language boundary, and the way
        /// they fail is silent — a mesh dropped by both sides is simply not solid, which reads as
        /// a hole in the hull long after anyone connects it to a renamed mesh. So the hull names
        /// the bake stamped into its pieces are read back and checked against the model.
        /// </remarks>
        private static bool VerifyCollisionCoverage(Transform model, HashSet<string> baked)
        {
            List<string> uncovered = new List<string>();
            foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null || baked.Contains(filter.name)
                    || NoStructuralCollider.Contains(filter.name)
                    || filter.GetComponent<Collider>() != null
                    || filter.GetComponentInParent<ArticulatedPart>() != null)
                    continue;

                uncovered.Add(filter.name);
            }

            if (uncovered.Count == 0)
                return true;

            Debug.LogError($"[PlayerShipBuilder] {uncovered.Count} mesh(es) have no collision: " +
                           string.Join(", ", uncovered.Take(12)) +
                           ". They are neither in the collision bake nor excluded — reconcile " +
                           "COLLISION_SKIP / COLLISION_OWN_COLLIDER_PREFIXES in " +
                           "player_ship_export.py with NoStructuralCollider here.");
            return false;
        }

        /// <summary>Reads the source mesh back out of a baked hull's <c>COL_&lt;mesh&gt;_&lt;n&gt;</c> name.</summary>
        private static string SourceMeshOf(string hullName)
        {
            string name = hullName.StartsWith(CollisionPrefix)
                ? hullName.Substring(CollisionPrefix.Length)
                : hullName;

            // Source meshes carry Blender's own dotted names ("Cube.005"), so the piece index is
            // split off the end rather than the name split on the first separator.
            int tail = name.LastIndexOf('_');
            return tail > 0 ? name.Substring(0, tail) : name;
        }

        private static bool IsFitting(string name)
        {
            foreach (string prefix in FittingPrefixes)
                if (name.StartsWith(prefix))
                    return true;
            return false;
        }

        // ─────────── Removable modules ───────────

        /// <summary>Prefix ship_parts.py stamps on every module mesh: <c>Part_&lt;Kind&gt;_&lt;Side&gt;</c>.</summary>
        private const string PartPrefix = "Part_";

        /// <summary>
        /// Mounts on this hull: one anti-gravity spine, one nose intake, one gun, and mirrored pairs
        /// of nuclear motors, reactor cores, belly motors and flank turbines. Pinned so a re-export
        /// that silently drops a mesh fails the build rather than shipping a hull with a hole in it
        /// that nothing in the game can fill.
        /// </summary>
        private const int ExpectedSockets = 11;

        /// <summary>
        /// Turn every <c>Part_*</c> mesh into a <see cref="ShipPartSocket"/> and hang the whole set
        /// off one <see cref="ShipPartRack"/> on the root.
        ///
        /// <para>
        /// No pivot GameObject, unlike the doors: a socket does not animate anything, it only shows
        /// or hides the mesh it is on, so the component goes straight onto the module. Fewer objects
        /// and no reparenting means the collision pass above has already given each one exactly the
        /// collider the socket needs.
        /// </para>
        /// <para>
        /// Sockets are sorted by name, and that order is the bit order of the replicated and saved
        /// mask. Hierarchy order would work today and break the first time somebody reorders the
        /// FBX; a name sort is stable across re-exports, which the mask has to outlive.
        /// </para>
        /// </summary>
        private static bool BuildPartSockets(GameObject root, Transform model)
        {
            var found = new List<ShipPartSocket>();
            var unknown = new List<string>();

            foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!filter.name.StartsWith(PartPrefix)) continue;

                if (!TryParseKind(filter.name, out ShipPartKind kind))
                {
                    unknown.Add(filter.name);
                    continue;
                }

                var socket = filter.gameObject.AddComponent<ShipPartSocket>();
                var so = new SerializedObject(socket);
                so.FindProperty("kind").enumValueIndex = (int)kind;
                so.FindProperty("partRenderer").objectReferenceValue = filter.GetComponent<Renderer>();
                so.FindProperty("partCollider").objectReferenceValue = filter.GetComponent<Collider>();
                so.ApplyModifiedPropertiesWithoutUndo();

                found.Add(socket);
            }

            if (unknown.Count > 0)
            {
                Debug.LogError($"[PlayerShipBuilder] {unknown.Count} mesh(es) carry the '{PartPrefix}' " +
                               $"prefix but name no known ShipPartKind: {string.Join(", ", unknown)}. " +
                               "Add the kind to ShipPartKind.cs, or fix PART_KINDS in ship_parts.py.");
                return false;
            }

            // Every kind must actually be present. Without this the build happily ships a hull whose
            // reactor sockets simply do not exist, and the only symptom is an item that fits nothing.
            var missing = System.Enum.GetValues(typeof(ShipPartKind)).Cast<ShipPartKind>()
                .Where(kind => found.All(socket => socket.Kind != kind))
                .ToList();

            if (missing.Count > 0)
            {
                Debug.LogError($"[PlayerShipBuilder] No socket for {string.Join(", ", missing)}. " +
                               "The model is missing those meshes — re-run ship_parts_export.py and " +
                               "player_ship_export.py, and check PART_KINDS in ship_parts.py.");
                return false;
            }

            found.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

            var rack = root.AddComponent<ShipPartRack>();
            var rackSo = new SerializedObject(rack);
            SerializedProperty sockets = rackSo.FindProperty("sockets");
            sockets.arraySize = found.Count;
            for (int i = 0; i < found.Count; i++)
                sockets.GetArrayElementAtIndex(i).objectReferenceValue = found[i];

            // Authored wrecked. The salvage loop has nothing to do on a ship that arrives whole.
            const int authoredMask = 0;
            rackSo.FindProperty("authoredInstalledMask").intValue = authoredMask;
            rackSo.ApplyModifiedPropertiesWithoutUndo();

            // Author the wreck into the prefab as well, not just into the mask. ShipPartSocket.Awake
            // hides an empty module correctly in play mode, but Awake does not run on a scene
            // instance in the editor — so without this the ship sits in the Scene view looking whole
            // and comes apart the moment anyone presses play, which reads as a bug in the sockets.
            for (int i = 0; i < found.Count; i++)
            {
                bool installed = (authoredMask & (1 << i)) != 0;

                var renderer = found[i].GetComponent<Renderer>();
                if (renderer != null) renderer.enabled = installed;

                var collider = found[i].GetComponent<Collider>();
                if (collider != null) collider.enabled = installed;
            }

            Debug.Log($"[PlayerShipBuilder] {found.Count} part socket(s): " +
                      string.Join(", ", found.Select(s => s.name)));
            return true;
        }

        /// <summary>Reads the kind out of <c>Part_&lt;Kind&gt;_&lt;Side&gt;</c>.</summary>
        private static bool TryParseKind(string meshName, out ShipPartKind kind)
        {
            kind = default;

            int start = PartPrefix.Length;
            int end = meshName.LastIndexOf('_');
            if (end <= start) return false;

            return System.Enum.TryParse(meshName.Substring(start, end - start), out kind);
        }

        /// <summary>
        /// The pilot must see out: the canopy's material arrives opaque from the export, so the
        /// dome is a solid ceiling from inside. This rewrites its (already double-sided) material
        /// copy to URP Lit transparent glass. Runs every build on purpose — the double-sided pass
        /// refreshes its copies from the source material each run, so a one-off edit would drift
        /// back to opaque on the next rebuild (see the Material Default Drift note in memory).
        /// </summary>
        private const float CanopyAlpha = 0.3f;

        private static void MakeCanopyGlass(PartLookup parts)
        {
            Transform canopy = parts.Find("Mesh_CanopyDome");
            Renderer renderer = canopy != null ? canopy.GetComponent<Renderer>() : null;
            if (renderer == null)
            {
                Debug.LogWarning("[PlayerShipBuilder] No canopy renderer — dome left opaque.");
                return;
            }

            foreach (Material mat in renderer.sharedMaterials)
            {
                if (mat == null)
                    continue;

                mat.SetOverrideTag("RenderType", "Transparent");
                if (mat.HasProperty("_Surface"))
                    mat.SetFloat("_Surface", 1f); // URP Lit: Transparent
                if (mat.HasProperty("_Blend"))
                    mat.SetFloat("_Blend", 0f);   // Alpha blend
                if (mat.HasProperty("_SrcBlend"))
                    mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                if (mat.HasProperty("_DstBlend"))
                    mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                if (mat.HasProperty("_ZWrite"))
                    mat.SetFloat("_ZWrite", 0f);
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

                if (mat.HasProperty("_BaseColor"))
                {
                    Color c = mat.GetColor("_BaseColor");
                    c.a = CanopyAlpha;
                    mat.SetColor("_BaseColor", c);
                }

                EditorUtility.SetDirty(mat);
            }
        }

        /// <summary>
        /// World-space z of a panel's bottom (or top) edge: the mean z of the vertices in the
        /// lowest (or highest) fifth of the mesh. Used where a tilted panel's AABB cannot say
        /// which horizontal position its edges actually sit at.
        /// </summary>
        private static float EdgeZ(Transform meshTransform, bool nearBottom)
        {
            Mesh mesh = meshTransform.GetComponent<MeshFilter>().sharedMesh;
            Vector3[] verts = mesh.vertices;

            float yMin = float.MaxValue, yMax = float.MinValue;
            var world = new Vector3[verts.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                world[i] = meshTransform.TransformPoint(verts[i]);
                yMin = Mathf.Min(yMin, world[i].y);
                yMax = Mathf.Max(yMax, world[i].y);
            }

            float band = Mathf.Max(0.05f, (yMax - yMin) * 0.2f);
            float sum = 0f;
            int count = 0;
            foreach (Vector3 p in world)
            {
                bool inBand = nearBottom ? p.y <= yMin + band : p.y >= yMax - band;
                if (!inBand)
                    continue;
                sum += p.z;
                count++;
            }
            return count > 0 ? sum / count : 0f;
        }

        // ─────────── Cockpit ───────────
        private static (Transform seat, Transform dismount, Transform cameraPivot) BuildCockpit(
            Transform root, PartLookup parts)
        {
            Bounds canopy = parts.B("Mesh_CanopyDome");
            Bounds deckFore = parts.B("Mesh_Deck_Fore");
            Bounds deckMain = parts.B("Mesh_Deck_Main");
            float floorTop = deckFore.max.y;

            GameObject group = new GameObject("Cockpit");
            group.transform.SetParent(root, false);

            Bounds hull = deckFore;
            hull.Encapsulate(deckMain);
            Transform cameraPivot = Empty(root, "CameraPivot",
                                          new Vector3(0f, canopy.max.y, hull.center.z));

            // The command chairs. The FRONT-LEFT one is the helm, and it needs nothing on it to be
            // one: the root MountModule is directly interactable, so a click on the chair walks up
            // to the module SteerModule listens to and takes the controls. Every other chair
            // becomes a passenger seat (see BuildPassengerSeat): a place to sit and ride, no helm.
            // The measured fallbacks keep an older, chair-less export building.
            List<Transform> chairs = parts.StartingWith("Cockpit_Seat_Command");
            Transform pilotChair = null;
            foreach (Transform chair in chairs)
            {
                if (pilotChair == null) { pilotChair = chair; continue; }
                Bounds cb = RendererBounds(chair);
                Bounds pb = RendererBounds(pilotChair);
                bool moreForward = cb.center.z > pb.center.z + 0.4f;
                bool sameRowMoreLeft = Mathf.Abs(cb.center.z - pb.center.z) <= 0.4f
                                       && cb.center.x < pb.center.x;
                if (moreForward || sameRowMoreLeft)
                    pilotChair = chair;
            }

            // On the cushion and facing the way the chair faces — see MeasureSeat. The
            // chair-less fallback has no cushion to read, so it stands a body on the deck.
            SeatPose helm = pilotChair != null
                ? MeasureSeat(pilotChair)
                : new SeatPose(new Vector3(canopy.center.x, floorTop + PlayerPivotHeight,
                                           canopy.center.z - 1.0f),
                               Quaternion.identity,
                               new Vector3(canopy.center.x + 1.0f, floorTop + PlayerPivotHeight,
                                           canopy.center.z - 1.4f));
            Vector3 seatPos = helm.Pivot;

            // The wheel is scenery — the chair is where you sit down. It keeps its collider so the
            // helm is solid to stand against, and like every other surface on the hull a click on
            // it resolves up to the root module.
            Transform modelledWheel = parts.Find("Cockpit_Steering_Wheel");
            if (modelledWheel != null)
                AdoptSteeringWheel(group.transform, modelledWheel);
            else
                BuildSteeringWheel(group.transform, seatPos + new Vector3(0f, 1.05f, 1.45f));

            // The pilot's own facing. MountModule takes the rider's rotation from this marker, so
            // this is what turns a body to face its chair rather than the ship.
            Transform seat = Empty(group.transform, "SeatPoint", seatPos);
            seat.localRotation = helm.Rotation;
            // Behind the chair and on the deck — the helm faces the console, so there is nowhere
            // forward to stand, and dismounting mid-flight must not drop the pilot through the sky.
            Transform dismount = Empty(group.transform, "DismountPoint", helm.Dismount);

            int seatIndex = 0;
            foreach (Transform chair in chairs)
            {
                if (chair == pilotChair)
                    continue;
                BuildPassengerSeat(group.transform, chair, cameraPivot, ++seatIndex);
            }

            return (seat, dismount, cameraPivot);
        }

        /// <summary>
        /// A ride-along seat: its own MountModule, so several people can be seated at once (the
        /// ship's root module carries only the pilot), and its own MountNetworkSync so remote
        /// machines seat the rider too. No SteerModule references it, so the occupant gets a chair
        /// and not a helm — and allowAISelfMovementWhenMounted stays on, so a passenger sitting
        /// down does not switch off the ship's own driver the way the pilot's mount deliberately
        /// does.
        ///
        /// <para>
        /// <b>Why the seat carries a trigger volume rather than living on the chair mesh.</b> The
        /// root module is directly interactable, so every solid collider on the hull — the chair
        /// meshes included — resolves up to the helm. A trigger is the one thing Interactor does
        /// not resolve upward: it answers only when it holds the interactable itself, and is
        /// see-through otherwise (see Interactor.ResolveAlongRay). Wrapping the chair in one puts
        /// this seat in front of the hull along the ray, so looking at a passenger chair offers
        /// the chair and looking anywhere else offers the controls.
        /// </para>
        /// <para>
        /// The module cannot simply go on the chair mesh instead: the chairs arrive from the FBX
        /// at ~150x scale and with the exporter's axis rotation baked into them, and MountModule
        /// reads its own transform's rotation for the mounted camera's yaw. Riding the chair's own
        /// transform would therefore inherit both — a passenger scaled 150x, facing whatever the
        /// exporter's baked yaw happens to be (it reads 180 on all four, whichever way the chair
        /// really points). This object is unscaled, and takes its rotation from the chair's
        /// GEOMETRY instead: see <see cref="MeasureSeat"/>.
        /// </para>
        /// </summary>
        private static void BuildPassengerSeat(Transform cockpit, Transform chair,
                                               Transform cameraPivot, int index)
        {
            Bounds cb = RendererBounds(chair);

            SeatPose pose = MeasureSeat(chair);

            GameObject seatGo = new GameObject("PassengerSeat" + index);
            seatGo.transform.SetParent(cockpit, false);
            // Turned to face the chair, which the mounted CAMERA reads off this transform (the
            // body reads the SeatPoint below). Two of these four chairs face sideways, and a
            // passenger in one of them was looking out of the side of their own head.
            seatGo.transform.localRotation = pose.Rotation;

            // Positions are still expressed in the cockpit's frame, so they are taken back out of
            // the rotation just applied — otherwise the seat swings around the cockpit origin
            // instead of staying on its own chair.
            Quaternion unturn = Quaternion.Inverse(pose.Rotation);

            Transform seatPoint = Empty(seatGo.transform, "SeatPoint", unturn * pose.Pivot);
            // Out of the chair and onto the deck — forwards for a chair that faces across the hull,
            // backwards for one that faces along it. See MeasureSeat.
            Transform dismount = Empty(seatGo.transform, "DismountPoint", unturn * pose.Dismount);

            MountModule seatModule = seatGo.AddComponent<MountModule>();
            Apply(seatModule, so =>
            {
                SerializedFields.Set(so, "seatPoint", seatPoint);
                SerializedFields.Set(so, "dismountPoint", dismount);
                SerializedFields.Set(so, "thirdPersonPivot", cameraPivot);
                SerializedFields.SetBool(so, "mountableByDirectInteraction", true);
                SerializedFields.SetBool(so, "allowAISelfMovementWhenMounted", true);
                SerializedFields.SetInt(so, "defaultPerspective", (int)MountModule.CameraPerspective.FirstPerson);
                SerializedFields.SetFloat(so, "fallbackDismountDistance", 3f);
            });
            seatGo.AddComponent<MountNetworkSync>();
            // Its own, not the hull's: a ChairPose seats the rider of the MountModule beside it,
            // and each passenger chair is a separate module with a separate occupant.
            seatGo.AddComponent<ChairPose>();

            // The click surface. Padded past the chair so the volume is reached before the chair's
            // own mesh collider along any ray that ends on the chair, and kept snug enough that
            // standing beside it and looking at the hull still offers the helm.
            BoxCollider volume = seatGo.AddComponent<BoxCollider>();
            volume.isTrigger = true;
            // Taken out of the seat's rotation like the markers above, and the SIZE swapped with
            // it: a box on a chair turned 90 degrees has its depth and width exchanged, so keeping
            // the axis-aligned extents would wrap the chair in a box of the wrong shape.
            volume.center = unturn * cb.center;
            Vector3 extents = unturn * cb.size;
            volume.size = new Vector3(Mathf.Abs(extents.x), Mathf.Abs(extents.y), Mathf.Abs(extents.z))
                          + Vector3.one * SeatVolumePadding;
        }

        // ─────────── Cabin alert ───────────

        /// <summary>
        /// The red lamps that throb in the cabin while the ship is coming down.
        ///
        /// <para>
        /// Measured, not authored: the lamps sit a little under the canopy and are spread along the
        /// span the chairs occupy, so the wash reaches every seat rather than pooling over one. They
        /// are saved DISABLED — <c>CabinAlert</c> switches them, and a parked ship is dark.
        /// </para>
        /// <para>
        /// Built here for the same reason the arrival seats are: this builder rewrites the prefab
        /// wholesale, so a lamp rig added by hand survives exactly until the next rebuild.
        /// </para>
        /// </summary>
        private static void BuildCabinAlert(Transform root, PartLookup parts)
        {
            Bounds canopy = parts.B("Mesh_CanopyDome");
            Bounds deckFore = parts.B("Mesh_Deck_Fore");

            List<Transform> chairs = parts.StartingWith("Cockpit_Seat_Command");
            if (chairs.Count == 0)
                return;

            // The span the crew actually occupies, so the lamps are placed against the seats rather
            // than against the hull — which is far longer than the cabin.
            Bounds seated = RendererBounds(chairs[0]);
            foreach (Transform chair in chairs)
                seated.Encapsulate(RendererBounds(chair));

            // Above a seated head but under the canopy. Clamped so an export whose canopy sits low
            // cannot put the lamps outside the hull.
            float lampY = Mathf.Min(deckFore.max.y + 2.2f, canopy.max.y - 0.6f);
            float side = Mathf.Max(1.2f, seated.extents.x + 0.3f);

            GameObject group = new GameObject("CabinAlert");
            group.transform.SetParent(root, false);

            Vector3[] positions =
            {
                new(seated.center.x, lampY, seated.max.z + 0.4f),
                new(seated.center.x - side, lampY, seated.center.z),
                new(seated.center.x + side, lampY, seated.center.z),
                new(seated.center.x, lampY, seated.min.z - 0.4f),
            };

            var lamps = new List<Light>();
            for (int i = 0; i < positions.Length; i++)
            {
                Transform marker = Empty(group.transform, "AlertLamp" + i, positions[i]);

                Light lamp = marker.gameObject.AddComponent<Light>();
                lamp.type = LightType.Point;
                lamp.color = new Color(1f, 0.14f, 0.1f);
                lamp.range = 7.5f;
                lamp.intensity = 0f;
                // Four pulsing shadow casters inside one hull buys nothing and costs a lot.
                lamp.shadows = LightShadows.None;
                lamp.enabled = false;

                lamps.Add(lamp);
            }

            var alert = group.AddComponent<SpaceGame.Vehicles.CabinAlert>();
            Apply(alert, so =>
            {
                SerializedProperty arr = so.FindProperty("lamps");
                arr.arraySize = lamps.Count;
                for (int i = 0; i < lamps.Count; i++)
                    arr.GetArrayElementAtIndex(i).objectReferenceValue = lamps[i];
            });
        }

        // ─────────── Arrival seats ───────────

        /// <summary>
        /// The four poses a crew member occupies while the ship is flying itself down — the crash
        /// landing that opens a world, and the descent every team makes at the start of a versus
        /// match.
        ///
        /// <para>
        /// <b>Its own markers, not the MountModule seat points.</b> Those are authored on the chair
        /// meshes and sit at two different heights, so no single offset stands all four bodies on
        /// the deck; and <c>Cockpit/SeatPoint</c> is the helm, which cannot be moved without moving
        /// where the pilot sits.
        /// </para>
        ///
        /// <para>
        /// <b>Pose and facing are measured PER SEAT</b> off the chair's own mesh — see
        /// <see cref="MeasureSeat"/>. Per seat and not one figure for the hull, because no two of
        /// these chairs are alike: their cushions stand at four different heights, and two of the
        /// four face sideways.
        /// </para>
        ///
        /// <para>
        /// Built here rather than added to the prefab by hand, and that is the whole point of this
        /// method: this builder rewrites the prefab wholesale, so anything hand-added to it is
        /// destroyed by the next rebuild — silently, leaving a ship that flies its descent with
        /// nobody aboard.
        /// </para>
        /// </summary>
        private static void BuildArrivalSeats(Transform root, PartLookup parts)
        {
            GameObject group = new GameObject("ArrivalSeats");
            group.transform.SetParent(root, false);

            List<Transform> chairs = parts.StartingWith("Cockpit_Seat_Command");
            int order = 0;

            foreach (Transform chair in chairs)
            {
                SeatPose pose = MeasureSeat(chair);

                Transform marker = Empty(group.transform, "ArrivalSeat" + (order + 1), pose.Pivot);
                // SeatedRider writes the rider's rotation from this marker every frame, so the two
                // sideways chairs seat their crew sideways instead of everyone facing the nose.
                marker.localRotation = pose.Rotation;

                var seat = marker.gameObject.AddComponent<SpaceGame.Gameplay.ShipSeat>();
                int index = order++;
                Apply(seat, so => SerializedFields.SetInt(so, "order", index));
            }
        }

        /// <summary>
        /// Where a body sits in <paramref name="chair"/>: on its cushion, facing the way the chair
        /// faces.
        ///
        /// <para>
        /// Both numbers are read off the chair's MESH, and both have to be. The chairs come from a
        /// hand-built blockout, so no two are alike — the cushions measure 0.83, 0.88, 0.92 and
        /// 1.56 m above their own decks — and their transforms are no help either: all four arrive
        /// from the FBX at ~150x scale with the exporter's axis rotation baked in, reading yaw 180
        /// whichever way the chair actually points. Asking the geometry is the only thing that
        /// distinguishes the two chairs that face the nose from the two that face sideways.
        /// </para>
        ///
        /// <para>
        /// An earlier version put the seat on the chair's BASE instead, which sank a body a metre
        /// into the pedestal; and the version before that raycast down for the deck and hit the
        /// chair's own MeshCollider, placing every seat on the BACKREST — the arrival crew rode the
        /// descent standing in mid-air two metres up. Neither the deck nor a raycast is the right
        /// question. The cushion is.
        /// </para>
        /// </summary>
        private readonly struct SeatPose
        {
            /// <summary>Where the player's pivot goes — already lifted off the cushion.</summary>
            public readonly Vector3 Pivot;

            /// <summary>Which way the occupant faces.</summary>
            public readonly Quaternion Rotation;

            /// <summary>Where they stand up — on the DECK, not at cushion height.</summary>
            public readonly Vector3 Dismount;

            public SeatPose(Vector3 pivot, Quaternion rotation, Vector3 dismount)
            {
                Pivot = pivot;
                Rotation = rotation;
                Dismount = dismount;
            }
        }

        /// <summary>
        /// Gap between the chair's own edge and the spot its occupant stands up on, so nobody is
        /// left standing inside the seat they just left.
        /// </summary>
        private const float SeatDismountClearance = 0.7f;

        /// <summary>
        /// How far the player pivot rides above the surface it is sitting on.
        ///
        /// <para>
        /// Measured off the "Sit Idle" clip rather than guessed: the pivot sits 1.0 m above the
        /// soles (<see cref="PlayerPivotHeight"/>), and in that pose the body's underside runs
        /// 0.43–0.49 m above the sole plane across the seat pan. 0.55 puts the middle of that on
        /// the cushion. Re-measure it if the seated pose's hip height changes —
        /// <c>sit_idle.py</c> prints the underside profile.
        /// </para>
        /// </summary>
        private const float SeatedPivotAboveCushion = 0.55f;

        private static SeatPose MeasureSeat(Transform chair)
        {
            Bounds bounds = RendererBounds(chair);
            float cushion = CushionHeight(chair, bounds);
            Vector3 forward = ChairForward(chair, cushion);

            // Which side you get out on, and it is not the same answer for every chair. A chair
            // facing down the ship has the console in front of it, so its occupant steps out
            // BACKWARDS into the cabin; a chair facing across the ship has the aisle in front of
            // it, so its occupant steps out FORWARDS. Decided by whether the chair looks along the
            // hull or across it, so a chair added later is handled without being named here.
            bool facesAlongHull = Mathf.Abs(Vector3.Dot(forward, Vector3.forward)) > 0.7f;
            Vector3 stepDirection = facesAlongHull ? -forward : forward;

            // Cleared past the chair's own edge along whichever axis we are stepping, so the
            // clearance means the same thing on a deep command chair and a shallow bench.
            float halfDepth = Mathf.Abs(Vector3.Dot(bounds.extents, stepDirection));
            Vector3 step = stepDirection * (halfDepth + SeatDismountClearance);

            // Standing height off the DECK the chair stands on, not off the cushion the body sits
            // on — otherwise getting up leaves the player hanging in the air by however high the
            // seat was.
            Vector3 dismount = new Vector3(bounds.center.x + step.x,
                                           bounds.min.y + PlayerPivotHeight,
                                           bounds.center.z + step.z);

            return new SeatPose(
                new Vector3(bounds.center.x, cushion + SeatedPivotAboveCushion, bounds.center.z),
                Quaternion.LookRotation(forward, Vector3.up),
                dismount);
        }

        /// <summary>
        /// The seat pan: the height with the most UPWARD-facing area in the chair's middle band,
        /// over the middle of its footprint.
        ///
        /// <para>
        /// Both filters are load-bearing. Without the height band the chair's base plate wins,
        /// which is a floor and not a seat; without the footprint one an armrest does. Measured on
        /// all four chairs before being trusted.
        /// </para>
        /// </summary>
        private static float CushionHeight(Transform chair, Bounds bounds)
        {
            var area = new Dictionary<int, float>();

            foreach (MeshFilter filter in chair.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = filter.sharedMesh;
                if (mesh == null) continue;

                Vector3[] verts = mesh.vertices;
                int[] tris = mesh.triangles;
                Matrix4x4 toWorld = filter.transform.localToWorldMatrix;

                for (int i = 0; i < tris.Length; i += 3)
                {
                    Vector3 a = toWorld.MultiplyPoint3x4(verts[tris[i]]);
                    Vector3 b = toWorld.MultiplyPoint3x4(verts[tris[i + 1]]);
                    Vector3 c = toWorld.MultiplyPoint3x4(verts[tris[i + 2]]);

                    Vector3 cross = Vector3.Cross(b - a, c - a);
                    float magnitude = cross.magnitude;
                    if (magnitude < 1e-6f) continue;
                    if (Vector3.Dot(cross / magnitude, Vector3.up) < 0.8f) continue;

                    Vector3 mid = (a + b + c) / 3f;
                    float height = Mathf.InverseLerp(bounds.min.y, bounds.max.y, mid.y);
                    if (height < 0.15f || height > 0.65f) continue;
                    if (Mathf.Abs(mid.x - bounds.center.x) > bounds.extents.x * 0.6f) continue;
                    if (Mathf.Abs(mid.z - bounds.center.z) > bounds.extents.z * 0.6f) continue;

                    int band = Mathf.RoundToInt(mid.y * 20f);   // 5 cm bands
                    area[band] = area.TryGetValue(band, out float sum)
                        ? sum + magnitude * 0.5f
                        : magnitude * 0.5f;
                }
            }

            if (area.Count > 0)
            {
                float best = float.NegativeInfinity;
                int bestBand = 0;
                foreach (KeyValuePair<int, float> entry in area)
                {
                    if (entry.Value <= best) continue;
                    best = entry.Value;
                    bestBand = entry.Key;
                }
                return bestBand / 20f;
            }

            Debug.LogWarning($"[PlayerShipBuilder] No seat pan found on '{chair.name}' — seating " +
                             "from its bounds instead. Its occupant will sit at the wrong height.",
                             chair);
            return bounds.min.y + bounds.size.y * 0.3f;
        }

        /// <summary>
        /// Which way the chair faces, from the mass of it: the backrest is what stands ABOVE the
        /// cushion, the pan is what lies just below, and a chair faces from its back toward its
        /// front. Falls back to the ship's nose for a chair with no discernible back.
        /// </summary>
        private static Vector3 ChairForward(Transform chair, float cushion)
        {
            Vector3 back = Vector3.zero, pan = Vector3.zero;
            int backCount = 0, panCount = 0;

            foreach (MeshFilter filter in chair.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = filter.sharedMesh;
                if (mesh == null) continue;

                Matrix4x4 toWorld = filter.transform.localToWorldMatrix;
                foreach (Vector3 local in mesh.vertices)
                {
                    Vector3 p = toWorld.MultiplyPoint3x4(local);
                    if (p.y > cushion + 0.15f) { back += p; backCount++; }
                    else if (p.y > cushion - 0.25f) { pan += p; panCount++; }
                }
            }

            if (backCount > 0 && panCount > 0)
            {
                Vector3 forward = (pan / panCount) - (back / backCount);
                forward.y = 0f;
                if (forward.sqrMagnitude > 1e-6f) return forward.normalized;
            }

            Debug.LogWarning($"[PlayerShipBuilder] Could not tell which way '{chair.name}' faces; " +
                             "seating its occupant facing the nose.", chair);
            return Vector3.forward;
        }

        private static Transform Empty(Transform parent, string name, Vector3 localPosition)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            return go.transform;
        }

        /// <summary>
        /// Move the modelled helm wheel out of the mesh soup and into the Cockpit group, giving
        /// it the interaction collider the primitive placeholder used to carry. Local mesh bounds
        /// rather than world Renderer.bounds: the wheel is raked toward the pilot, and a
        /// world-space AABB would give a box far larger than the wheel it wraps.
        /// </summary>
        private static Transform AdoptSteeringWheel(Transform cockpit, Transform wheel)
        {
            wheel.name = "SteeringWheel";
            wheel.SetParent(cockpit, worldPositionStays: true);

            MeshFilter filter = wheel.GetComponent<MeshFilter>();
            BoxCollider box = wheel.gameObject.AddComponent<BoxCollider>();
            if (filter != null && filter.sharedMesh != null)
            {
                box.center = filter.sharedMesh.bounds.center;
                box.size = filter.sharedMesh.bounds.size * 1.15f;
            }
            else
            {
                box.size = new Vector3(0.8f, 0.8f, 0.3f);
            }

            return wheel;
        }

        // Fallback only — used when the model carries no modelled yoke, so an older export still
        // builds; only the BoxCollider matters.
        private static Transform BuildSteeringWheel(Transform parent, Vector3 localPosition)
        {
            GameObject wheel = new GameObject("SteeringWheel");
            wheel.transform.SetParent(parent, false);
            wheel.transform.localPosition = localPosition;
            wheel.transform.localRotation = Quaternion.Euler(-65f, 0f, 0f);

            const float radius = 0.34f;
            for (int i = 0; i < 12; i++)
            {
                float angle = i * 30f;
                Quaternion around = Quaternion.Euler(0f, 0f, angle);
                GameObject seg = Primitive(wheel.transform, "Rim" + i, PrimitiveType.Cylinder);
                seg.transform.localPosition = around * new Vector3(0f, radius, 0f);
                seg.transform.localRotation = around * Quaternion.Euler(0f, 0f, 90f);
                seg.transform.localScale = new Vector3(0.05f, Mathf.PI * radius / 12f, 0.05f);
            }

            GameObject hub = Primitive(wheel.transform, "Hub", PrimitiveType.Cylinder);
            hub.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            hub.transform.localScale = new Vector3(0.14f, 0.04f, 0.14f);

            GameObject column = Primitive(wheel.transform, "Column", PrimitiveType.Cylinder);
            column.transform.localPosition = new Vector3(0f, -radius - 0.25f, 0f);
            column.transform.localScale = new Vector3(0.09f, 0.3f, 0.09f);

            BoxCollider box = wheel.AddComponent<BoxCollider>();
            box.size = new Vector3(radius * 2.2f, radius * 2.2f, 0.22f);
            return wheel.transform;
        }

        private static GameObject Primitive(Transform parent, string name, PrimitiveType type)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        // ─────────── Root components ───────────
        private static MountModule BuildRootComponents(GameObject root, Transform seat,
                                                       Transform dismount, Transform cameraPivot)
        {
            // NetworkObject first: several of the components below are NetworkBehaviours and must
            // find it on Add.
            root.AddComponent<NetworkObject>();

            Rigidbody body = root.AddComponent<Rigidbody>();
            // Sixty tonnes: collisions are real (the compound colliders above are what other
            // things hit) but nothing player-sized shoves this hull anywhere. Damping settles
            // residual pushes while it is parked; while driven the motor owns the velocity anyway.
            body.mass = 60000f;
            body.linearDamping = 1.0f;
            body.angularDamping = 4f;
            // Gravity is handed over by the motor: restWhenParked (below) turns it on whenever
            // nobody is flying, so an empty ship stands on its tracks as dead weight.
            body.useGravity = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.constraints = RigidbodyConstraints.FreezeRotation;

            Bounds collision = MeasureCollision(root.transform);
            HoverRigidbodyMotor motor = root.AddComponent<HoverRigidbodyMotor>();
            Apply(motor, so =>
            {
                SerializedFields.Set(so, "body", body);
                // Deliberately ponderous: a 30 m, sixty-tonne hull that winds up slowly, turns
                // like a ship and rides its ground cushion softly.
                SerializedFields.SetFloat(so, "maxSpeed", 32f);
                SerializedFields.SetFloat(so, "acceleration", 10f);
                SerializedFields.SetFloat(so, "deceleration", 12f);
                SerializedFields.SetFloat(so, "faceRotateSpeed", 1.2f);
                SerializedFields.SetFloat(so, "riderTurnSpeed", 45f);
                SerializedFields.SetFloat(so, "rideHeight", HoverClearance);
                SerializedFields.SetFloat(so, "heightGain", 3.5f);
                SerializedFields.SetFloat(so, "maxFollowGrade", 30f);
                SerializedFields.SetFloat(so, "minClimbRate", 3f);
                SerializedFields.SetFloat(so, "maxSinkRate", 8f);
                // Parked = physics owns it: gravity on, servo off, ship stands on its tracks.
                SerializedFields.SetBool(so, "restWhenParked", true);
                SetVector2(so, "groundSensor.footprintExtents",
                           new Vector2(collision.extents.x, collision.extents.z));
            });

            root.AddComponent<AgentController>();

            MountModule mount = root.AddComponent<MountModule>();
            Apply(mount, so =>
            {
                SerializedFields.Set(so, "seatPoint", seat);
                SerializedFields.Set(so, "dismountPoint", dismount);
                SerializedFields.Set(so, "thirdPersonPivot", cameraPivot);
                // Boarded like every other mount in the project: look at the hull and press the
                // interact key. Interactor resolves an IInteractable by walking UP from the
                // collider it hit, so this module answers for the whole fuselage — the pilot's
                // chair included, which is what makes the chair the helm without anything sitting
                // on it. The passenger seats carry their own trigger volumes (BuildPassengerSeat)
                // precisely so they win that walk-up and seat their occupant instead.
                SerializedFields.SetBool(so, "mountableByDirectInteraction", true);
                SerializedFields.SetBool(so, "allowAISelfMovementWhenMounted", false);
                SerializedFields.SetInt(so, "defaultPerspective", (int)MountModule.CameraPerspective.ThirdPerson);
                // Framed for a 30 m hull.
                SerializedFields.SetVector3(so, "thirdPersonOffset", new Vector3(0f, 12f, -34f));
                SerializedFields.SetFloat(so, "thirdPersonDistance", 34f);
                SerializedFields.SetFloat(so, "thirdPersonLookAhead", 20f);
                SerializedFields.SetFloat(so, "fallbackDismountDistance", 6f);
            });

            // Sits the pilot down in the helm chair. On the root, so it serves BOTH ways a body
            // ends up in a cockpit seat: this module's rider, and the whole crew that SeatedRider
            // straps in for the descent — that one finds this component on the hull and drives it
            // directly, because the arrival deliberately does not go through a mount.
            root.AddComponent<ChairPose>();

            SteerModule steer = root.AddComponent<SteerModule>();
            Apply(steer, so =>
            {
                SerializedFields.Set(so, "mountModule", mount);
                SerializedFields.SetString(so, "moveActionName", "Move");
                // No vertical action: the hover servo owns the altitude, exactly like ShipRV.
                SerializedFields.SetString(so, "verticalActionName", string.Empty);
                SerializedFields.SetBool(so, "jumpEnabled", false);
                SerializedFields.SetBool(so, "leapEnabled", false);
                SerializedFields.SetBool(so, "riderCanRun", false);
                SerializedFields.SetFloat(so, "turnSmoothTime", 0.3f);
            });

            // Netcode: the doors' ArticulatedPartInteraction and the mount sync both ride the
            // messaging channel through this relay; NetAuthority stops remote copies simulating.
            root.AddComponent<NetRelay>();
            root.AddComponent<NetAuthority>();
            root.AddComponent<MountNetworkSync>();

            ClientNetworkTransform netTransform = root.AddComponent<ClientNetworkTransform>();
            netTransform.SyncPositionX = true;
            netTransform.SyncPositionY = true;
            netTransform.SyncPositionZ = true;
            netTransform.SyncRotAngleX = true;
            netTransform.SyncRotAngleY = true;
            netTransform.SyncRotAngleZ = true;
            netTransform.InLocalSpace = false;
            netTransform.Interpolate = true;

            // Persistence: identity + pose + motor + every hatch, matching the shipped ShipRV set.
            // SaveableWiring stamps the prefabId and would add missing savers; adding them here
            // keeps the prefab correct even before that pass runs.
            root.AddComponent<SaveableEntity>();
            root.AddComponent<TransformSaveable>();
            root.AddComponent<MotorStateSaveable>();
            root.AddComponent<ArticulatedPartsSaveable>();

            // The arrival: holds the crew in the ArrivalSeats markers on every machine while the
            // hull flies its descent, and lets them go when it lands. A NetworkBehaviour, so it
            // needs the NetworkObject added at the top of this method.
            root.AddComponent<SpaceGame.Gameplay.Arrival.SeatedRider>();

            // Team livery. The recolour is plain presentation and paints the ship's four painted
            // materials; the accent beside it is what puts a team's swatch on the wire, so every
            // machine paints the same hull the same colour. Outside a versus match nothing writes a
            // swatch and the ship keeps its authored paint.
            ShipAccentRecolor recolor = root.AddComponent<ShipAccentRecolor>();
            ShipTeamAccent accent = root.AddComponent<ShipTeamAccent>();
            Apply(accent, so => SerializedFields.Set(so, "recolor", recolor));

            root.AddComponent<UnderTerrainGuard>();

            SandstormShelter shelter = root.AddComponent<SandstormShelter>();
            Bounds interior = MeasureCollision(root.transform);
            Apply(shelter, so => SetBounds(so, "localVolume",
                new Bounds(new Vector3(interior.center.x, interior.center.y + 1.5f, interior.center.z),
                           new Vector3(interior.size.x * 0.8f, 4f, interior.size.z * 0.8f))));

            return mount;
        }

        private static Bounds MeasureCollision(Transform root)
        {
            Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
            Bounds b = colliders[0].bounds;
            for (int i = 1; i < colliders.Length; i++)
                b.Encapsulate(colliders[i].bounds);
            return b;
        }

        // ─────────── Wiring ───────────

        // EVERY leaf is a switch for the whole side assembly — leaves, stair and platform — so
        // whichever panel the player is looking at, pressing it opens the door, runs the stair
        // out and extends the platform (and mixed states resolve toward "close everything", so
        // four switches can never wedge the group). The back door is its own switch.
        // ArticulatedPartInteraction already carries the request/announce netcode and the
        // late-joiner ask, so nothing here needs a new NetMsg id.
        private static void WireInteractions(ArticulatedPart[] leaves, ArticulatedPart stair,
                                             ArticulatedPart platform, ArticulatedPart backDoor)
        {
            Object[] sideParts = new List<Object>(leaves) { stair, platform }.ToArray();
            foreach (ArticulatedPart leaf in leaves)
            {
                ArticulatedPartInteraction side =
                    leaf.gameObject.AddComponent<ArticulatedPartInteraction>();
                Apply(side, so => SetArray(so, "parts", sideParts));
            }

            ArticulatedPartInteraction back = backDoor.GetComponent<ArticulatedPartInteraction>();
            Apply(back, so => SetArray(so, "parts", new Object[] { backDoor }));
        }

        private static void WireDeployment(GameObject root, MountModule mount, ArticulatedPart backDoor,
                                           ArticulatedPart[] leaves, ArticulatedPart stair,
                                           ArticulatedPart platform)
        {
            VehicleDeploymentController deployment = root.AddComponent<VehicleDeploymentController>();
            var close = new List<Object> { backDoor, stair, platform };
            close.AddRange(leaves);
            Apply(deployment, so =>
            {
                SerializedFields.Set(so, "mountModule", mount);
                SerializedFields.SetBool(so, "retractOnDismount", true);
                SetArray(so, "deployOnMount");
                SetArray(so, "closeOnMount", close.ToArray());
            });
        }

        // ─────────── Scene placement ───────────

        /// <summary>
        /// Drops one PlayerShip beside the ShipRV in the test world (skipped if the scene already
        /// holds one). Placing AFTER the hash-stamping pass matters: the instance then inherits a
        /// real GlobalObjectIdHash instead of poisoning the scene with a 0.
        /// </summary>
        public static void PlaceInTestScene()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                Debug.LogError($"[PlayerShipBuilder] No prefab at {PrefabPath} — build first.");
                return;
            }

            // Additive, never Single: this can run while somebody has a scene open in the editor,
            // and stealing their scene (or silently discarding its unsaved changes) is worse than
            // any convenience. If the test world itself is the open scene, place but let the user
            // save on their own terms.
            var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(TestScenePath);
            bool wasOpen = scene.IsValid() && scene.isLoaded;
            if (!wasOpen)
                scene = EditorSceneManager.OpenScene(TestScenePath, OpenSceneMode.Additive);

            if (scene.GetRootGameObjects().Any(go => go.name == "PlayerShip"))
            {
                Debug.Log("[PlayerShipBuilder] Test world already holds a PlayerShip — left as is.");
                if (!wasOpen)
                    EditorSceneManager.CloseScene(scene, true);
                return;
            }

            GameObject anchor = scene.GetRootGameObjects().FirstOrDefault(go => go.name == "ShipRV");
            Vector3 position = anchor != null
                ? anchor.transform.position + new Vector3(45f, 0f, 0f)
                : Vector3.zero;

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            instance.transform.position = position;
            EditorSceneManager.MarkSceneDirty(scene);

            if (!wasOpen)
            {
                EditorSceneManager.SaveScene(scene);
                EditorSceneManager.CloseScene(scene, true);
                Debug.Log($"[PlayerShipBuilder] Placed PlayerShip at {position} in {TestScenePath} (saved and closed).");
            }
            else
            {
                Debug.Log($"[PlayerShipBuilder] Placed PlayerShip at {position} in the OPEN test world — save the scene to keep it.");
            }
        }

        // ─────────── Proof ───────────

        private static bool Verify()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var problems = new List<string>();

            if (prefab == null)
            {
                Debug.LogError("[PlayerShipBuilder] Verify: prefab missing.");
                return false;
            }

            var netObject = prefab.GetComponent<NetworkObject>();
            if (netObject == null) problems.Add("no NetworkObject");
            else if (netObject.PrefabIdHash == 0) problems.Add("GlobalObjectIdHash is 0");

            var rack = prefab.GetComponent<ShipPartRack>();
            if (rack == null) problems.Add("no ShipPartRack, so nothing can be salvaged into this hull");
            else
            {
                if (rack.Sockets.Count != ExpectedSockets)
                    problems.Add($"{rack.Sockets.Count} part sockets, expected {ExpectedSockets}");
                if (rack.Sockets.Any(socket => socket == null))
                    problems.Add("a null socket in the rack — its index is a bit of the saved mask");
            }

            if (prefab.GetComponentsInChildren<ArticulatedPart>(true).Length != 7)
                problems.Add("expected 7 ArticulatedParts (back door, 4 leaves, stair, platform)");
            if (prefab.GetComponentsInChildren<ArticulatedPartInteraction>(true).Length != 5)
                problems.Add("expected 5 door switches (every sliding leaf + the back door)");
            // One mount per chair: the front-left chair takes the root module (nothing sits on the
            // chair — the module answers for the whole hull), the rest have their own passenger
            // modules behind their own trigger volumes.
            int chairCount = prefab.GetComponentsInChildren<Transform>(true)
                .Count(t => t.name.StartsWith("Cockpit_Seat_Command"));

            // A passenger seat whose module went un-interactable is the silent failure here: the
            // seat is still in the prefab, still networked, still saved — and clicking the chair
            // quietly hands the occupant the CONTROLS instead, because the ray falls through to
            // the root module. Nothing logs.
            foreach (MountModule seat in prefab.GetComponentsInChildren<MountModule>(true))
            {
                if (!seat.MountableByDirectInteraction)
                    problems.Add($"'{seat.name}' cannot be boarded by interaction, so its chair "
                                 + "resolves up to the helm");
                if (seat.gameObject == prefab)
                    continue;
                BoxCollider volume = seat.GetComponent<BoxCollider>();
                if (volume == null || !volume.isTrigger)
                    problems.Add($"passenger seat '{seat.name}' has no trigger volume, so the hull "
                                 + "answers for its chair");
            }

            int mounts = prefab.GetComponentsInChildren<MountModule>(true).Length;
            if (chairCount > 0 && mounts != chairCount)
                problems.Add($"expected {chairCount} MountModules (root + passengers), found {mounts}");
            if (prefab.GetComponent<SaveableEntity>() == null)
                problems.Add("no SaveableEntity");

            // The arrival and the team livery. Every one of these fails SILENTLY when missing: a
            // ship with no SeatedRider flies its descent with nobody aboard, one with no ShipSeat
            // markers seats nobody in it, and one with no ShipTeamAccent leaves every team's hull
            // in the same authored paint. Checked here because this builder rewrites the prefab
            // wholesale, so a rebuild is exactly when they would go missing.
            if (prefab.GetComponent<SpaceGame.Gameplay.Arrival.SeatedRider>() == null)
                problems.Add("no SeatedRider — nobody can ride this hull down");

            int arrivalSeats = prefab.GetComponentsInChildren<SpaceGame.Gameplay.ShipSeat>(true).Length;
            if (chairCount > 0 && arrivalSeats != chairCount)
                problems.Add($"expected {chairCount} ShipSeat markers (one per chair), found {arrivalSeats}");

            if (prefab.GetComponent<ShipAccentRecolor>() == null)
                problems.Add("no ShipAccentRecolor — team colours have nothing to paint");
            if (prefab.GetComponent<ShipTeamAccent>() == null)
                problems.Add("no ShipTeamAccent — no team colour ever reaches the clients");

            if (problems.Count == 0)
                return true;

            Debug.LogError("[PlayerShipBuilder] Verify FAILED: " + string.Join("; ", problems));
            return false;
        }

        // ─────────── SerializedObject helpers ───────────
        // SerializedFields covers the scalar setters; these are the shapes it lacks. Everything
        // goes through one Apply so no builder code can forget ApplyModifiedProperties.

        private static void Apply(Object target, System.Action<SerializedObject> edit)
        {
            var so = new SerializedObject(target);
            edit(so);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetVector2(SerializedObject so, string name, Vector2 value)
        {
            SerializedProperty p = so.FindProperty(name);
            if (p == null)
            {
                Debug.LogError($"[PlayerShipBuilder] {so.targetObject.GetType().Name} has no field '{name}'");
                return;
            }
            p.vector2Value = value;
        }

        private static void SetBounds(SerializedObject so, string name, Bounds value)
        {
            SerializedProperty p = so.FindProperty(name);
            if (p == null)
            {
                Debug.LogError($"[PlayerShipBuilder] {so.targetObject.GetType().Name} has no field '{name}'");
                return;
            }
            p.boundsValue = value;
        }

        private static void SetArray(SerializedObject so, string name, params Object[] values)
        {
            SerializedProperty p = so.FindProperty(name);
            if (p == null)
            {
                Debug.LogError($"[PlayerShipBuilder] {so.targetObject.GetType().Name} has no field '{name}'");
                return;
            }
            p.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }
    }
}
