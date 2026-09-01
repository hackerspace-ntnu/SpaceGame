using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Vehicles;

namespace SpaceGame.Gameplay
{
    /// <summary>
    /// Puts one ship on the ground for each team, and answers where inside it a player starts.
    ///
    /// <para>
    /// Place one in a versus scene. Everything it does happens on the server: the ships are
    /// <c>NetworkObject</c>s spawned through <see cref="IWorldService"/> and reach clients by
    /// replicating, so a client that tried to spawn its own would be building a ship nobody else
    /// can see — the "works in solo, invisible in multiplayer" fault that service exists to
    /// prevent.
    /// </para>
    ///
    /// <para>
    /// Ships are grounded per team rather than all at once, because in a streamed world the ground
    /// arrives per chunk. A client whose team starts on the far side of the map has no business
    /// waiting for the enemy's terrain to load, and the server holds only the chunks its tracked
    /// players have pulled in — so insisting on every team at once is a wait that can genuinely
    /// never end.
    /// </para>
    ///
    /// <para>
    /// A plain <see cref="MonoBehaviour"/>, deliberately. It owns no replicated state and sends no
    /// RPCs — the ships it makes are networked by <see cref="IWorldService"/>, and its one caller
    /// is a coroutine that already runs only on the server. Being a <c>NetworkBehaviour</c> would
    /// buy nothing and cost a scene-placed <c>NetworkObject</c>, whose id has to survive being
    /// authored into a scene to work at all.
    /// </para>
    ///
    /// <para>
    /// Seat resolution lives in <c>VersusShipSpawner.Seats.cs</c>.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public partial class VersusShipSpawner : MonoBehaviour
    {
        public static VersusShipSpawner Instance { get; private set; }

        [Tooltip("The ship every team gets. One prefab for all of them: identical ships make the " +
                 "start fair without anyone having to balance them. It MUST be registered in the " +
                 "network prefab list, or it spawns for the host and for nobody else.")]
        [SerializeField] private GameObject shipPrefab;

        [Tooltip("Where the teams start, and the measurements for putting them there. Overridden by " +
                 "anything set on VersusShipSpawns at runtime.")]
        [SerializeField] private VersusShipSpawnConfig config;

        private readonly Dictionary<int, GameObject> shipByTeam = new();

        /// <summary>
        /// Each team's resolved touchdown, kept once it has been measured.
        ///
        /// <para>
        /// Two reasons, and the second is the important one. Resolving costs a footprint of ground
        /// probes per candidate spot and the callers ask again every frame while the world streams
        /// in, so re-measuring is a few thousand terrain samples a frame for nothing. And the answer
        /// is supposed to be a FACT about the arena: the arrival plans an arc onto it, flies for
        /// twenty-six seconds and then lands there, so a second resolve that returned a slightly
        /// different spot — because a chunk arrived, or a rock loaded — would land the hull off the
        /// end of its own trajectory.
        /// </para>
        /// </summary>
        private readonly Dictionary<int, (Vector3 Position, float Yaw)> landingByTeam = new();

        private IReadOnlyList<ShipSpawnPoint> points;

        /// <summary>Set once the layout has been resolved, successfully or not — see <see cref="TryResolvePoints"/>.</summary>
        private bool layoutResolved;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance != this) return;

