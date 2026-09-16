// A player's reputation, saved with the player rather than with the world.
//
// PATH C, and that is the whole design decision. Goodwill is per (faction, PLAYER), so the natural
// home is the player-scoped record: it is captured by PlayerSaveService on every save and on
// disconnect, it is keyed by profile, and chunk streaming never touches it. A player's standing
// with the Sand Tribe therefore survives regardless of which chunks happened to be loaded, follows
// them into any world session they rejoin, and cannot be lost by their body being somewhere the
// world store was not looking.
//
// The rows live on FactionGoodwillLedger, which is one server-side object for the whole world. This
// saver is the seam: it copies this player's slice out on capture and pushes it back on restore.
//
// NOT deferred. The restore needs nothing that does not exist yet — no ground, no chunk, no other
// object — and it has to land BEFORE any agent can target this player, which binding already
// guarantees. A deferred restore would leave a returning outlaw briefly welcome.
//
// The restore also hands the war tier to WarPartyDirector (rosters spec §7), so escalation survives
// a quit during a party's cooldown. RestoreRow raises no event, so a restored AtWar band does not
// open a war here — the director reconciles it into one on its next decision.
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    public class FactionGoodwillSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "factionGoodwill";     // written into save files — NEVER rename

        public string SaveKey => Key;

        [Serializable]
        public struct Standing
        {
            /// <summary>The faction's asset GUID. The only stable name a save file can use.</summary>
            public string factionId;

            public float value;

            /// <summary>
            /// Saved rather than recomputed from <see cref="value"/>, because hysteresis makes the
            /// band a function of its own history: a player sitting inside the sticky part of
            /// HostileOnSight would come back Wary and be forgiven over a loading screen.
            /// </summary>
            public GoodwillBand band;

            /// <summary>
            /// How far this tribe's war with this player had escalated (rosters spec §7). Saved here,
            /// beside the band it belongs to, because between war parties it lives only in the
            /// director's memory — and a reload that reset it would send scouts after a player who had
            /// already beaten two parties. Appended 2026-09-16; older saves read 0.
            /// </summary>
            public int warTier;
        }

        public struct State
        {
            public List<Standing> standings;

            /// <summary>
            /// UTC ticks at capture. What makes an absence count (design §3.4): the elapsed real
            /// time is converted to in-game hours and decayed once on restore, so a player who
            /// leaves a tribe furious and comes back a week later is not still hunted.
            /// </summary>
            public long capturedUtcTicks;
        }

        public object CaptureState()
        {
            FactionGoodwillLedger ledger = FactionGoodwillLedger.Instance;
            if (ledger == null) return null;

            if (!TryGetProfile(out string profileId)) return null;

            WarPartyDirector director = WarPartyDirector.Instance;

            var standings = new List<Standing>();
            foreach ((FactionDefinition faction, string id, float value, GoodwillBand band) in ledger.All())
            {
                if (id != profileId || faction == null || string.IsNullOrEmpty(faction.ID)) continue;

                int warTier = director != null ? director.WarTierFor(faction, profileId) : 0;

                // A row sitting at neutral with no war says exactly what a missing row says. Skipping it
                // keeps a save from growing an entry per tribe per player who has never met either.
                if (Mathf.Approximately(value, GoodwillMath.Neutral) && band == GoodwillBand.Wary && warTier == 0)
                    continue;

                standings.Add(new Standing { factionId = faction.ID, value = value, band = band, warTier = warTier });
            }

            if (standings.Count == 0) return null;

            return new State
            {
                standings = standings,
                capturedUtcTicks = DateTime.UtcNow.Ticks,
            };
        }

        public void RestoreState(JObject state)
        {
            if (state == null) return;

            FactionGoodwillLedger ledger = FactionGoodwillLedger.Instance;
            if (ledger == null || !TryGetProfile(out string profileId)) return;

            // Through SaveSerializer.Serializer, always — it carries the Unity struct converters.
            State restored = state.ToObject<State>(SaveSerializer.Serializer);
            if (restored.standings == null) return;

            WarPartyDirector director = WarPartyDirector.Instance;

            foreach (Standing standing in restored.standings)
            {
                FactionDefinition faction = Registry<FactionDefinition>.Get(standing.factionId);

                // A faction that no longer exists, or whose asset has not loaded. Dropping the row
                // is right: there is nothing left to have an opinion.
                if (faction == null) continue;

                ledger.RestoreRow(faction, profileId, standing.value, standing.band);
                if (director != null) director.RestoreWarTier(faction, profileId, standing.warTier);
            }

            ApplyAbsence(ledger, restored.capturedUtcTicks);
        }

        /// <summary>
        /// Forgive the time the world was not running. Clamped at zero so a clock that has moved
        /// backwards — a machine whose time was corrected, a save copied between computers — cannot
        /// make a tribe angrier than it was.
        /// </summary>
        private static void ApplyAbsence(FactionGoodwillLedger ledger, long capturedUtcTicks)
        {
            if (capturedUtcTicks <= 0L) return;

            double seconds = (DateTime.UtcNow - new DateTime(capturedUtcTicks, DateTimeKind.Utc)).TotalSeconds;
            if (seconds > 0d) ledger.ApplyAbsence(seconds);
        }

        /// <summary>
        /// Which profile this body speaks for. Resolved through the same service that binds the
        /// player-scoped record this saver lives in, so the key can never disagree with its own file.
        /// </summary>
        private bool TryGetProfile(out string profileId)
        {
            profileId = null;

            PlayerSaveService players = SaveManager.Instance?.Players;
            return players != null && players.TryGetProfileFor(gameObject, out profileId);
        }
    }
}
