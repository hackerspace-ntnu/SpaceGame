// workshop_layout.json, written by _Source~/models/buildings/workshop_export.py beside the FBX it describes.
// Everything in it is in Unity axes, in the prefab's own space (origin: the main room's pit-floor centre).
using System;
using UnityEngine;
using SpaceGame.World;

namespace SpaceGame.EditorTools
{
    [Serializable]
    public sealed class WorkshopLayout
    {
        private const string RoomChannel = "Room";

        [Serializable] public sealed class Turntable { public string node; }
        [Serializable] public sealed class Spinner { public string node; public string channel; public float[] axis; public float ratio; }
        [Serializable] public sealed class Slider { public string node; public string channel; public float[] dir; public float perUnit; }
        [Serializable] public sealed class Cable { public string node; public string channel; public float length; }
        [Serializable] public sealed class Room { public string name; public float[] pos; public float radius; }
        [Serializable] public sealed class Lamp { public float[] pos; }

        /// <summary>A tunnel a turning room can face, and how far the room turns (its own positive way) to face it.</summary>
        [Serializable] public sealed class Port { public string tunnel; public float degrees; }

        /// <summary>
        /// A turning room and the wheel in its middle: where the wheel is held, where its holder stands, the room's
        /// centre and inner radius, and the tunnels it can face. Its entrance faces the port at 0 degrees as modelled.
        /// </summary>
        [Serializable]
        public sealed class Wheel
        {
            public string id; public string turntable; public string channel;
            public float[] pos; public float[] stance; public float[] centre; public float radius; public Port[] ports;
        }

        /// <summary>One run of tunnel, forks and all, and the rooms with a mouth on it (by wheel id; "Final" is the engine hall).</summary>
        [Serializable] public sealed class Tunnel { public string id; public float[] pos; public string[] rooms; }

        /// <summary>The hoist's travel. The arrays run bridge, trolley, hook; <c>hookLimits</c> is a row per bridge stop.</summary>
        [Serializable] public sealed class Hoist { public string node; public float[] metresPerStop; public int[] stops; public int[] hookLimits; }

        public Turntable[] turntables;
        public Spinner[] spinners;
        public Slider[] sliders;
        public Cable[] cables;
        public Wheel[] wheels;
        public Tunnel[] tunnels;
        public Room[] rooms;
        public Lamp[] lamps;
        public Hoist hoist;

        public static WorkshopLayout Parse(string json) => JsonUtility.FromJson<WorkshopLayout>(json);

        public static Vector3 V(float[] a) => new(a[0], a[1], a[2]);

        /// <summary>
        /// The export's channel names. "Room3" is the fourth turning room. A name it does not know is a layout newer
        /// than this code.
        /// </summary>
        public static WorkshopChannel Channel(string name)
        {
            int room = RoomOf(name);
            if (room >= 0) return (WorkshopChannel)((int)WorkshopChannel.Room + room);

            return name switch
            {
                "Idle" => WorkshopChannel.Idle,
                "Crane" => WorkshopChannel.Crane,
                "Ambient" => WorkshopChannel.Ambient,
                "HoistBridge" => WorkshopChannel.HoistBridge,
                "HoistTrolley" => WorkshopChannel.HoistTrolley,
                _ => throw new ArgumentException($"workshop_layout.json names a channel this build does not know: '{name}'"),
            };
        }

        /// <summary>The room a channel named "Room3" belongs to; -1 for any other channel.</summary>
        public static int RoomOf(string channel) =>
            channel.StartsWith(RoomChannel) && int.TryParse(channel.Substring(RoomChannel.Length), out int room) ? room : -1;
    }
}
