// Turning a body into a record, and a record back into a body.
//
// The two halves of containment that touch the save system, kept together because they are exact
// inverses and drift the moment they are written apart. Everything here is the AUTHORITY's work:
// a capture despawns a networked entity and a release spawns one, which is the server's business
// and nobody else's (GDC-L1-MP-0004).
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Core;
using SpaceGame.Core.Persistence;
using SpaceGame.Persistence;

namespace SpaceGame.Gameplay.Containment
{
    /// <summary>
    /// The capture and release of a living thing as a saved record.
    ///
    /// <para>
    /// Static and free of any container: a canister, a cage or a trap all want the same two verbs,
    /// and the state — which container holds which captive — belongs to the container
    /// (<see cref="ContainerHold"/>), not here.
    /// </para>
    /// </summary>
    public static class Captivity
    {
        /// <summary>
        /// The <c>ItemState</c> key a container's captive is stored under. Written into save
        /// files — <b>never rename</b>.
        /// </summary>
        public const string StateKey = "containment.captive";

        /// <summary>
        /// Could this body be turned into a record that can be turned back into a body?
        ///
        /// <para>
        /// Asked BEFORE anything is despawned, and every "no" is loud rather than silent, because
        /// the alternative is the one failure the persistence rules exist to prevent: a creature
        /// that goes into a container and can never come out, with nothing in the console.
        /// </para>
        /// </summary>
        /// <param name="why">A player-facing sentence. Null when the answer is yes.</param>
        public static bool CanRecord(GameObject body, out string why)
        {
            why = null;
            if (body == null) return false;

            SaveableEntity entity = body.GetComponent<SaveableEntity>();
            if (entity == null)
            {
                why = $"{body.name} is not something the world can put back.";
                return false;
            }

            // Somebody else's record. A player lives in PlayerSaveService keyed by profile, and
            // anything else marked External is owned by a system that would keep writing it.
            // Neither may be folded into an item — a player is held for a few seconds instead
            // (see BottledPlayer), and an externally-owned object is not ours to take.
            if (!entity.BelongsToWorld)
            {
                why = $"{body.name} cannot be contained.";
                return false;
            }

            // EMPTY and UNRESOLVABLE are opposites here, exactly as WorldSaveStore treats them: an
            // id nobody has registered yet is recoverable once the wiring is fixed, a blank one can
            // never name anything. Both are refused at the door, because a capture is destructive
            // and the object is the only copy that exists.
            if (string.IsNullOrEmpty(entity.PrefabId))
            {
                why = $"{body.name} carries no prefab id, so it could never be released again.";
                Debug.LogError($"[Containment] Refusing to contain '{body.name}': its SaveableEntity " +
                               "has an EMPTY prefabId, so the record would name nothing and the body " +
                               "would be gone for good. Run Tools > Save System > Wire Saveable " +
                               "Prefabs to stamp it.", body);
                return false;
            }

            if (!SaveablePrefabRegistry.TryGet(entity.PrefabId, out _))
            {
                why = $"{body.name} cannot be rebuilt, so it cannot be contained.";
                Debug.LogError($"[Containment] Refusing to contain '{body.name}': no prefab is " +
                               $"registered for id '{entity.PrefabId}', so releasing it would fail. " +
                               "Register it with NetworkManager, put it under " +
                               $"Resources/{SaveablePrefabRegistry.ResourcesFolder}, or run " +
                               "Tools > Save System > Wire Saveable Prefabs.", body);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Fold a body — and whoever is riding it — into a record, and take it out of the world.
        ///
        /// <para>
        /// <b>Authority only.</b> The caller has already decided that this machine is the one that
        /// simulates the captor; this despawns a networked object, which no client may do.
        /// </para>
        /// <para>
        /// <b>Nothing is despawned until everything has been recorded.</b> A capture that discovers
        /// halfway through that the rider cannot be recorded has already deleted the mount, so the
        /// order here is: dismount, record every part, and only then take them out of the world.
        /// </para>
        /// </summary>
        /// <returns>
        /// The record, or null when the capture did not happen — in which case nothing has been
        /// changed and the caller must not treat the body as contained.
        /// </returns>
        public static CaptiveRecord Capture(GameObject body)
        {
            if (body == null) return null;
            if (!CanRecord(body, out _)) return null;

            // Taken out of the saddle FIRST, while both objects are still in the world. Recording a
            // rider in its seat would record a pose that means nothing (the seat decides where a
            // rider sits) and, more to the point, would leave it parented to an object that is
            // about to be despawned — Unity refuses to reparent out of an inactive hierarchy, so
            // the rider would go down with the mount.
            GameObject rider = null;
            NpcPassenger passenger = body.GetComponent<NpcPassenger>();

            if (passenger != null && passenger.HasRider)
            {
                rider = passenger.Dismount();

                if (rider == null)
                {
                    // Dismount refuses on a machine that does not simulate the mount, and on one
                    // that is being torn down. Either way the pair cannot be taken whole, and a
                    // pair that cannot be taken whole is not taken at all.
                    Debug.LogWarning($"[Containment] '{body.name}' has a rider that would not " +
                                     "dismount, so the capture is refused rather than separating " +
                                     "them.", body);
                    return null;
                }

                if (!CanRecord(rider, out _)) return null;
            }

            CaptiveRecord record = RecordOne(body);
            if (rider != null) record.Carried.Add(RecordOne(rider));

            // Through the world service and not a bare Destroy: it despawns for every peer, and it
            // tells the save system first, so an AUTHORED captive is tombstoned and its scene stops
            // putting it back. The tombstone is why a released captive is given a FRESH identity —
            // see CaptiveRecord.
            if (rider != null) GameServices.World.Despawn(rider);
            GameServices.World.Despawn(body);

            return record;
        }

        /// <summary>
        /// Put a record back in the world at <paramref name="point"/>, rider and all.
        ///
        /// <para>
        /// The sequence is <c>WorldSaveStore.SpawnEntities</c>' own, and the order is the whole
        /// point: savers are attached before the identity, the identity before the state, and the
        /// network spawn last of all — a client must not be handed the object until its health,
        /// faction and mind are already the ones it went in with.
        /// </para>
        /// <para>
        /// <b>Clearing the container is the CALLER's half of the same step.</b> Nothing here knows
        /// which container this came out of; see <see cref="ContainerHold.TryUncork"/>, which
        /// clears before it spawns so that a second uncork on the same frame finds an empty bottle.
        /// </para>
        /// </summary>
        /// <returns>The released body, or null when the record could not be rebuilt.</returns>
        public static GameObject Release(CaptiveRecord record, Vector3 point, Quaternion rotation)
        {
            GameObject body = Rebuild(record, point, rotation);
            if (body == null) return null;

            if (record.Carried == null) return body;

            NpcPassenger passenger = body.GetComponent<NpcPassenger>();

            foreach (CaptiveRecord carried in record.Carried)
            {
                // Beside the carrier rather than under it: the seat is what places a rider, and
                // Seat re-parents from wherever the rider happens to be standing.
                GameObject rider = Rebuild(carried, point, rotation);
                if (rider == null) continue;

                if (passenger != null) passenger.Seat(rider);
            }

            return body;
        }

        /// <summary>One body into the world save's own record shape.</summary>
        private static CaptiveRecord RecordOne(GameObject body)
        {
            SaveableEntity entity = body.GetComponent<SaveableEntity>();

            var record = new CaptiveRecord
            {
                DisplayName = body.name,
                Entity = new EntityRecord
                {
                    PrefabId = entity.PrefabId,

                    // Deliberately blank. The identity of a captive is the container's, not the
                    // world store's, and carrying the old one back out would contradict the
                    // tombstone a captured authored creature leaves behind. CaptiveRecord's class
                    // summary has the full reasoning.
                    InstanceId = string.Empty,
                    Scene = string.Empty,

                    // Whatever it was before, it comes back as a runtime spawn: no scene file
                    // contains it any more, so a delta would have nothing to be a delta of.
                    Authored = false,

                    // Where it went in. Not where it comes out — Release places it at the point the
                    // captor aimed at — but the record's shape carries a pose, and a record whose
                    // pose means nothing is one the world store would refuse to spawn if it ever
                    // reached the world store by another route.
                    Position = body.transform.position,
                    Rotation = body.transform.rotation,
                    Scale = body.transform.localScale,
                    HasPose = true,
                    HasScale = true,
                },
            };

            // A fresh bag, the way CaptureEntity builds one: this is a snapshot of the body at the
            // moment it went in, not a merge onto anything.
            var bag = new StateBag();
            entity.Capture(bag);
            record.Entity.State = bag;

            return record;
        }

        /// <summary>
        /// Instantiate, wire, restore, spawn — one record, no riders. The half that fails loudly.
        /// </summary>
        private static GameObject Rebuild(CaptiveRecord record, Vector3 point, Quaternion rotation)
        {
            if (record?.Entity == null) return null;

            if (!SaveablePrefabRegistry.TryGet(record.Entity.PrefabId, out GameObject prefab))
            {
                // Loud, and the record is NOT consumed — the caller keeps it, so the captive can
                // still be let out by a build in which the prefab is reachable. Silently dropping
                // it is how a creature disappears with nothing in the console.
                Debug.LogError($"[Containment] No prefab registered for id '{record.Entity.PrefabId}', " +
                               $"so the contained '{record.Describe()}' cannot be released. The " +
                               "record is being kept. Register it with NetworkManager, put it under " +
                               $"Resources/{SaveablePrefabRegistry.ResourcesFolder}, or run " +
                               "Tools > Save System > Wire Saveable Prefabs.");
                return null;
            }

            GameObject instance = Object.Instantiate(prefab, point, rotation);
            if (record.Entity.HasScale) instance.transform.localScale = record.Entity.Scale;

            // Savers before identity, identity before state. Restore hands each payload to the
            // saver that owns its key, so a saver added after the record was written is handed
            // nothing at all unless it is attached first.
            SaveablePolicy.EnsureSpawned(instance);

            SaveableEntity saveable = SaveableEntity.EnsureRuntime(instance, record.Entity.PrefabId);
            saveable.Restore(record.Entity.State);

            SaveNetworking.SpawnIfNetworked(instance);

            // The deferred pass, run by hand. Nothing else will: SaveManager only runs one per
            // world load and per player bind, and a release happens in the middle of a session.
            // After the network spawn, because a deferred saver that seats or mounts needs an
            // object every machine already has.
            saveable.NotifyLoadComplete();

            return instance;
        }
    }
}
