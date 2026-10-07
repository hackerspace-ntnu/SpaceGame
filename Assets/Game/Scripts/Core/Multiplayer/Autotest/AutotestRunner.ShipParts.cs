// The hull-module fit both halves of the two-process run check.
//
// What this exists to catch is the one failure static analysis cannot see: the host fits a module,
// sees the engine appear on the hull, and the client is still looking at a hole. The fitted set is
// a NetworkVariable bitmask, so the questions are whether it replicates at all, whether the
// receiving machine actually shows the geometry, and — because a wreck is a long-lived thing that
// players join in the middle of — whether a client that arrives AFTER the fit is told about it.
//
// The ship is spawned rather than found: persistentScene holds no PlayerShip (only the test world
// does), and a run that quietly reported "no ship" would have proven nothing while looking green.
using System.Collections;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Vehicles;

namespace SpaceGame.Core
{
    internal sealed partial class AutotestRunner
    {
        /// <summary>How far ahead of the host the wreck is put down, in metres.</summary>
        private const float ShipDropDistance = 60f;

        /// <summary>Longest the client waits for a fit the host says it made.</summary>
        private const float FitWaitSeconds = 30f;

        /// <summary>
        /// Host: spawn a wrecked hull and fit two modules to it.
        ///
        /// <para>
        /// The fit goes through <see cref="ShipPartRack.TryInstall"/>, which is the method the
        /// item's <c>Use()</c> calls on the server and the only place the mask is ever widened.
        /// The press-to-server hop above it is <c>EquipmentController</c>'s, shared with every
        /// artifact in the game and already covered; what is untested until this runs is what
        /// happens on the far side of the NetworkVariable.
        /// </para>
        /// </summary>
        private IEnumerator FitShipPartsAsHost()
        {
            GameObject player = AutotestProbes.LocalPlayerObject();
            var prefab = FindShipPrefab();

            if (prefab == null)
            {
                Report("HOST_SHIPPARTS", "no PlayerShip prefab in the network prefab list");
                yield break;
            }

            Vector3 where = player != null
                ? player.transform.position + player.transform.forward * ShipDropDistance
                : Vector3.zero;

            GameObject ship = GameServices.World.Spawn(prefab, where, Quaternion.identity);
            if (ship == null)
            {
                Report("HOST_SHIPPARTS", "spawn refused");
                yield break;
            }

            var rack = ship.GetComponent<ShipPartRack>();
            if (rack == null)
            {
                Report("HOST_SHIPPARTS", "the spawned ship has no ShipPartRack");
                yield break;
            }

            // The id both processes use to find the same hull. The client cannot be told this on a
            // command line — only the running host knows it — so it is reported and matched in the
            // log, the same way the victim and quarry ids are.
            var netObj = ship.GetComponent<NetworkObject>();
            Report("HOST_SHIP_ID", netObj != null ? netObj.NetworkObjectId : 0);
            Report("HOST_SHIP_SOCKETS", rack.Sockets.Count);
            Report("HOST_SHIP_MASK_BEFORE", rack.InstalledMask);

            yield return new WaitForSeconds(4f);

            // Two sockets of DIFFERENT kinds, so a mask that happened to be a constant, or one
            // that only ever carried its lowest bit, could not pass.
            int first = FirstSocketOfKind(rack, ShipPartKind.NuclearMotor);
            int second = FirstSocketOfKind(rack, ShipPartKind.AirIntake);

            Report("HOST_SHIP_FIT_MOTOR", first >= 0 && rack.TryInstall(first, ShipPartKind.NuclearMotor));
            Report("HOST_SHIP_FIT_INTAKE", second >= 0 && rack.TryInstall(second, ShipPartKind.AirIntake));

            // The rule that stops one socket eating two modules. Refused here is a PASS.
            Report("HOST_SHIP_REFIT_REFUSED",
                   first < 0 || !rack.TryInstall(first, ShipPartKind.NuclearMotor));

            // And the wrong module in the right hole.
            Report("HOST_SHIP_WRONGKIND_REFUSED",
                   second < 0 || !rack.TryInstall(second, ShipPartKind.NuclearMotor));

            Report("HOST_SHIP_MASK_AFTER", rack.InstalledMask);
            Report("HOST_SHIP_VISIBLE_AFTER", VisibleModules(rack));
        }

        /// <summary>
        /// Client: find the hull the host spawned and read what it was told about it.
        ///
        /// <para>
        /// Both numbers matter and they fail differently. A mask that never arrives means the
        /// replication is broken; a mask that arrives while nothing on the hull changed means the
        /// receiving machine never applied it, which on the host is invisible because the host
        /// applies it on the way out.
        /// </para>
        /// </summary>
        private IEnumerator ReadShipPartsAsClient()
        {
            yield return WaitAtMost(() => FindClientRack() != null, FitWaitSeconds);

            ShipPartRack rack = FindClientRack();
            if (rack == null)
            {
                // Almost always the network prefab list: the host instantiates its own copy without
                // consulting it, so an unregistered hull is a host that works and a client with
                // nothing there at all.
                Report("CLIENT_SHIP", "no PlayerShip replicated");
                yield break;
            }

            var netObj = rack.GetComponent<NetworkObject>();
            Report("CLIENT_SHIP_ID", netObj != null ? netObj.NetworkObjectId : 0);
            Report("CLIENT_SHIP_SOCKETS", rack.Sockets.Count);

            // Wait for a fit rather than for a fixed time: "the mask never arrived" is the answer
            // this step exists to produce, so it is reported rather than allowed to end the run.
            yield return WaitAtMost(() => rack.InstalledMask != 0, FitWaitSeconds);

            Report("CLIENT_SHIP_MASK_SEEN", rack.InstalledMask);
            Report("CLIENT_SHIP_VISIBLE_SEEN", VisibleModules(rack));
        }

        /// <summary>
        /// How many modules this machine is actually DRAWING. The mask is the claim; this is the
        /// hull. They have to agree, or a client is looking at a hole the server thinks is filled.
        /// </summary>
        private static int VisibleModules(ShipPartRack rack) =>
            rack.Sockets.Count(socket =>
            {
                if (socket == null) return false;
                var renderer = socket.GetComponent<Renderer>();
                return renderer != null && renderer.enabled;
            });

        private static int FirstSocketOfKind(ShipPartRack rack, ShipPartKind kind)
        {
            for (int i = 0; i < rack.Sockets.Count; i++)
                if (rack.Sockets[i] != null && rack.Sockets[i].Kind == kind)
                    return i;

            return -1;
        }

        /// <summary>
        /// The ship prefab as the NetworkManager knows it. Read from the prefab list rather than
        /// Resources: this prefab is not under a Resources folder, and the list is the thing whose
        /// contents actually decide whether a client can be shown one.
        /// </summary>
        private static GameObject FindShipPrefab()
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null) return null;

            foreach (var entry in manager.NetworkConfig.Prefabs.Prefabs)
            {
                if (entry?.Prefab == null) continue;
                if (entry.Prefab.GetComponent<ShipPartRack>() != null) return entry.Prefab;
            }

            return null;
        }

        private static ShipPartRack FindClientRack()
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null) return null;

            foreach (var pair in manager.SpawnManager.SpawnedObjects)
            {
                if (pair.Value == null) continue;

                var rack = pair.Value.GetComponent<ShipPartRack>();
                if (rack != null) return rack;
            }

            return null;
        }
    }
}
