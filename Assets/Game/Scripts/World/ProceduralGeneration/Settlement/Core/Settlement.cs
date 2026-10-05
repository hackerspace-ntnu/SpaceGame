// The one component a settlement needs. Drop it on a GameObject, assign a SettlementConfig, press
// Generate in the inspector (or Generate + Bake World NavMesh when the layout is final).
//
// Generate lays the buildings out one of two ways: with a street style on the config, along a grown
// street network on terraces (SettlementStreetLayout); without, as a cluster grown outward from this
// transform, each building a chosen number of metres from a neighbour's wall. Then it scatters
// decorations around them and fills the dwellings with people: one character per bed (special
// characters first), each standing on any NavMesh surface inside, and -- when the config has a
// culture -- each made a resident with a name, a job, a home and bonds (ResidentAssignment). Seeded off
// this transform's own position, so the same config reused at a different spot produces a different
// (but reproducible) layout, and the size multiplier scales how much of the config this one
// settlement gets. Sculpts the terrain so buildings sit flat without leaving a hard flattened disc.
//
// At runtime the same component hosts the settlement's people (SettlementSociety): the places its
// buildings' spots brought along, the residents' day plans, their conversations and their rumours.
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using SpaceGame.Agents.Expeditions;
using SpaceGame.Agents.Residents;
using SpaceGame.Core.Persistence;

namespace SpaceGame.World
{
    public class Settlement : MonoBehaviour
    {
        [SerializeField] private SettlementConfig config;

        [Tooltip("How much of the config this settlement gets. 1 = the config as written; 2 = every entry twice; " +
                 "1.5 = the config plus a random half of it again; 0.5 = a random half. Applies to buildings, " +
                 "decorations and characters alike, before each copy's own spawn chance.")]
        [SerializeField, Min(0f)] private float sizeMultiplier = 1f;

        // Lets a later Clear/regenerate undo the previous terrain edit exactly. Internal state, not
        // configuration, so it stays hidden.
        [SerializeField, HideInInspector]
        private List<SettlementTerrainSculptor.TerrainPatchBackup> terrainBackup = new();

        // How far the last Generate spread (outermost building wall + outskirts), for the gizmo.
        // The layout decides it; nothing configures it.
        [SerializeField, HideInInspector] private float generatedExtent;

        // A point in the largest walkable area joining the last Generate's doors and spots: what residents'
        // places must be reachable from. Found on the layout itself, so it is output, not configuration.
        [SerializeField, HideInInspector] private Vector3 walkableHeart;

        private SettlementSociety society;
        private SettlementProps props;
        private SaveableEntity identity;
        private bool warnedNoIdentity;

        private const string GeneratedRootName = "Generated";
        private const string LivestockRootName = "Livestock";
        private static readonly Vector2 DefaultFootprint = new Vector2(8f, 8f);
        // Tries per item before it is given up on; every miss is reported by Generate.
        private const int PlacementAttempts = 30;
        // The heart counts as on the NavMesh when the mesh is this close, metres.
        private const float HeartOnMeshWithin = 2f;
        // Halvings when sliding a new building up to its neighbour: 2^-20 of the slide, far below visible.
        private const int GapSearchSteps = 20;
        // Float slack when re-checking a gap the slide already guaranteed.
        private const float GapTolerance = 0.001f;

        /// <summary>What this settlement is made of. A subclass whose configuration lives on the component itself overrides it.</summary>
        protected virtual SettlementConfig Config => config;

        /// <summary>The seed every roll in this settlement derives from: its own position.</summary>
        public int Seed => SettlementPlacementUtil.SeedFromPosition(transform.position);

        /// <summary>Everything the last Generate placed; null before the first.</summary>
        public Transform GeneratedRoot => transform.Find(GeneratedRootName);

        /// <summary>The beds of every dwelling Generate placed, as Populate counts them; 0 before a Generate.</summary>
        public int Beds => GeneratedRoot is { } generated ? BedsUnder(generated) : 0;

        private static int BedsUnder(Transform root)
        {
            int beds = 0;
            foreach (Dwelling dwelling in root.GetComponentsInChildren<Dwelling>()) beds += dwelling.Beds;
            return beds;
        }

        /// <summary>Where the settlement's walkable area is; every resident place must be reachable from here.</summary>
        public Vector3 WalkableHeart => walkableHeart;

        /// <summary>The people living here; null when the config has no culture (its characters are plain NPCs).</summary>
        public SettlementSociety Society =>
            society ??= HasResidents ? new SettlementSociety(this, Culture) : null;

        /// <summary>Every prop and rest the generated buildings brought along, gathered once.</summary>
        public SettlementProps Props => props ??= new SettlementProps(GeneratedRoot);

        /// <summary>Whether Generate makes residents of the characters: the config has a culture.</summary>
        public bool HasResidents => Culture != null;

        /// <summary>Who the people are: lines, names, archetypes. Null = the characters are plain NPCs.</summary>
        public SettlementCulture Culture => Config != null ? Config.culture : null;

