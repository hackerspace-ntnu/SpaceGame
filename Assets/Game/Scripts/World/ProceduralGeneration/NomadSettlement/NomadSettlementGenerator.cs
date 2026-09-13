// One nomad settlement, bound to one GameObject.
//
// Everything it makes lives under a single "Generated" child, so Clear is one DestroyImmediate and
// deleting the root in the Hierarchy takes the whole town with it -- the same contract as
// RobotSettlementGenerator, which is the shape this project already knows for "a settlement you can
// throw away and roll again".
//
// Edit-time tool. Generate() is never called in play mode: the output is committed to a chunk scene
// and streams in as ordinary scene content, so every machine loads the same bytes and no netcode is
// involved in the buildings. The nomads are hand-placed instances of networked, saveable prefabs --
// the documented scene-placed chunk instance path the Clanker garrison already uses.
//
// Seeded with System.Random, never UnityEngine.Random: one seed is one town, and global Random is
// shared with everything else in the editor. RobotSettlementGenerator's use of it is the documented
// exception in TerrainGeneration.md, not a pattern to copy.
//
// GROUND. Heights come from the terrain HEIGHTMAP, never a raycast. Terrain and buildings share the
// Default layer, so a downward ray can hit a building already placed -- that is how the Clanker town
// once parked a vehicle on a roof. A structure is seated at the LOWEST sample under its footprint
// and refused outright if the ground there is not level, so nothing is left floating at one corner.
//
// WIRING. PatrolModule, BasePatrolModule and HerdModule keep their settings in private
// [SerializeField] fields, and no Configure method may be added to them. They are therefore wired
// with SerializedObject, which is how NomadPrefabBuilder already sets WatchModule's private fields.
// A field name that does not exist writes nothing and reports nothing, so Generate() counts what it
// actually wired and says so in the console.
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.World.Safety;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SpaceGame.World
{
    public enum NomadSettlementSize { Small, Medium, Large }

    public class NomadSettlementGenerator : MonoBehaviour
    {
        [Header("Size")]
        public NomadSettlementSize size = NomadSettlementSize.Medium;

        [Tooltip("One seed is one town. Change it and the town changes.")]
        public int seed = 1;

        [Header("Prefabs (filled in by NomadSettlementPlacer)")]
        public GameObject[] largeBuildings;
        public GameObject[] mediumBuildings;
        public GameObject[] smallBuildings;
        [Tooltip("The freestanding shade sails only. The wall-mounted ones are already hung on the " +
                 "buildings by NomadSettlementBuilder.")]
        public GameObject[] tents;
        public GameObject[] nomads;
        public GameObject mountedNomad;

        [Tooltip("Distinguishes this town's flocks from every other town's. HerdModule keys herds by " +
                 "a GLOBAL string, so two settlements sharing this would share movement broadcasts " +
                 "across kilometres.")]
        public string herdPrefix = "nomad_s00";

        public const string GeneratedRootName = "Generated";

        // How level the ground under a structure has to be.
        //
        // A GRADE, not a fixed drop. A flat 0.45 m allowance was the first attempt and it refused
        // almost everything: the same tolerance that is generous under a 2 m tent is a 4 % slope under
        // an 11 m building, and on dune terrain the big prefabs were rejected everywhere while the
        // small ones placed fine -- three towns came out nearly empty. What the eye actually objects
        // to is a tilted plinth, which is a ratio, so the allowance scales with the footprint and the
        // floor only keeps small things from being held to an impossible standard.
        private const float MinGroundRange = 0.5f;
        private const float MaxGrade = 0.10f;

        /// <summary>Clear ground around every structure, on top of its own footprint.</summary>
        private const float BuildingPadding = 2f;
        private const float MinSpacing = 6f;

        /// <summary>
        /// The street between two neighbours, as a share of their own longest sides.
        ///
        /// A FRACTION, not a fixed metre count, for the same reason MaxGrade is a grade: two metres
        /// of clear ground reads as a lane between two 6 m tents and as a crack between two 50 m
        /// towers. The buildings were scaled up by half and the flat padding did not follow, which
        /// is exactly what "no distance between the buildings" looked like. At 0.45 two neighbours
        /// of longest side S sit at least 0.45*S + 2 m apart, edge to edge.
        /// </summary>
        private const float NeighbourGap = 0.45f;

        /// <summary>
        /// Head room on a computed ring radius. The ring is sized from the MEAN structure in its
        /// band and the band holds whatever the deck deals it, so a band of mostly-large draws needs
        /// a little more circumference than the mean asked for.
        /// </summary>
        private const float RingSlack = 1.12f;

        /// <summary>
        /// How much of the full radial clearance a band keeps from the band inside it.
        ///
        /// The full figure is the sum of the two structures' clearance radii, which is the right
        /// answer only for two that end up on the same heading. Bands are rings of many, so holding
        /// every band to the worst case pushes a large town past 400 m across and past any level
        /// ground the world has. Four fifths of it, and the overlap test still refuses the pairs that
        /// do land radially.
        /// </summary>
        private const float BandStep = 0.8f;

        /// <summary>Clear ground the placer looks for beyond the outermost tent.</summary>
        private const float SiteMargin = 10f;

        /// <summary>
        /// The largest site the world can actually hold, radius in metres.
        ///
        /// This is not a taste knob, it is the chunk. A settlement's whole disc has to lie inside ONE
        /// 500 m chunk -- everything it makes goes into a single chunk scene -- so a site of radius R
        /// can only be centred in the (500 - 2R) square at that chunk's middle, and it has to be level
        /// ground and 800 m from every other town as well. At 200 m that leaves a 100 m square per
        /// chunk to find flat ground in and the large towns simply do not place. A plan that wants
        /// more than this is scaled down to fit rather than shipped unplaceable.
        /// </summary>
        private const float MaxSiteRadius = 165f;

        private const int PlacementAttempts = 60;

        /// <summary>Attempts spent walking a structure's own ring slot before the band is searched at random.</summary>
        private const int SlotAttempts = 12;

        /// <summary>Waypoints in one patrol circuit. Eight reads as a round, not a triangle.</summary>
        private const int WaypointsPerRoute = 8;

        /// <summary>
        /// What a town of each size is made of. The whole difference between a hamlet and a town is
        /// one row of this table, which is why there is no per-size code anywhere below it.
        /// </summary>
        private struct Plan
        {
            public int Large, Medium, Small, Tents;
            public int Residents, Flocks, FlockSize, PatrolRoutes, PatrolSize, Mounted;
            public float CoreRadius, MidRadius, OuterRadius, TentRadius;
        }

        /// <summary>
        /// The four prefab sets a town is built from, in the order its bands use them. Passed around
        /// as one value because the radii below are MEASURED off these prefabs, and the placer has to
        /// ask how big a town will be before there is a generator standing on the ground to ask.
        /// </summary>
        public readonly struct PrefabSets
        {
            public readonly GameObject[] Large, Medium, Small, Tents;

            public PrefabSets(GameObject[] large, GameObject[] medium, GameObject[] small,
                              GameObject[] tents)
            {
                Large = large;
                Medium = medium;
                Small = small;
                Tents = tents;
            }
        }

        public PrefabSets Sets =>
            new PrefabSets(largeBuildings, mediumBuildings, smallBuildings, tents);

        /// <summary>
        /// What a town of each size is made of, before the ground is measured. The radii are filled
        /// in by <see cref="Fit"/> from the prefabs themselves.
        /// </summary>
        private static Plan CountsFor(NomadSettlementSize size) => size switch
        {
            NomadSettlementSize.Large => new Plan
            {
                Large = 7, Medium = 11, Small = 7, Tents = 12,
                Residents = 12, Flocks = 3, FlockSize = 4, PatrolRoutes = 2, PatrolSize = 3, Mounted = 2,
            },
            NomadSettlementSize.Medium => new Plan
            {
                Large = 3, Medium = 6, Small = 5, Tents = 7,
                Residents = 7, Flocks = 2, FlockSize = 3, PatrolRoutes = 1, PatrolSize = 3, Mounted = 1,
            },
            _ => new Plan
            {
                Large = 1, Medium = 3, Small = 4, Tents = 4,
                Residents = 4, Flocks = 1, FlockSize = 3, PatrolRoutes = 0, PatrolSize = 0, Mounted = 0,
            },
        };

        /// <summary>
        /// The radii a town of this size needs to hold its own prefabs, measured rather than typed in.
        ///
        /// They used to be four hand-tuned numbers sized off an 11.3 m prefab. Then the buildings were
        /// scaled up by half and the numbers were not, so seven landmarks that wanted 90 m of core
        /// ring were still being dealt 26 m of it: the ring slots all collided, placement fell through
        /// to random attempts in the same crowded band, and what survived stood shoulder to shoulder.
        ///
        /// A band's ring has a circumference of 2*pi*r, and N structures whose clearance DIAMETER is D
        /// need N*D of it -- so the ring cannot be tighter than N*D/(2*pi). It also has to clear the
        /// band inside it by half of each one's structure. The wider of those two is the ring, and the
        /// band's outer radius is then whatever puts <see cref="PlaceStructures"/>'s own ring line
        /// there: the core band rings at 70 % of its radius, every other band at 55 % across it.
        /// </summary>
        private static Plan Fit(Plan plan, PrefabSets sets)
        {
            float dL = MeanClearance(sets.Large);
            float dM = MeanClearance(sets.Medium);
            float dS = MeanClearance(sets.Small);
            float dT = MeanClearance(sets.Tents);

            // Core: no inner edge to clear, so the ring only has to hold its own count.
            float ringL = Ring(plan.Large, dL);
            plan.CoreRadius = ringL / 0.7f;

            float ringM = Mathf.Max(Ring(plan.Medium, dM), ringL + (dL + dM) * 0.5f * BandStep);
            plan.MidRadius = plan.CoreRadius + ((ringM - plan.CoreRadius) / 0.55f);

            float ringS = Mathf.Max(Ring(plan.Small, dS), ringM + (dM + dS) * 0.5f * BandStep);
            plan.OuterRadius = plan.MidRadius + ((ringS - plan.MidRadius) / 0.55f);

            // The tents share the mid band's inner edge and scatter at random rather than on a ring,
            // so theirs is an area, not a circumference: the annulus has to hold their footprints
            // with room to miss each other in.
            float area = plan.Tents * Mathf.PI * dT * dT;
            plan.TentRadius = Mathf.Max(plan.OuterRadius + dT,
                                        Mathf.Sqrt((plan.MidRadius * plan.MidRadius) + (area / Mathf.PI)));

            // The chunk has the last word. Scaled as a whole so the bands keep their proportions:
            // a town squeezed into the ground available is still a town, a town with one band
            // squeezed is a ring of buildings standing in another ring.
            float want = plan.TentRadius + SiteMargin;
            if (want > MaxSiteRadius)
            {
                float k = (MaxSiteRadius - SiteMargin) / plan.TentRadius;
                plan.CoreRadius *= k;
                plan.MidRadius *= k;
                plan.OuterRadius *= k;
                plan.TentRadius *= k;
            }

            return plan;
        }

        private static float Ring(int count, float diameter) =>
            count <= 0 ? 0f : count * diameter * RingSlack / (Mathf.PI * 2f);

        /// <summary>
        /// The typical structure in a set, as the diameter of the clear ground it wants. The mean
        /// rather than the largest: one 77 m landmark in a deck of forty should not space the whole
        /// band as though every draw were that big, and a draw that does not fit its slot walks round
        /// the ring until it does.
        /// </summary>
        private static float MeanClearance(GameObject[] prefabs)
        {
            if (prefabs == null || prefabs.Length == 0) return MinSpacing;

            float total = 0f;
            int counted = 0;
            foreach (GameObject prefab in prefabs)
            {
                if (prefab == null) continue;
                total += ClearanceRadius(prefab) * 2f;
                counted++;
            }

            return counted == 0 ? MinSpacing : total / counted;
        }

        private static Plan PlanFor(NomadSettlementSize size, PrefabSets sets) =>
            Fit(CountsFor(size), sets);

        /// <summary>How many structures a town of this size asks for. The placer checks what it got.</summary>
        public static int ExpectedStructures(NomadSettlementSize size)
        {
            Plan plan = CountsFor(size);
            return plan.Large + plan.Medium + plan.Small + plan.Tents;
        }

        /// <summary>
        /// Radius of level ground a town of this size needs: its outermost tent plus a margin.
        ///
        /// Static because the placer has to search for the ground BEFORE there is a generator standing
        /// on it, and a second copy of these numbers over there would drift the first time a plan
        /// changed.
        /// </summary>
        public static float SiteRadiusFor(NomadSettlementSize size, PrefabSets sets) =>
            PlanFor(size, sets).TentRadius + SiteMargin;

        public float SiteRadius => SiteRadiusFor(size, Sets);

        // ── generate ─────────────────────────────────────────────────────────────

        [ContextMenu("Generate")]
        public void Generate()
        {
            Clear();

            Plan plan = PlanFor(size, Sets);
            var random = new System.Random(seed);
            var taken = new List<(Vector2 centre, float radius)>();

            Transform root = new GameObject(GeneratedRootName).transform;
            root.SetParent(transform, worldPositionStays: false);
            root.localPosition = Vector3.zero;

            // Largest first: a large building needs the most clear ground, so letting the huts take
            // the middle would leave the landmarks nowhere to stand. Only the large ones face the
            // centre, because they are what the town is arranged around.
            int large = PlaceStructures(largeBuildings, plan.Large, 0f, plan.CoreRadius, true, true, root, random, taken);
            int medium = PlaceStructures(mediumBuildings, plan.Medium, plan.CoreRadius, plan.MidRadius, false, true, root, random, taken);
            int small = PlaceStructures(smallBuildings, plan.Small, plan.MidRadius, plan.OuterRadius, false, true, root, random, taken);
            int tent = PlaceStructures(tents, plan.Tents, plan.MidRadius, plan.TentRadius, false, false, root, random, taken);

            List<Transform[]> routes = BuildPatrolRoutes(plan, root);
            int people = PlacePeople(plan, routes, root, random);

            // Reported against what was asked for, because a short count is the failure this has
            // already had: a town missing six of its seven landmarks still logs a cheerful line if
            // the line only says what it managed.
            Debug.Log($"[NomadSettlementGenerator] {name} ({size}): " +
                      $"large {large}/{plan.Large}, medium {medium}/{plan.Medium}, " +
                      $"small {small}/{plan.Small}, tents {tent}/{plan.Tents}, {people} nomads, " +
                      $"rings {plan.CoreRadius:F0}/{plan.MidRadius:F0}/{plan.OuterRadius:F0}/" +
                      $"{plan.TentRadius:F0} m.", this);
        }

        [ContextMenu("Reroll (new seed + generate)")]
        public void Reroll()
        {
            seed = Random.Range(1, int.MaxValue);
            Generate();
        }

        [ContextMenu("Clear")]
        public void Clear()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform child = transform.GetChild(i);
                if (child.name != GeneratedRootName) continue;

                if (Application.isPlaying) Destroy(child.gameObject);
                else DestroyImmediate(child.gameObject);
            }
        }

        // ── structures ───────────────────────────────────────────────────────────

        /// <summary>
        /// Fills one band with one class of structure.
        ///
        /// <paramref name="onRing"/> picks the strategy, and the difference is not cosmetic. Buildings
        /// go on evenly spaced angular slots around the middle of their band, because pure rejection
        /// sampling cannot pack them: seven large buildings need 13 m between centres, and dropping
        /// them at random inside the core meant most attempts landed on a spot already taken -- the
        /// first build placed one of seven. Slots put them where they fit by construction and the
        /// jitter keeps it from reading as a wheel. Tents stay random, because scattered is the look.
        ///
        /// Either way a candidate still has to pass the ground test, and a slot that cannot be seated
        /// falls through to random attempts rather than leaving a gap in the ring.
        /// </summary>
        private int PlaceStructures(GameObject[] prefabs, int count, float minRadius, float maxRadius,
                                    bool faceCentre, bool onRing, Transform root, System.Random random,
                                    List<(Vector2 centre, float radius)> taken)
        {
            if (prefabs == null || prefabs.Length == 0 || count <= 0) return 0;

            // Inside the core there is no inner edge to sit outside of, so the ring is a fraction of
            // the band; elsewhere it sits just past the middle, leaving the outer half for jitter.
            float ringRadius = minRadius > 0f
                ? Mathf.Lerp(minRadius, maxRadius, 0.55f)
                : maxRadius * 0.7f;
            float ringJitter = (maxRadius - ringRadius) * 0.35f;
            float step = Mathf.PI * 2f / count;
            float startAngle = (float)(random.NextDouble() * Mathf.PI * 2.0);

            int placed = 0;
            for (int i = 0; i < count; i++)
            {
                GameObject prefab = prefabs[random.Next(prefabs.Length)];
                if (prefab == null) continue;

                float clearance = ClearanceRadius(prefab);

                for (int attempt = 0; attempt < PlacementAttempts; attempt++)
                {
                    // The first few attempts walk this slot a little further round the ring; after
                    // that the band is searched at random.
                    Vector2 local;
                    if (onRing && attempt < SlotAttempts)
                    {
                        float angle = startAngle + (i + attempt * 0.22f) * step;
                        float radius = ringRadius + (float)(random.NextDouble() * 2.0 - 1.0) * ringJitter;
                        local = new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
                    }
                    else
                    {
                        local = PointInAnnulus(random, minRadius, maxRadius);
                    }

                    if (IsTaken(local, clearance, taken)) continue;
                    if (!TrySeat(local, prefab, out float groundY)) continue;

                    // Facing the centre is a yaw and nothing more -- no 90 degree snapping, because
                    // nomad adobe is not axis-aligned the way the industrial prefabs are.
                    float yaw = faceCentre
                        ? Mathf.Atan2(-local.x, -local.y) * Mathf.Rad2Deg
                        : (float)(random.NextDouble() * 360.0);

                    Vector3 origin = transform.position;
                    Place(prefab, new Vector3(origin.x + local.x, groundY, origin.z + local.y),
                          Quaternion.Euler(0f, yaw, 0f), root);

                    taken.Add((local, clearance));
                    placed++;
                    break;
                }
            }

            return placed;
        }

        /// <summary>
        /// Reads the ground across the footprint and decides whether a structure may stand there.
        ///
        /// Seated at the LOWEST sample, not the average: at the average half the footprint hangs over
        /// air, and on this terrain that reads as a building floating at one corner. Embedding the low
        /// side instead is invisible. A footprint with any sample off the terrain is refused outright
        /// rather than seated on a guess.
        /// </summary>
        private bool TrySeat(Vector2 local, GameObject prefab, out float groundY)
        {
            groundY = 0f;

            Vector3 origin = transform.position;
            // Sampled as the square of the longest side: the structure has not been yawed yet, and a
            // yaw swings the long side over ground the narrow side never covered.
            float longest = LongestSide(prefab);
            float half = longest * 0.5f;
            float allowed = Mathf.Max(MinGroundRange, longest * MaxGrade);

            float min = float.PositiveInfinity;
            float max = float.NegativeInfinity;

            for (int ix = -1; ix <= 1; ix++)
            for (int iz = -1; iz <= 1; iz++)
            {
                var point = new Vector3(origin.x + local.x + ix * half, 0f, origin.z + local.y + iz * half);
                if (!TerrainProbe.TryGetTerrainHeight(point, out float height)) return false;

                if (height < min) min = height;
                if (height > max) max = height;
            }

            if (max - min > allowed) return false;

            groundY = min;
            return true;
        }

        /// <summary>
        /// The prefab's longest horizontal side, in metres.
        ///
        /// Measured by transforming each mesh's eight bounding corners into the prefab root's space,
        /// NOT by reading Renderer.bounds. Two reasons, and the first one cost a whole build:
        ///
        ///   * Renderer.bounds on a prefab ASSET is not a reliable world measurement -- the renderer
        ///     is not in a scene and has no world transform to be relative to. NomadSettlementBuilder
        ///     works around it by instantiating the prefab at the origin before measuring anything;
        ///     this does the arithmetic instead, so nothing has to be spawned to ask how big it is.
        ///   * mesh.bounds alone is just as wrong the other way: every nomad building is an FBX whose
        ///     meshes sit under children at localScale 100 with the exporter's -90 deg X baked in, so
        ///     the raw mesh reads a couple of centimetres across.
        ///
        /// Transforming the corners goes through both, and the prefab root is at identity, so the
        /// result is honest root-local metres. The set measures 1.97-11.30 m across (TENTS.md and the
        /// NomadSettlementBuilder size-class header), which is what the placer's report checks against.
        ///
        /// COLLIDERS, not renderers. Each building carries nested sail prefabs -- cloth canopies that
        /// stand several metres off its walls -- and measuring those in made a large building read as
        /// 52 m across, which is a clearance no town has room for and left four buildings of seven
        /// unplaced. NomadSettlementBuilder gives every structural part a convex hull and gives the
        /// sails none on purpose ("cloth over head height"), so the collider set is exactly the
        /// building's own body. It is also the right question: a player walks into masonry, not shade.
        /// </summary>
        private static float LongestSide(GameObject prefab)
        {
            // A sail has no collider at all -- that is the point of the rule above -- so a freestanding
            // tent has to be measured off its meshes instead, or every tent reads as the MinSpacing
            // fallback and the scatter is quietly uniform.
            Component[] parts = prefab.GetComponentsInChildren<MeshCollider>(true);
            if (parts.Length == 0) parts = prefab.GetComponentsInChildren<MeshFilter>(true);

            Transform root = prefab.transform;
            Bounds? combined = null;

            foreach (Component part in parts)
            {
                Mesh mesh = part is MeshCollider collider ? collider.sharedMesh
                          : part is MeshFilter filter ? filter.sharedMesh
                          : null;
                if (mesh == null) continue;

                Bounds local = mesh.bounds;
                Vector3 centre = local.center;
                Vector3 extents = local.extents;

                for (int corner = 0; corner < 8; corner++)
                {
                    var offset = new Vector3(
                        (corner & 1) == 0 ? -extents.x : extents.x,
                        (corner & 2) == 0 ? -extents.y : extents.y,
                        (corner & 4) == 0 ? -extents.z : extents.z);

                    Vector3 inRoot = root.InverseTransformPoint(part.transform.TransformPoint(centre + offset));
                    if (combined == null) combined = new Bounds(inRoot, Vector3.zero);
                    else
                    {
                        Bounds grown = combined.Value;
                        grown.Encapsulate(inRoot);
                        combined = grown;
                    }
                }
            }

            return combined.HasValue
                ? Mathf.Max(combined.Value.size.x, combined.Value.size.z)
                : MinSpacing;
        }

        /// <summary>How wide the prefabs in a set are, for the placer's report. Empty set reads as zero.</summary>
        public static Vector2 FootprintRange(GameObject[] prefabs)
        {
            float min = float.PositiveInfinity, max = 0f;
            foreach (GameObject prefab in prefabs)
            {
                if (prefab == null) continue;
                float side = LongestSide(prefab);
                if (side < min) min = side;
                if (side > max) max = side;
            }
            return max > 0f ? new Vector2(min, max) : Vector2.zero;
        }

        /// <summary>
        /// Prefabs in this set wider than <paramref name="limit"/> metres, named.
        ///
        /// A range alone hides which asset is the problem, and one oversized prefab does real damage:
        /// its clearance is half its width, so a single 44 m building eats a whole core ring and the
        /// town comes up short with nothing to say why. The documented set is 1.97-11.30 m, so
        /// anything far past that is an asset to look at, not a number to design around.
        /// </summary>
        public static List<string> OversizedPrefabs(GameObject[] prefabs, float limit)
        {
            var oversized = new List<string>();
            foreach (GameObject prefab in prefabs)
            {
                if (prefab == null) continue;
                float side = LongestSide(prefab);
                if (side > limit) oversized.Add($"{prefab.name} {side:F1} m");
            }
            return oversized;
        }

        /// <summary>
        /// The longest side, not the average: a yaw must not push a building into its neighbour. Plus
        /// a street that scales with the building, because a flat padding that reads as a lane between
        /// two tents reads as a crack between two towers -- see <see cref="NeighbourGap"/>.
        /// </summary>
        private static float ClearanceRadius(GameObject prefab) =>
            Mathf.Max((LongestSide(prefab) * (1f + NeighbourGap)) + BuildingPadding, MinSpacing) * 0.5f;

        private static bool IsTaken(Vector2 centre, float radius, List<(Vector2 centre, float radius)> taken)
        {
            foreach ((Vector2 other, float otherRadius) in taken)
            {
                float minimum = radius + otherRadius;
                if ((other - centre).sqrMagnitude < minimum * minimum) return true;
            }
            return false;
        }

        // ── people ───────────────────────────────────────────────────────────────

        /// <summary>
        /// A ring of empty waypoints per route: one at the town edge, and on a large town a second
        /// loop through the core.
        ///
        /// Terrain-snapped, NOT NavMesh-snapped. On a first run the NavMesh covering this town does
        /// not exist yet -- the bake happens after the settlement is placed -- and PatrolModule
        /// samples the NavMesh itself at runtime anyway. Plain GameObjects, so a designer can drag one.
        /// </summary>
        private List<Transform[]> BuildPatrolRoutes(Plan plan, Transform root)
        {
            var routes = new List<Transform[]>();
            if (plan.PatrolRoutes <= 0) return routes;

            Vector3 origin = transform.position;

            for (int route = 0; route < plan.PatrolRoutes; route++)
            {
                float radius = route == 0 ? plan.OuterRadius : plan.CoreRadius;
                float phase = route * Mathf.PI / WaypointsPerRoute;

                Transform routeRoot = new GameObject($"PatrolRoute{route}").transform;
                routeRoot.SetParent(root, worldPositionStays: false);
                routeRoot.localPosition = Vector3.zero;

                var waypoints = new Transform[WaypointsPerRoute];
                for (int i = 0; i < WaypointsPerRoute; i++)
                {
                    float angle = phase + i * Mathf.PI * 2f / WaypointsPerRoute;
                    var point = new Vector3(origin.x + Mathf.Cos(angle) * radius, 0f,
                                            origin.z + Mathf.Sin(angle) * radius);
                    point.y = TerrainProbe.TryGetTerrainHeight(point, out float y) ? y : origin.y;

                    var waypoint = new GameObject($"Waypoint{i}");
                    waypoint.transform.SetParent(routeRoot, worldPositionStays: false);
                    waypoint.transform.position = point;
                    waypoints[i] = waypoint.transform;
                }

                routes.Add(waypoints);
            }

            return routes;
        }

        /// <summary>
        /// The town's people: residents who mill about the settlement, flocks that cross it as a
        /// group, and patrols that walk its edge.
        ///
        /// All three are existing modules. A resident is PatrolModule anchored to this root; a flock
        /// is BasePatrolModule plus HerdModule at Social priority, which is the pairing
        /// BasePatrolModule's own header prescribes; a patroller is PatrolModule on a fixed route.
        /// </summary>
        private int PlacePeople(Plan plan, List<Transform[]> routes, Transform root, System.Random random)
        {
            if (nomads == null || nomads.Length == 0) return 0;

            int index = 0;
            int placed = 0;

            for (int i = 0; i < plan.Residents; i++)
            {
                GameObject person = PlacePerson(root, random, plan.OuterRadius, ref index);
                if (person == null) continue;
                MakeResident(person, transform, plan.OuterRadius);
                placed++;
            }

            for (int flock = 0; flock < plan.Flocks; flock++)
            {
                Vector2 offset = PointInCircle(random, plan.MidRadius);
                Vector3 origin = transform.position;
                var anchorPoint = new Vector3(origin.x + offset.x, 0f, origin.z + offset.y);
                anchorPoint.y = TerrainProbe.TryGetTerrainHeight(anchorPoint, out float y) ? y : origin.y;

                var anchor = new GameObject($"FlockAnchor{flock}").transform;
                anchor.SetParent(root, worldPositionStays: false);
                anchor.position = anchorPoint;

                for (int i = 0; i < plan.FlockSize; i++)
                {
                    GameObject person = PlacePerson(root, random, plan.MidRadius, ref index);
                    if (person == null) continue;
                    MakeFlocker(person, anchor, plan.OuterRadius, $"{herdPrefix}_flock{flock}");
                    placed++;
                }
            }

            for (int route = 0; route < routes.Count; route++)
            {
                for (int i = 0; i < plan.PatrolSize; i++)
                {
                    GameObject person = PlacePerson(root, random, plan.OuterRadius, ref index);
                    if (person == null) continue;
                    MakePatroller(person, routes[route]);
                    placed++;
                }
            }

            for (int i = 0; i < plan.Mounted && mountedNomad != null; i++)
            {
                Vector2 offset = PointInCircle(random, plan.OuterRadius);
                Vector3 origin = transform.position;
                var point = new Vector3(origin.x + offset.x, 0f, origin.z + offset.y);
                point.y = TerrainProbe.TryGetTerrainHeight(point, out float y) ? y : origin.y;

                Place(mountedNomad, point, Quaternion.Euler(0f, (float)(random.NextDouble() * 360.0), 0f), root);
                placed++;
            }

            return placed;
        }

        private GameObject PlacePerson(Transform root, System.Random random, float radius, ref int index)
        {
            GameObject prefab = nomads[index++ % nomads.Length];
            if (prefab == null) return null;

            Vector2 offset = PointInCircle(random, radius);
            Vector3 origin = transform.position;
            var point = new Vector3(origin.x + offset.x, 0f, origin.z + offset.y);
            point.y = TerrainProbe.TryGetTerrainHeight(point, out float y) ? y : origin.y;

            return Place(prefab, point, Quaternion.Euler(0f, (float)(random.NextDouble() * 360.0), 0f), root);
        }

        // ── behaviour wiring ─────────────────────────────────────────────────────
        //
        // WanderModule is switched off wherever one of these takes over. It sits at priority 0 and so
        // does PatrolModule, and AgentController takes the first non-null result at the highest
        // priority -- a tie resolves by component order, so leaving both enabled would make it
        // undefined which one drives.

        private static void MakeResident(GameObject person, Transform centre, float radius)
        {
#if UNITY_EDITOR
            SilenceWander(person);
            var patrol = Add<PatrolModule>(person);

            var so = new SerializedObject(patrol);
            so.FindProperty("mode").enumValueIndex = 0;              // PatrolMode.RadiusBased
            so.FindProperty("radiusCenter").objectReferenceValue = centre;
            so.FindProperty("patrolRadius").floatValue = radius;
            so.FindProperty("priority").intValue = 0;                // ModulePriority.Fallback
            so.ApplyModifiedPropertiesWithoutUndo();
#endif
        }

        private static void MakePatroller(GameObject person, Transform[] waypoints)
        {
#if UNITY_EDITOR
            SilenceWander(person);
            var patrol = Add<PatrolModule>(person);

            var so = new SerializedObject(patrol);
            so.FindProperty("mode").enumValueIndex = 1;              // PatrolMode.PatrolPoints
            so.FindProperty("selectionMode").enumValueIndex = 0;     // PatrolSelectionMode.SequentialLoop
            so.FindProperty("priority").intValue = 0;

            SerializedProperty points = so.FindProperty("patrolPoints");
            points.arraySize = waypoints.Length;
            for (int i = 0; i < waypoints.Length; i++)
                points.GetArrayElementAtIndex(i).objectReferenceValue = waypoints[i];

            so.ApplyModifiedPropertiesWithoutUndo();
#endif
        }

        private static void MakeFlocker(GameObject person, Transform anchor, float radius, string herdId)
        {
#if UNITY_EDITOR
            SilenceWander(person);

            var roamSo = new SerializedObject(Add<BasePatrolModule>(person));
            roamSo.FindProperty("baseTransform").objectReferenceValue = anchor;
            roamSo.FindProperty("patrolRadius").floatValue = radius;
            roamSo.FindProperty("priority").intValue = 0;            // ModulePriority.Fallback
            roamSo.ApplyModifiedPropertiesWithoutUndo();

            var herdSo = new SerializedObject(Add<HerdModule>(person));
            herdSo.FindProperty("herdId").stringValue = herdId;
            herdSo.FindProperty("priority").intValue = 15;           // ModulePriority.Social
            herdSo.ApplyModifiedPropertiesWithoutUndo();
#endif
        }

        /// <summary>
        /// Disabled, not removed. The component belongs to the prefab, and stripping it from an
        /// instance is an override a prefab rebuild would have to reconcile.
        /// </summary>
        private static void SilenceWander(GameObject person)
        {
            if (person.TryGetComponent(out WanderModule wander)) wander.enabled = false;
        }

        // ── helpers ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Instantiates as a prefab LINK at edit time, so the town stays connected to its prefabs and
        /// a rebuilt building updates every copy of itself.
        ///
        /// Not named Instantiate: that would hide Object.Instantiate on a MonoBehaviour, and the next
        /// person to add a plain spawn here would silently get this one instead.
        /// </summary>
        private static GameObject Place(GameObject prefab, Vector3 position, Quaternion rotation, Transform parent)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                instance.transform.SetPositionAndRotation(position, rotation);
                return instance;
            }
