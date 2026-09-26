using System.Linq;
using SpaceGame.Presentation;
using UnityEditor.Animations;
using UnityEngine;
using static SpaceGame.EditorTools.AnimatorGraph;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// The Base Layer: standing, walking, crouching, falling, landing and sitting.
    ///
    /// <para>
    /// Locomotion must stay on layer 0 and in one state: AgentAnimatorDriver reads layer 0's
    /// current state to finish a stride before stopping, and a stride it cannot recognise as the
    /// same state is one it abandons.
    /// </para>
    /// </summary>
    internal static class HumanoidBaseLayer
    {
        public const string MoveState = "Move";
        public const string CrouchState = "Crouch";
        public const string AirState = "Air";
        public const string LandState = "Land";
        public const string SitState = "Sit";

        public static void Build(AnimatorController controller, HumanoidAnimationProfile profile)
        {
            LocomotionSet set = profile.Locomotion;
            HumanoidAnimationProfile.Timings time = profile.Timing;
            AnimatorStateMachine sm = controller.layers[0].stateMachine;

            BlendTree idle = Tree1D(controller, "Idle", HumanoidParams.IdleIndex,
                                    set.Idles.Select((clip, i) => ((Motion)clip, (float)i)).ToArray());

            BlendTree move = Tree2D(controller, MoveState, HumanoidParams.SpeedX, HumanoidParams.SpeedY,
                                    new[] { ((Motion)idle, Vector2.zero) }
                                        .Concat(set.Move.Select(c => ((Motion)c.clip, c.position)))
                                        .ToArray());

            BlendTree crouch = Tree2D(controller, CrouchState, HumanoidParams.SpeedX, HumanoidParams.SpeedY,
                                      set.Crouch.Select(c => ((Motion)c.clip, c.position)).ToArray());

            BlendTree air = Tree1D(controller, AirState, HumanoidParams.FallSpeed,
                                   (set.JumpUp, -1f), (set.Fall, 1f));

            AnimatorState moveState = AddState(sm, MoveState, move);
            moveState.speedParameterActive = true;
            moveState.speedParameter = HumanoidParams.MoveAnimSpeed;
            moveState.cycleOffsetParameterActive = true;
            moveState.cycleOffsetParameter = HumanoidParams.CycleOffset;
            sm.defaultState = moveState;

            AnimatorState crouchState = AddState(sm, CrouchState, crouch);
            crouchState.speedParameterActive = true;
            crouchState.speedParameter = HumanoidParams.MoveAnimSpeed;

            AnimatorState airState = AddState(sm, AirState, air);
            AnimatorState landState = AddState(sm, LandState, set.Land);
            AnimatorState sitState = AddState(sm, SitState, set.Sit);

            When(moveState, airState, time.intoAir, IfNot(HumanoidParams.IsGrounded));
            When(moveState, crouchState, time.intoCrouch, If(HumanoidParams.IsCrouching));

            When(airState, landState, time.intoLanding,
                 If(HumanoidParams.Immobilized), Greater(HumanoidParams.FallSpeed, time.landingFallSpeed));
            When(airState, moveState, time.airToGround, If(HumanoidParams.IsGrounded));

            // Hands back on exit time alone once grounded. It used to also require SpeedX > 0,
            // which stranded a body that landed standing still — or walking straight ahead, where
            // SpeedX is zero — in the landing pose until it happened to strafe right.
            AtEnd(landState, moveState, time.landingExitTime, time.outOfLanding, If(HumanoidParams.IsGrounded));

            When(crouchState, moveState, time.outOfCrouch,
                 If(HumanoidParams.IsGrounded), IfNot(HumanoidParams.IsCrouching), IfNot(HumanoidParams.Immobilized));

            AnyWhen(sm, sitState, time.sit, If(HumanoidParams.Seated));
            When(sitState, moveState, time.sit, IfNot(HumanoidParams.Seated));
        }
    }
}
