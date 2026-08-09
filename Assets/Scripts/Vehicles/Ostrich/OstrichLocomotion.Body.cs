// Where the BODY goes: along its path, up and down over the ground, leaning on the loaded foot,
// and down under gravity when nothing is holding it.
//
// NOTHING THAT MOVES AT STRIDE FREQUENCY IS FILTERED. A first-order smooth costs both amplitude
// and phase at the frequency it is asked to pass, so a bob or a lean routed through one arrives
// late and shallow, and how late depends on speed -- which is most of what "the legs lag behind"
// turned out to mean. `heightSmooth` is kept, but only for the GROUND, which genuinely is noise: a
// foot onto a rock, a ray catching an edge. The gait's own motion is added on top of it, unfiltered
// and exactly in phase with the footfalls it belongs to.
//
// The lean gets there a different way. Counting planted feet gives a square wave that has to be
// filtered; weighting them by LOAD gives a curve that does not.
using SpaceGame.Walker;
using UnityEngine;

public partial class OstrichLocomotion
{
    /// The bird's position along its path, with no bob or lean in it. Those are display offsets
    /// applied on top each frame; folding them back into the position they were computed from
    /// would let the lean integrate and walk the bird sideways off its own course.
    private Vector3 pathPos;
    private float currentYaw;
    private Vector3 lastBodyPos;
    private Vector3 velocity;

    private float smoothedHeight;
    private float smoothedPitch;
    private float smoothedRoll;
    /// The lean, held rather than filtered: it is read straight off the load, and only carried over
    /// through the moments -- a run's flight phase -- when there is no load to read it from.
    private float swayHold;
    private bool heightPrimed;
    private float fallVelocity;

    /// How far above its resting height the body has to be before gravity claims it.
    private float FallThreshold => legReach * fallThresholdFraction;

    /// Puts the body's own state back where a freshly measured rig expects it. Called at the end of
    /// Initialise, so the rig file does not have to reach in and set half a dozen fields it does
    /// not own.
    private void ResetBodyState()
    {
        currentYaw = body.eulerAngles.y;
        pathPos = body.position;
        lastBodyPos = body.position;
        smoothedHeight = body.position.y;
        heightPrimed = false;
    }

    /// Drop the bird so the legs start within reach of the ground.
    public void SnapToGround()
    {
        if (!ready) return;
        // A deliberately longer ray than the per-frame ones: this is called on a bird that may have
        // been dropped into the scene anywhere above its ground.
        if (!ground.Ray(body.position + Vector3.up * rayStartAbove * 4f, rayLength * 4f,
                        out RaycastHit hit)) return;

        body.position += Vector3.up * ((hit.point.y + rideHeight) - body.position.y);
        pathPos = body.position;
        smoothedHeight = body.position.y;
        heightPrimed = true;
        fallVelocity = 0f;
        IsFalling = false;

        GroundFeet();
        lastBodyPos = body.position;
    }

    /// Carry the path forward under the commanded twist, and advance the gait clock by the distance
    /// covered rather than by the time taken.
    private void MoveBody(float dt)
    {
        currentYaw += commandedYawRate * dt;

        Vector3 forward = Quaternion.AngleAxis(currentYaw, Vector3.up) * Vector3.forward;
        Vector3 moved = forward * (commandedSpeed * dt);
        pathPos += moved;

        gait.Advance(Pace * dt, WalkerGait.CycleDistance(strideLength, CurrentDuty));

        diagnostics.AchievedSpeed = dt > 0f ? moved.magnitude / dt * Mathf.Sign(commandedSpeed) : 0f;
    }

    /// Height, bob, lean and attitude. The station holds its deck level here; this does the
    /// opposite, and deliberately.
    private void PoseBody(float dt)
    {
        // Height comes from the GROUND under the body, with the planted feet only able to hold it
        // up (a step onto a rock), never to pull it along.
        //
        // Deriving it from the feet alone is a feedback loop with no damping: a lifted foot raises
        // the body, the raised body places the next foothold higher, and the bird climbs into the
        // sky. Asked for half speed it reached six metres before this was pinned to the ground.
        bool hasGround = ground.Below(pathPos, out RaycastHit under);
        float groundY = hasGround ? under.point.y : pathPos.y - rideHeight;

        float support = SurveyFeet(groundY, out int carrying, out float load, out float leanX);

        // Twice the stride frequency, lowest just as a foot takes the load.
        // Sized against LEG REACH, not ride height: this model's origin sits at foot level, so its
        // ride height is ~0 and a bob scaled by it would not exist.
        float bob = -Mathf.Cos(2f * Mathf.PI * 2f * (gait.Phase - CurrentDuty))
                    * bobAmount * legReach * Effort;

        float restingHeight = groundY + rideHeight;
        if (UpdateFall(dt, hasGround, carrying, restingHeight)) bob = 0f;
        else SettleOnto(dt, support + rideHeight);

        // Lean over whichever foot is carrying the weight, in proportion to how much of it that
        // foot has. Taken from where the feet actually are rather than from a hand-signed sine, so
        // it cannot lean the wrong way if the rig's left and right come in swapped -- and taken
        // from the LOAD rather than from a headcount, which is what lets it be used as it stands.
        //
        // With one foot loaded the weighted mean is that foot's offset whatever its share, so the
        // lean holds steady as the last foot unloads, and simply stays where it was through a run's
        // flight phase, where there is no load to read at all.
        if (load > 1e-4f) swayHold = leanX / load * swayAmount * Effort;

        float targetPitch = -runPitch * RunBlend;
        float targetRoll = turnRoll * Mathf.Clamp(commandedYawRate / Mathf.Max(maxYawRate, 1e-4f), -1f, 1f);
        smoothedPitch = Mathf.Lerp(smoothedPitch, targetPitch, 1f - Mathf.Exp(-attitudeSmooth * dt));
        smoothedRoll = Mathf.Lerp(smoothedRoll, targetRoll, 1f - Mathf.Exp(-attitudeSmooth * dt));

        Quaternion yaw = Quaternion.AngleAxis(currentYaw, Vector3.up);
        body.rotation = yaw * Quaternion.Euler(smoothedPitch, 0f, -smoothedRoll);

        // The path carries the height; bob and lean are added on top of it and never written back,
        // so they stay a gait rather than becoming a drift. Keeping the bob out of pathPos is also
        // what keeps it out of the fall test, which reads pathPos.y.
        pathPos.y = smoothedHeight;
        body.position = pathPos + Vector3.up * bob + yaw * Vector3.right * swayHold;
    }

