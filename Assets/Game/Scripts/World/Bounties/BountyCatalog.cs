// Every bounty hunt in the game, in one order every machine agrees on: loaded from Resources/Bounties/ and sorted by
// id. The wire names a hunt by its place in that order (BountyProgress, ClosedHunt), saves by its id.
using System;
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.World.Posters;

namespace SpaceGame.World.Bounties
{
    public static class BountyCatalog
    {
        public const string ResourcesFolder = "Bounties";

        private static BountyHunt[] all;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => all = null;

        public static IReadOnlyList<BountyHunt> All => all ??= Load();

        public static BountyHunt At(int index) => index >= 0 && index < All.Count ? All[index] : null;

        public static int IndexOf(BountyHunt bounty) => bounty == null ? -1 : Array.IndexOf(all ??= Load(), bounty);

        public static BountyHunt ById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (BountyHunt bounty in All)
                if (bounty.Id == id) return bounty;
            return null;
        }

        /// <summary>
        /// The bounty a hung poster offers: the one whose poster prints the same target under the same name. A placed
        /// poster cannot know which prefab it came from, but two sheets naming the same outlaw are the same bounty.
        /// </summary>
        public static BountyHunt For(WantedPoster poster)
        {
            if (poster == null || poster.Agent == null) return null;
            foreach (BountyHunt bounty in All)
                if (bounty.Poster != null && bounty.Target == poster.Agent && bounty.TargetName == poster.DisplayName)
                    return bounty;
            return null;
        }

        private static BountyHunt[] Load()
        {
            BountyHunt[] loaded = Resources.LoadAll<BountyHunt>(ResourcesFolder);
            Array.Sort(loaded, (a, b) => string.CompareOrdinal(a.Id, b.Id));
            return loaded;
        }
    }
}
