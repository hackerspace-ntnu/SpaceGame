using Unity.Netcode;
using UnityEngine;
using SpaceGame.Core;

namespace SpaceGame.World
{
    /// <summary>
    /// Keeps every moving part of a workshop where it belongs: turns each cog, shaft and gear at the rate the room
    /// or motor it belongs to demands, and runs the hoist round the main room by itself. Runs on every machine and
    /// decides nothing: each <see cref="WorkshopWheel"/> decides its own room, and the hoist is a place for every
    /// second on a clock all machines share (<see cref="WorkshopHoistRound"/>).
    ///
    /// <para>
    /// A room's angle is read straight off its <see cref="WorkshopTurntable"/> every frame, so its hand cog and the
    /// pinion that drives it turn exactly as far as the room they drive, whoever is turning it.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WorkshopMachinery : MonoBehaviour
    {
        [Header("Rooms")]
        [Tooltip("Every turning room, in the layout's order: room i drives channel WorkshopChannel.Room + i.")]
        [SerializeField] private WorkshopTurntable[] rooms = System.Array.Empty<WorkshopTurntable>();

        [Header("Gears")]
        [SerializeField] private WorkshopGearTrain gearTrain;
        [Tooltip("Units a second the gear wall's channel runs at. Its motor never stops.")]
        [SerializeField, Min(0f)] private float idlePerSecond = 5f;

        [Header("Hoist")]
        [SerializeField] private WorkshopHoistRound hoistRound = new();

        private float[] values;

        private float[] Values => values ??= new float[(int)WorkshopChannel.Room + rooms.Length];

        /// <summary>
        /// The clock the hoist keeps. The server's in a networked game, so every machine has the hoist in the same
        /// place; this machine's own otherwise.
        /// </summary>
        private static double Clock =>
            Network.IsNetworked && NetworkManager.Singleton != null ? NetworkManager.Singleton.ServerTime.Time : Time.timeAsDouble;

        private void Update()
        {
            float dt = Time.deltaTime;

            for (int room = 0; room < rooms.Length; room++)
                if (rooms[room] != null) Values[(int)WorkshopChannel.Room + room] = rooms[room].Angle;

            Set(WorkshopChannel.Idle, Get(WorkshopChannel.Idle) + (idlePerSecond * dt));
            Set(WorkshopChannel.Ambient, Get(WorkshopChannel.Ambient) + dt);

            Vector3 hoist = hoistRound.At(Clock);
            Set(WorkshopChannel.HoistBridge, hoist.x);
            Set(WorkshopChannel.HoistTrolley, hoist.y);
            Set(WorkshopChannel.Crane, hoist.z);

            if (gearTrain != null) gearTrain.Apply(Values);
        }

        private float Get(WorkshopChannel channel) => Values[(int)channel];

        private void Set(WorkshopChannel channel, float value) => Values[(int)channel] = value;
    }
}