#endif
            return Instantiate(prefab, position, rotation, parent);
        }

        private static T Add<T>(GameObject target) where T : Component
        {
            T existing = target.GetComponent<T>();
            return existing != null ? existing : target.AddComponent<T>();
        }

        /// <summary>Uniform over the AREA of the ring, not the radius -- otherwise the middle crowds.</summary>
        private static Vector2 PointInAnnulus(System.Random random, float minRadius, float maxRadius)
        {
            float t = (float)random.NextDouble();
            float radius = Mathf.Sqrt(Mathf.Lerp(minRadius * minRadius, maxRadius * maxRadius, t));
            float angle = (float)(random.NextDouble() * Mathf.PI * 2.0);
            return new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
        }

        private static Vector2 PointInCircle(System.Random random, float radius)
        {
            float r = radius * Mathf.Sqrt((float)random.NextDouble());
            float angle = (float)(random.NextDouble() * Mathf.PI * 2.0);
            return new Vector2(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r);
        }

        private void OnDrawGizmosSelected()
        {
            Plan plan = PlanFor(size, Sets);
            Gizmos.color = new Color(0.95f, 0.75f, 0.3f, 0.5f);
            DrawCircle(plan.CoreRadius);
            Gizmos.color = new Color(0.9f, 0.55f, 0.25f, 0.4f);
            DrawCircle(plan.OuterRadius);
            Gizmos.color = new Color(0.8f, 0.8f, 0.8f, 0.25f);
            DrawCircle(plan.TentRadius);
        }

        private void DrawCircle(float radius, int segments = 48)
        {
            Vector3 centre = transform.position;
            Vector3 previous = centre + new Vector3(radius, 0f, 0f);
            for (int i = 1; i <= segments; i++)
            {
                float t = i / (float)segments * Mathf.PI * 2f;
                Vector3 next = centre + new Vector3(Mathf.Cos(t) * radius, 0f, Mathf.Sin(t) * radius);
                Gizmos.DrawLine(previous, next);
                previous = next;
            }
        }
    }
}
