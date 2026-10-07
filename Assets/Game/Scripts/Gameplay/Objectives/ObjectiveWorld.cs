using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Core.Persistence;
using SpaceGame.Items;
using SpaceGame.Vehicles;

namespace SpaceGame.Gameplay.Objectives
{
    /// <summary>
    /// Everything an <see cref="ObjectiveStep"/> is allowed to ask about the world, in one place.
    ///
    /// <para>
    /// Steps are stateless assets; this is where the few facts they need to remember during the
    /// current step live, and where every lookup they share is written once — the crew's hull, the
    /// crew themselves, a loose item lying somewhere near. Everything under "per step" is wiped by
    /// <see cref="ForgetStep"/> whenever the crew's step changes.
    /// </para>
    /// </summary>
    public sealed class ObjectiveWorld
    {
        /// <summary>How many loose contacts one search considers. Far more than a crash site holds.</summary>
        private const int LooseSearchLimit = 64;

        private readonly HashSet<InventoryItem> used = new();
        private readonly HashSet<ulong> finished = new();
        private readonly List<ScanContact> contacts = new(LooseSearchLimit);

        /// <summary>
        /// The crew's hull. A story world has exactly one ship with module sockets, and the
        /// objective chain runs only in a story world.
        /// </summary>
        public ShipPartRack Ship => ShipPartRack.Active.Count > 0 ? ShipPartRack.Active[0] : null;

        /// <summary>Every player in the session. Replicated by Netcode, so it is the same list on every machine.</summary>
        public IReadOnlyList<PlayerIdentity> Crew => PlayerIdentity.All;

        // ── Per step ────────────────────────────────────────────────────────────

        /// <summary>
        /// EVERY machine: the basic controls THIS machine's player has used during the current
        /// step. Written by <see cref="LearnControlsStep"/>, which is the only step that asks.
        /// </summary>
        public LearnControlsStep.Control LocalControlsUsed { get; set; }

        /// <summary>The crew have moved to another step: nothing remembered for the last one counts.</summary>
        public void ForgetStep()
        {
            used.Clear();
            finished.Clear();
            LocalControlsUsed = LearnControlsStep.Control.None;
        }

        /// <summary>SERVER: somebody fired <paramref name="item"/> during the current step.</summary>
        public void RecordUse(InventoryItem item)
        {
            if (item != null) used.Add(item);
        }

        public bool WasUsed(InventoryItem item) => item != null && used.Contains(item);

        /// <summary>SERVER: the player on <paramref name="clientId"/> has done their part of the current step.</summary>
        public void RecordFinished(ulong clientId) => finished.Add(clientId);

        /// <summary>
        /// SERVER: every player in the session has done their part of the current step. A player
        /// who leaves stops counting; one who joins mid-step has to do it too.
        /// </summary>
        public bool EveryoneFinished
        {
            get
            {
                if (Crew.Count == 0) return false;

                foreach (PlayerIdentity member in Crew)
                    if (member != null && !finished.Contains(member.OwnerClientId))
                        return false;

                return true;
            }
        }

        // ── Lookups, any machine ───────────────────────────────────────────────

        /// <summary>
        /// The nearest copy of <paramref name="item"/> lying loose within <paramref name="radius"/>
        /// of <paramref name="near"/>. False when every copy is in somebody's hands or pack — or
        /// lies in a chunk that has not streamed in, which is the same thing to a waypoint.
        /// </summary>
        public bool TryFindLoose(InventoryItem item, Vector3 near, float radius, out Vector3 position)
        {
            position = default;
            if (item == null) return false;

            ScannerRegistry.Collect(near, radius, contacts, LooseSearchLimit);

            // Collect sorts nearest first, so the first match is the answer.
            foreach (ScanContact contact in contacts)
            {
                if (contact.Target is not PickupableItem pickup || pickup.Item != item) continue;

                // The copy in a hand is an Instantiate of the same prefab, pickup and all, and it
                // registers like any other — without this the waypoint follows the player carrying
                // the thing it is pointing them at. The mark is what every equip path puts on it.
                if (BodyAttachment.Covers(pickup.transform, null)) continue;

                position = contact.Position;
                return true;
            }

            return false;
        }

        /// <summary>
        /// The centre of the first empty module socket on the crew's hull that takes
        /// <paramref name="kind"/>, or of any empty socket when no kind is given.
        /// </summary>
        public bool TryGetEmptySocket(out Vector3 position, ShipPartKind? kind = null)
        {
            position = default;
            ShipPartRack ship = Ship;
            if (ship == null) return false;

            foreach (ShipPartSocket socket in ship.Sockets)
            {
                if (socket == null || socket.Installed) continue;
                if (kind.HasValue && socket.Kind != kind.Value) continue;

                position = socket.Centre;
                return true;
            }

            return false;
        }

        /// <summary>The crew's hull's first socket that takes <paramref name="kind"/>, whatever is in it; null for none.</summary>
        public ShipPartSocket SocketOf(ShipPartKind kind)
        {
            ShipPartRack ship = Ship;
            if (ship == null) return null;

            foreach (ShipPartSocket socket in ship.Sockets)
                if (socket != null && socket.Kind == kind) return socket;

            return null;
        }

        /// <summary>
        /// EVERY machine: the flat distance from <paramref name="point"/> to the nearest member of the crew,
        /// metres; infinity with nobody in the session. Player positions are replicated, so the server asks it
        /// of everyone.
        /// </summary>
        public float NearestCrewDistance(Vector3 point)
        {
            float nearest = float.PositiveInfinity;

            foreach (PlayerIdentity member in Crew)
            {
                if (member == null) continue;
                Vector3 at = member.transform.position;
                float dx = at.x - point.x, dz = at.z - point.z;
                nearest = Mathf.Min(nearest, Mathf.Sqrt(dx * dx + dz * dz));
            }

            return nearest;
        }

        /// <summary>
        /// A point on the ground at a ship-relative offset: sideways and forward in the hull's own
        /// frame, so "in front of the nose" means the same thing however the wreck came to rest.
        /// False while the ground there has not streamed in — never a guessed height.
        /// </summary>
        public bool TryGroundNearShip(Vector3 localOffset, float probeHeight, out Vector3 point)
        {
            point = default;
            ShipPartRack ship = Ship;
            if (ship == null) return false;

            Vector3 flat = ship.transform.TransformPoint(localOffset);
            if (!ShipGrounding.TryResolveGround(new Vector2(flat.x, flat.z), flat.y + probeHeight, out float y))
                return false;

            point = new Vector3(flat.x, y, flat.z);
            return true;
        }

        /// <summary>
        /// SERVER: puts a copy of <paramref name="item"/> on the ground for everyone to see, and
        /// gives it a save identity the way a dropped item gets one. Without that stamp the
        /// module is captured with no prefab id, dropped at the next save, and gone on load —
        /// while the saved progress says its step already begun, so nothing would place it again.
        /// </summary>
        public void Spawn(InventoryItem item, Vector3 position, Quaternion rotation)
        {
            GameObject spawned = GameServices.World.Spawn(item.itemPrefab, position, rotation);
            if (spawned != null) SaveableEntity.EnsureRuntime(spawned, item.ID);
        }
    }
}
