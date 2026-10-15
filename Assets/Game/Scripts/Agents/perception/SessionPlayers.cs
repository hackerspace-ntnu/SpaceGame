// The player bodies the deciding machine reacts to: every spawned player in a session, and the one
// local player offline.
//
// PlayerIdentity registers itself in OnNetworkSpawn, so with no NetworkManager listening — a world
// played straight out of the editor — PlayerIdentity.All is empty. A sensor that loops over it
// alone notices nobody offline, with a clean console: residents never remarked on a sprint or a gun
// and never felt a shove, while talking to them (which is handed the player directly) still worked.
using System.Collections.Generic;
using SpaceGame.Core;
using SpaceGame.Presentation;
using UnityEngine;

namespace SpaceGame.Agents
{
    public static class SessionPlayers
    {
        /// <summary>Replaces the contents of <paramref name="into"/> with every player body this machine should react to.</summary>
        public static void Collect(List<Transform> into)
        {
            into.Clear();

            if (!Network.IsNetworked)
            {
                Transform local = GameplayMenuScope.LocalPlayerTransform;
                if (local != null) into.Add(local);
                return;
            }

            IReadOnlyList<PlayerIdentity> roster = PlayerIdentity.All;
            for (int i = 0; i < roster.Count; i++)
                if (roster[i] != null) into.Add(roster[i].transform);
        }
    }
}
