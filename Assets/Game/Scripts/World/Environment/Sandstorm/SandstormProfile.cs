// What KIND of storm this is. Everything tunable about a sandstorm lives here.
//
// One asset per kind — "Great Haboob", "Dust Squall", "The Wailing Wall" — and adding a new kind
// is duplicating an asset, not writing code. Nothing in this file knows about scenes, networking
// or rendering; the systems that do all read from here so that a designer changing a number in
// the inspector changes the storm's shape, its damage and its look together.
using UnityEngine;

namespace SpaceGame.World.Weather
{
    [CreateAssetMenu(menuName = "World/Sandstorm Profile", fileName = "SandstormProfile")]
    public class SandstormProfile : ScriptableObject
    {
        [Header("Shape")]
        public StormShapeKind shape = StormShapeKind.Cell;

        [Tooltip("Cell: core radius in metres. Wall: half the front's thickness.")]
        [Min(1f)] public float radius = 350f;

        [Tooltip("Wall only: half-width across the heading. Zero means the wall spans the map — " +
                 "which is the whole point of a haboob, so leave it at zero unless you want a " +
                 "front the player can flank.")]
        [Min(0f)] public float lateralExtent = 0f;

        [Tooltip("Metres over which the storm fades out at its horizontal edges. Generous values " +
                 "read as weather; small ones read as a wall of fog with a seam in it.")]
        [Min(0f)] public float edgeFeather = 150f;

        [Tooltip("World Y the storm's base sits at. Below it the sand is just as thick, so a " +
                 "canyon is no escape.")]
        public float baseHeight = 0f;

        [Tooltip("Metres of sand above the base. This is the number that makes a storm look huge; " +
                 "600-1500 for something that towers over the dunes.")]
        [Min(10f)] public float height = 900f;

        [Tooltip("Metres of fade at the top, so the storm dissolves into the sky instead of " +
                 "ending in a straight line. Roughly a third of the height works well.")]
        [Min(0f)] public float heightFeather = 350f;

        [Header("Motion")]
        [Tooltip("Metres per second the storm travels along its heading. Zero parks it. " +
                 "10 m/s crosses the 4 km map in about seven minutes.")]
        [Min(0f)] public float travelSpeed = 11f;

        [Tooltip("How far the storm wanders sideways off its heading, in metres. Keeps a front " +
                 "from tracking like a train on rails.")]
        [Min(0f)] public float wanderAmplitude = 70f;

        [Tooltip("Seconds for roughly one sideways wander.")]
        [Min(1f)] public float wanderPeriod = 45f;

        [Header("Lifetime")]
        [Tooltip("Seconds from arrival to nothing. Zero means the storm never expires — that is " +
                 "how a permanently parked hazard region is authored.")]
        [Min(0f)] public float duration = 300f;

        [Tooltip("Intensity across the storm's life, sampled 0..1 over the duration. The ramp in " +
                 "is the player's warning and the ramp out is their relief; both want to be slow.")]
        public AnimationCurve intensityOverLife = new AnimationCurve(
            new Keyframe(0f, 0f), new Keyframe(0.2f, 1f), new Keyframe(0.8f, 1f), new Keyframe(1f, 0f));

        [Tooltip("Intensity held by a storm with no duration — a parked hazard region.")]
        [Range(0f, 1f)] public float steadyIntensity = 1f;

        [Tooltip("How much intensity breathes up and down as the storm sits over you. A little " +
                 "goes a long way: it is what stops the fog looking like a static filter.")]
        [Range(0f, 0.5f)] public float gustAmplitude = 0.12f;

        [Tooltip("Seconds for roughly one gust.")]
        [Min(0.5f)] public float gustPeriod = 8f;

        [Header("Gameplay")]
        [Tooltip("Health per second at full exposure. Shelter and protective gear scale this " +
                 "down; at zero the storm is pure spectacle.")]
        [Min(0f)] public float damagePerSecond = 7f;

        [Tooltip("How far you can see, in metres, at full intensity. Single digits mean genuinely " +
                 "blind; 20-40 means unpleasant but navigable.")]
        [Min(1f)] public float visibilityAtFullIntensity = 9f;

        [Tooltip("What an agent's acquisition range is multiplied by at full intensity. 0.15 means " +
                 "a robot that normally spots you at 35 m only manages 5 m.")]
        [Range(0.02f, 1f)] public float aiSightFactorAtFullIntensity = 0.15f;

        [Header("Look — the sand itself")]
        [Tooltip("How hard the noise carves into the storm's edges. Low values give a smooth " +
                 "falloff that reads as fog; high values give the torn, billowing edge that reads " +
                 "as a body of dust. This is the single most important number for the silhouette.")]
        [Range(0f, 0.95f)] public float erosion = 0.6f;

        [Tooltip("How much light scatters forward through the sand. High values make a storm glow " +
                 "when it is between you and the sun and stay dull when it is not — which is most " +
                 "of what tells the eye this is dust and not a painted wall.")]
        [Range(0f, 0.95f)] public float forwardScatter = 0.65f;

        [Tooltip("Master brightness of the sand, as a multiple of full daylight. At 1 the storm " +
                 "renders at roughly the colour swatches below with the sun high, and dims " +
                 "toward night on its own. This is THE dial for 'the inside of the storm is too " +
                 "dark': in there the sun march is fully occluded, so this is the only light " +
                 "there is. It moves the silhouette as well.")]
        [Range(0f, 2f)] public float ambient = 0.9f;