        /// <summary>The street style the layout was planned with; null for a cluster.</summary>
        public SettlementStreetStyle StreetStyle => Config != null ? Config.streets : null;

        /// <summary>
        /// Which settlement this is, for anything that outlives its chunk: the baked id of its own authored
        /// <see cref="SaveableEntity"/>. Identity, not position: <see cref="Seed"/> moves with the transform.
        /// Empty, with one warning, until <c>Tools/SpaceGame/Expeditions/Stamp Settlement Identity</c> has run.
        /// </summary>
        public string SettlementId
        {
            get
            {
                if (identity == null) identity = GetComponent<SaveableEntity>();
                if (identity != null && identity.IsAuthored && !string.IsNullOrEmpty(identity.InstanceId))
                    return identity.InstanceId;

                if (!warnedNoIdentity)
                {
                    warnedNoIdentity = true;
                    Debug.LogWarning($"[{GetType().Name}] {name} has no authored SaveableEntity, so it has no settlement id. " +
                                     "Run Tools/SpaceGame/Expeditions/Stamp Settlement Identity.", this);
                }
                return string.Empty;
            }
        }

        /// <summary>Every character prefab Generate may place: the config's characters and special characters.</summary>
        public IEnumerable<GameObject> CharacterPrefabs()
        {
            if (Config == null) yield break;
            foreach (var entry in Config.characters)
                if (entry?.prefab != null) yield return entry.prefab;
            foreach (var special in Config.specialCharacters)
                if (special?.prefab != null) yield return special.prefab;
        }

        private void OnEnable() => Society?.Enable();

        private void OnDisable() => society?.Disable();

        private void Start() => society?.Start();

        private void Update() => society?.Tick();

        private struct PlacedBuilding
        {
            /// <summary>Relative to this transform's position, world-aligned.</summary>
            public SettlementFootprint footprint;
            public float baseY;
            public Transform instance;
        }

        /// <summary>
        /// Lays out the whole settlement. Use the inspector's Generate button, which first gives the character
        /// prefabs the resident stack; this alone assumes they have it.
        /// </summary>
        public void Generate()
        {
            if (Config == null)
            {
                Debug.LogError($"[{GetType().Name}] No config assigned.", this);
                return;
            }

            Clear();

            int seed = SettlementPlacementUtil.SeedFromPosition(transform.position);
            var rng = new SettlementPlacementUtil.SeededRng(seed);

            Transform root = new GameObject(GeneratedRootName).transform;
            root.SetParent(transform, worldPositionStays: false);
            root.localPosition = Vector3.zero;

            // Everything this component places renders at exactly its own prefab's authored
            // scale, always -- never adjusted here. Cancel out whatever scale this Settlement's
            // own transform happens to carry (some scenes scale it up to size an under-authored
            // building kit) so it can never leak into a placed instance. A prefab that reads the
            // wrong size must be corrected in the prefab itself (as NomadSail_* was -- see
            // ArtPipeline.md), never compensated for by scaling the container it's placed under.
            // Positions ignore it too: every distance in the config is plain metres.
            Vector3 lossyScale = transform.lossyScale;
            root.localScale = new Vector3(1f / lossyScale.x, 1f / lossyScale.y, 1f / lossyScale.z);

            // Layer 1: buildings and the ground under them -- planned along streets on terraces when
            // the config has a street style, else grown as a cluster on flattened pads.
            List<GameObject> buildingPrefabs = RollCopies(Config.buildings, ref rng);
            SettlementLayoutResult layout = Config.streets != null
                ? SettlementStreetLayout.Generate(Config, buildingPrefabs, root, transform.position, seed)
                : LayOutCluster(buildingPrefabs, root, seed, ref rng);
            generatedExtent = layout.extent;
            terrainBackup = layout.terrainBackup;

            // Layer 2: decorations, spawned after the terrain sculpt so they land on the real final
            // surface. Kept clear of each other by a flat spacing value (config.decorationSpacing)
            // instead of a mesh-derived footprint -- a large sail-tent canopy is meant to be able to
            // sit close to, or even overlap, its neighbours, unlike a building. Buildings, walls and
            // stairs get a real collision check (their actual colliders): a tent may stand right up
            // against a wall, just not inside it -- and never on a street.
            var solids = new List<Transform>(layout.buildings);
            if (layout.pavingRoot != null) solids.Add(layout.pavingRoot);
            Transform decorationsRoot = new GameObject("Decorations").transform;
            decorationsRoot.SetParent(root, worldPositionStays: false);
            List<GameObject> decorationPrefabs = RollCopies(Config.decorations, ref rng);
            int decorationsPlaced = ScatterDecorations(decorationPrefabs, decorationsRoot, solids, layout.paving, ref rng);

            // Anything placed that is itself a hand-authored arrangement (a section prefab) gets its
            // optional items rolled -- before the people come, so a spot on a missed item is no place.
            SpawnChance.RollAll(root);

            // A gate or a carried prop is state every machine must agree on: its building gets a networked wrapper.
            SettlementNetworking.Wrap(layout.buildings);

            // Layer 3: the people, last -- one per bed, standing on any NavMesh surface the finished
            // settlement has (ground, courtyards, walkable roofs); the NavMesh keeps them out of walls.
            Transform charactersRoot = new GameObject("Characters").transform;
            charactersRoot.SetParent(root, worldPositionStays: false);
            Population population = Populate(root, charactersRoot, RollCopies(Config.characters, ref rng), layout.lanes, ref rng);

            Report(root, layout, buildingPrefabs.Count, decorationsPlaced, decorationPrefabs.Count, population);
        }

