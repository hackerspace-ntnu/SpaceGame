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
    /// </summary>
    public static class PlayerProfile
    {
        private const string PrefsKey = "SpaceGame.PlayerProfileId";

        private static string cachedId;

        /// <summary>This machine's profile id, created on first use.</summary>
        public static string LocalId
        {
            get
            {
                if (!string.IsNullOrEmpty(cachedId)) return cachedId;

                cachedId = PlayerPrefs.GetString(PrefsKey, string.Empty);

                if (string.IsNullOrEmpty(cachedId))
                {
                    cachedId = Guid.NewGuid().ToString("N");
                    PlayerPrefs.SetString(PrefsKey, cachedId);
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
