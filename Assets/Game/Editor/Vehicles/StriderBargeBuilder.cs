// The Striders' crewed dune barges: prefab VARIANTS of the three dune barges (DuneBargeBuilder), so the
// hull, its walkable interior, its doors and hatches stay the dune barge's, and the Strider half is added
// on top -- exactly as the walking house is a variant of the RigWalker:
//
//   * the agent brain drives it: AgentController + FormationModule hand MoveIntents to a TrackedHullMotor,
//     the shared IMovementMotor for big transform-driven hulls (no mover script of its own);
//   * NetAuthority, so the motor and the brain run on the server only and the hull's own
//     ClientNetworkTransform carries the pose to every client;
//   * the Strider faction, and a crew: lookout posts on the open roof, a gangway at the tracks' feet and
//     a CrewShift, wired by CrewDeckWiring as the houses' are;
//   * TrackContacts: one marker where each track meets the ground, front and rear of its ground run, and
//     the rolling dust each one churns up there (VehicleDustWiring).
//
// Everything is measured from the barge itself: the ride height and footprint from the track links' mesh,
// the posts from the roof (downward rays against the hull's own colliders). The dune barges are untouched.
//
// Re-run from: Tools > SpaceGame > Vehicles > Build Strider Barges (after Build Dune Barge Prefabs, and
// after Wire Strider City changes the column: the formation's regroup distance follows it).
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using SpaceGame.Agents;
using SpaceGame.Core;
using SpaceGame.Core.Persistence;
using SpaceGame.Core.Persistence.EditorTools;
using SpaceGame.Vehicles;
using SpaceGame.Vehicles.Motors;

namespace SpaceGame.EditorTools
{
    public static class StriderBargeBuilder
    {
        // Under Agents/Vehicles: RagdollWiring treats that folder as vehicles and never gives one a ragdoll.
        private const string Folder = "Assets/Game/Prefabs/Agents/Vehicles/Ground/";

        /// <summary>Dune-barge prefab -> its Strider variant. The city lists them in this order.</summary>
        public static readonly (string Base, string Variant)[] Barges =
        {
            ("DuneBarge", "StriderDuneBarge"),
            ("DuneBargeLookout", "StriderDuneBargeLookout"),
            ("DuneBargeCompact", "StriderDuneBargeCompact"),
        };

        public static string PrefabPath(string variant) => Folder + variant + ".prefab";

        public const string TrackContactsName = "TrackContacts";
        private static readonly Regex TrackLink = new Regex(@"^Mesh_TrackAssembly_(?<unit>[A-Za-z]+)_Link");
        /// <summary>A track link's vertex this close above the lowest is on the ground run, not the return run.</summary>
        private const float GroundRunTolerance = 0.4f;
        /// <summary>Puffs of sand per second from each track contact at cruise speed (RollingDust).</summary>
        public const float TrackDustPerContact = 3f;

        /// <summary>Lookouts at the four corners of the roof, as signs of the roof's half extents.</summary>
        private static readonly Vector2[] PostLayout =
        {
            new Vector2(-1f, 1f), new Vector2(1f, 1f),
            new Vector2(-1f, -1f), new Vector2(1f, -1f),
        };

        /// <summary>One post per <see cref="PostLayout"/> entry; the crew each barge carries.</summary>
        public static int CrewPosts => PostLayout.Length;

        /// <summary>How far in from the roof's edge a post stands: a seated lookout's knees clear the rim.</summary>
        private const float PostEdgeInset = 1.5f;
        /// <summary>Spacing of the downward rays that map the roof, m.</summary>
        private const float RoofProbeStep = 1f;
        /// <summary>Roof cells that differ by more than this are not one deck (a crenellation, a step down).</summary>
        private const float RoofStepTolerance = 0.5f;
        /// <summary>Steepest surface a post may stand on.</summary>
        private const float RoofMaxSlopeDegrees = 10f;
        private const float RoofProbeHeadroom = 10f;

        /// <summary>How far out from the side of the hull the gangway is, clear of the tracks.</summary>
        private const float GangwayStandoff = 4f;
        /// <summary>Unseat reach from the gangway to NavMesh, as the houses'.</summary>
        private const float GangwayNavMeshReach = 8f;

        // Formation: a barge parks where it is told (it brakes on a curve and turns on the spot), so its
        // slot tolerance is the monowheels' 4 m, not the walking houses' 8 -- the column's rows are sized
        // for barges with that much slack (StriderCityTemplateTests).
        private const float RestRadius = 45f;
        private const float SlotTolerance = 4f;
        private const float FormationNavSample = 20f;