        private struct Population
        {
            public int placed, movingIn, wanted, beds, specials;
            public int stock, stockWanted;
            public List<string> problems, penProblems;
            /// <summary>Roles of character copies left out because this settlement has no place for them.</summary>
            public List<string> leftOut;
            /// <summary>Each quota of the culture's expedition profile: the residents now holding its role, and the quota.</summary>
            public List<(ExpeditionRole role, int held, int count)> quotas;
        }

        /// <summary>
        /// Who moves in: every special character, then copies of the config's characters (seeded order) until
        /// the dwellings' beds are full -- or, with no dwellings at all, every copy rolled. A copy made for one role
        /// stays out when that role has no place here (a miner where there is no mine). A culture that sends bands
        /// out keeps its quotas from the start: copies certain to have a quota's role move in first
        /// (<see cref="ExpeditionRules.PlanMoveIn"/>). Then checks every door and spot can be walked to, places the
        /// people, and makes residents of them when there is a culture.
        /// A culture that sends bands out gets its muster spot on one of the <paramref name="lanes"/> first.
        /// </summary>
        private Population Populate(Transform root, Transform parent, List<GameObject> pool, IReadOnlyList<SettlementMuster.StreetPoint[]> lanes,
                                    ref SettlementPlacementUtil.SeededRng rng)
        {
            var dwellings = new List<Dwelling>(root.GetComponentsInChildren<Dwelling>());
            var population = new Population
            {
                problems = new List<string>(), penProblems = new List<string>(), leftOut = new List<string>(),
                quotas = new List<(ExpeditionRole role, int held, int count)>(),
            };
            population.beds = BedsUnder(root);

            var newcomers = new List<(GameObject prefab, ResidentArchetype archetype, bool special)>();
            foreach (var special in Config.specialCharacters)
                if (special?.prefab != null) newcomers.Add((special.prefab, special.archetype, true));
            population.specials = newcomers.Count;

            Shuffle(pool, ref rng);
            ResidentAssignment.Places places = HasResidents ? ResidentAssignment.Places.Of(this) : null;
            var hostable = new List<GameObject>(pool.Count);
            foreach (GameObject prefab in pool)
            {
                ResidentArchetype role = ResidentAssignment.RoleOf(prefab);
                if (places != null && role != null && !places.CanHost(role)) population.leftOut.Add(role.roleName);
                else hostable.Add(prefab);
            }
            int room = population.beds > 0 ? Mathf.Max(0, population.beds - population.specials) : hostable.Count;
            foreach (int i in MoveIn(hostable, newcomers, population.beds, room)) newcomers.Add((hostable[i], null, false));
            population.movingIn = newcomers.Count;
            population.wanted = population.beds > 0 ? Mathf.Max(population.beds, population.specials) : newcomers.Count;

            var pens = new List<SettlementPen>(root.GetComponentsInChildren<SettlementPen>());
            bool hasPlaces = dwellings.Count > 0 || root.GetComponentInChildren<SettlementSpot>() != null;
            if (newcomers.Count == 0 && !hasPlaces && pens.Count == 0) return population;

            using SettlementWalkableArea walkable = SettlementWalkableArea.Bake(transform.position, generatedExtent, this);
            if (walkable == null) return population;   // Bake has logged why.
            if (walkable.IsEmpty)
            {
                Debug.LogError($"[{GetType().Name}] No NavMesh surface within {generatedExtent:0.#} m -- no characters placed.", this);
                return population;
            }

            float[] doorDistances = ResidentTuning.Instance.doorStandDistances;
            walkableHeart = SettlementPlaces.FindHeart(root, doorDistances, transform.position);
            PlaceMusterSpot(root, lanes);
            population.problems = SettlementPlaces.Problems(root, walkableHeart, doorDistances);

            List<ResidentAssignment.Newcomer> placed = PlaceCharacters(newcomers, parent, walkable, ref rng);
            population.placed = placed.Count;
            if (HasResidents)
            {
                ResidentAssignment.Assign(this, Culture, placed, dwellings);
                population.quotas = QuotaFill(placed, population.beds);
            }
            StockPens(pens, root, walkable, ref population);
            return population;
        }

