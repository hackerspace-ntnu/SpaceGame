using UnityEngine;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// Every parameter of the generated humanoid controller, by name and type.
    ///
    /// <para>
    /// <see cref="Contract"/> is what the builder writes and what HumanoidControllerAssetTests
    /// checks the built controller against, so a parameter code writes and the controller lacks
    /// fails a test instead of failing silently. That silence is the whole history of this file:
    /// <c>Die</c>, <c>Throw</c>, <c>ShootRifle</c>, <c>Pet</c> and <c>IsSeated</c> were all
    /// written for months against a controller that had none of them.
    /// </para>
    /// </summary>
    public static class HumanoidParams
    {
        public const string SpeedX = "SpeedX";
        public const string SpeedY = "SpeedY";
        public const string FallSpeed = "FallSpeed";
        public const string MoveAnimSpeed = "MoveAnimSpeed";
        public const string IdleIndex = "IdleIndex";
        public const string CycleOffset = "CycleOffset";
        public const string AimPitch = "AimPitch";
        public const string IsGrounded = "IsGrounded";
        public const string IsCrouching = "IsCrouching";

        /// <summary>
        /// Spelled as stored. Nine controllers — every creature's as well as this one — carry the
        /// parameter under this name, and renaming it means rebuilding all of them at once.
        /// </summary>
        public const string Immobilized = "IsImmobalized";

        public const string Seated = "Seated";
        public const string IsGliding = "IsGliding";
        public const string HoldStyle = "HoldStyle";
        public const string HoldMirror = "HoldMirror";
        public const string WornLeftStyle = "WornLeftStyle";
        public const string ArmRaise = "ArmRaise";

        public static readonly int IdleIndexHash = Animator.StringToHash(IdleIndex);
        public static readonly int CycleOffsetHash = Animator.StringToHash(CycleOffset);

        /// <summary>Mirror switch of an action layer's states, one per slot.</summary>
        public static string ActionMirror(CharacterAction.Slot slot) => "ActionMirror" + slot;

        /// <summary>
        /// Playback speed of an action layer's states, one per slot. Defaults to 1: at 0 every
        /// action would freeze on its first frame.
        /// </summary>
        public static string ActionSpeed(CharacterAction.Slot slot) => "ActionSpeed" + slot;

        /// <summary>Every parameter the controller has, with its type and default.</summary>
        public static readonly (string Name, AnimatorControllerParameterType Type, float Default)[] Contract =
            BuildContract();

        private static (string, AnimatorControllerParameterType, float)[] BuildContract()
        {
            const AnimatorControllerParameterType F = AnimatorControllerParameterType.Float;
            const AnimatorControllerParameterType B = AnimatorControllerParameterType.Bool;
            const AnimatorControllerParameterType I = AnimatorControllerParameterType.Int;

            var list = new System.Collections.Generic.List<(string, AnimatorControllerParameterType, float)>
            {
                (SpeedX, F, 0f), (SpeedY, F, 0f), (FallSpeed, F, 0f), (MoveAnimSpeed, F, 1f),
                (IdleIndex, F, 0f), (CycleOffset, F, 0f), (AimPitch, F, 0f),
                (IsGrounded, B, 1f), (IsCrouching, B, 0f), (Immobilized, B, 0f), (Seated, B, 0f),
                (IsGliding, B, 0f), (HoldMirror, B, 0f),
                (HoldStyle, I, 0f), (WornLeftStyle, I, 0f), (ArmRaise, I, 0f),
            };

            foreach (CharacterAction.Slot slot in System.Enum.GetValues(typeof(CharacterAction.Slot)))
            {
                list.Add((ActionMirror(slot), B, 0f));
                list.Add((ActionSpeed(slot), F, 1f));
            }

            return list.ToArray();
        }
    }
}
