using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Items;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Keeps a scanner contact dark once it has been spent.
    ///
    /// <para>
    /// <see cref="ScanBeacon.Active"/> is how a cache, a wreck or a quest marker goes off the air
    /// after it has been looted — the object stays, the return stops. Without a record of it, every
    /// looted cache in the world lights up again on load and the scanner sends the player back to
    /// somewhere they have already been.
    /// </para>
    /// <para>
    /// Active is the authored default, so only the dark ones cost anything to store.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(ScanBeacon))]
    public class ScanBeaconSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "beacon";

        private ScanBeacon beacon;

        private ScanBeacon Beacon =>
            beacon != null ? beacon : beacon = GetComponent<ScanBeacon>();

        public string SaveKey => Key;

        public struct State
        {
            public bool active;
        }

        public object CaptureState()
        {
            if (Beacon == null) return null;

            return Beacon.Active ? null : new State { active = false };
        }

        public void RestoreState(JObject state)
        {
            if (Beacon == null) return;

            // No entry now means "at its default", which for a beacon is lit — so a beacon switched
            // off by a previous restore of the same object is switched back on rather than left.
            Beacon.Active = state == null || state.ToObject<State>(SaveSerializer.Serializer).active;
        }
    }
}
