// Where the BODY goes: along its path, up and down over the ground, and down under gravity when
// nothing is holding it.
//
// Height comes from the GROUND under the body, with the planted feet only able to hold it UP (a
// step onto a rock), never to pull it along. Deriving it from the feet alone is a feedback loop
// with no damping: a lifted foot raises the body, the raised body places the next foothold higher,
// and the machine climbs into the sky. Asked for half speed the bird reached six metres before this
// was pinned to the ground.
//
// Gravity lives here rather than in a robot's own file because EVERY legged machine needs it. Any
// of them spawned above its terrain in a streamed world hangs in the sky otherwise; that was found
// and fixed on the ostrich only, and the six-legged machine still had the bug.
using UnityEngine;

namespace SpaceGame.Locomotion
{
    public abstract partial class LeggedLocomotion
    {
        /// The machine's position along its path, with no bob or lean in it. Those are display
        /// offsets applied on top each frame; folding them back into the position they were
        /// computed from would let the lean integrate and walk the body sideways off its course.
        private Vector3 pathPos;
        private float currentYaw;
        private Vector3 lastBodyPos;
        private Vector3 velocity;

        /// This frame's commanded velocity turned into world space, once, at the top of the frame
        /// and after the yaw has been advanced.
        ///
        /// Cached rather than recomputed because THREE places need it and they must agree: the path
        /// integrates it, the gait drifts the feet against it, and the foothold clamp aims at where
        /// the hip will be a swing later. Two of those deriving travel from `currentYaw` and one
        /// from a velocity is exactly how the gait and the footholds end up pointing in different
        /// directions on a machine that is not travelling along its nose.
        private Vector3 commandedWorldVelocity;

        private float smoothedHeight;
        private bool heightPrimed;
        private float fallVelocity;

        /// How far above its resting height the body has to be before gravity claims it.
        private float FallThreshold => shortestLegReach * fallThresholdFraction;

        /// How much of a leg's reach the body may plan to use. Correcting to the very limit leaves
        /// the leg dead straight and unreachable again on the next bump; this keeps a bend in it.
        private const float ReachCorrectionFraction = 0.95f;

        /// Ceiling on one frame's reach correction, as a fraction of leg reach.
        private const float MaxReachCorrection = 0.5f;

        /// Feet in the machine's own yaw frame, for the support-plane fit. Allocated once at
        /// Initialise: invariant I3, nothing in the per-frame path allocates.
        private Vector3[] supportPoints;

        /// Ground profile ahead, for the climb gate. Allocated once, same invariant, and reused by
        /// the driver's detour scan -- both callers are on the main thread and never nested.
        private readonly WalkerClimb.Sample[] climbSamples = new WalkerClimb.Sample[4];

        /// Below this much of the commanded travel surviving, the machine is reported as BLOCKED
        /// rather than merely slowed. The taper means a hillside just under the limit still lets a
        /// trickle through, and a driver that waited for an exact zero would sit there creeping up
        /// it forever instead of going around.
        private const float BlockedBelow = 0.25f;

        /// Puts the body's own state back where a freshly measured rig expects it. Called at the
        /// end of Initialise, so the rig file does not have to reach in and set half a dozen fields
        /// it does not own.
        private void ResetBodyState()
        {
            currentYaw = body.eulerAngles.y;
            pathPos = body.position;
            lastBodyPos = body.position;
            smoothedHeight = body.position.y;
            heightPrimed = false;
            commandedWorldVelocity = Vector3.zero;
        }

