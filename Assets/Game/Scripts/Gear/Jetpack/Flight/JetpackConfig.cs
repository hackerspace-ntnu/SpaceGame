using System;
using UnityEngine;

namespace SpaceGame.Gear.Jetpack
{
    /// <summary>
    /// Every number the jetpack flies on, in one serialized block so it can be tuned in the
    /// Inspector without a recompile.
    ///
    /// <para>
    /// Split into the three things that are actually separate decisions: how hard it pushes
    /// (<see cref="ThrustAcceleration"/> and the drags), how fast it can re-aim
    /// (<see cref="VectorRateDegreesPerSecond"/> and <see cref="MaxDeflectionDegrees"/>), and how
    /// long it lasts (the heat rates). Tuning one should not silently move the others, which is
    /// why the thrust is expressed as an ACCELERATION rather than a force: a force would couple
    /// the feel to the player prefab's mass, and that mass exists to make walking work.
    /// </para>
    /// <para>
    /// Gravity here is 18 m/s², not 9.81 — this world's own value. The jetpack integrates its own
    /// g rather than leaving Unity's on, for the reason the wingsuit does: one source of weight.
    /// </para>
    /// </summary>
    [Serializable]
    public class JetpackConfig
    {
        [Header("Thrust")]
        [Tooltip("How hard the motors push at full throttle, m/s². It must beat gravity by enough " +
                 "to climb briskly — at 18 m/s² of gravity, 30 leaves 12 m/s² of climb, which is " +
                 "about a second and a half to clear a two-storey building.")]
        [Min(0f)] public float ThrustAcceleration = 30f;

        [Tooltip("How hard the descent servo is allowed to push, as a MULTIPLE of gravity. It is " +
                 "bounded from BOTH sides and the window is narrow. Too low and letting go after " +
                 "a climb is a fall rather than a settle, and a relight after an overheat cannot " +
                 "arrest it either. Too high — above 1/cos(max deflection), about 1.31 at 40 " +
                 "degrees — and the servo holds its sink rate with the nozzles hard over, which " +
                 "turns coming down into free flight in any direction. 1.25 arrests 8 m/s in " +
                 "under two seconds and still drops faster at full rake.")]
        [Min(0f)] public float HoverAuthority = 1.25f;

        [Tooltip("How hard the descent servo pulls the vertical speed toward the sink rate, per " +
                 "second. This is what makes letting go settle instead of bobbing or diving: " +
                 "without it the servo cancels gravity exactly and whatever climb the pilot " +
                 "arrived with is kept forever.")]
        [Min(0f)] public float HoverDamping = 2.5f;

        [Tooltip("How fast the pack sinks with Space released, m/s. NOT a fall: the motors stay " +
                 "lit and hold this speed, so letting go is a controlled way down and landing " +
                 "from it is survivable. Free fall in this world reaches three times this in a " +
                 "second — that is what an overheat costs, and it is the difference the pilot is " +
                 "meant to feel between letting go and being cut off.")]
        [Min(0f)] public float DescentSpeed = 4f;

        [Tooltip("This world's gravity, m/s². The flight integrates its own so there is exactly " +
                 "one source of weight; leaving Unity's on as well doubles it.")]
        [Min(0f)] public float Gravity = 18f;

        [Header("Drag")]
        [Tooltip("Horizontal drag, per second. This is what gives the jetpack a terminal speed " +
                 "instead of accelerating forever: at full lateral thrust the speed settles near " +
                 "thrust/drag, so 0.9 against a raked ~19 m/s² of lateral thrust cruises near " +
                 "21 m/s. Raise it for a twitchier, more damped machine.")]
        [Min(0f)] public float HorizontalDrag = 0.9f;

        [Tooltip("Vertical drag, per second. Lower than the horizontal one on purpose: a fall " +
                 "should stay a fall. This is only here to stop a long cut from reaching a speed " +
                 "no landing can survive before the player has a chance to relight.")]
        [Min(0f)] public float VerticalDrag = 0.12f;

