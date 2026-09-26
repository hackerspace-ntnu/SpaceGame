using UnityEngine;
using SpaceGame.Gameplay;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// Opens and shuts a character's jaw while it is talking.
    ///
    /// <para>
    /// "Talking" is whatever the dialog popup is typing out for this character: every NPC line --
    /// chatter, dialog, a trader's greeting, a warning -- goes through <see cref="NpcDialogPopupUI"/>,
    /// and the popup knows who said it and which letter it has just revealed. The jaw follows that
    /// letter, so the mouth moves in step with the text, pauses where the typewriter pauses on a
    /// comma, and stops the moment the player skips to the end of the line.
    /// </para>
    ///
    /// <para>
    /// Purely cosmetic, like <see cref="EyeBlink"/>: every machine animates the mouth for the lines
    /// its own popup shows. Nothing is sent and nothing is saved.
    /// </para>
    ///
    /// <para>
    /// Runs in LateUpdate, after the Animator. A Humanoid avatar that maps the bone as its Jaw drives
    /// it EVERY frame, to the centre of the jaw muscle rather than to the rest pose -- 10 degrees
    /// clenched on Raxy, lower lip pushed into the upper -- and no clip here animates the jaw. So on
    /// such a rig this holds the rest pose every frame and opens from it. On a rig whose avatar
    /// leaves the jaw alone it writes only while the mouth is open or closing.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TalkingMouth : MonoBehaviour
    {
        [Tooltip("The jaw bone. The drifter builder finds it by name ('Jaw').")]
        [SerializeField] private Transform jaw;

        [Tooltip("The axis, in the jaw's own space, that drops the chin when turned by a positive " +
                 "angle. The drifter builder measures it off the rig: the character's right, as the " +
                 "jaw sees it at rest.")]
        [SerializeField] private Vector3 openAxis = Vector3.right;

        [Tooltip("How far the jaw drops, in degrees, on a wide-open vowel (a, o).")]
        [SerializeField, Range(0f, 45f)] private float wideOpenDegrees = 20f;

        [Tooltip("How open the jaw is on the narrower vowels (e, i, u, y), as a fraction of wide open.")]
        [SerializeField, Range(0f, 1f)] private float narrowVowelOpen = 0.6f;

        [Tooltip("How open the jaw is on a consonant, as a fraction of wide open. The lips close " +
                 "completely for m, b and p, and between words.")]
        [SerializeField, Range(0f, 1f)] private float consonantOpen = 0.25f;

        [Tooltip("Seconds the jaw takes to reach a letter's opening. Longer than one typed letter " +
                 "(1/28 s), so letters blur into syllables rather than chattering.")]
        [SerializeField, Range(0.01f, 0.3f)] private float response = 0.06f;

        private HealthComponent health;
        private Quaternion restRotation;
        private float open;
        private float openVelocity;
        private bool jawMoved;
        private bool animatorDrivesJaw;

        private bool Dead => health != null && !health.Alive;

        private void Awake()
        {
            health = GetComponent<HealthComponent>();

            // Loud, because the failure is silent: a mouth with no jaw simply never moves.
            if (jaw == null)
                Debug.LogError($"{name}: TalkingMouth has no jaw bone, so its mouth will never move.", this);
            else
                restRotation = jaw.localRotation;
        }

        // Not in Awake: the Animator on the model below may not have bound its avatar yet.
        private void Start()
        {
            var animator = GetComponentInChildren<Animator>();
            animatorDrivesJaw = jaw != null && animator != null && animator.isHuman &&
                                animator.GetBoneTransform(HumanBodyBones.Jaw) == jaw;
        }

        private void OnDisable()
        {
            open = 0f;
            openVelocity = 0f;
            if (jaw != null && jawMoved)
            {
                jaw.localRotation = restRotation;
                jawMoved = false;
            }
        }

        private void LateUpdate()
        {
            if (jaw == null) return;

            float target = 0f;
            var popup = NpcDialogPopupUI.Instance;
            if (!Dead && popup != null && popup.TryGetSpokenCharacter(transform, out char letter))
                target = Openness(letter);

            open = Mathf.SmoothDamp(open, target, ref openVelocity, response);
            if (target == 0f && open < 0.01f)
            {
                open = 0f;
                openVelocity = 0f;
                if (!jawMoved && !animatorDrivesJaw) return;
            }

            jaw.localRotation = restRotation * Quaternion.AngleAxis(open * wideOpenDegrees, openAxis);
            jawMoved = open > 0f;
        }

        /// <summary>How far the letter opens the jaw, 0 (shut) to 1 (wide).</summary>
        private float Openness(char letter)
        {
            switch (char.ToLowerInvariant(letter))
            {
                case 'a': case 'o':
                    return 1f;
                case 'e': case 'i': case 'u': case 'y':
                    return narrowVowelOpen;
                case 'm': case 'b': case 'p':
                    return 0f;
                default:
                    return char.IsLetterOrDigit(letter) ? consonantOpen : 0f;
            }
        }
    }
}