        /// Drop the machine so the legs start within reach of the ground.
        public void SnapToGround()
        {
            if (!ready) return;
            // A deliberately longer ray than the per-frame ones: this is called on a machine that
            // may have been dropped into the scene anywhere above its ground.
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

        /// Carry the path forward under the commanded twist, and advance the gait clock by the
        /// distance covered rather than by the time taken.
        ///
        /// The twist's linear half is a velocity in the BODY's frame, so it is resolved onto the
        /// machine's own forward AND right here. A machine commanded straight sideways travels
        /// sideways with its nose where it was; there is no heading change to derive it from.
        private void AdvancePath(float dt)
        {
            currentYaw += CommandedYawRate * dt;

            Quaternion heading = Quaternion.AngleAxis(currentYaw, Vector3.up);
            Vector3 forward = heading * Vector3.forward;
            Vector3 right = heading * Vector3.right;

            // The gate runs HERE, once, on the raw request, and everything downstream reads the
            // result through CommandedVelocity. It is deliberately the first thing the frame does
            // with the command: the clock, the foot drift and the foothold clamp all derive from
            // this vector, and they have to derive from the same one.
            Vector2 request = commandedVelocity;
            travelVelocity = ApplyClimbGate(request, forward * request.y + right * request.x);

            Vector2 v = travelVelocity;
            commandedWorldVelocity = forward * v.y + right * v.x;

            Vector3 moved = commandedWorldVelocity * dt;
            pathPos += moved;

            gait.Advance(Pace * dt, WalkerGait.CycleDistance(cycleStride, CurrentDuty));

            // Signed by the FORWARD channel, since that is the only one an "is it reversing?" reader
            // can mean; a purely lateral command reports its ground speed as positive.
            diagnostics.AchievedSpeed = v.y < 0f ? -moved.magnitude / dt : moved.magnitude / dt;
        }

        /// Cut the commanded travel down to what the ground ahead will actually let the legs walk
        /// up. The measurement itself is WalkerClimb; this is the wiring.
        ///
        /// ─────────── why this does not break invariant I1 ───────────
        ///
        /// I1 forbids gating motion on the machine's OWN output, because the clock is advanced by
        /// distance travelled and a machine that stops itself can never re-open a phase slice to
        /// un-stick itself. That latch cost a session once. This gate is a function of the terrain
        /// and of the COMMANDED DIRECTION -- both of them inputs, neither of them anything the
        /// machine produced -- so there is no loop to close. Three things keep it that way, and all
        /// three are load-bearing:
        ///
        ///   1. THE YAW CHANNEL IS NEVER TOUCHED. A machine stopped against a hillside can always
        ///      turn, and Pace counts yaw rate, so the clock keeps turning while it does.
        ///   2. THE PROBE FOLLOWS THE SIGN OF TRAVEL. Reversing probes BEHIND the machine, which is
        ///      downhill, which always passes. Backing off is always available.
        ///   3. DOWNHILL IS NEVER GATED, in WalkerClimb.GradeScale.
        ///
        /// A gate written against the machine's measured velocity instead of its command would have
        /// all three of these and still latch, which is the distinction worth keeping hold of.
        private Vector2 ApplyClimbGate(Vector2 request, Vector3 worldRequest)
        {
            ClimbScale = 1f;
            ClimbBlocked = false;

            // Nobody is asking it to go anywhere, so there is nothing to refuse. Stated as a
            // standing machine rather than a blocked one so a parked machine does not send its
            // driver hunting for a detour it never wanted.
            Vector3 flat = worldRequest;
            flat.y = 0f;
            if (flat.sqrMagnitude < 1e-6f) return request;

            ClimbScale = ClimbScaleTowards(flat.normalized);
            ClimbBlocked = ClimbScale < BlockedBelow;
            return request * ClimbScale;
        }

        /// How much travel survives in one world direction. Public through `CanTravel` so a driver
        /// can ask the same question of a heading it is considering rather than one it has already
        /// committed to.
        private float ClimbScaleTowards(Vector3 flatDirection)
        {
            if (maxClimbAngle >= 89f) return 1f;

            // No ground under the machine is no information -- an unloaded chunk, or a fall in
            // progress -- and the rule everywhere else in this file is that missing terrain never
            // refuses anything.
            if (!ground.Below(pathPos, out RaycastHit under)) return 1f;

            float run = Mathf.Max(shortestLegReach * climbProbeRun, 0.1f);

            // The probe rays start clear of anything the machine could legitimately be walking up:
            // the whole run taken at 45 degrees, plus a leg reach for a step, plus the usual
            // margin. A ray started below the ground it is probing fires from inside the mesh and
            // reports nothing, which is precisely how the foothold search once went blind while
            // trying to climb.
            float lift = run + shortestLegReach + rayStartAbove;

            ground.SampleAlong(under.point, flatDirection, run, lift, climbSamples);

            return WalkerClimb.TravelScale(under.point.y, climbSamples, climbSamples.Length,
                                           maxClimbAngle, climbTaperAngle, stepUpHeight);
        }

        /// Whether the machine would get anywhere travelling in `worldDirection`. For a driver
        /// looking for a way around; costs one probe run of rays per call, so ask it about a
        /// handful of candidate headings, not about every heading.
        public bool CanTravel(Vector3 worldDirection)
        {
            if (!ready) return true;

            Vector3 flat = worldDirection;
            flat.y = 0f;
            if (flat.sqrMagnitude < 1e-6f) return true;

            return ClimbScaleTowards(flat.normalized) >= BlockedBelow;
        }

        /// Survey the feet, let gravity have its say, then let the body motion pose what is left.
        private void PoseBody(float dt)
        {
            SupportState support = Survey();

            var pose = new BodyPose
            {
                PathPos = pathPos,
                Yaw = currentYaw,
                Attitude = Quaternion.AngleAxis(currentYaw, Vector3.up),
            };
            bodyMotion.Pose(BuildFrame(), support, dt, ref pose);
            ApplyReachCorrection(ref pose);

            float restingHeight = support.GroundY + rideHeight;
            if (UpdateFall(dt, support.HasGround, support.CarryingCount, restingHeight))
            {
                // A falling machine is not bobbing. The gait's own motion belongs to walking.
                pose.DisplayOffset = Vector3.zero;
            }
            else
            {
                SettleOnto(dt, pose.PathPos.y);
            }

            // The path carries the height; bob and lean are added on top of it and never written
            // back, so they stay a gait rather than becoming a drift. Keeping the offset out of
            // pathPos is also what keeps it out of the fall test, which reads pathPos.y.
            pathPos.y = smoothedHeight;
            body.rotation = pose.Attitude;
            body.position = pathPos + pose.DisplayOffset;
        }

        /// Drop the body until every grounded foot is back inside its leg's reach.
        ///
        /// This is core rather than policy because it is not a matter of taste. A body motion may
        /// choose to ride level, or to lean, or to bob -- but a leg that cannot span the distance to
        /// its own foot is not making a stylistic point, it is detached, and the machine is walking
        /// on the legs that can still reach while the rest hang in the air.
        ///
        /// Tilting toward the support plane recovers most of it; this catches whatever is left --
        /// a deck held deliberately flat, a slope past the tilt cap, a step down under one leg.
        /// Lowering is the only correction available: raising would strand the leg further, and
        /// there is nothing to be done about a foot that is out of reach HORIZONTALLY, since no
        /// change of height brings it closer. That case is the gait's to fix, by stepping.
        private void ApplyReachCorrection(ref BodyPose pose)
        {
            float drop = 0f;

            foreach (LegState leg in legs)
            {
                if (leg.Swinging || !leg.Grounded) continue;

                // Where this hip WILL be under the pose just chosen. The hip sits on the coxa's yaw
                // axis and the coxa is rigid to the body, so its body-space position is a constant
                // measured once -- this frame's yaw cannot have moved it.
                Vector3 hip = pose.PathPos + pose.DisplayOffset + pose.Attitude * leg.Measure.HipLocal;
                Vector3 toHip = hip - leg.Foot;

                float reach = leg.Rig.Geometry.MaxReach * ReachCorrectionFraction;
                float across = toHip.x * toHip.x + toHip.z * toHip.z;
                float spare = reach * reach - across;
                if (spare <= 0f) continue;

                drop = Mathf.Max(drop, toHip.y - Mathf.Sqrt(spare));
            }

            if (drop <= 0f) return;

            // Bounded, so one bad foothold cannot slam the body into the ground. Past this the leg
            // is beyond rescuing by posture and the gait has to step it.
            pose.PathPos.y -= Mathf.Min(drop, shortestLegReach * MaxReachCorrection);
        }

        private BodyFrame BuildFrame() => new BodyFrame
        {
            Body = body,
            CommandedSpeed = CommandedSpeed,
            CommandedYawRate = CommandedYawRate,
            MaxYawRate = MaxYawRate,
            RunBlend = RunBlend,
            Effort = Effort,
            Phase = gait.Phase,
            Duty = CurrentDuty,
            RideHeight = rideHeight,
            LegReach = shortestLegReach,
        };

        /// What the feet are doing for the body this frame: how high they hold it, how many are on
        /// the ground at all, and where their load is centred across the body.
        ///
        /// A foot only holds the machine up if it is ON something. A stranded foot -- one hanging
        /// in the air where the rest pose left it, because there was no ground to place it on --
        /// used to carry the body just as well as one standing on rock, and that is what left the
        /// bird parked in the sky: the feet held it up, and the gait only ever re-places a foot
        /// while the machine is moving, so a parked one could never come down on its own.
        private SupportState Survey()
        {
            bool hasGround = ground.Below(pathPos, out RaycastHit under);
            float groundY = hasGround ? under.point.y : pathPos.y - rideHeight;

            var s = new SupportState
            {
                SupportHeight = groundY,
                GroundY = groundY,
                HasGround = hasGround,
                Plane = WalkerSupportPlane.Level,
            };

            // The feet are gathered in the machine's own yaw frame, so the plane's normal comes back
            // as the pitch and roll of the ground under the body rather than as a world direction.
            Quaternion toLocal = Quaternion.Inverse(Quaternion.AngleAxis(currentYaw, Vector3.up));

            foreach (LegState leg in legs)
            {
                leg.Grounded = false;
                if (leg.Swinging) continue;
                if (!IsOnGround(leg)) continue;

                leg.Grounded = true;
                supportPoints[s.GroundedCount++] = toLocal * (leg.Foot - pathPos);

                // A foothold the leg cannot span is not under load. The gait resolves footholds by
                // dropping a ray and taking whatever it finds, so a machine in the air places its
                // next foothold on the ground far below and would otherwise be hauled down onto a
                // foot its leg could never reach -- the body winched down a wire, not a fall.
                if (Vector3.Distance(leg.Rig.Anchor.position, leg.Foot) > leg.Rig.Geometry.MaxReach)
                    continue;

                s.SupportHeight = s.CarryingCount == 0 ? leg.Foot.y : s.SupportHeight + leg.Foot.y;
                s.LeanX += body.InverseTransformPoint(leg.Foot).x * leg.Load;
                s.Load += leg.Load;
                s.CarryingCount++;
            }

            if (s.CarryingCount > 0)
                s.SupportHeight = Mathf.Max(s.SupportHeight / s.CarryingCount, groundY);

            // Where there is a plane to have, its height wins. The mean of the CARRYING feet is
            // biased uphill on a slope, because the downhill legs that would pull it back down are
            // precisely the ones that have stopped carrying.
            if (WalkerSupportPlane.TryFit(supportPoints, s.GroundedCount, out WalkerSupportPlane plane))
            {
                s.Plane = plane;
                s.SupportHeight = Mathf.Max(pathPos.y + plane.Height, groundY);
            }

            return s;
        }

        /// Whether a planted foot is actually standing on something, rather than hanging in the air
        /// where the rest pose left it. One ray per planted foot per frame, against the five to nine
        /// the foothold search already spends.
        ///
        /// Deliberately says nothing about whether the leg can still SPAN the distance. That is a
        /// separate question, asked separately in the survey, because the two answers are wanted by
        /// different callers: gravity needs to know what is carrying the machine, and the support
        /// plane needs to know where the ground is -- including under a leg that has run out of
        /// reach, which is the one the body most needs to hear about.
        private bool IsOnGround(LegState leg)
        {
            if (!ground.Below(leg.Foot, out RaycastHit hit)) return false;
            return leg.Foot.y - hit.point.y <= CarryTolerance;
        }

        /// How far above the ground a planted foot may sit and still count as carrying.
        ///
        /// Authored absolutely, because on the machine it was tuned for it means "a foot's
        /// thickness". Floored at a fraction of leg reach so a machine an order of magnitude larger
        /// is not held to a bird's tolerance and does not conclude, from ray noise on its own
        /// scale, that nothing is holding it up.
        private float CarryTolerance
            => Mathf.Max(footGroundTolerance, shortestLegReach * 0.05f);

        /// Nothing under the machine and nothing holding it up: gravity owns the height. Returns
        /// whether it took over, and writes the drop straight to the path if so.
        ///
        /// BOTH conditions are needed. Without the height test a run's flight phase -- half a stride
        /// with both feet genuinely in the air -- reads as a fall and the machine drops onto its own
        /// stride. Without the foot test a machine whose feet are stranded above real ground never
        /// falls at all, which was the original bug.
        ///
        /// A ray that finds NO ground is deliberately not a fall. In a streamed world that means the
        /// chunk underneath has not loaded yet, not that there is nothing there; falling on it would
        /// send the machine to the bottom of the scene while the terrain was still on its way. It
        /// holds its height instead, and falls once there is something to fall onto.
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

        /// Ease onto the height the ground is offering. This filter is for terrain noise only; a
        /// gait's own motion is added after it, at full amplitude and in phase.
        private void SettleOnto(float dt, float targetHeight)
        {
            if (!heightPrimed) { smoothedHeight = targetHeight; heightPrimed = true; }
            smoothedHeight = Mathf.Lerp(smoothedHeight, targetHeight, 1f - Mathf.Exp(-heightSmooth * dt));
        }

        private void Land()
        {
            fallVelocity = 0f;
            IsFalling = false;
            GroundFeet();
        }

        /// What the driver reports as this machine's velocity. Smoothed, because it feeds a camera
        /// and a look-ahead rather than anything the gait depends on.
        private void TrackVelocity(float dt)
        {
            velocity = Vector3.Lerp(velocity, (body.position - lastBodyPos) / dt, 1f - Mathf.Exp(-8f * dt));
            lastBodyPos = body.position;
        }
    }
}
