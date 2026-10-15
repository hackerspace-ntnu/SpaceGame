// The raiders who stole the workshop's gear, holed up at their camp (GearThiefCamp, an outpost placed in a chunk).
//
// Server only. The thieves are ordinary saved entities (GameServices.World.Spawn) put on the camp's spots, and the
// gear lies on one of its prop rests as an ordinary saved pickup, so a band that has been spawned comes back with the
// world on its own and is never spawned twice: the mission saves that the camp was chosen, and that is the whole
// record of it. Then it plays out by itself:
//   - a player who sneaks in unseen can simply pick the gear up;
//   - once any thief has a player in its sights, the carrier, who stands guard beside the gear, snatches it up and runs
//     (FleeModule) while the rest fight; he runs again whenever a player gets close;
//   - knocked down, netted or held, he loses his grip on it; killed, his loot table drops it.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using SpaceGame.Core;
using SpaceGame.Core.Persistence;
using SpaceGame.Items;

namespace SpaceGame.World
{
    [Serializable]
    public sealed class GearThiefBand
    {
        [Tooltip("A thief: an outlaw with a bag and a loot table that drops it (a GearThief).")]
        [SerializeField] private GameObject thiefPrefab;

        [Tooltip("The one who guards the gear and runs with it (a GearThief with IsCarrier, and a FleeModule).")]
        [SerializeField] private GameObject carrierPrefab;

        [Tooltip("How many are in the band, the carrier included.")]
        [SerializeField, Min(1)] private int count = 3;

        [Tooltip("How far from the carrier the gear may lie for him to snatch it up when the camp is alarmed, in metres.")]
        [SerializeField, Min(0.5f)] private float snatchReach = 5f;

        [Tooltip("The carrier runs again whenever a player comes this close while he has the gear, in metres.")]
        [SerializeField, Min(1f)] private float runFrom = 30f;

        [Tooltip("How far from a post the NavMesh may be found to stand a thief on, in metres.")]
        [SerializeField, Min(0.5f)] private float navSampleDistance = 4f;

        [Tooltip("How far above its rest the gear is dropped, so it settles onto it, in metres.")]
        [SerializeField, Min(0f)] private float gearDropHeight = 0.3f;

        /// <summary>
        /// Server: the band at <paramref name="camp"/>'s posts, the carrier on the one beside the gear, and
        /// <paramref name="stolen"/> lying on the camp's rest. False when it has no prefabs.
        /// </summary>
        public bool TrySpawn(GearThiefCamp camp, InventoryItem stolen)
        {
            if (thiefPrefab == null || carrierPrefab == null || stolen == null || stolen.itemPrefab == null)
            {
                Debug.LogError("[WorkshopMission] the gear thieves have no prefabs or no gear. Rebuild Workshop Prefab.");
                return false;
            }

            Pose rest = camp.GearRest;
            List<Pose> posts = camp.Posts();
            if (posts.Count == 0) posts.Add(new Pose(camp.transform.position, camp.transform.rotation));

            for (int i = 0; i < count; i++)
            {
                Pose post = posts[i % posts.Count];
                Vector3 at = NavMesh.SamplePosition(post.position, out NavMeshHit hit, navSampleDistance, NavMesh.AllAreas)
                    ? hit.position : post.position;
                GameObject spawned = GameServices.World.Spawn(i == 0 ? carrierPrefab : thiefPrefab, at, post.rotation);
                if (spawned == null) continue;

                // A band at its camp must not hold nine chunks loaded round each of its members.
                if (spawned.TryGetComponent(out SceneTracked tracked)) tracked.SetKeepChunksLoaded(false);
                if (spawned.TryGetComponent(out GearThief thief)) thief.Guard(post);
            }

            GameObject gear = GameServices.World.Spawn(stolen.itemPrefab, rest.position + Vector3.up * gearDropHeight, rest.rotation);
            if (gear != null) SaveableEntity.EnsureRuntime(gear, stolen.ID);
            return true;
        }

        /// <summary>Server, every think once the band exists. True while the carrier has the gear.</summary>
        public bool Tick(InventoryItem stolen, IReadOnlyList<Transform> players)
        {
            GearThief carrier = Carrier();
            if (carrier == null || !carrier.IsAlive) return false;   // dead: his loot table has dropped it

            bool holds = carrier.Holds(stolen);
            if (holds && carrier.IsDown)
            {
                carrier.Drop(stolen);
                return false;
            }

            if (!holds && !carrier.IsDown && Alarmed() &&
                LooseItems.TryFind(stolen, carrier.transform.position, snatchReach, out PickupableItem lying, out _))
            {
                GameServices.World.Despawn(lying.gameObject);
                holds = carrier.Take(stolen);
            }

            if (holds && AnyWithin(players, carrier.transform.position, runFrom)) carrier.Run();
            return holds;
        }

        /// <summary>The live carrier, on any machine.</summary>
        public static GearThief Carrier()
        {
            foreach (GearThief thief in GearThief.All)
                if (thief.IsCarrier && thief.IsAlive) return thief;
            return null;
        }

        private static bool Alarmed()
        {
            foreach (GearThief thief in GearThief.All)
                if (thief.IsAlive && thief.IsAlarmed) return true;
            return false;
        }

        private static bool AnyWithin(IReadOnlyList<Transform> players, Vector3 point, float radius)
        {
            foreach (Transform player in players)
                if (player != null && (player.position - point).sqrMagnitude <= radius * radius) return true;
            return false;
        }
    }
}