            Instance = null;
            ForgetSeats();
        }

        /// <summary>
        /// This team's authored point at an arbitrary height, for deciding which chunks to load.
        ///
        /// <para>
        /// Deliberately unvalidated, and it has to be: no ground has streamed in yet, and a version
        /// that insisted on a measured height would be waiting for the very world this answer is
        /// about to load. Never a position to place anything at — that is
        /// <see cref="ShipGrounding.TryResolvePose"/>. The same split
        /// <c>SpawnPoint.Anchor</c> makes, and for the same reason.
        /// </para>
        /// </summary>
        public bool TryGetAnchor(int team, out Vector3 anchor)
        {
            anchor = Vector3.zero;

            if (!TryResolvePoints()) return false;
            if (!ShipSpawnLayout.TryPointForTeam(points, team, out ShipSpawnPoint point)) return false;

            anchor = point.At(0f);
            return true;
        }

        /// <summary>
        /// How many teams this arena has ships for, or zero when the layout cannot be resolved.
        ///
        /// <para>
        /// The layout's count rather than the session's, deliberately. It is the number of points
        /// that actually exist to land on, and a match whose team count outran its arena's would
        /// otherwise send the arrival looking for a place that was never authored.
        /// </para>
        /// </summary>
        public int TeamCount => TryResolvePoints() ? points.Count : 0;

        /// <summary>
        /// Every team's authored point, for a preload that has to cover the whole arena.
        ///
        /// <para>
        /// The versus arrival lands a ship for EVERY team, including ones nobody is on, and ground
        /// can only be measured on chunks something has streamed. Streaming around the joining
        /// player's own team alone is what would leave an empty team's ship circling a site the
        /// server has no terrain for — so the preload takes the lot.
        /// </para>
        /// </summary>
        public bool TryGetAnchors(List<Vector3> into)
        {
            into.Clear();

            if (!TryResolvePoints()) return false;

            for (int i = 0; i < points.Count; i++)
                into.Add(points[i].At(0f));

            return into.Count > 0;
        }

        /// <summary>
        /// Where this team's ship comes to rest: the nearest spot to its authored point that the
        /// hull actually fits on, with its belly on the ground and the heading the layout asked for.
        ///
        /// <para>
        /// Measured across the whole hull rather than under its origin, because a ship cannot tilt —
        /// see <see cref="HullFootprint"/> for the failure that fixes. The spot may be nudged off
        /// the authored point onto ground level enough to rest on; the ring layout is a staging
        /// arrangement, not a surveyed pad.
        /// </para>
        ///
        /// <para>
        /// False means "not yet" — in a streamed world the chunks under this point have not loaded,
        /// so there is nothing to measure — and the caller is expected to wait a frame and ask
        /// again. The contract <see cref="ShipGrounding"/> documents.
        /// </para>
        /// </summary>
        public bool TryLandingPose(int team, out Vector3 position, out float yaw)
        {
            position = Vector3.zero;
            yaw = 0f;

            if (!TryResolvePoints()) return false;

            if (!ShipSpawnLayout.TryPointForTeam(points, team, out ShipSpawnPoint point))
            {
                Debug.LogError($"[VersusShipSpawner] {VersusRules.TeamName(team)} has no spawn point " +
                               "in the resolved layout.", this);
                return false;
            }

            if (landingByTeam.TryGetValue(team, out (Vector3 Position, float Yaw) settled))
            {
                position = settled.Position;
                yaw = settled.Yaw;
                return true;
            }

            yaw = point.Yaw;

            if (!ShipGrounding.TryResolveHullLanding(point.GroundXZ, yaw, shipPrefab,
                                                     ProbeHeight, Landing, out position))
                return false;

            landingByTeam[team] = (position, yaw);
            return true;
        }

        /// <summary>
        /// Drops the resolved touchdowns when this spawner goes away with its scene, so a second
        /// match does not land its ships where the last arena's terrain happened to be.
        /// </summary>
        private void ForgetLandings() => landingByTeam.Clear();

        // A runtime layout may be set with no config asset at all — see TryResolvePoints — so the
        // placement numbers cannot be read straight off one. The fallbacks are the asset's own
        // defaults, which is what an arena that never authored an asset was always going to get.
        private float ProbeHeight => config != null ? config.ProbeHeight : 200f;

        private LandingTolerance Landing =>
            config != null ? config.Landing : new LandingTolerance(1f, 60f, 12f, 0.05f);

        /// <summary>
        /// How far the hull's belly hangs below its own origin, so a caller checking a landing knows
        /// what "on the ground" means for this ship. Measured off the prefab, which is the only copy
        /// guaranteed to be standing level.
        /// </summary>
        public float ShipBellyDrop => ShipHull.BellyDrop(shipPrefab);

        /// <summary>
        /// This team's ship, spawning it if it is not standing yet, or false when the ground under
        /// its start point cannot be measured.
        ///
        /// <para>
        /// False means "not yet" — the chunk has not loaded — and the caller is expected to wait a
        /// frame and ask again. Server-only, in that everything it can do that matters is a network
        /// spawn; its callers are <c>NetworkGameManager</c>'s spawn flow and the arrival, both of
        /// which already run nowhere else.
        /// </para>
        /// </summary>
        public bool TryEnsureShip(int team, out GameObject ship)
        {
            // A destroyed GameObject compares equal to null, so a ship lost with its scene is
            // dropped and rebuilt rather than handed back as a reference to nothing.
            if (shipByTeam.TryGetValue(team, out ship) && ship != null) return true;

            ship = null;

            if (!TryLandingPose(team, out Vector3 position, out float yaw)) return false;

            ship = EnsureShipAt(team, position, Quaternion.Euler(0f, yaw, 0f));
            return ship != null;
        }

        /// <summary>
        /// This team's ship, created at an arbitrary pose if it does not exist yet.
        ///
        /// <para>
        /// The seam the arrival needs: a ship that is going to FLY down starts at the top of its
        /// arc, not on its landing point, and nothing else about making it — the prefab, the name,
        /// the livery, the record of which hull belongs to which team — should be duplicated to say
        /// so. This class stays the single place a team ship comes into being, so the arrival and
        /// the ordinary spawn cannot end up putting two hulls on one team.
        /// </para>
        ///
        /// <para>
        /// Null on failure, always with a reason logged. Server-only: the ship is a
        /// <c>NetworkObject</c> spawned through <see cref="IWorldService"/>, so a client calling
        /// this would be building a hull nobody else can see.
        /// </para>
        /// </summary>
        public GameObject EnsureShipAt(int team, Vector3 position, Quaternion rotation)
        {
            if (shipByTeam.TryGetValue(team, out GameObject existing) && existing != null)
                return existing;

            shipByTeam.Remove(team);

            if (shipPrefab == null)
            {
                Debug.LogError("[VersusShipSpawner] No ship prefab assigned — teams have nowhere to " +
                               "start.", this);
                return null;
            }

            GameObject ship = GameServices.World.Spawn(shipPrefab, position, rotation);

            if (ship == null)
            {
                Debug.LogError($"[VersusShipSpawner] Spawning the ship for {VersusRules.TeamName(team)} " +
                               "returned nothing. Is it registered in the network prefab list?", this);
                return null;
            }

            ship.name = $"{shipPrefab.name} ({VersusRules.TeamName(team)})";
            PaintForTeam(ship, team);

            shipByTeam[team] = ship;
            return ship;
        }

        /// <summary>
        /// Puts this team's colour on the hull, so a ship can be told from an enemy ship at the only
        /// range that matters — across the arena.
        ///
        /// <para>
        /// The swatch is read from <see cref="VersusSession"/> HERE, on the server, and replicated by
        /// <see cref="ShipTeamAccent"/>. Every peer then paints from one answer rather than each
        /// deriving its own from a lobby it may have adopted differently.
        /// </para>
        /// </summary>
        private static void PaintForTeam(GameObject ship, int team)
        {
            var accent = ship.GetComponent<ShipTeamAccent>();

            if (accent == null)
            {
                Debug.LogWarning($"[VersusShipSpawner] '{ship.name}' has no ShipTeamAccent, so every " +
                                 "team's ship will wear the same authored paint.", ship);
                return;
            }

            accent.SetSwatch(VersusSession.IsActive
                ? VersusSession.ColorOf(team)
                : ShipAccentPalette.NoTeam);
        }

        /// <summary>
        /// The layout, resolved once and kept.
        ///
        /// <para>
        /// Resolved lazily rather than in <c>OnNetworkSpawn</c> because the runtime override is set
        /// by whoever launched the match, and there is no ordering guarantee that they got to it
        /// before this object spawned. Cached because the alternative — re-resolving per client —
        /// is a layout that can change underneath a match already in progress.
        /// </para>
        /// </summary>
        private bool TryResolvePoints()
        {
            if (layoutResolved) return points != null && points.Count > 0;

            layoutResolved = true;

            if (config == null && !VersusShipSpawns.HasOverride)
            {
                Debug.LogError("[VersusShipSpawner] No spawn config assigned and no runtime layout " +
                               "set — there is nowhere to put the ships.", this);
                return false;
            }

            int teamCount = VersusSession.IsActive ? VersusSession.TeamCount : VersusRules.DefaultTeams;

            if (!VersusShipSpawns.TryResolve(config, teamCount, out points, out string refusal))
            {
                Debug.LogError($"[VersusShipSpawner] Cannot place {teamCount} teams: {refusal}", this);
                return false;
            }

            return points.Count > 0;
        }
    }
}
