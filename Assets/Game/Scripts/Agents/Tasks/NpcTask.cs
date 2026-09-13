// What an NPC is currently trying to get done, as data.
//
// A task names a KIND of place and how long to spend there. It never names a position and never
// touches movement — that is the whole design constraint. "Tracking a sand-rat", "picking over a
// wreck" and "running goods between trade posts" are the same three fields with different values
// in them, which is why adding a fifth kind of NPC business costs no code at all.
using System;
using UnityEngine;
using SpaceGame.Items;
using SpaceGame.World;

namespace SpaceGame.Agents
{
    [Serializable]
    public class NpcTask
    {
        [Tooltip("What the NPC would say it is doing, in the first person-ish: \"picking over the " +
                 "old wreck\". Shown in chatter and when the player asks in dialog, so write it to " +
                 "be read.")]
        public string label = "wandering";

        [Tooltip("The kind of place this task needs. Sites come from WorldSiteRegistry — if the " +
                 "world has none of this kind, the NPC falls back to roaming instead of standing still.")]
        public SiteKind targetSite = SiteKind.Landmark;

        [Tooltip("How far the NPC will travel to find a site of that kind. This is what makes a " +
                 "wanderer local and a salesman continental — 300 m keeps them near home, 2000 m " +
                 "sends them across the map.")]
        public float searchRadius = 600f;

        [Tooltip("Measure the search from the NPC's home site rather than from wherever it is " +
                 "standing. On for anything with a base to roam around; off for a traveller that " +
                 "should keep moving outward rather than yo-yo back past its own camp.")]
        public bool searchFromHome = true;

        [Tooltip("How long to spend working the site once there, in seconds (min, max). The NPC " +
                 "wanders inside the site for this long — travel yields the frame on arrival.")]
        public Vector2 dwellSeconds = new Vector2(20f, 60f);

        [Tooltip("Optional. Put into the NPC's own EntityInventoryComponent when the task finishes " +
                 "— which is how a scavenger accumulates the scrap it will later trade you.")]
        public InventoryItem yields;

        [Range(0f, 1f)]
        [Tooltip("Chance the yield actually turns up. Below 1 gives you NPCs who come back empty " +
                 "handed, which is what makes the ones who don't worth talking to.")]
        public float yieldChance = 1f;

        [TextArea(1, 3)]
        [Tooltip("Things to say while doing this, picked at random by ChatterModule. Leave empty " +
                 "and the NPC works in silence.")]
        public string[] chatter;

        [Tooltip("Optional. Animator BOOL held true for as long as the NPC is working this site, " +
                 "and cleared the moment it moves on — 'IsGrazing' for a feeding animal. " +
                 "A bool rather than a trigger because dwelling lasts tens of seconds: a one-shot " +
                 "would play once and leave the animal standing at attention for the rest of its " +
                 "meal. Left empty, the NPC works the site in its ordinary idle, which is right " +
                 "for anything whose job does not look like anything in particular.")]
        public string dwellFlag = "";

        [Tooltip("Relative chance of this task being picked next. 0 disables it without deleting it.")]
        public float weight = 1f;

        [Tooltip("How close counts as arrived when the destination is not a registered site. A real " +
                 "site uses its own radius instead.")]
        public float arriveRadius = 6f;

        [Tooltip("Locomotion speed while travelling for this task, multiplying whatever the motor's " +
                 "base speed is.")]
        public float travelSpeedMultiplier = 1f;

        /// <summary>
        /// A dwell duration for one visit. Sampled per arrival rather than stored, so two NPCs on
        /// the same task asset do not leave the same site in lockstep.
        /// </summary>
        public float RollDwell() =>
            UnityEngine.Random.Range(Mathf.Min(dwellSeconds.x, dwellSeconds.y),
                                     Mathf.Max(dwellSeconds.x, dwellSeconds.y));

        public bool RollYield() => yields != null && UnityEngine.Random.value <= yieldChance;

        public string RandomChatter() =>
            chatter != null && chatter.Length > 0
                ? chatter[UnityEngine.Random.Range(0, chatter.Length)]
                : null;
    }
}
