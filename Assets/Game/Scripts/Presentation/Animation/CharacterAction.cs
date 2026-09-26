using System;
using System.Collections.Generic;
using SpaceGame.Items;
using UnityEngine;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// One thing a humanoid body can do on top of walking: a wave, a punch, a reach for a shelf,
    /// the talking loop. The unit the whole humanoid animation system is built from.
    ///
    /// <para>
    /// An action names the part of the body it takes over (<see cref="Slot"/>), how it plays
    /// (<see cref="Playback"/>) and the clips it may play (<see cref="Variant"/>s, one picked
    /// per play). The humanoid controller is GENERATED from these — Tools/SpaceGame/Animation/
    /// Rebuild Humanoid Controller writes one state per variant on the action layer of its slot —
    /// so adding an animation is: create an action asset under
    /// <c>Assets/Game/ScriptableObjects/Animation/Actions/</c>, drop the clip in, Rebuild. No
    /// trigger, no parameter, no transition to wire, and so no parameter name to misspell: code
    /// holds a reference to the asset and <see cref="CharacterActions.Play"/> crossfades straight
    /// to its state.
    /// </para>
    /// <para>
    /// Everything here is presentation. Nothing gameplay-relevant may wait on an action finishing;
    /// the timings in <see cref="marks"/> exist so an authority can schedule its OWN timer to
    /// match the clip (an NPC's punch lands on its contact frame), never so it can listen for one.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceGame/Animation/Character Action", fileName = "NewAction")]
    public sealed class CharacterAction : ScriptableObject
    {
        /// <summary>
        /// The part of the body the action takes over. Each is its own masked layer, stacked in
        /// this order above the hold poses, so an arm gesture plays over a full-body action and
        /// over whatever the hands are holding. Append only: assets store the numbers.
        /// </summary>
        public enum Slot
        {
            Full,
            Upper,
            LeftArm,
            RightArm,

            /// <summary>
            /// Takes nothing over: the clip's motion away from its own first frame is ADDED to the
            /// upper body's pose, whatever that is — a recoil kick over any hold pose, at any aim
            /// pitch. The clip must end where it starts, or the pose jumps when the layer lets go.
            /// </summary>
            Additive
        }

        public enum Playback
        {
            /// <summary>Plays once and hands the body back.</summary>
            OneShot,

            /// <summary>Plays until stopped. Playing it again while it runs does nothing.</summary>
            Loop,

            /// <summary>An enter clip, then the loop until stopped, then an exit clip.</summary>
            EnterLoopExit
        }

        /// <summary>Named moments inside a clip. Append only: assets store the numbers.</summary>
        public enum Mark
        {
            /// <summary>The frame a strike connects.</summary>
            Contact,

            /// <summary>The frame a thrown thing leaves the hand.</summary>
            Release
        }

        [Serializable]
        public sealed class Variant
        {
            [Tooltip("The clip. For Loop and EnterLoopExit this is the loop; for an aimed variant " +
                     "it is the level pose.")]
            public AnimationClip clip;

            [Tooltip("Optional. With Aim Up also set, the variant is a blend over the look pitch " +
                     "(AimPitch): this clip at the bottom of the range, Clip level, Aim Up at the top.")]
            public AnimationClip aimDown;

            [Tooltip("Optional. See Aim Down.")]
            public AnimationClip aimUp;

            [Tooltip("EnterLoopExit only: played once before the loop.")]
            public AnimationClip enter;

            [Tooltip("EnterLoopExit only: played once when the action is stopped.")]
            public AnimationClip exit;

            [Tooltip("How often this variant is picked relative to the others.")]
            [Min(0f)] public float selectionWeight = 1f;

            // Zero, not a negative sentinel, means "unset": every asset written before this field
            // existed, and every variant added in the Inspector, reads back as zero — and a blow
            // landing on the very first frame is no strike anyone would author.
            [Tooltip("Where this variant's blow lands in its clip, 0-1, when it is not the action's " +
                     "Contact mark — variants cut from different takes strike at different times. " +
                     "0 uses the action's mark.")]
            [Range(0f, 1f)] public float contactAt;

            public bool IsAimed => aimDown != null && aimUp != null;
        }

        [Serializable]
        public struct PhaseMark
        {
            public Mark mark;

            [Tooltip("Where in the main clip it happens, 0-1.")]
            [Range(0f, 1f)] public float normalizedTime;
        }

        [SerializeField] private Slot slot = Slot.Upper;
        [SerializeField] private Playback playback = Playback.OneShot;
        [SerializeField] private Variant[] variants = Array.Empty<Variant>();

        [Tooltip("The arm the clips were authored on.")]
        [SerializeField] private ItemGrip.Hand authoredHand = ItemGrip.Hand.Right;

        [Tooltip("Mirror the clip when a caller asks for the other arm — a stab from a blade worn " +
                 "on the left wrist.")]
        [SerializeField] private bool mirrorForOtherArm;

        [Tooltip("Playback speed range. Each character plays at its own fixed point in the range, " +
                 "so a crowd waving does not wave in lockstep.")]
        [SerializeField] private Vector2 speedRange = Vector2.one;

        [Tooltip("Seconds to blend in.")]
        [SerializeField, Min(0f)] private float fadeIn = 0.12f;

        [Tooltip("Seconds to blend back out at the end, or after Stop.")]
        [SerializeField, Min(0f)] private float fadeOut = 0.15f;

        [SerializeField] private PhaseMark[] marks = Array.Empty<PhaseMark>();

        [Tooltip("What this action can express. Gameplay asks BodyLanguage for a cue ('greet', " +
                 "'hurt') and gets one of the actions tagged with it.")]
        [SerializeField] private CharacterCue[] cues = Array.Empty<CharacterCue>();

        [Tooltip("The postures a cue may pick this action in. Nothing ticked means by slot: a " +
                 "full-body action only standing still, anything else in every posture.")]
        [SerializeField] private BodyPosture postures;

        public Slot BodySlot => slot;
        public IReadOnlyList<CharacterCue> Cues => cues;

        /// <summary>The postures this action fits, with the by-slot default filled in.</summary>
        public BodyPosture Postures =>
            postures != 0 ? postures : slot == Slot.Full ? BodyPosture.Standing : BodyPostures.Any;

        public bool Fits(BodyPosture posture) => (Postures & posture) != 0;
        public Playback Mode => playback;
        public int VariantCount => variants.Length;
        public float FadeIn => fadeIn;
        public float FadeOut => fadeOut;
        public bool Loops => playback != Playback.OneShot;

        public Variant GetVariant(int index) => variants[index];

        /// <summary>Whether playing this for <paramref name="arm"/> means playing it mirrored.</summary>
        public bool Mirrors(ItemGrip.Hand? arm) =>
            mirrorForOtherArm && arm.HasValue && arm.Value != authoredHand;

        /// <summary>
        /// The playback speed for a character, fixed by <paramref name="seed"/>: the same body
        /// always plays this action at the same speed, on every machine, with nothing sent.
        /// </summary>
        public float SpeedFor(int seed)
        {
            // Animator.StringToHash rather than string.GetHashCode: the second is not guaranteed
            // stable between runtimes, and an editor host and a built client must agree.
            float t = (uint)Hash(seed, Animator.StringToHash(name)) / (float)uint.MaxValue;
            return Mathf.Lerp(speedRange.x, speedRange.y, t);
        }

        /// <summary>A variant index, drawn by selection weight. -1 when there are none.</summary>
        public int PickVariant(System.Random random)
        {
            float total = 0f;
            foreach (Variant v in variants) total += v.selectionWeight;
            if (variants.Length == 0) return -1;
            if (total <= 0f) return random.Next(variants.Length);

            float pick = (float)random.NextDouble() * total;
            for (int i = 0; i < variants.Length; i++)
            {
                pick -= variants[i].selectionWeight;
                if (pick < 0f) return i;
            }
            return variants.Length - 1;
        }

        public bool TryGetMark(Mark mark, out float normalizedTime)
        {
            foreach (PhaseMark m in marks)
            {
                if (m.mark != mark) continue;
                normalizedTime = m.normalizedTime;
                return true;
            }
            normalizedTime = 0f;
            return false;
        }

        /// <summary>
        /// <see cref="TryGetMark(Mark, out float)"/> for one variant: its own
        /// <see cref="Variant.contactAt"/> when it has one, else the action's mark. Only Contact
        /// varies per variant — a combo's punches, each cut from its own moment of a take, land at
        /// different times, while nothing yet throws differently per clip.
        /// </summary>
        public bool TryGetMark(Mark mark, int variant, out float normalizedTime)
        {
            if (mark == Mark.Contact && variant >= 0 && variant < variants.Length && variants[variant].contactAt > 0f)
            {
                normalizedTime = variants[variant].contactAt;
                return true;
            }
            return TryGetMark(mark, out normalizedTime);
        }

        /// <summary>
        /// Seconds from the start of a play until <paramref name="mark"/>, at
        /// <paramref name="speed"/>, counting the enter clip. For an authority's own timer —
        /// the animation is never asked.
        /// </summary>
        public float SecondsTo(Mark mark, int variant, float speed)
        {
            if (variant < 0 || variant >= variants.Length || !TryGetMark(mark, variant, out float at)) return 0f;

            Variant v = variants[variant];
            float enter = playback == Playback.EnterLoopExit && v.enter != null ? v.enter.length : 0f;
            float main = v.clip != null ? v.clip.length : 0f;
            return (enter + main * at) / Mathf.Max(0.01f, speed);
        }

        /// <summary>Seconds a one-shot variant plays for at <paramref name="speed"/>.</summary>
        public float Seconds(int variant, float speed)
        {
            if (variant < 0 || variant >= variants.Length || variants[variant].clip == null) return 0f;
            return variants[variant].clip.length / Mathf.Max(0.01f, speed);
        }

        private static int Hash(int a, int b)
        {
            unchecked
            {
                uint h = (uint)a * 0x9E3779B1u ^ (uint)b * 0x85EBCA77u;
                h ^= h >> 15;
                h *= 0xC2B2AE3Du;
                return (int)(h ^ (h >> 13));
            }
        }

        private void OnValidate()
        {
            if (speedRange.x <= 0f) speedRange.x = 0.01f;
            if (speedRange.y < speedRange.x) speedRange.y = speedRange.x;
        }
    }
}
