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

        /// <summary>The main room's id among the wheels.</summary>
        public const string MainWheelId = "Main";

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

        /// <summary>
        /// A door leaf, modelled standing open: it turns <c>shut</c> degrees about <c>axis</c> to stand shut. <c>door</c>
        /// names the gear door it belongs to: empty for the gate, "Vault" for the vault's.
        /// </summary>
        [Serializable] public sealed class Hinge { public string node; public float[] axis; public float shut; public string door; }

        /// <summary>
        /// The hoist's travel. The arrays run bridge, trolley, hook, lift; <c>hookLimits</c> is a row per bridge stop.
        /// <c>bridgeTravel</c> and <c>trolleyTravel</c> are how far either may run when driven by hand, least and most,
        /// in metres from where it is modelled (the round keeps to its stops).
        /// </summary>
        [Serializable]
        public sealed class Hoist
        {
            public string node; public float[] metresPerStop; public int[] stops; public int[] hookLimits;
            public float[] bridgeTravel; public float[] trolleyTravel;
        }

        /// <summary>A marker empty in the model, by name: where it is, and its own forward (Blender +Y) and up.</summary>
        [Serializable] public sealed class Mark { public string name; public float[] pos; public float[] fwd; public float[] up; }

        public Turntable[] turntables;
        public Spinner[] spinners;
        public Slider[] sliders;
        public Cable[] cables;
        public Hinge[] hinges;
        public Wheel[] wheels;
        public Tunnel[] tunnels;
        public Room[] rooms;
        public Lamp[] lamps;
        public Hoist hoist;
        public Mark[] marks;

        public static WorkshopLayout Parse(string json) => JsonUtility.FromJson<WorkshopLayout>(json);

        public static Vector3 V(float[] a) => new(a[0], a[1], a[2]);

        /// <summary>The marker named <paramref name="name"/>; a layout without it was exported from a model that lacks it.</summary>
        public Mark MarkNamed(string name)
        {
            if (marks != null)
                foreach (Mark mark in marks)
                    if (mark.name == name) return mark;
            throw new ArgumentException($"workshop_layout.json has no marker '{name}'. Is it in workshop.blend? Re-run workshop_export.py.");
        }

        /// <summary>The marker as a pose in the prefab's space: at its point, its forward and up as modelled.</summary>
        public Pose PoseOf(string name)
        {
            Mark mark = MarkNamed(name);
            return new Pose(V(mark.pos), Quaternion.LookRotation(V(mark.fwd), V(mark.up)));
        }

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
                "HoistLift" => WorkshopChannel.HoistLift,
                _ => throw new ArgumentException($"workshop_layout.json names a channel this build does not know: '{name}'"),
            };
        }

        /// <summary>The room a channel named "Room3" belongs to; -1 for any other channel.</summary>
        public static int RoomOf(string channel) =>
            channel.StartsWith(RoomChannel) && int.TryParse(channel.Substring(RoomChannel.Length), out int room) ? room : -1;
    }
}
