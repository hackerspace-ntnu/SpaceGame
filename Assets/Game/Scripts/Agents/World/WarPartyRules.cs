// The rules of a war party, pure: what each outcome is worth, when to give up, where a party may be
// moved while nobody is watching, and what counts as caught or defeated.
// Design: docs/superpowers/specs/2026-09-16-rosters-and-war-parties-design.md §5.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Agents
{
    /// <summary>How a war party ended. None: it did not end by any rule (its quarry left the game).</summary>
    public enum Reckoning
    {
        None,
        Caught,
        Defeated,
        Abandoned,
    }

    [Serializable]
    public struct WarPartySettings
    {
        [Tooltip("Seconds after a party resolves before the next one is raised.")]
        public float partyCooldown;

        [Tooltip("How far from the quarry to look for a camp to raise the party at.")]
        public float campSearchRadius;

        [Tooltip("Metres beyond the world sim's spawn radius a party is kept while it catches up unseen. " +
                 "Covers a player moving for one world-sim tick.")]
        public float stagingMargin;

        [Tooltip("With no usable camp, the party starts this far beyond the staging distance.")]
        public float fallbackExtra;

        [Tooltip("Further than this from its quarry, a party gives up. No goodwill, no tier change.")]
        public float maxPursuitDistance;

        [Tooltip("Seconds without contact before the party gets a fresh, fuzzy fix on its quarry.")]
        public float trailInterval;

        [Tooltip("How far from the quarry's real position a fix may be. Tracks, not telepathy.")]
        public float trailFuzz;

        [Tooltip("Goodwill returned when the party kills its quarry.")]
        public float caughtCredit;

        [Tooltip("Goodwill returned when the party is wiped out. The next party is a tier stronger.")]
        public float defeatedCredit;

        public static WarPartySettings Default => new WarPartySettings
        {
            partyCooldown = 60f,
            campSearchRadius = 1000f,
            stagingMargin = 30f,
            fallbackExtra = 100f,
            maxPursuitDistance = 1500f,
            trailInterval = 10f,
            trailFuzz = 30f,
            caughtCredit = 15f,
            defeatedCredit = 4f,
        };
    }

    public static class WarPartyRules
    {
        public static float CreditFor(Reckoning outcome, in WarPartySettings settings) => outcome switch
        {
            Reckoning.Caught   => Mathf.Abs(settings.caughtCredit),
            Reckoning.Defeated => Mathf.Abs(settings.defeatedCredit),
            _                  => 0f,
        };

        /// <summary>Beating a party makes the next one stronger, up to the roster's last tier.</summary>
        public static int NextTier(Reckoning outcome, int tier, int maxTier)
        {
            int cap = Mathf.Max(0, maxTier);
            int next = outcome == Reckoning.Defeated ? tier + 1 : tier;
            return Mathf.Clamp(next, 0, cap);
        }

        public static float StagingDistance(float spawnRadius, float margin) => spawnRadius + Mathf.Max(0f, margin);

        /// <summary>
        /// Too far from its quarry to go on. Never while <paramref name="inFlight"/>: a party still in its
        /// vessel set out from its home site, however far that is, and is caught up or flown in from there.
        /// </summary>
        public static bool ShouldAbandon(Vector3 party, Vector3 quarry, float maxPursuit, bool inFlight = false) =>
            !inFlight && FlatDistance(party, quarry) > maxPursuit;

        /// <summary>No player is within <paramref name="staging"/> of <paramref name="point"/>.</summary>
        public static bool IsUnobserved(Vector3 point, IReadOnlyList<Vector3> players, float staging)
        {
            for (int i = 0; i < players.Count; i++)
                if (FlatDistance(point, players[i]) < staging) return false;

            return true;
        }

        /// <summary>
        /// Move a folded party along its path to <paramref name="standoff"/> from its lead, if it is
        /// further than that and no player could see where it lands. The last stretch is always walked.
        /// </summary>
        public static bool TryCatchUp(Vector3 party, Vector3 lead, float standoff,
                                      IReadOnlyList<Vector3> players, float staging, out Vector3 moved)
        {
            moved = party;

            Vector3 fromLead = party - lead;
            fromLead.y = 0f;

            float distance = fromLead.magnitude;
            if (distance <= standoff) return false;

            Vector3 candidate = lead + fromLead / distance * standoff;
            candidate.y = party.y;

            if (!IsUnobserved(candidate, players, staging)) return false;

            moved = candidate;
            return true;
        }

        /// <summary>The quarry's real position, off by up to <paramref name="fuzz"/> on the flat.</summary>
        public static Vector3 TrailFix(Vector3 real, Vector2 disc, float fuzz)
        {
            Vector2 offset = Vector2.ClampMagnitude(disc, 1f) * Mathf.Max(0f, fuzz);
            return real + new Vector3(offset.x, 0f, offset.y);
        }

        public static Vector3 FallbackOrigin(Vector3 lastKnown, Vector2 direction, float distance)
        {
            Vector2 dir = direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector2.up;
            return lastKnown + new Vector3(dir.x, 0f, dir.y) * distance;
        }

        public static bool IsDefeated(int fightersSpawned, int fightersDead, bool wipedOut) =>
            wipedOut || (fightersSpawned > 0 && fightersDead >= fightersSpawned);

        /// <summary>
        /// Nothing of a spawned party is left in the field: no spawned member, and no fighter standing —
        /// a rider who dismounted is in neither list the mounts are, and still fights.
        /// </summary>
        public static bool IsWipedOut(int liveMembers, int standingFighters) =>
            liveMembers <= 0 && standingFighters <= 0;

        public static bool IsCaughtBy(string killerGroupId, string partyGroupId) =>
            !string.IsNullOrEmpty(partyGroupId) && killerGroupId == partyGroupId;

        private static float FlatDistance(Vector3 a, Vector3 b)
        {
            Vector3 d = a - b;
            d.y = 0f;
            return d.magnitude;
        }
    }
}
