// The Strider city seen from afar, drawn on every machine (SettlementLods.md, "The distant city").
//
// A folded group is a record with no GameObjects (NpcGroup), so beyond spawnRadius the city used to be
// invisible. This draws each opted-in group DistantGroups publishes: every vehicle's merged far level
// (MergedLod -- its Mesh LODs pick the coarse levels at this range) in the column the live spawn would
// use (GroupColumnLayout over NpcGroupComposition.Resolve, dealt from the replicated roster seed: the
// same order on every machine), each standing on the terrain under it, each with a copy of its far dust.
// Plain renderers, no NetworkObjects, nothing saved.
//
// Drawn while the group is folded and there is a camera and loaded terrain under the slot -- so never over
// the void. Not hidden by distance: the server spawns a second or more after a player crosses spawnRadius
// (its sim tick and player refresh), and hiding at the crossing left only dust standing there meanwhile. When the group spawns the renderers go and the dust stays to
// settle: the live city arrives inside the cloud, in the same slots, drawing its own merged level.
using System.Collections.Generic;
using SpaceGame.Vehicles;
using SpaceGame.World;
using UnityEngine;

namespace SpaceGame.Agents
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(DistantGroups))]
    public sealed class DistantGroupSilhouette : MonoBehaviour
    {
        [Tooltip("Seconds the drawn city takes to close most of the gap to where the server last said it was. A " +
                 "folded group steps once per sim tick; this turns the steps into a march.")]
        [Min(0.01f)]
        [SerializeField] private float positionLag = 1.5f;

        [Tooltip("A jump farther than this (m) is a load, a refold or a teleport: snap instead of gliding.")]
        [Min(1f)]
        [SerializeField] private float snapDistance = 60f;

        /// <summary>A vehicle of the column and where it stands relative to the leader, heading +Z.</summary>
        public readonly struct Place
        {
            public readonly GameObject Prefab;
            public readonly Vector3 Local;

            public Place(GameObject prefab, Vector3 local)
            {
                Prefab = prefab;
                Local = local;
            }
        }

        private sealed class Vehicle
        {
            public Transform Body;
            public MeshRenderer Renderer;
            public FarDust Dust;
            public Vector3 Local;
            /// <summary>The merged mesh's lowest point in its own space: stood on the ground, not sunk into it.</summary>
            public float Sole;
        }

        private sealed class View
        {
            public int TemplateHash;
            public int RosterSeed;
            public GameObject Root;
            public readonly List<Vehicle> Vehicles = new();
            public Vector3 ShownPosition;
            public float ShownYaw;
            public bool Posed;
        }

        private DistantGroups source;
        private readonly Dictionary<int, View> views = new();
        private readonly HashSet<int> published = new();
        private readonly List<int> gone = new();

        /// <summary>
        /// The vehicles of a group with <paramref name="template"/> and <paramref name="rosterSeed"/>, in the
        /// order and places the live spawn gives them with the leader at the origin heading +Z. Crew are
        /// left out: they ride the carriers.
        /// </summary>
        public static List<Place> Layout(NpcGroupTemplate template, int rosterSeed)
        {
            var group = new NpcGroup { Id = template.id, TemplateId = template.id, RosterSeed = rosterSeed };
            List<PlannedMember> plan = NpcGroupComposition.Resolve(group, template);
            var places = new List<Place>();
            foreach (ColumnPlace place in GroupColumnLayout.Places(plan, Vector3.zero, Vector3.forward, template.formation))
            {
                PlannedMember member = plan[place.PlanIndex];
                if (!member.Crew) places.Add(new Place(member.Prefab, place.Position));
            }
            return places;
        }

        /// <summary>
        /// Folded, with a camera (NaN: none, not drawn). Hidden by <c>Spawned</c> alone, never by distance, so the
        /// city stands until the live one replaces it; the two overlap, identical, for at most one publish.
        /// </summary>
        public static bool ShouldShow(bool spawned, float cameraDistance) =>
            !spawned && !float.IsNaN(cameraDistance);

        public static Vector3 FollowPosition(Vector3 shown, Vector3 target, float dt, float lag) =>
            Vector3.Lerp(shown, target, 1f - Mathf.Exp(-dt / lag));

        public static float FollowYaw(float shown, float target, float dt, float lag) =>
            Mathf.LerpAngle(shown, target, 1f - Mathf.Exp(-dt / lag));

        /// <summary>The loaded terrain's height under <paramref name="at"/>; NaN off every loaded tile.</summary>
        public static float GroundUnder(Terrain[] terrains, Vector3 at) =>
            SettlementPlacementUtil.TerrainHeightAt(terrains, new Vector2(at.x, at.z), float.NaN);

        private void Awake() => source = GetComponent<DistantGroups>();

        private void LateUpdate()
        {
            NpcWorldSim sim = NpcWorldSim.Instance;
            if (sim == null) return;

            Camera cam = Camera.main;
            Terrain[] terrains = Terrain.activeTerrains;
            published.Clear();
            for (int i = 0; i < source.Count; i++)
            {
                DistantGroupState state = source[i];
                published.Add(state.GroupHash);
                View view = ViewFor(state, sim);
                bool snapped = Follow(view, state, Time.deltaTime);
                float distance = cam == null
                    ? float.NaN
                    : Vector2.Distance(new Vector2(cam.transform.position.x, cam.transform.position.z),
                                       new Vector2(view.ShownPosition.x, view.ShownPosition.z));
                Pose(view, terrains, ShouldShow(state.Spawned, distance), snapped);
            }

            gone.Clear();
            foreach (int hash in views.Keys)
                if (!published.Contains(hash)) gone.Add(hash);
            foreach (int hash in gone)
            {
                Destroy(views[hash].Root);
                views.Remove(hash);
            }
        }

        private View ViewFor(DistantGroupState state, NpcWorldSim sim)
        {
            if (views.TryGetValue(state.GroupHash, out View view))
            {
                if (view.TemplateHash == state.TemplateHash && view.RosterSeed == state.RosterSeed) return view;
                Destroy(view.Root);
            }

            view = Build(state, sim.FindTemplateByHash(state.TemplateHash));
            views[state.GroupHash] = view;
            return view;
        }

        /// <summary>The group's vehicles as plain renderers under one root, each with a copy of its far dust.</summary>
        private View Build(DistantGroupState state, NpcGroupTemplate template)
        {
            var view = new View
            {
                TemplateHash = state.TemplateHash,
                RosterSeed = state.RosterSeed,
                Root = new GameObject($"Distant_{state.GroupHash}"),
            };
            view.Root.transform.SetParent(transform, false);
            if (template == null)
            {
                Debug.LogError($"[DistantGroupSilhouette] No template hashes to {state.TemplateHash}: this machine's NpcWorldSim " +
                               "lacks one the server has. Nothing is drawn for that group.", this);
                return view;
            }

            foreach (Place place in Layout(template, state.RosterSeed))
            {
                var merged = place.Prefab.GetComponent<MergedLod>();
                if (merged == null)
                {
                    Debug.LogError($"[DistantGroupSilhouette] {place.Prefab.name} has no merged far level; run " +
                                   "Tools/SpaceGame/Art/Bake Settlement LODs. It is left out of the distant city.", this);
                    continue;
                }

                var body = new GameObject(place.Prefab.name);
                body.transform.SetParent(view.Root.transform, false);
                body.AddComponent<MeshFilter>().sharedMesh = merged.Mesh;
                var renderer = body.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = merged.Materials;
                renderer.enabled = false;

                FarDust dust = null;
                FarDust farDust = place.Prefab.GetComponentInChildren<FarDust>(true);
                if (farDust != null) dust = Instantiate(farDust.gameObject, body.transform, false).GetComponent<FarDust>();

                view.Vehicles.Add(new Vehicle
                {
                    Body = body.transform, Renderer = renderer, Dust = dust,
                    Local = place.Local, Sole = merged.Mesh.bounds.min.y,
                });
            }
            return view;
        }

        /// <summary>Glides the shown pose toward the published one; snaps on the first pose and on a jump. True when it snapped.</summary>
        private bool Follow(View view, DistantGroupState state, float dt)
        {
            if (!view.Posed || Vector3.Distance(view.ShownPosition, state.Position) > snapDistance)
            {
                view.ShownPosition = state.Position;
                view.ShownYaw = state.Yaw;
                view.Posed = true;
                return true;
            }

            view.ShownPosition = FollowPosition(view.ShownPosition, state.Position, dt, positionLag);
            view.ShownYaw = FollowYaw(view.ShownYaw, state.Yaw, dt, positionLag);
            return false;
        }

        /// <summary>
        /// Stands every vehicle on the ground under its slot. A hidden one keeps its last place, so its far
        /// dust reads no motion and the cloud it left settles where the city was drawn.
        /// </summary>
        private static void Pose(View view, Terrain[] terrains, bool show, bool snapped)
        {
            Quaternion facing = Quaternion.Euler(0f, view.ShownYaw, 0f);
            foreach (Vehicle vehicle in view.Vehicles)
            {
                Vector3 at = view.ShownPosition + facing * vehicle.Local;
                float ground = GroundUnder(terrains, at);
                bool drawn = show && !float.IsNaN(ground);
                vehicle.Renderer.enabled = drawn;
                if (!drawn) continue;

                vehicle.Body.SetPositionAndRotation(new Vector3(at.x, ground - vehicle.Sole, at.z), facing);
                if (snapped && vehicle.Dust != null) vehicle.Dust.ResetBaseline();
            }
        }
    }
}
