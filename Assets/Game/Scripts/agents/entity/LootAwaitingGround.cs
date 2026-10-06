// A body killed aloft (seated in an IAirborneCarrier) holds its loot until it is down, so the pack lies
// beside the body on ground somebody can reach (D8), not 200 m below the kill point: through the terrain,
// or off the Sky City deck. EntityLootTable adds this to the corpse for that one death; it removes itself
// once the drop is made, so nobody else pays for a per-frame check.
//
// Down: solid ground within landedHeight under the body's root (a limp ragdoll drags its root after the
// hips) and, for a ragdoll, the body settled: IsSettled, not IsAtRest, because a corpse on the drifting
// Sky City deck or a dune slope never reads at rest, and only the corpse's limp ceiling ends that wait.
// Despawned on the way down: the drop is made at the last ground seen below it, never at altitude. No
// ground ever seen (over the map's edge): nothing drops, loudly. Server only: it is only ever added where
// the death was simulated. Not saved, and NpcFlightModule keeps the corpse out of the save until it is
// done (WhenDropped): see NpcFlight.md Gotchas.
using System;
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Gameplay.Ragdoll;

namespace SpaceGame.Agents
{
    [DisallowMultipleComponent]
    public class LootAwaitingGround : MonoBehaviour
    {
        // The ground ray starts inside the body, so a body lying ON the ground still finds it below.
        private const float ProbeLift = 0.5f;
        private const int HitCapacity = 32;
        private static readonly RaycastHit[] hits = new RaycastHit[HitCapacity];
        private static bool quitting;

        private Action drop;
        private Action dropped;
        private float landedHeight;
        private float searchDepth;
        private bool hasGround;
        private Vector3 lastGround;
        private RagdollRig rig;

        /// <summary>Hold <paramref name="dropLoot"/> until <paramref name="body"/> is on the ground.</summary>
        public static void Begin(GameObject body, Action dropLoot, float landedHeight, float searchDepth)
        {
            if (!body.TryGetComponent(out LootAwaitingGround waiting)) waiting = body.AddComponent<LootAwaitingGround>();
            waiting.drop = dropLoot;
            waiting.landedHeight = landedHeight;
            waiting.searchDepth = searchDepth;
            waiting.rig = body.GetComponent<RagdollRig>();
        }

        /// <summary>
        /// Run <paramref name="then"/> once <paramref name="body"/> has no loot waiting on it: when the
        /// drop is made (or given up), or at once when nothing is held. NpcFlightModule keeps a pilot killed
        /// aloft out of the save until then, so a save taken during the fall never keeps the pack on it.
        /// Independent of OnDeath handler order: asked before the loot table has heard the death, the
        /// table's seated-aloft latch says a drop is coming, and the waiter is made now for Begin to fill.
        /// </summary>
        public static void WhenDropped(GameObject body, Action then)
        {
            if (body == null) { then(); return; }

            if (!body.TryGetComponent(out LootAwaitingGround waiting))
            {
                if (!body.TryGetComponent(out EntityLootTable table) || !table.DropsOnceDown) { then(); return; }
                waiting = body.AddComponent<LootAwaitingGround>();
            }
            waiting.dropped += then;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void WatchForQuit()
        {
            quitting = false;
            // Removed first: with domain reload off, statics and subscriptions outlive a play session.
            Application.quitting -= OnQuitting;
            Application.quitting += OnQuitting;
        }

        private static void OnQuitting() => quitting = true;

        private void Update()
        {
            if (drop == null || !TryGroundBelow(out Vector3 ground)) return;

            hasGround = true;
            lastGround = ground;
            if (transform.position.y - ground.y > landedHeight) return;
            if (rig != null && !rig.IsSettled) return;

            Release();
            if (Application.isPlaying) Destroy(this);
            else DestroyImmediate(this);
        }

        /// <summary>
        /// Despawned before it landed: Remains took a body left hanging, or its chunk went. A scene being
        /// torn down, the application quitting or the server shutting down is not that, and spawns nothing.
        /// </summary>
        private void OnDisable()
        {
            if (drop == null || !gameObject.scene.isLoaded || quitting || ShuttingDown) return;

            if (!hasGround)
            {
                Debug.LogWarning($"[Loot] '{name}' was killed aloft and taken away before any ground was seen below it; its loot was not dropped.", this);
                drop = null;
                Finish();
                return;
            }

            // It is leaving anyway: stood on the ground it was falling to, so the drop reads that, not the sky.
            transform.position = lastGround;
            Release();
        }

        private static bool ShuttingDown =>
            NetworkManager.Singleton != null && NetworkManager.Singleton.ShutdownInProgress;

        private void Release()
        {
            Action pending = drop;
            drop = null;
            pending();
            Finish();
        }

        private void Finish()
        {
            Action waiting = dropped;
            dropped = null;
            waiting?.Invoke();
        }

        /// <summary>
        /// The nearest solid surface under the body: never the body itself, and never a dynamic body
        /// (the wreck spiralling down beside it, a ragdoll, an item). A kinematic deck counts.
        /// </summary>
        private bool TryGroundBelow(out Vector3 ground)
        {
            Vector3 origin = transform.position + Vector3.up * ProbeLift;
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, hits, ProbeLift + searchDepth,
                                                PerceptionModule.SolidGeometryLayers, QueryTriggerInteraction.Ignore);

            float nearest = float.PositiveInfinity;
            ground = origin;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = hits[i];
                Rigidbody body = hit.collider.attachedRigidbody;
                if (hit.distance >= nearest || hit.collider.transform.IsChildOf(transform) ||
                    (body != null && !body.isKinematic)) continue;
                nearest = hit.distance;
                ground = hit.point;
            }
            return nearest < float.PositiveInfinity;
        }
    }
}
