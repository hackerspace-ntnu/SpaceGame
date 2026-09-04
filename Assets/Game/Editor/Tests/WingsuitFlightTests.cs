// What the wingsuit is, expressed as the three things that would stop being true if somebody
// retuned it into an aircraft.
//
// The flight model itself is the ornithopter's and is pinned by OrnithopterFlightModelTests; there
// is no value in testing lift and drag a second time here. What IS worth pinning is the difference
// between the two machines — a wingsuit has no engine — and the two derived numbers the feel is
// actually described in, both of which fall out of tunables somebody will edit by eye.
//
// In Editor/ rather than beside the other EditMode tests because these reach Assembly-CSharp types.
using NUnit.Framework;
using SpaceGame.Gear.Wingsuit;
using SpaceGame.Vehicles.Ornithopter;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public class WingsuitFlightTests
    {
        private static WingsuitFlightConfig Config() => new WingsuitFlightConfig();

        [Test]
        public void TheSuitCanPutNoEnergyIntoItself()
        {
            // Both halves of "it cannot climb under its own power", because they fail differently:
            // a non-zero thrust here would let a held Space climb, and a positive flap input would
            // climb even with the thrust at zero.
            Assert.AreEqual(0f, Config().FlapThrust,
                "A wingsuit has no engine. FlapThrust must stay zero — see WingsuitFlightConfig.");

            OrnithopterFlightInput asked = WingsuitControl.Stick(
                noseStick: 1f, rollStick: 0f, rudderStick: 0f, tucking: false);
            Assert.LessOrEqual(asked.Flap, 0f, "The stick must never ask for a positive flap.");

            OrnithopterFlightInput tucked = WingsuitControl.Stick(
                noseStick: 0f, rollStick: 0f, rudderStick: 0f, tucking: true);
            Assert.Less(tucked.Flap, 0f, "Tucking is the one flap input the suit does have.");
        }

        [Test]
        public void ItGlidesAboutFourToOneAndStallsAroundEighteen()
        {
            WingsuitFlightConfig cfg = Config();

            // Both derived from the lift and drag curves rather than tuned, so an edit to mass,
            // area or the drag polar moves them without anybody noticing. These are the numbers
            // the feel is described in, so they are the ones worth a failing test.
            float glide = WingsuitFlightConfig.BestGlideRatio(cfg);
            Assert.That(glide, Is.InRange(3.4f, 4.6f),
                $"Best glide is {glide:F2}:1. Under three and the suit is a parachute; over five " +
                "and it is an aircraft. Retune DragCoefficientZeroLift / InducedDragFactor.");

            float stall = OrnithopterFlightModel.StallSpeed(cfg);
            Assert.That(stall, Is.InRange(15f, 21f),
                $"Stall is {stall:F1} m/s. Mass, WingArea and the lift curve all feed it.");
        }

        [Test]
        public void AGlideTradesHeightForDistanceAndNeverGainsAny()
        {
            WingsuitFlightConfig cfg = Config();

            // Deployed, at cruise, hands off. Over ten seconds this must go down — a wing with no
            // thrust that finds level flight has invented energy from somewhere.
            OrnithopterFlightState state = OrnithopterFlightState.Launch(24f, 0f, -14f);
            state.Deployment = 1f;
            state.WingSpread = 1f;
            state.Pitch = 0f;

            float height = 0f;
            for (int i = 0; i < 500; i++)
            {
                state = OrnithopterFlightModel.Step(state, OrnithopterFlightInput.Neutral, cfg, 0.02f);
                height += OrnithopterFlightModel.VelocityOf(state).y * 0.02f;
            }

            Assert.Less(height, 0f,
                $"Ten seconds of hands-off glide gained {height:F1} m. A wingsuit has no thrust: " +
                "the only way up is to spend speed, and a trim state cannot do it forever.");
        }
    }
}
