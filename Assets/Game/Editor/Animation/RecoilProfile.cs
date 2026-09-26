using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// What the recoil clips are generated from: per recoil, which humanoid muscles kick, how far,
    /// and how fast they come back.
    ///
    /// <para>
    /// One asset, at <see cref="RecoilClipGenerator.ProfilePath"/>. Edit it, then run
    /// Tools/SpaceGame/Animation/Generate Recoil Clips; the clips and their actions are output and
    /// the next run writes over any hand edit to them.
    /// </para>
    /// <para>
    /// Every value is a DELTA in muscle units, not a pose: the clips play on the additive action
    /// layer, which adds them to whatever the body is already doing. A muscle unit is a fraction of
    /// that joint's range in the positive or negative direction (0.1 on Arm Down-Up is about ten
    /// degrees of lift). The sign conventions are Unity's: positive Down-Up is up, positive
    /// Front-Back is forward, positive Forearm Stretch straightens the elbow.
    /// </para>
    /// <para>
    /// A torso rock is spread over Spine, Chest and UpperChest rather than put on one of them:
    /// Chest and UpperChest are optional humanoid bones, and a muscle whose bone an avatar lacks
    /// moves nothing.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceGame/Animation/Recoil Profile", fileName = "RecoilProfile")]
    public sealed class RecoilProfile : ScriptableObject
    {
        /// <summary>One muscle's share of a kick.</summary>
        [Serializable]
        public struct MuscleKick
        {
            [Tooltip("A humanoid muscle, exactly as HumanTrait.MuscleName spells it " +
                     "(\"Right Hand Down-Up\", \"UpperChest Front-Back\"). The generator refuses a " +
                     "name Unity does not know.")]
            public string muscle;

            [Tooltip("How far it moves at the top of the kick, in muscle units, signed.")]
            public float peak;
        }

        /// <summary>One recoil: one clip and the action that plays it.</summary>
        [Serializable]
        public sealed class Recoil
        {
            [Tooltip("The action asset's name, and the clip's. Code and prefabs reference the action.")]
            public string actionName;

            [Tooltip("Seconds from the shot to the top of the kick. The kick eases out — it leaves " +
                     "fast and decelerates into the top — so this is how long the barrel climbs.")]
            [Min(0.01f)] public float kickSeconds = 0.05f;

            [Tooltip("Seconds from the shot until the body is back where it was. The clip's length.")]
            [Min(0.02f)] public float settleSeconds = 0.3f;

            [Tooltip("The action's blend in. Short: a kick that fades in has already happened.")]
            [Min(0f)] public float fadeIn = 0.02f;

            [Tooltip("The action's blend out. 0 lets the clip play to its end: it settles back to " +
                     "rest by itself, and a fade would only cut that settle short.")]
            [Min(0f)] public float fadeOut;

            public MuscleKick[] muscles = Array.Empty<MuscleKick>();
        }

        [Tooltip("Frame rate of the generated clips — what the Animation window steps through. The " +
                 "curves are continuous, so it changes nothing about how they play.")]
        [SerializeField, Min(1f)] private float frameRate = 60f;

        [SerializeField] private Recoil[] recoils = Array.Empty<Recoil>();

        public float FrameRate => frameRate;
        public IReadOnlyList<Recoil> Recoils => recoils;
    }
}
