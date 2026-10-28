using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// How big each wild creature is in the game, and whether it takes a saddle.
    ///
    /// <para>
    /// One asset, at <see cref="WildCreatureBuilder.RosterPath"/>. The Blender side owns the shape
    /// and writes the manifest; this owns the game's half: the manifest's height is whatever the
    /// model was sculpted at, and the player is 3 m tall, so a creature sized for a 1.8 m person
    /// comes out knee-high. Edit a row, then run Tools/SpaceGame/Creatures/Rebuild Wild Creatures.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceGame/Creatures/Wild Creature Roster", fileName = "WildCreatureRoster")]
    public sealed class WildCreatureRoster : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            [Tooltip("The manifest's name (Art/Models/Creatures/Organic/<name>/<name>.creature.json).")]
            public string name;

            [Tooltip("Standing height in game metres, ground to the highest point of the rest pose. " +
                     "The FBX is imported scaled by height / the manifest's height. The player is 3 m.")]
            public float height;

            [Tooltip("Takes a saddle and can be ridden: keeps the Esko's mount and saddle stack.")]
            public bool rideable;

            [Tooltip("Back bone the saddle hangs off, by name. The saddle sits on the highest point " +
                     "of the skin that bone moves, so pick a bone mid-back, clear of neck and hips.")]
            public string saddleBone;
        }

        [SerializeField] private List<Entry> entries = new List<Entry>();

        public Entry Find(string creature) => entries.FirstOrDefault(e => e.name == creature);
    }
}