        [Tooltip("How far the wind draws the billows out sideways. 1 is still air and looks like " +
                 "smoke; 3 or so gives the horizontal banding that reads as sand being driven.")]
        [Range(1f, 6f)] public float windStretch = 3f;

        [Header("Look — standing inside it")]
        [Tooltip("Metres around your head that stay clear of fog. This is what lets you keep the " +
                 "ground under your boots and the thing you are standing next to. Without it a " +
                 "raymarch that starts at the camera fogs your own feet.")]
        [Range(0f, 20f)] public float interiorClearRadius = 5f;

        [Tooltip("The most the interior fog may ever hide, 0 to 1. Deliberately NOT 1: a storm " +
                 "that reaches full opacity is a blank screen, and a blank screen is not " +
                 "'blinded', it is 'broken'. 0.85 leaves you shapes and the ground.")]
        [Range(0.2f, 1f)] public float maxFogOpacity = 0.85f;

        [Header("Look — interior fog")]
        [Tooltip("Colour the air takes on inside the storm. This is also what darkens the world, " +
                 "so a dim ochre reads as a serious storm and a pale tan as a dusty afternoon.")]
        [ColorUsage(false, true)] public Color fogColor = new Color(0.62f, 0.44f, 0.26f);

        [Tooltip("Extra fog thickness beyond what visibility already implies. Leave at 1 unless " +
                 "the storm needs to feel heavier than it is dangerous.")]
        [Range(0.25f, 4f)] public float fogDensityScale = 1f;

        [Tooltip("Size of the sand billows inside the storm, in metres. Large values look like " +
                 "weather rolling past; small ones like television static.")]
        [Min(1f)] public float noiseScale = 90f;

        [Tooltip("Metres per second the interior billows drift.")]
        [Min(0f)] public float noiseSpeed = 14f;

        [Tooltip("Screen-space warping, as a fraction of the screen. Tiny numbers only — this is " +
                 "the heat-haze of grit moving in front of your eyes, not a drunk filter.")]
        [Range(0f, 0.02f)] public float distortionStrength = 0.006f;

        [Tooltip("Grit scrolling across the view. Sells 'sand in the air' more cheaply than any " +
                 "particle system can.")]
        [Range(0f, 1f)] public float grainStrength = 0.35f;

        [Header("Look — the wall seen from outside")]
        [Tooltip("Colour of the storm's silhouette against the sky. Usually brighter and more " +
                 "saturated than the interior fog — a storm looks worse from outside than in.")]
        [ColorUsage(false, true)] public Color wallColor = new Color(0.76f, 0.55f, 0.33f);

        [Tooltip("How opaque the silhouette gets at its densest.")]
        [Range(0f, 1f)] public float wallOpacity = 1f;

        [Tooltip("How much sand a metre of the silhouette blocks. This is a LOOK value, not the " +
                 "gameplay visibility above: at 0.014 you can still make out structure through the " +
                 "thin top of the storm from a kilometre away, which is what stops it reading as " +
                 "a solid cutout. Raise it for a heavier, more opaque front.")]
        [Range(0.001f, 0.1f)] public float wallExtinction = 0.014f;

        [Tooltip("How fast the wall billows upward, in metres per second. Slow: a storm this big " +
                 "should look like it is moving in slow motion from a distance.")]
        [Min(0f)] public float wallBillowSpeed = 22f;

        [Tooltip("Size of the billows on the silhouette, in metres.")]
        [Min(1f)] public float wallNoiseScale = 220f;

        [Tooltip("How wide the silhouette is drawn, in metres either side of centre, when the " +
                 "wall is unbounded. Only affects what is drawn — an unbounded wall still blows " +
                 "everywhere. Wide enough to reach past the horizon and you will never see an end.")]
        [Min(100f)] public float wallDrawHalfWidth = 2600f;

        [Header("Audio")]
        [Tooltip("Looping storm roar. Left empty, the storm is silent.")]
        public AudioClip loop;

        [Tooltip("Volume at full intensity out in the open.")]
        [Range(0f, 1f)] public float maxVolume = 0.9f;

        [Tooltip("Volume multiplier when fully sheltered — inside the ship with the door shut you " +
                 "should still hear it, muffled, hammering on the hull.")]
        [Range(0f, 1f)] public float shelteredVolume = 0.3f;

        [Tooltip("Low-pass cutoff in Hz when fully sheltered. This is most of what makes shutting " +
                 "the door feel like safety.")]
        [Range(200f, 22000f)] public float shelteredCutoff = 750f;

        [Header("Near detail")]
        [Tooltip("Optional prefab spawned at the camera while you are in the storm — VFX Graph or " +
                 "particle streaks. SandstormVfx drives its intensity; see that component.")]
        public GameObject nearDetailPrefab;

        [Tooltip("Intensity below which the near detail is not spawned at all. The cheapest " +
                 "particle is the one that never exists.")]
        [Range(0f, 1f)] public float nearDetailThreshold = 0.15f;

        /// <summary>
        /// The storm's geometry at a given centre and heading. The one place a profile turns into
        /// a footprint, so damage, audio and both render layers cannot drift apart.
        /// </summary>
        public StormFootprint Footprint(Vector2 center, Vector2 heading) => new StormFootprint
        {
            Kind = shape,
            Center = center,
            Heading = heading,
            Radius = radius,
            LateralExtent = shape == StormShapeKind.Wall ? lateralExtent : 0f,
            EdgeFeather = edgeFeather,
            BaseY = baseHeight,
            Height = height,
            HeightFeather = heightFeather,
        };

        private void OnValidate()
        {
            heightFeather = Mathf.Min(heightFeather, height);
        }
    }
}