        /// <summary>
        /// The indices of <paramref name="pool"/> that move into <paramref name="room"/> beds, in pool order. Without an
        /// expedition profile, the first copies; with one, copies certain to have a quota's role first, counting the
        /// <paramref name="settled"/> special characters (<see cref="ExpeditionRules.PlanMoveIn"/>).
        /// </summary>
        private List<int> MoveIn(List<GameObject> pool, IEnumerable<(GameObject prefab, ResidentArchetype archetype, bool special)> settled,
                                 int beds, int room)
        {
            ExpeditionProfile profile = Culture != null ? Culture.expeditions : null;
            if (profile == null)
                return ExpeditionRules.PlanMoveIn(new MoveInCandidate[pool.Count], System.Array.Empty<(ExpeditionRole, int)>(),
                                                  System.Array.Empty<ExpeditionRole>(), room);

            List<ResidentArchetype> usable = ResidentAssignment.UsableArchetypes(this, Culture, Seed);
            List<MoveInCandidate> candidates = pool
                .Select(prefab => new MoveInCandidate(CertainRoles(prefab, usable), ResidentAssignment.RoleOf(prefab) != null))
                .ToList();
            List<ExpeditionRole> settledRoles = settled
                .Select(s => s.archetype != null ? s.archetype.expeditionRoles : CertainRoles(s.prefab, usable))
                .ToList();
            return ExpeditionRules.PlanMoveIn(candidates, ExpeditionRules.Quotas(profile, beds), settledRoles, room);
        }

        // The roles every archetype a copy of the prefab may be dealt here shares; None when its archetype is not certain.
        private ExpeditionRole CertainRoles(GameObject prefab, List<ResidentArchetype> usable)
        {
            IReadOnlyList<ResidentArchetype> dealt = ResidentAssignment.CertainOneOf(prefab, Culture, usable);
            if (dealt.Count == 0) return ExpeditionRole.None;

            ExpeditionRole roles = dealt[0].expeditionRoles;
            foreach (ResidentArchetype archetype in dealt) roles &= archetype.expeditionRoles;
            return roles;
        }

        /// <summary>For each quota of the culture's expedition profile: how many of the <paramref name="placed"/> residents hold its role.</summary>
        private List<(ExpeditionRole role, int held, int count)> QuotaFill(List<ResidentAssignment.Newcomer> placed, int beds)
        {
            var fill = new List<(ExpeditionRole role, int held, int count)>();
            ExpeditionProfile profile = Culture.expeditions;
            if (profile == null) return fill;

            foreach ((ExpeditionRole role, int count) in ExpeditionRules.Quotas(profile, beds))
            {
                int held = placed.Count(n => n.body.TryGetComponent(out Resident resident) && resident.archetype != null &&
                                             (resident.archetype.expeditionRoles & role) != 0);
                fill.Add((role, held, count));
            }
            return fill;
        }

        /// <summary>
        /// Fills every pen with its stock, after the residents so theirs are untouched, each pen from a seed of its
        /// own position. A pen the stock could walk out of while its gate is shut is reported, since the animals would
        /// not stay in it.
        /// </summary>
        private void StockPens(List<SettlementPen> pens, Transform root, SettlementWalkableArea walkable, ref Population population)
        {
            if (pens.Count == 0) return;

            Transform livestockRoot = new GameObject(LivestockRootName).transform;
            livestockRoot.SetParent(root, worldPositionStays: false);
            foreach (SettlementPen pen in pens)
            {
                if (pen.StockPrefab == null)
                {
                    population.penProblems.Add(pen.name + " has no stock prefab");
                    continue;
                }

                SettlementWalkableArea.Patch floor = walkable.PatchWithin(pen.WorldBounds);
                if (floor.IsEmpty)
                {
                    population.penProblems.Add(pen.name + " has no walkable ground inside its box");
                    continue;
                }

                var rng = new SettlementPlacementUtil.SeededRng(Seed ^ SettlementPlacementUtil.SeedFromPosition(pen.transform.position));
                List<Vector3> points = pen.PlanStock(ref rng, floor.Sample, out int wanted);
                population.stockWanted += wanted;
                population.stock += points.Count;
                if (points.Count < wanted)
                    population.penProblems.Add(pen.name + " fits " + points.Count + "/" + wanted + " animals");

                foreach (Vector3 point in points)
                {
                    Quaternion facing = Quaternion.Euler(0f, rng.NextFloat01() * 360f, 0f);
                    SettlementPlacementUtil.SpawnPrefab(pen.StockPrefab, livestockRoot, point, facing);
                }

                if (points.Count > 0 && CanWalkOut(points[0]))
                    population.penProblems.Add(pen.name + " is not closed: stock can walk out with the gate shut" +
                                               (pen.Gate == null ? " (and no gate is assigned)" : ""));
            }
        }

        // Whether a complete path leads from inside a pen to the settlement's walkable heart.
        private bool CanWalkOut(Vector3 from)
        {
            var path = new UnityEngine.AI.NavMeshPath();
            return UnityEngine.AI.NavMesh.CalculatePath(from, walkableHeart, UnityEngine.AI.NavMesh.AllAreas, path) &&
                   path.status == UnityEngine.AI.NavMeshPathStatus.PathComplete;
        }

