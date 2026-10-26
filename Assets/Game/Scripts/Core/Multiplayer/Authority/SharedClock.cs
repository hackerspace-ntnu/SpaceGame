using Unity.Netcode;
using UnityEngine;

namespace SpaceGame.Core
{
    /// <summary>
    /// A clock every machine in the session reads the same: the server's time while networked, local
    /// time otherwise. For presentation that must happen together everywhere without a message — an
    /// idle fidget, the witch's pulse.
    /// </summary>
    public static class SharedClock
    {
        public static double Now
        {
            get
            {
                NetworkManager network = NetworkManager.Singleton;
                return network != null && network.IsListening ? network.ServerTime.Time : Time.timeAsDouble;
            }
        }
    }
}
