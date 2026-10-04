using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// One shape drawn on the front of an eyeball -- a pupil, an iris, or a catchlight.
    ///
    /// <para>
    /// It lives in the <b>eye plane</b>: the flat map you get by taking the angle out from the gaze
    /// as a distance and the angle around the gaze as a direction. Every number in it is therefore
    /// DEGREES OF EYEBALL, not pixels and not a UV radius -- so a shape keeps its proportions
    /// however the texture stretches, and a "25" means the same thing on a big eye and a small one.
    /// </para>
    ///
    /// <para>
    /// The outline is a superellipse, which is what lets one type cover a round pupil, a cat's
    /// slit, a goat's bar and a diamond without a branch for each. Its edge is where
    /// <c>|x/side|^n + |y/up|^n = 1</c>.
    /// </para>
    /// </summary>
    [Serializable]
    public struct EyeShape
    {
        [Tooltip("Half-height, in degrees out from the gaze. This is the size knob; a drifter's " +
                 "iris is 45 and its pupil 25. The lids show roughly the middle 50 degrees.")]
        [Range(0f, 90f)]
        public float Size;

        [Tooltip("Width divided by height. 1 is round. Below 1 is a tall slit (cat); above 1 is a " +
                 "wide bar (goat, octopus).")]
        [Range(0.02f, 6f)]
        public float Aspect;

        [Tooltip("How square or pointed the outline is. 2 is a true ellipse. Higher rounds toward " +
                 "a rectangle; 1 is a diamond; below 1 pinches into a star or a lens.")]
        [Range(0.3f, 10f)]
        public float Roundness;

        [Tooltip("Turns the shape, in degrees. Only visible once Aspect is not 1 or Roundness is " +
                 "not 2. 0 keeps the height axis pointing up.")]
        [Range(-180f, 180f)]
        public float Rotation;

        [Tooltip("Slides the shape up (+) or down (-) the eyeball, in degrees. Off-centre pupils " +
                 "read as a glance; large values push it under the lid.")]
        [Range(-90f, 90f)]
        public float OffsetUp;

        [Tooltip("Slides the shape sideways, in degrees. Both eyes share one texture, so this " +
                 "moves BOTH the same way.")]
        [Range(-90f, 90f)]
        public float OffsetSide;

        /// <summary>A plain round shape of a given half-angle -- the default every preset starts from.</summary>
        public static EyeShape Round(float size) => new EyeShape
        {
            Size = size,
            Aspect = 1f,
            Roundness = 2f,
            Rotation = 0f,
            OffsetUp = 0f,
            OffsetSide = 0f,
        };
    }

    /// <summary>
    /// A painted specular dot. Baked into the albedo, so it reads the same under any light and from
    /// any angle -- which is the point: a stylized eye's glint is a drawn feature, not a reflection.
    /// </summary>
    [Serializable]
    public struct EyeCatchlight
    {
        public Color Colour;

        [Tooltip("How strongly it covers what is underneath. 1 is opaque.")]
        [Range(0f, 1f)]
        public float Strength;

        [Tooltip("Where it sits and what shape it is. Use OffsetUp / OffsetSide to place it -- " +
                 "up and toward one side is the classic.")]
        public EyeShape Shape;
    }

    /// <summary>What part of the eye the emission map lights up.</summary>
    public enum EyeEmissionArea
    {
        /// <summary>The iris alone. The pupil stays a hole and the eyeball stays unlit.</summary>
        Iris,

        /// <summary>Iris and pupil together -- a lamp behind the whole aperture.</summary>
        IrisAndPupil,

        /// <summary>The entire ball, sclera included.</summary>
        WholeEye,
    }

    /// <summary>
    /// One eye look, as an asset you can duplicate and tune.
    ///
    /// <para>
    /// <b>Create ▸ SpaceGame ▸ Art ▸ Eye Style.</b> Every field is live in the Inspector with a
    /// preview of the eyeball above it; the Bake button writes the texture and the material.
    /// The colour is in the pixels once baked, so re-bake after changing anything.
    /// </para>
    ///
    /// <para>
    /// This is editor-only art data. Nothing loads it at runtime -- the game only ever sees the
    /// baked <c>.mat</c>, so an eye costs a texture fetch and nothing else.
    /// </para>
    /// </summary>
    [CreateAssetMenu(fileName = "NewEye", menuName = "SpaceGame/Art/Eye Style")]
    public sealed class EyeStyle : ScriptableObject
    {
        [Header("Eyeball")]
        [Tooltip("Everything outside the iris. Near-black reads as \"all iris\" and alien; an " +
                 "off-white reads as a human eye.")]
        public Color Sclera = new Color(0.07f, 0.05f, 0.04f, 1f);

        [Header("Iris")]
        [Tooltip("Iris colour at its centre -- the bright end of the gradient.")]
        public Color IrisInner = new Color(1f, 0.64f, 0.23f, 1f);

        [Tooltip("Iris colour at its outer edge. Darker than the inner colour is what the \"dark " +
                 "sides\" of a stylized eye actually are.")]
        public Color IrisOuter = new Color(0.77f, 0.28f, 0.04f, 1f);

        [Tooltip("The dark band ringing the iris. It follows the iris outline, so a squashed iris " +
                 "gets a squashed ring for free.")]
        public Color LimbalRing = new Color(0.08f, 0.04f, 0.04f, 1f);

        public EyeShape Iris = EyeShape.Round(45f);

        [Tooltip("How far the limbal ring reaches inwards from the iris edge, in degrees. 0 is no ring.")]
        [Range(0f, 40f)]
        public float LimbalWidth = 6f;

        [Header("Pupil")]
        public Color Pupil = new Color(0.03f, 0.02f, 0.04f, 1f);

        [Tooltip("Set Aspect below 1 for a cat's slit, above 1 for a goat's bar, and Roundness " +
                 "below 2 to point the ends.")]
        public EyeShape PupilShape = EyeShape.Round(25f);

        [Header("Catchlights")]
        [Tooltip("Painted glints, drawn in order -- a later one covers an earlier one. Empty is " +
                 "legal and gives a dead, matte eye.")]
        public List<EyeCatchlight> Catchlights = new List<EyeCatchlight>();

        [Header("Edges")]
        [Tooltip("How soft every edge is, in degrees. Under a degree is hard and graphic; ten " +
                 "degrees dissolves the shapes into each other.")]
        [Range(0f, 20f)]
        public float EdgeSoftness = 1.4f;

        [Header("Glow")]
        [Tooltip("Colour of the emission. Strength 0 writes no emission map at all and leaves the " +
                 "material unlit, which is what a human eye wants.")]
        public Color Emission = Color.black;

        [Tooltip("Keep this well under 1. An iris that is both a bright albedo and an HDR emitter " +
                 "clips to white and the colour is gone.")]
        [Range(0f, 3f)]
        public float EmissionStrength;

        public EyeEmissionArea EmissionArea = EyeEmissionArea.Iris;

        [Header("Material")]
        [Tooltip("Wetter than skin, but well short of a mirror -- the catchlight is already " +
                 "painted in, and a glossier ball lays a second one on top that washes the colour out.")]
        [Range(0f, 1f)]
        public float Smoothness = 0.55f;

        /// <summary>
        /// The two catchlights every built-in style wears: a big one up and to one side, and a
        /// smaller, dimmer spark opposite it.
        /// </summary>
        public static List<EyeCatchlight> DefaultCatchlights() => new List<EyeCatchlight>
        {
            new EyeCatchlight
            {
                Colour = Color.white,
                Strength = 1f,
                Shape = new EyeShape
                {
                    Size = 9f, Aspect = 1f, Roundness = 2f, Rotation = 0f,
                    OffsetUp = 14.2f, OffsetSide = -11.1f,
                },
            },
            new EyeCatchlight
            {
                Colour = Color.white,
                Strength = 0.6f,
                Shape = new EyeShape
                {
                    Size = 4.5f, Aspect = 1f, Roundness = 2f, Rotation = 0f,
                    OffsetUp = -22.1f, OffsetSide = 15.5f,
                },
            },
        };
    }
}
