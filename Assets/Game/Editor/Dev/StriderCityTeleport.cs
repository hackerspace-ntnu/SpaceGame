// Dev shortcut to the Striders' walking city, so a play session can meet it without a long walk.
//
// The city exists as live objects only within NpcWorldSim's spawnRadius of a player, so this lands
// the local player inside that radius and lets the sim unfold it. It waits for the ground first:
// the streamer is told to preload the chunks at the landing point, and the player is only moved once
// they are in. Moving first means arriving over chunks that have not loaded — nothing to stand on,
// nothing for UnderTerrainGuard to lift onto, and a fall that never ends.
//
// Host / single-player only: it is the editor, and NetworkedTeleport routes the move through the
// body's owner.
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Core;
using SpaceGame.World;

namespace SpaceGame.EditorTools
{
    public static class StriderCityTeleport
    {
        private const string MenuPath = "Tools/SpaceGame/Dev/Teleport to Strider City";

        /// <summary>
        /// Metres from the city's position to land at: inside NpcWorldSim's 250 m spawnRadius, so the
        /// city unfolds, and clear of the 60 m-wide column of 21 m houses walking through.
        /// </summary>
        private const float LandingDistance = 150f;

        /// <summary>Pivot above the ground, as SpawnPoint's groundClearance: the capsule's foot sits ~1 m under the pivot.</summary>
        private const float LandingClearance = 1.2f;

        [MenuItem(MenuPath)]
        private static void Teleport()
        {
            NpcGroup city = NpcWorldSim.Instance != null ? NpcWorldSim.Instance.FindGroup(RosterAuthoring.StriderCityTemplateId) : null;
            NetworkObject playerObject = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClient?.PlayerObject : null;
            GameObject player = playerObject != null ? playerObject.gameObject : null;
            var streamer = Object.FindFirstObjectByType<WorldStreamer>();
            if (city == null || player == null || streamer == null || !streamer.IsReady)
            {
                Debug.LogError($"[StriderCityTeleport] Needs a running world: city group {(city != null ? "found" : "missing")}, " +
                               $"local player {(player != null ? "found" : "missing")}, " +
                               $"streamer {(streamer != null && streamer.IsReady ? "ready" : "not ready")}.");
                return;
            }

            // A new player arrives gliding in a mount; a teleport must not take the mount along.
            // Falls back to the mount the player is parented under, because the arrival DuneOrnithopter
            // glider is not always registered as LocalRiderMount.
            MountModule mount = MountModule.LocalRiderMount != null ? MountModule.LocalRiderMount : player.GetComponentInParent<MountModule>();
            if (mount != null) mount.Dismount();

            // Approach from the player's side, so the city is ahead on arrival.
            Vector3 fromCity = Vector3.ProjectOnPlane(player.transform.position - city.Position, Vector3.up).normalized;
            if (fromCity == Vector3.zero) fromCity = Vector3.forward;
            Vector3 landing = city.Position + fromCity * LandingDistance;

            Debug.Log($"[StriderCityTeleport] {(mount != null ? $"Dismounted {mount.name}; " : string.Empty)}" +
                      $"loading the ground at {landing:F0}, {LandingDistance:F0} m from the city at {city.Position:F0}.");
            streamer.PreloadChunksAroundPosition(landing, () => Land(player, streamer, landing, city.Position));
        }

        [MenuItem(MenuPath, true)]
        private static bool CanTeleport() => EditorApplication.isPlaying;

        private static void Land(GameObject player, WorldStreamer streamer, Vector3 landing, Vector3 cityPosition)
        {
            if (player == null) return;

            // The terrain first — the city's recorded height means nothing — then a raycast down from
            // over it, so a rock or building standing there is landed on rather than inside.
            if (!streamer.TryGetTerrainHeight(landing, out float terrainY) ||
                !streamer.TrySampleGroundHeight(new Vector3(landing.x, terrainY, landing.z), out float groundY))
            {
                Debug.LogError($"[StriderCityTeleport] The chunks at {landing:F0} loaded but have no ground to land on.");
                return;
            }

            Vector3 arrival = new Vector3(landing.x, groundY + LandingClearance, landing.z);
            Vector3 facing = Vector3.ProjectOnPlane(cityPosition - arrival, Vector3.up);
            NetworkedTeleport.Move(player, arrival, facing == Vector3.zero ? player.transform.rotation : Quaternion.LookRotation(facing));
            Debug.Log($"[StriderCityTeleport] Landed at {arrival:F0}, facing the city.");
        }
    }
}
