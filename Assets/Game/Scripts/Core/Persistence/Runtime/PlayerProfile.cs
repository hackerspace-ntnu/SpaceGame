using System;
using UnityEngine;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// The identity a save file uses for a player.
    ///
    /// Netcode client ids cannot serve: they are handed out per connection, so the host is 0 this
    /// session and a joining friend is 1, and next session those swap. Keying saved players by
    /// client id would hand a returning player someone else's inventory.
    ///
    /// This is a GUID generated once per machine and kept in PlayerPrefs. It is stable across
    /// sessions, unique between the people in a co-op game, and — being local-only — needs no
    /// account system behind it.
    ///
    /// <para>
    /// <b>Per-instance, not merely per-machine.</b> Every instance of the game on one machine shares
    /// a single PlayerPrefs file, so an unqualified key hands the SAME identity to two players at
    /// one PC, to every Multiplayer Play Mode virtual player, and to a build launched beside the
    /// editor. That is not a cosmetic clash: <c>PlayerSaveService</c> holds one live player per
    /// profile, so the second claimant overwrites the first, both restore the same position and
    /// inventory, and only one of them is ever captured back out.
    /// </para>
    ///
    /// <para>
    /// The key is therefore namespaced by the same instance name UGS signs in under — see
    /// <see cref="SessionLauncher.ResolveProfileName"/>, which exists because anonymous
    /// authentication caches its credential in this very PlayerPrefs file and had the identical
    /// collision. Reusing that resolver rather than inventing a second rule is the point: two
    /// answers to "which instance am I" could disagree, and the pair that disagreed would be a
    /// player whose save and whose lobby seat belong to different people.
    /// </para>
    ///
    /// <para>
    /// An ordinary launch resolves to null and keeps the bare key, so a real player's identity
    /// survives updating the game — and survives this change, which must not orphan the save of
    /// anyone already playing.
    /// </para>
    /// </summary>
    public static class PlayerProfile
    {
        private const string PrefsKey = "SpaceGame.PlayerProfileId";

        private static string cachedId;

        /// <summary>
        /// Where this instance's id is stored. The bare key for a normal launch; suffixed with the
        /// instance name for anything running beside another copy on the same machine.
        ///
        /// Public rather than internal so it can be tested: the tests live in
        /// Assembly-CSharp-Editor, which is a different assembly and cannot see internals. Pure, so
        /// the namespacing rule can be checked without touching PlayerPrefs at all.
        /// </summary>
        public static string PrefsKeyFor(string instanceName) =>
            string.IsNullOrEmpty(instanceName) ? PrefsKey : $"{PrefsKey}.{instanceName}";

        /// <summary>This machine's profile id, created on first use.</summary>
        public static string LocalId
        {
            get
            {
                if (!string.IsNullOrEmpty(cachedId)) return cachedId;

                string key = PrefsKeyFor(SessionLauncher.ResolveProfileName(
                    Environment.GetCommandLineArgs(), Application.dataPath));

                cachedId = PlayerPrefs.GetString(key, string.Empty);

                if (string.IsNullOrEmpty(cachedId))
                {
                    cachedId = Guid.NewGuid().ToString("N");
                    PlayerPrefs.SetString(key, cachedId);
                    PlayerPrefs.Save();
                }

                return cachedId;
            }
        }

        /// <summary>The name shown beside a save slot. Falls back to the device name.</summary>
        public static string DisplayName => SystemInfo.deviceName;

        /// <summary>Forgets the cached id so the next read re-reads PlayerPrefs. For tests.</summary>
        public static void InvalidateCache() => cachedId = null;
    }
}
