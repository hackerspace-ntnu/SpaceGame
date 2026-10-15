// What a resident can see about one player at a glance, and the one thing worth reacting to.
//
// Read on the SERVER for every machine's player, so it only uses state that is true there: the held
// item asset, worn gear, the replicated sprint flag, the body's facing and this resident's own ears
// (ProvocationModule's gunshot record). Nothing reads a camera — a remote player has none on the server.
//
// "Menacing" reuses MenaceSensor's rule rather than inventing a second one: a weapon in hand AND a shot
// this resident heard from that player a moment ago. Holding a gun is "armed"; having just fired it
// beside somebody is a threat.
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Characters;
using SpaceGame.Items;

namespace SpaceGame.Agents.Residents
{
    public struct PlayerRead
    {
        /// <summary>Seconds after a shot that an armed player still counts as menacing (MenaceSensor's brandish window).</summary>
        public const float MenaceWindowSeconds = 6f;

        /// <summary>Seconds a witnessed hit keeps a player "hitting" in this resident's eyes.</summary>
        public const float RecentHitSeconds = 10f;

        public Transform player;
        public ulong playerId;
        public bool armed, menacing, curio, gauntlet, sprinting;
        public int recentHits;
        public float distance;
        public InventoryItem held;

        public static PlayerRead Of(Transform player, Transform resident)
        {
            var read = new PlayerRead { player = player };
            if (player == null || resident == null) return read;

            read.distance = Vector3.Distance(player.position, resident.position);
            if (player.TryGetComponent(out NetworkObject networkObject))
                read.playerId = networkObject.NetworkObjectId;

            EquipmentController hand = player.GetComponentInChildren<EquipmentController>();
            read.held = hand != null ? hand.HeldItemAsset : null;
            read.armed = read.held != null && read.held.menacing;
            read.curio = read.held != null && read.held.curiosity != Curiosity.None;

            BodyEquipmentController body = player.GetComponentInChildren<BodyEquipmentController>();
            read.gauntlet = body != null && (body.WornIn(BodySlot.LeftGauntlet) != null ||
                                             body.WornIn(BodySlot.RightGauntlet) != null);

            PlayerStance stance = player.GetComponentInChildren<PlayerStance>();
            read.sprinting = stance != null && stance.IsSprinting;

            var self = resident.GetComponent<Resident>();
            ProvocationModule provocation = self != null ? self.Provocation : resident.GetComponent<ProvocationModule>();
            read.menacing = read.armed && provocation != null &&
                            provocation.HeardGunshotFrom(player, MenaceWindowSeconds);

            string profile = ResidentMemory.ProfileOf(player);
            if (self != null && self.Memory != null && profile != null)
                read.recentHits = self.Memory.HitsSeenSince(profile, Time.time - RecentHitSeconds);

            return read;
        }

        /// <summary>The single most important thing about this player, by fixed priority.</summary>
        public Observation Observe(bool kinHarmed)
        {
            if (kinHarmed) return Observation.KinHarmed;
            if (recentHits > 0) return Observation.Hitting;
            if (menacing) return Observation.Menacing;
            if (armed) return Observation.ArmedHeld;
            if (gauntlet) return Observation.Gauntlet;
            if (curio) return Observation.Curio;
            if (sprinting) return Observation.Sprinting;
            return Observation.Approaching;
        }
    }
}