        // A people that sends bands out musters them at a spot of its profile's muster use. One a prefab brought along is
        // kept; else SettlementMuster's rule places one where the road out's paving ends, on this Generate's throwaway NavMesh and before the places
        // are checked, so the summary names it when nobody can walk to it.
        private void PlaceMusterSpot(Transform root, IReadOnlyList<SettlementMuster.StreetPoint[]> lanes)
        {
            ExpeditionProfile profile = Culture != null ? Culture.expeditions : null;
            if (profile == null || profile.musterUse == null || SettlementMuster.Find(root, profile.musterUse) != null) return;

            if (!SettlementMuster.TryChoose(transform.position, lanes, ExpeditionTuning.Instance.musterInset, out Pose pose))
                Debug.LogWarning($"[{GetType().Name}] No street leaves the centre, so {profile.name} has no muster spot here: " +
                                 $"put a SettlementSpot using {profile.musterUse.name} under {root.name} by hand.", this);
            else if (!SettlementMuster.TryPlace(root, profile.musterUse, pose, walkableHeart, out _, out string why))
                Debug.LogWarning($"[{GetType().Name}] No muster spot for {profile.name}: {why}.", this);
        }

        private static void Shuffle(List<GameObject> list, ref SettlementPlacementUtil.SeededRng rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.NextIndex(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        /// <summary>
        /// Buildings grown as a cluster outward from the first one, each spaced by the real distance
        /// between its walls and its neighbours' -- no radius to fit them into, so the settlement is
        /// as big as its buildings make it -- then the ground flattened under them.
        /// </summary>
        private SettlementLayoutResult LayOutCluster(List<GameObject> prefabs, Transform root, int seed,
                                                            ref SettlementPlacementUtil.SeededRng rng)
        {
            var result = new SettlementLayoutResult();
            List<PlacedBuilding> buildings = PlaceBuildings(prefabs, root, ref rng);

            float clusterRadius = 0f;
            var footprints = new List<SettlementTerrainSculptor.BuildingFootprint>(buildings.Count);
            Vector2 originXZ = new Vector2(transform.position.x, transform.position.z);
            foreach (var b in buildings)
            {
                clusterRadius = Mathf.Max(clusterRadius, b.footprint.center.magnitude + b.footprint.Circumradius);
                SettlementFootprint world = b.footprint.MovedTo(b.footprint.center + originXZ);
                footprints.Add(new SettlementTerrainSculptor.BuildingFootprint { footprint = world, baseY = b.baseY });
                result.buildings.Add(b.instance);
                result.buildingFootprints.Add(world);
            }
            result.extent = clusterRadius + Config.outskirts;
            result.terrainBackup = SettlementTerrainSculptor.Shape(
                transform.position, result.extent, Config.blendDistance, Config.flattenPadding,
                Config.ambientNoiseAmplitude, Config.ambientNoiseScale, seed, footprints);
            return result;
        }

        [ContextMenu("Clear")]
        public void Clear()
        {
            SettlementTerrainSculptor.Restore(terrainBackup);
            terrainBackup.Clear();
            generatedExtent = 0f;
            // Its places and roster are gathered from what is about to be destroyed.
            society = null;
            props = null;

            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform child = transform.GetChild(i);
                if (child.name != GeneratedRootName) continue;
                if (Application.isPlaying) Destroy(child.gameObject);
                else DestroyImmediate(child.gameObject);
            }
        }

        /// <summary>Rotation for a freshly placed item at this XZ offset from the settlement center. Base: a random axis-aligned yaw.</summary>
        protected virtual Quaternion GetSpawnRotation(Vector2 localXZ, ref SettlementPlacementUtil.SeededRng rng)
        {
            int step = Mathf.Clamp(Mathf.FloorToInt(rng.NextFloat01() * 4f), 0, 3);
            return Quaternion.Euler(0f, step * 90f, 0f);
        }

        /// <summary>
        /// The copies this settlement gets: the config's full list <see cref="sizeMultiplier"/> times
        /// over -- whole repeats first, then a random pick of the list (no copy twice) for the
        /// fraction -- and then each copy's own spawnChance.
        /// </summary>
        private List<GameObject> RollCopies(SettlementConfig.SpawnEntry[] entries, ref SettlementPlacementUtil.SeededRng rng)
        {
            var pool = new List<SettlementConfig.SpawnEntry>();
            if (entries != null)
            {
                foreach (var entry in entries)
                {
                    if (entry.prefab == null) continue;
                    for (int i = 0; i < entry.count; i++) pool.Add(entry);
                }
            }

            int target = Mathf.RoundToInt(pool.Count * sizeMultiplier);
            var picked = new List<SettlementConfig.SpawnEntry>(target);
            while (pool.Count > 0 && target - picked.Count >= pool.Count) picked.AddRange(pool);

            // Partial Fisher-Yates: the first (target - picked) of a seeded shuffle.
            for (int i = 0; picked.Count < target; i++)
            {
                int j = i + rng.NextIndex(pool.Count - i);
                (pool[i], pool[j]) = (pool[j], pool[i]);
                picked.Add(pool[i]);
            }

            var prefabs = new List<GameObject>(picked.Count);
            foreach (var entry in picked)
            {
                if (rng.NextChance(entry.spawnChance)) prefabs.Add(entry.prefab);
            }
            return prefabs;
        }

        private List<PlacedBuilding> PlaceBuildings(List<GameObject> prefabs, Transform parent, ref SettlementPlacementUtil.SeededRng rng)
        {
            var placed = new List<PlacedBuilding>(prefabs.Count);
            foreach (var prefab in prefabs)
            {
                Rect local = SettlementPlacementUtil.MeasureFootprint(prefab, DefaultFootprint);
                if (TryPlaceBuilding(prefab, local, placed, parent, ref rng, out PlacedBuilding building))
                    placed.Add(building);
            }
            return placed;
        }

        private struct BuildingCandidate
        {
            public Vector2 pivot;
            public Quaternion rotation;
            public SettlementFootprint footprint;
        }

        /// <summary>
        /// The first building stands on this transform. For every later one, each attempt picks a
        /// placed building and a direction and slides in from there until its wall is a random
        /// minBuildingGap..maxBuildingGap from that neighbour's; attempts that come within
        /// minBuildingGap of any other building are dropped. Of the rest, the one nearest the
        /// centre that has ground under it wins -- keeping the nearest is what grows a compact
        /// cluster instead of long branching arms (measured on 20 mixed buildings: ~40 m across
        /// instead of 55-98 m for taking the first that fits).
        /// </summary>
        private bool TryPlaceBuilding(
            GameObject prefab, Rect local, List<PlacedBuilding> placed, Transform parent,
            ref SettlementPlacementUtil.SeededRng rng, out PlacedBuilding result)
        {
            var candidates = new List<BuildingCandidate>();
            if (placed.Count == 0)
            {
                Quaternion rotation = GetSpawnRotation(Vector2.zero, ref rng);
                candidates.Add(new BuildingCandidate
                {
                    pivot = Vector2.zero, rotation = rotation, footprint = FootprintAt(Vector2.zero, local, rotation),
                });
            }
            else
            {
                for (int attempt = 0; attempt < PlacementAttempts; attempt++)
                {
                    SettlementFootprint anchor = placed[rng.NextIndex(placed.Count)].footprint;
                    float angle = rng.NextFloat01() * Mathf.PI * 2f;
                    Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    float gap = rng.NextRange(Config.minBuildingGap, Config.maxBuildingGap);
                    float reach = anchor.Circumradius + local.size.magnitude * 0.5f + gap;

                    // Facing may depend on where the building ends up (DesertSettlement faces the
                    // centre), so it is chosen at the outer end of the slide and held for the slide.
                    Quaternion rotation = GetSpawnRotation(anchor.center + direction * reach, ref rng);
                    SettlementFootprint footprint = SlideUpTo(anchor, direction, reach, gap, FootprintAt(Vector2.zero, local, rotation));
                    if (TooCloseToAnyBuilding(footprint, placed)) continue;

                    candidates.Add(new BuildingCandidate
                    {
                        pivot = footprint.center - Rotate(local.center, rotation), rotation = rotation, footprint = footprint,
                    });
                }
                candidates.Sort((a, b) => a.footprint.center.sqrMagnitude.CompareTo(b.footprint.center.sqrMagnitude));
            }

            foreach (var candidate in candidates)
            {
                // Ground only: a building stands on the terrain, never on another building's roof or
                // eave -- the flatten step would then raise a cliff under it to that height.
                Vector3 world = ToWorld(candidate.pivot);
                if (!SettlementPlacementUtil.SampleGround(world, out float groundY, ignoreUnder: parent)) continue;

                GameObject go = SettlementPlacementUtil.SpawnPrefab(prefab, parent, new Vector3(world.x, groundY, world.z), candidate.rotation);
                result = new PlacedBuilding { footprint = candidate.footprint, baseY = groundY, instance = go.transform };
                return true;
            }

            result = default;
            return false;
        }

        /// <summary>
        /// Moves <paramref name="shape"/> out from <paramref name="anchor"/>'s centre along
        /// <paramref name="direction"/> to the nearest spot where their walls are
        /// <paramref name="gap"/> apart. The wall-to-wall distance only grows along the slide (both
        /// are convex and start overlapping), so a bisection finds it; at <paramref name="reach"/>
        /// the two circumcircles alone are <paramref name="gap"/> apart, so the answer is inside.
        /// </summary>
        private static SettlementFootprint SlideUpTo(in SettlementFootprint anchor, Vector2 direction, float reach, float gap, SettlementFootprint shape)
        {
            float near = 0f, far = reach;
            for (int i = 0; i < GapSearchSteps; i++)
            {
                float mid = (near + far) * 0.5f;
                if (SettlementFootprint.Gap(anchor, shape.MovedTo(anchor.center + direction * mid)) >= gap) far = mid;
                else near = mid;
            }
            return shape.MovedTo(anchor.center + direction * far);
        }

        private bool TooCloseToAnyBuilding(in SettlementFootprint footprint, List<PlacedBuilding> placed)
        {
            foreach (var other in placed)
            {
                if (SettlementFootprint.Gap(other.footprint, footprint) < Config.minBuildingGap - GapTolerance) return true;
            }
            return false;
        }

        private int ScatterDecorations(List<GameObject> prefabs, Transform parent, List<Transform> solids,
                                       IReadOnlyList<SettlementFootprint> paving, ref SettlementPlacementUtil.SeededRng rng)
        {
            var placed = new List<Vector3>(prefabs.Count);
            foreach (var prefab in prefabs)
            {
                for (int attempt = 0; attempt < PlacementAttempts; attempt++)
                {
                    Vector2 localXZ = rng.NextPointInDisk(generatedExtent);
                    Vector3 world = ToWorld(localXZ);
                    if (!SettlementPlacementUtil.SampleGround(world, out float groundY)) continue;

                    Vector3 spawnPos = new Vector3(world.x, groundY, world.z);
                    if (IsCloserThan(spawnPos, placed, Config.decorationSpacing)) continue;
                    if (OverlapsAnySolid(spawnPos, Config.decorationSpacing * 0.5f, solids)) continue;
                    if (IsOnPaving(spawnPos, Config.decorationSpacing * 0.5f, paving)) continue;

                    SettlementPlacementUtil.SpawnPrefab(prefab, parent, spawnPos, GetSpawnRotation(localXZ, ref rng));
                    placed.Add(spawnPos);
                    break;
                }
            }
            return placed.Count;
        }

        private static bool IsOnPaving(Vector3 worldPos, float radius, IReadOnlyList<SettlementFootprint> paving)
        {
            Vector2 xz = new Vector2(worldPos.x, worldPos.z);
            foreach (var piece in paving)
            {
                if (piece.SignedDistance(xz) < radius) return true;
            }
            return false;
        }

        private List<ResidentAssignment.Newcomer> PlaceCharacters(
            List<(GameObject prefab, ResidentArchetype archetype, bool special)> newcomers, Transform parent,
            SettlementWalkableArea walkable, ref SettlementPlacementUtil.SeededRng rng)
        {
            var spawned = new List<ResidentAssignment.Newcomer>(newcomers.Count);
            var taken = new List<Vector3>(newcomers.Count);
            // Residents stand where the settlement's heart can walk to: a roof of a sealed building is walkable too, and a body put there
            // can never come down. Judged only once the heart is on the mesh (a settlement whose heart was not found places anywhere).
            bool reachableOnly = HasResidents && UnityEngine.AI.NavMesh.SamplePosition(walkableHeart, out _, HeartOnMeshWithin, UnityEngine.AI.NavMesh.AllAreas);
            foreach (var (prefab, archetype, special) in newcomers)
            {
                for (int attempt = 0; attempt < PlacementAttempts; attempt++)
                {
                    Vector3 spawnPos = walkable.Sample(ref rng);
                    if (IsCloserThan(spawnPos, taken, Config.characterSpacing)) continue;
                    if (reachableOnly && !NavMeshReach.CanWalk(walkableHeart, spawnPos)) continue;

                    Vector2 localXZ = new Vector2(spawnPos.x - transform.position.x, spawnPos.z - transform.position.z);
                    GameObject body = SettlementPlacementUtil.SpawnPrefab(prefab, parent, spawnPos, GetSpawnRotation(localXZ, ref rng));
                    spawned.Add(new ResidentAssignment.Newcomer(body, archetype, special, prefab));
                    taken.Add(spawnPos);
                    break;
                }
            }
            return spawned;
        }

        private void Report(Transform root, SettlementLayoutResult layout, int buildingsWanted, int decorations, int decorationsWanted,
                            Population population)
        {
            int buildings = layout.buildings.Count;
            int characters = population.placed, charactersWanted = population.wanted;
            string people = population.beds > 0
                ? $"{characters}/{charactersWanted} characters in {population.beds} beds ({population.specials} special)"
                : $"{characters}/{charactersWanted} characters (no dwellings: they sleep in the open)";
            string livestock = population.stockWanted > 0 ? $", {population.stock}/{population.stockWanted} penned animals" : "";
            string quotas = population.quotas.Count > 0
                ? $" (quotas: {string.Join(", ", population.quotas.Select(q => $"{q.role} {q.held}/{q.count}"))})"
                : "";
            var quotasShort = population.quotas.Where(q => q.held < q.count).ToList();
            string summary = $"[{GetType().Name}] Generated {buildings}/{buildingsWanted} buildings, " +
                             $"{decorations}/{decorationsWanted} decorations, {people}{livestock}" +
                             (HasResidents ? " as residents" + quotas : " as plain NPCs (no culture)") +
                             $" over {generatedExtent:0.#} m under {root.name}" +
                             (layout.summary.Length > 0 ? $"; {layout.summary}." : ".");

            if (buildings == buildingsWanted && decorations == decorationsWanted && characters == charactersWanted &&
                layout.streetsEndingAtWalls == 0 && layout.wallsTooTall == 0 && population.problems.Count == 0 &&
                population.penProblems.Count == 0 && quotasShort.Count == 0)
            {
                Debug.Log(summary, root);
                return;
            }

            string missingBuildings = Config.streets != null
                ? " Missing buildings found no frontage before the streets stopped growing -- raise maxSegments or frontageSlack."
                : " Missing buildings found no spot minBuildingGap from every other building with ground under it.";
            Debug.LogWarning(summary +
                             (buildings < buildingsWanted ? missingBuildings : "") +
                             (decorations < decorationsWanted ? " Missing decorations found no free ground -- raise outskirts or lower decorationSpacing." : "") +
                             (characters < charactersWanted ? MissingCharacters(population) : "") +
                             (quotasShort.Count > 0
                                 ? $" Short of the expedition quota for {string.Join(", ", quotasShort.Select(q => $"{q.role} ({q.count - q.held} short)"))}: " +
                                   "the characters list rolls too few copies certain to have the role here -- add copies of a character " +
                                   "prefab made for it (its Resident names the archetype), or run Tools/SpaceGame/Expeditions/Apply Role Quotas."
                                 : "") +
                             (population.problems.Count > 0 ? $" {population.problems.Count} place(s) residents cannot use: {string.Join("; ", population.problems)}." : "") +
                             (population.penProblems.Count > 0 ? $" Pen problem(s): {string.Join("; ", population.penProblems)}." : "") +
                             (layout.streetsEndingAtWalls > 0 ? $" {layout.streetsEndingAtWalls} street(s) run into another on a different terrace and end at its wall -- too steep to step down in time; raise contourBias so streets follow the slope." : "") +
                             (layout.wallsTooTall > 0 ? $" {layout.wallsTooTall} terrace wall(s) need more than maxWallCourses courses and hang short of the ground in front -- raise maxWallCourses (with a pillar deep enough) or lower wallReach." : ""),
                             root);
        }

        // Short of people for one of two reasons: the characters list ran out before the beds did, or the
        // NavMesh had no room left for them.
        private string MissingCharacters(Population population)
        {
            bool listRanOut = population.movingIn < population.wanted;
            string leftOut = population.leftOut.Count > 0
                ? $" {population.leftOut.Count} cop(ies) made for a role with no place here stayed out " +
                  $"({string.Join(", ", population.leftOut.GroupBy(r => r).Select(g => $"{g.Key} x{g.Count()}"))})."
                : "";
            return listRanOut
                ? $" {population.wanted - population.movingIn} bed(s) stayed empty: the characters list rolled fewer copies than there are beds -- raise counts.{leftOut}"
                : " Missing characters found no free NavMesh -- raise outskirts or lower characterSpacing.";
        }

        private Vector3 ToWorld(Vector2 localXZ) => transform.position + new Vector3(localXZ.x, 0f, localXZ.y);

        private static SettlementFootprint FootprintAt(Vector2 pivot, Rect local, Quaternion rotation) =>
            new SettlementFootprint(pivot + Rotate(local.center, rotation), local.size, rotation);

        private static Vector2 Rotate(Vector2 xz, Quaternion rotation)
        {
            Vector3 r = rotation * new Vector3(xz.x, 0f, xz.y);
            return new Vector2(r.x, r.z);
        }

        internal static bool IsCloserThan(Vector3 point, List<Vector3> others, float distance)
        {
            float sqr = distance * distance;
            foreach (var other in others)
            {
                if ((other - point).sqrMagnitude < sqr) return true;
            }
            return false;
        }

        /// <summary>True if any real collider under one of <paramref name="solids"/> overlaps a sphere at <paramref name="worldPos"/> -- a literal collision test.</summary>
        private static bool OverlapsAnySolid(Vector3 worldPos, float radius, List<Transform> solids)
        {
            Collider[] hits = Physics.OverlapSphere(worldPos, radius, ~0, QueryTriggerInteraction.Ignore);
            foreach (var hit in hits)
            {
                foreach (var solid in solids)
                {
                    if (hit.transform.IsChildOf(solid)) return true;
                }
            }
            return false;
        }

        private void OnDrawGizmosSelected()
        {
            if (Config == null || generatedExtent <= 0f) return;
            Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.5f);
            DrawCircle(transform.position, generatedExtent);
            Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.3f);
            DrawCircle(transform.position, generatedExtent + Config.blendDistance);
        }

        private static void DrawCircle(Vector3 center, float radius, int segments = 48)
        {
            Vector3 prev = center + new Vector3(radius, 0f, 0f);
            for (int i = 1; i <= segments; i++)
            {
                float t = (i / (float)segments) * Mathf.PI * 2f;
                Vector3 next = center + new Vector3(Mathf.Cos(t) * radius, 0f, Mathf.Sin(t) * radius);
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }
    }
}
