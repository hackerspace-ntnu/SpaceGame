using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Presentation;
using SpaceGame.Vehicles;
using SpaceGame.World;
using SpaceGame.World.Weather;

namespace SpaceGame.Gameplay
{
    /// <summary>
    /// Reads the ship the terminal stands in and hands the screen a <see cref="TelemetrySnapshot"/>
    /// a few times a second.
    ///
    /// <para>
    /// Everything read here is already replicated — the oxygen plant's state and the part rack's
    /// mask are NetworkVariables, the crew roster is <see cref="PlayerIdentity"/>, the storm and
    /// the clock run off the shared session clock — so host and clients compose the same screen
    /// from the same numbers with no message of this system's own.
    /// </para>
    /// <para>
    /// The ship is whatever this fixture is nested under. Standing on its own in a chunk it
    /// reports a hull with nothing fitted, which is honest.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ShipTelemetrySource : MonoBehaviour
    {
        [SerializeField] private TerminalScreen screen;

        [Tooltip("Seconds between readings. A terminal is read, not watched; five a second is plenty.")]
        [SerializeField, Min(0.05f)] private float refreshSeconds = 0.2f;

        private Transform ship;
        private OxygenGenerator oxygen;
        private ShipPartRack rack;
        private float nextRefresh;

        /// <summary>
        /// What kind of module each socket takes. Read once and handed out by reference in every
        /// snapshot: a rack's sockets are fixed by the prefab and cannot change while the ship
        /// exists, and rebuilding this array five times a second would be five allocations a
        /// second for a fact that never moves.
        /// </summary>
        private ShipPartKind[] partKinds = System.Array.Empty<ShipPartKind>();

        private readonly List<string> names = new();
        private readonly List<Vector2> offsets = new();

        private void Start()
        {
            ship = transform.root;
            oxygen = ship.GetComponentInChildren<OxygenGenerator>(true);
            rack = ship.GetComponentInChildren<ShipPartRack>(true);

            if (rack == null) return;

            IReadOnlyList<ShipPartSocket> sockets = rack.Sockets;
            partKinds = new ShipPartKind[sockets.Count];
            for (int i = 0; i < sockets.Count; i++)
                if (sockets[i] != null) partKinds[i] = sockets[i].Kind;
        }

        private void Update()
        {
            if (screen == null || Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + refreshSeconds;
            screen.Present(Read());
        }

        /// <summary>One reading of the ship, now.</summary>
        public TelemetrySnapshot Read()
        {
            TelemetrySnapshot s = TelemetrySnapshot.Empty;

            s.PartKinds = partKinds;
            if (rack != null) s.PartsInstalledMask = rack.InstalledMask;

            if (oxygen != null)
            {
                s.OxygenPresent = true;
                s.OxygenPowered = oxygen.Powered;
                s.OxygenBattery01 = oxygen.HasBattery ? oxygen.BatteryCharge : -1f;
                s.OxygenTank01 = oxygen.HasTank ? oxygen.TankCharge : -1f;
                s.OxygenFilling = oxygen.IsFilling;
            }

            ReadCrew();
            s.CrewNames = names.ToArray();
            s.CrewOffsets = offsets.ToArray();

            s.Storm01 = Mathf.Clamp01(Sandstorms.IntensityAt(ship.position));
            s.TimeOfDay01 = DayNightCycle.Live.Count > 0 ? DayNightCycle.Live[0].TimeOfDay : 0f;
            s.Position = ship.position;
            s.HeadingDegrees = ship.eulerAngles.y;
            return s;
        }

        /// <summary>
        /// Who is in the session and where they stand relative to the hull, x across and y
        /// forward. Rotation only, never <c>InverseTransformPoint</c>: a scaled hull would scale
        /// the metres. Offline there are no identities; the one player there is gets a line.
        /// </summary>
        private void ReadCrew()
        {
            names.Clear();
            offsets.Clear();

            Quaternion toShip = Quaternion.Inverse(ship.rotation);

            foreach (PlayerIdentity identity in PlayerIdentity.All)
            {
                if (identity == null) continue;
                names.Add(identity.DisplayName);
                Vector3 local = toShip * (identity.transform.position - ship.position);
                offsets.Add(new Vector2(local.x, local.z));
            }

            if (names.Count > 0) return;

            Transform local1 = GameplayMenuScope.LocalPlayerTransform;
            if (local1 == null) return;
            names.Add("YOU");
            Vector3 solo = toShip * (local1.position - ship.position);
            offsets.Add(new Vector2(solo.x, solo.z));
        }
    }
}