        [Header("Vectoring")]
        [Tooltip("How far the nozzles can swing off vertical, degrees. This is the ceiling on how " +
                 "much of the thrust can be aimed sideways, so it sets the top speed as surely as " +
                 "the drag does — at 40° a third of the push goes horizontal.")]
        [Range(0f, 80f)] public float MaxDeflectionDegrees = 40f;

        [Tooltip("How fast the nozzles swing toward where the input asked, degrees per second. " +
                 "THIS IS THE LEARNING CURVE. The thrust follows the nozzles, not the key, so a " +
                 "low rate means committing to a direction before you get it and flying arcs " +
                 "rather than corners. Raising it far turns the jetpack into a hover car.")]
        [Min(1f)] public float VectorRateDegreesPerSecond = 110f;

        [Tooltip("How much of the player's look pitch is folded into the nozzle command, 0..1. " +
                 "At 0.7, looking straight down while holding W rakes the nozzles to full " +
                 "deflection and the flight goes flat and fast; at 0 the keys are the only " +
                 "steering and where you look means nothing.")]
        [Range(0f, 1f)] public float LookPitchShare = 0.7f;

        [Tooltip("How much of the maximum deflection the keys alone ask for, level-headed, 0..1. " +
                 "The room left for the look to work in — at 1 the keys saturate the clamp on " +
                 "their own and looking about changes nothing. At 0.7 a level W rakes 28 of the " +
                 "40 degrees, looking down reaches all 40, and looking up backs off to about 8.")]
        [Range(0.1f, 1f)] public float NeutralDeflectionShare = 0.7f;

        [Tooltip("Degrees the two pods roll AWAY from each other per unit of sideways command, " +
                 "so strafing reads as the machine leaning into it rather than sliding. Cosmetic " +
                 "in the sense that it does not change the thrust — the sideways rake already " +
                 "did that — and load-bearing in the sense that without it nothing on screen " +
                 "says which way you asked to go.")]
        [Range(0f, 45f)] public float DifferentialRollDegrees = 18f;

        [Header("Launch")]
        [Tooltip("Upward speed the double tap kicks the player off with, m/s. The boost. It is a " +
                 "velocity rather than an impulse so it reads the same whether the player was " +
                 "standing still or already falling — a takeoff that depends on what you were " +
                 "doing beforehand is a takeoff nobody can aim.")]
        [Min(0f)] public float LaunchKick = 7f;

        [Tooltip("Fraction of the speed the player already had that carries into the flight. A " +
                 "run at the launch should be worth something.")]
        [Range(0f, 1f)] public float SpeedCarry = 1f;

        [Header("Heat")]
        [Tooltip("Heat per second at full throttle. Against a 100-point scale, 6.667 is fifteen " +
                 "seconds of held thrust from cold.")]
        [Min(0f)] public float ThrustHeatPerSecond = 100f / 15f;

        [Tooltip("Heat shed per second while sinking with Space released. This is the pilot's " +
                 "recovery, and the only one they can ask for — so it decides the rhythm: at 5 " +
                 "against thrust's 6.67, every second of climb is bought with about one and a " +
                 "third of coming down. Raise it and the pack is nearly always ready; lower it " +
                 "and a long flight is mostly descent.")]
        [Min(0f)] public float DescendCoolPerSecond = 5f;

        [Tooltip("Heat shed per second with the motors dead — an overheat, or a pack stowed on " +
                 "the back. Faster than the descent's, because nothing is burning. At 10 a full " +
                 "pack is cold again in ten seconds.")]
        [Min(0f)] public float CoolPerSecond = 10f;

        [Tooltip("Heat at which the motors cut, hard. The top of the scale.")]
        [Min(1f)] public float OverheatAt = 100f;

        [Tooltip("Heat the motors will relight at after an overheat. Low enough that an overheat " +
                 "is a real fall, high enough that there is something to fight for on the way " +
                 "down — at 10 heat/s of cooling this is eight seconds from the cut.")]
        [Min(0f)] public float RelightAt = 20f;

        [Tooltip("Fraction of full heat above which the nozzles glow and smoke. The warning, and " +
                 "it has to arrive with time left to act on it.")]
        [Range(0f, 1f)] public float WarnFraction = 0.7f;
    }
}