        [MenuItem("Tools/SpaceGame/Vehicles/Build Strider Barges")]
        public static void BuildAll()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogError("[StriderBargeBuilder] Build out of Play mode: a prefab saved in Play mode ships with no save id.");
                return;
            }

            var built = new List<string>();
            foreach ((string baseName, string variant) in Barges)
                if (Build(baseName, variant)) built.Add(PrefabPath(variant));
            if (built.Count == 0) return;

            AssetDatabase.SaveAssets();
            // A variant's NetworkObject hash resolves only against the saved asset: re-import and
            // reserialize, or the hash never reaches the YAML.
            foreach (string path in built) AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            AssetDatabase.ForceReserializeAssets(built);
            Debug.Log(NetworkPrefabRegistrar.Sync(out _, out _));
            if (!SaveableWiring.TryWirePrefabs())
                Debug.LogError("[StriderBargeBuilder] Save wiring failed; run Tools > Save System > Wire Saveable Prefabs.");
            // Last write: see NetworkObjectDefaults.
            foreach (string path in built) NetworkObjectDefaults.KeepSceneMigrationSync(path);
        }

        private static bool Build(string baseName, string variant)
        {
            var barge = AssetDatabase.LoadAssetAtPath<GameObject>(DuneBargeBuilder.PrefabPath(baseName));
            if (barge == null)
            {
                Debug.LogError($"[StriderBargeBuilder] No {DuneBargeBuilder.PrefabPath(baseName)}; run Tools > Vehicles > Build Dune Barge Prefabs.");
                return false;
            }

            // A preview scene: the roof is mapped with raycasts, and nothing else may answer them. The
            // instance stays connected to the dune barge, which is what makes the save a variant.
            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = (GameObject)PrefabUtility.InstantiatePrefab(barge, scene);
                root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                Physics.SyncTransforms();

                if (!TryMeasureTracks(root, out Vector3[] contacts, out string[] names)) return false;
                float rideHeight = -contacts.Min(c => c.y);
                if (!TryFindRoof(root, scene.GetPhysicsScene(), out Vector3[] posts, out Vector3 roofCentre)) return false;

                Transform[] markers = AddTrackContacts(root, contacts, names, rideHeight);
                AddBrain(root, variant, contacts, rideHeight);
                VehicleDustWiring.AddRollingDust(root, markers, root.GetComponent<TrackedHullMotor>().TopSpeed, TrackDustPerContact);
                var gangway = new Vector3(HullHalfWidth(root) + GangwayStandoff, -rideHeight, contacts.Average(c => c.z));
                CrewDeckWiring.AddCrewDeck(root, posts, roofCentre, gangway, GangwayNavMeshReach);

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(variant), out bool saved);
                if (!saved)
                {
                    Debug.LogError($"[StriderBargeBuilder] The AssetDatabase refused {PrefabPath(variant)}.");
                    return false;
                }
                Debug.Log($"[StriderBargeBuilder] Built {PrefabPath(variant)}: ride height {rideHeight:F2} m, " +
                          $"{contacts.Length} track contacts, {posts.Length} crew posts on the roof.");
                return true;
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private static void AddBrain(GameObject root, string variant, Vector3[] contacts, float rideHeight)
        {
            root.AddComponent<NetAuthority>();
            EntityFactionWiring.Ensure(root, variant);

            float minX = contacts.Min(c => c.x), maxX = contacts.Max(c => c.x);
            float minZ = contacts.Min(c => c.z), maxZ = contacts.Max(c => c.z);
            var motor = root.AddComponent<TrackedHullMotor>();
            SerializedFields.Edit(motor, so =>
            {
                SerializedFields.SetFloat(so, "rideHeight", rideHeight);
                SerializedFields.SetVector2(so, "footprintCenter", new Vector2((minX + maxX) * 0.5f, (minZ + maxZ) * 0.5f));
                SerializedFields.SetVector2(so, "footprintSize", new Vector2(maxX - minX, maxZ - minZ));
            });

            SerializedFields.Edit(root.AddComponent<AgentController>(), so => SerializedFields.Set(so, "MotorComponent", motor));

            // AddComponent does not run Reset, so the priority is set here.
            SerializedFields.Edit(root.AddComponent<FormationModule>(), so =>
            {
                SerializedFields.SetInt(so, "priority", ModulePriority.Social);
                SerializedFields.SetString(so, "formationId", string.Empty);
                SerializedFields.SetFloat(so, "restRadius", RestRadius);
                SerializedFields.SetBool(so, "holdSlotAtRest", true);
                SerializedFields.SetFloat(so, "slotTolerance", SlotTolerance);
                SerializedFields.SetFloat(so, "regroupDistance", RosterAuthoring.CityRegroupDistance);
                SerializedFields.SetFloat(so, "navSampleDistance", FormationNavSample);
            });
        }

        private static Transform[] AddTrackContacts(GameObject root, Vector3[] contacts, string[] names, float rideHeight)
        {
            var holder = new GameObject(TrackContactsName).transform;
            holder.SetParent(root.transform, false);
            var markers = new Transform[contacts.Length];
            for (int i = 0; i < contacts.Length; i++)
            {
                markers[i] = new GameObject(names[i]).transform;
                markers[i].SetParent(holder, false);
                markers[i].localPosition = new Vector3(contacts[i].x, -rideHeight, contacts[i].z);
            }
            return markers;
        }

        /// <summary>
        /// Where each track touches the ground: per track unit and side, the vertices of its links at the
        /// bottom of the loop are the ground run; its front and rear ends are the contacts (root space).
        /// Read from the mesh, not the renderer bounds: one side of a track can be a single mesh round the
        /// whole loop, whose bounds say nothing about where it meets the ground.
        /// </summary>
        private static bool TryMeasureTracks(GameObject root, out Vector3[] contacts, out string[] names)
        {
            var runs = new Dictionary<string, List<Vector3>>();
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                Match match = TrackLink.Match(filter.name);
                if (!match.Success || filter.sharedMesh == null) continue;
                Matrix4x4 toRoot = root.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                foreach (Vector3 vertex in filter.sharedMesh.vertices)
                {
                    Vector3 p = toRoot.MultiplyPoint3x4(vertex);
                    string key = $"{match.Groups["unit"].Value}_{(p.x < 0f ? "L" : "R")}";
                    if (!runs.TryGetValue(key, out List<Vector3> list)) runs[key] = list = new List<Vector3>();
                    list.Add(p);
                }
            }

            var found = new List<Vector3>();
            var labels = new List<string>();
            foreach (KeyValuePair<string, List<Vector3>> run in runs.OrderBy(r => r.Key))
            {
                float bottom = run.Value.Min(v => v.y);
                List<Vector3> ground = run.Value.Where(v => v.y <= bottom + GroundRunTolerance).ToList();
                float x = ground.Average(v => v.x);
                found.Add(new Vector3(x, bottom, ground.Max(v => v.z)));
                labels.Add($"TrackContact_{run.Key}_Front");
                found.Add(new Vector3(x, bottom, ground.Min(v => v.z)));
                labels.Add($"TrackContact_{run.Key}_Rear");
            }

            contacts = found.ToArray();
            names = labels.ToArray();
            if (contacts.Length >= 4) return true;
            Debug.LogError($"[StriderBargeBuilder] {root.name}: found {contacts.Length / 2} track runs, expected two main tracks. " +
                           "Were the Mesh_TrackAssembly_*_Link_* meshes renamed?");
            return false;
        }

        /// <summary>
        /// The roof the crew stand on: map the top surface with downward rays against the barge's own
        /// colliders, keep the flat cells outside every room, and take the largest connected deck of them.
        /// Posts go at its corners, inset from the rim, each on a cell the rays actually found.
        /// </summary>
        private static bool TryFindRoof(GameObject root, PhysicsScene physics, out Vector3[] posts, out Vector3 centre)
        {
            posts = null;
            centre = default;
            Bounds hull = RootBounds(root);
            Bounds[] rooms = Rooms(root);
            float flat = Mathf.Cos(RoofMaxSlopeDegrees * Mathf.Deg2Rad);

            int columns = Mathf.FloorToInt(hull.size.x / RoofProbeStep) + 1;
            int rows = Mathf.FloorToInt(hull.size.z / RoofProbeStep) + 1;
            var top = new float?[columns, rows];
            for (int c = 0; c < columns; c++)
            for (int r = 0; r < rows; r++)
            {
                var from = new Vector3(hull.min.x + c * RoofProbeStep, hull.max.y + RoofProbeHeadroom, hull.min.z + r * RoofProbeStep);
                if (!physics.Raycast(root.transform.TransformPoint(from), Vector3.down, out RaycastHit hit,
                                     hull.size.y + 2f * RoofProbeHeadroom, ~0, QueryTriggerInteraction.Ignore)) continue;
                Vector3 local = root.transform.InverseTransformPoint(hit.point);
                if (hit.normal.y < flat || rooms.Any(room => room.Contains(local))) continue;
                top[c, r] = local.y;
            }

            List<(int c, int r)> deck = LargestDeck(top, columns, rows);
            if (deck.Count == 0)
            {
                Debug.LogError($"[StriderBargeBuilder] {root.name}: no open roof found; the crew have nowhere to stand.");
                return false;
            }

            Vector3 Cell((int c, int r) cell) =>
                new Vector3(hull.min.x + cell.c * RoofProbeStep, top[cell.c, cell.r].Value, hull.min.z + cell.r * RoofProbeStep);
            List<Vector3> cells = deck.Select(Cell).ToList();
            float minX = cells.Min(p => p.x), maxX = cells.Max(p => p.x), minZ = cells.Min(p => p.z), maxZ = cells.Max(p => p.z);
            centre = new Vector3((minX + maxX) * 0.5f, cells.Average(p => p.y), (minZ + maxZ) * 0.5f);
            var reach = new Vector2(Mathf.Max(0f, (maxX - minX) * 0.5f - PostEdgeInset), Mathf.Max(0f, (maxZ - minZ) * 0.5f - PostEdgeInset));

            var chosen = new List<Vector3>();
            foreach (Vector2 corner in PostLayout)
            {
                var target = new Vector3(centre.x + corner.x * reach.x, 0f, centre.z + corner.y * reach.y);
                Vector3 spot = cells.Where(p => !chosen.Contains(p))
                                    .OrderBy(p => new Vector2(p.x - target.x, p.z - target.z).sqrMagnitude).First();
                chosen.Add(spot);
            }

            posts = chosen.ToArray();
            return true;
        }

        /// <summary>The biggest 4-connected patch of probed cells whose neighbours step by no more than RoofStepTolerance.</summary>
        private static List<(int c, int r)> LargestDeck(float?[,] top, int columns, int rows)
        {
            var seen = new bool[columns, rows];
            var best = new List<(int c, int r)>();
            for (int c = 0; c < columns; c++)
            for (int r = 0; r < rows; r++)
            {
                if (seen[c, r] || !top[c, r].HasValue) continue;
                var patch = new List<(int c, int r)>();
                var open = new Stack<(int c, int r)>();
                open.Push((c, r));
                seen[c, r] = true;
                while (open.Count > 0)
                {
                    (int c, int r) cell = open.Pop();
                    patch.Add(cell);
                    foreach ((int dc, int dr) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    {
                        int nc = cell.c + dc, nr = cell.r + dr;
                        if (nc < 0 || nr < 0 || nc >= columns || nr >= rows || seen[nc, nr] || !top[nc, nr].HasValue) continue;
                        if (Mathf.Abs(top[nc, nr].Value - top[cell.c, cell.r].Value) > RoofStepTolerance) continue;
                        seen[nc, nr] = true;
                        open.Push((nc, nr));
                    }
                }
                if (patch.Count > best.Count) best = patch;
            }
            return best;
        }

        /// <summary>The rooms the interior is drawn in (InteriorReveal's volumes): a post is never inside one.</summary>
        private static Bounds[] Rooms(GameObject root)
        {
            var reveal = root.GetComponent<InteriorReveal>();
            if (reveal == null) return System.Array.Empty<Bounds>();
            SerializedProperty volumes = new SerializedObject(reveal).FindProperty("localVolumes");
            var rooms = new Bounds[volumes.arraySize];
            for (int i = 0; i < rooms.Length; i++) rooms[i] = volumes.GetArrayElementAtIndex(i).boundsValue;
            return rooms;
        }

        /// <summary>The barge's solid colliders' bounds in root space.</summary>
        private static Bounds RootBounds(GameObject root)
        {
            Bounds? all = null;
            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
            {
                if (collider.isTrigger) continue;
                Bounds world = collider.bounds;
                var local = new Bounds(root.transform.InverseTransformPoint(world.center), Vector3.zero);
                local.Encapsulate(root.transform.InverseTransformPoint(world.min));
                local.Encapsulate(root.transform.InverseTransformPoint(world.max));
                if (all == null) all = local;
                else { Bounds b = all.Value; b.Encapsulate(local); all = b; }
            }
            return all ?? new Bounds();
        }

        private static float HullHalfWidth(GameObject root)
        {
            Bounds hull = RootBounds(root);
            return Mathf.Max(Mathf.Abs(hull.min.x), Mathf.Abs(hull.max.x));
        }
    }
}