    /// What the feet are doing for the body this frame: how high they hold it, how many are on the
    /// ground at all, and where their load is centred across the body.
    ///
    /// A foot only holds the bird up if it is on something. A stranded foot -- one hanging in the
    /// air where the rest pose left it, because there was no ground to place it on -- used to carry
    /// the body just as well as one standing on rock, and that is what left the bird parked in the
    /// sky: the feet held it up, and the gait only ever re-places a foot while the bird is moving,
    /// so a parked one could never come down on its own.
    private float SurveyFeet(float groundY, out int carrying, out float load, out float leanX)
    {
        float support = groundY;
        carrying = 0;
        load = 0f;
        leanX = 0f;

        foreach (LegState leg in legs)
        {
            if (leg.Swinging) continue;
            if (!IsCarryingWeight(leg)) continue;
            support = carrying == 0 ? leg.Foot.y : support + leg.Foot.y;
            leanX += body.InverseTransformPoint(leg.Foot).x * leg.Load;
            load += leg.Load;
            carrying++;
        }

        if (carrying == 0) return support;
        return Mathf.Max(support / carrying, groundY);
    }

    /// Nothing under the bird and nothing holding it up: gravity owns the height. Returns whether
    /// it took over, and writes the drop straight to the path if so.
    ///
    /// Both conditions are needed. Without the height test a run's flight phase -- half a stride
    /// with both feet genuinely in the air -- reads as a fall and the bird drops onto its own
    /// stride. Without the foot test a bird whose feet are stranded above real ground never falls
    /// at all, which was the original bug.
    ///
    /// A ray that finds NO ground is deliberately not a fall. In a streamed world that means the
    /// chunk underneath has not loaded yet, not that there is nothing there; falling on it would
    /// send the bird to the bottom of the scene while the terrain was still on its way. It holds
    /// its height instead, and falls once there is something to fall onto.
    private bool UpdateFall(float dt, bool hasGround, int carrying, float restingHeight)
    {
        IsFalling = hasGround && carrying == 0 && pathPos.y > restingHeight + FallThreshold;
        if (!IsFalling)
        {
            fallVelocity = 0f;
            return false;
        }

        fallVelocity = Mathf.Min(fallVelocity + fallGravity * dt, maxFallSpeed);
        float falling = pathPos.y - fallVelocity * dt;
        if (falling <= restingHeight)
        {
            falling = restingHeight;
            Land();
        }

        // Written straight to the path rather than through heightSmooth. The smoothing is an
        // exponential approach -- fastest at the start, slowest at the end -- which is the exact
        // opposite shape to a fall, and running the drop through it turns gravity back into the
        // glide this is here to replace.
        pathPos.y = falling;
        smoothedHeight = falling;
        heightPrimed = true;
        return true;
    }

    /// Ease onto the height the ground is offering. This filter is for terrain noise only; the bob
    /// is added after it, at full height and in phase.
    private void SettleOnto(float dt, float targetHeight)
    {
        if (!heightPrimed) { smoothedHeight = targetHeight; heightPrimed = true; }
        smoothedHeight = Mathf.Lerp(smoothedHeight, targetHeight, 1f - Mathf.Exp(-heightSmooth * dt));
    }

    /// Whether a planted foot is actually on the ground beneath it, rather than hanging in the air
    /// where the rest pose left it. One ray per planted foot per frame, against the five to nine
    /// the foothold search already spends.
    private bool IsCarryingWeight(LegState leg)
    {
        // A foothold the leg cannot span is not under load either. The gait resolves footholds by
        // dropping a ray and taking whatever it finds, so a bird in the air places its next
        // foothold on the ground far below and would otherwise be hauled down onto a foot its leg
        // could never reach -- which looks like the body being winched down a wire, not a fall.
        if (Vector3.Distance(leg.Rig.Hip.position, leg.Foot) > leg.Rig.Geometry.MaxReach)
            return false;

        if (!ground.Below(leg.Foot, out RaycastHit hit)) return false;
        return leg.Foot.y - hit.point.y <= footGroundTolerance;
    }

    private void Land()
    {
        fallVelocity = 0f;
        IsFalling = false;
        GroundFeet();
    }

    /// What the driver reports as this machine's velocity. Smoothed, because it feeds a camera and
    /// a look-ahead rather than anything the gait depends on.
    private void TrackVelocity(float dt)
    {
        velocity = Vector3.Lerp(velocity, (body.position - lastBodyPos) / dt, 1f - Mathf.Exp(-8f * dt));
        lastBodyPos = body.position;
    }
}
