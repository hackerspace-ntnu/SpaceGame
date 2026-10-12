// GearDoorLayout.json (the workshop gate) and VaultDoorLayout.json (its vault): a gear door's puzzle, found once by a
// seeded search (tools/gear_door_layout.py) and checked by a solver.
//
// Door-plane coordinates: u along the shut door (the player's right facing it; prefab +z on the gate), v up from the
// floor, both in metres from the door's middle at floor level. A gate pile spot is `out` metres in front of the gate's
// face and `u` along it; a vault pile spot is `u` along the cog tray beside the door.
using System;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    [Serializable]
    public sealed class GearDoorLayout
    {
        public const string Path = "Assets/Game/Editor/Workshop/GearDoorLayout.json";
        public const string VaultPath = "Assets/Game/Editor/Workshop/VaultDoorLayout.json";

        [Serializable] public sealed class Fixed { public string kind; public int teeth; public float[] at; }
        [Serializable] public sealed class Placed { public int peg; public string kind; }
        [Serializable] public sealed class Loose { public string kind; public float @out; public float u; public float yaw; }

        public int seed;
        public float perTooth;
        public float addendum;
        public float clearance;
        public float tolerance;

        /// <summary>Each peg as (u, v).</summary>
        public float[][] pegs;

        public Fixed driver;
        public Fixed output;

        /// <summary>The one way the search found to carry the drive to the lock. Read by the tests, never by the game.</summary>
        public Placed[] solution;

        /// <summary>The chain gears already on their pegs at the start: part of the solution, not in the pile.</summary>
        public Placed[] preinstalled;

        public Loose[] pile;

        public static GearDoorLayout Load(string path = Path) =>
            Newtonsoft.Json.JsonConvert.DeserializeObject<GearDoorLayout>(System.IO.File.ReadAllText(path));

        public static Vector2 V(float[] a) => new(a[0], a[1]);
    }
}
