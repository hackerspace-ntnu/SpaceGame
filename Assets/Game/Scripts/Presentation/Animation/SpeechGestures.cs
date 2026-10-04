using UnityEngine;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// A body talks with its hands: while the dialog popup types out a line this character said,
    /// it holds a talking loop and gestures on the beats, greets at the start of a conversation and
    /// reacts to its own questions and exclamations.
    ///
    /// <para>
    /// Everything it does goes through <see cref="BodyLanguage"/>: the loop is
    /// <see cref="BodyLanguage.Hold"/> of <see cref="talking"/>, and the rest are moments
    /// (<see cref="CharacterMoment.ConversationStarted"/>, <see cref="CharacterMoment.QuestionAsked"/>,
    /// <see cref="CharacterMoment.Exclaimed"/>, <see cref="CharacterMoment.SpeechBeat"/>) whose
    /// gestures the reaction table picks. Speech on the move gets arm gestures only; standing, the
    /// whole body may talk (GDC-L1-ANIM-0005: the incidental motion is what sells life).
    /// </para>
    /// <para>
    /// <b>Multiplayer.</b> Follows the popup, like <see cref="TalkingMouth"/>: chatter and war cries
    /// show on every nearby machine and gesture there; a dialog line is local to the player who
    /// pressed interact, and only that player sees the NPC gesture. Beat timing is this machine's
    /// own, so two players watching the same chatter may see different beats — cosmetic, nothing is
    /// sent.
    /// </para>
    /// <para><b>Persistence:</b> none.</para>
    /// </summary>
    public sealed class SpeechGestures : MonoBehaviour
    {
        [Tooltip("The loop held while a line is being said: the talking idle, or its seated form.")]
        [SerializeField] private CharacterCue talking;

        [Tooltip("Seconds between speech-beat gestures, drawn from this range each time.")]
        [SerializeField] private Vector2 beatEvery = new Vector2(1.1f, 2.4f);

        [Tooltip("Seconds into a line before its first beat, so the opening gesture has room.")]
        [SerializeField, Min(0f)] private float firstBeatAfter = 0.5f;

        [Tooltip("Seconds of silence after which the next line opens a new conversation (and may greet).")]
        [SerializeField, Min(0f)] private float newConversationAfter = 10f;

        private BodyLanguage body;
        private int lineSeen = -1;
        private float nextBeatAt;
        private float lastSpokeAt = float.NegativeInfinity;
        private bool holding;

        private void Awake() => body = BodyLanguage.Of(this);

        private void OnDisable() => StopTalking();

        private void Update()
        {
            if (body == null) return;

            NpcDialogPopupUI popup = NpcDialogPopupUI.Instance;
            if (popup == null || !popup.IsTyping || !Says(popup.Speaker))
            {
                StopTalking();
                return;
            }

            if (popup.LineNumber != lineSeen)
            {
                lineSeen = popup.LineNumber;
                StartLine(popup.Line);
            }

            body.Hold(talking);
            holding = true;
            lastSpokeAt = Time.time;

            if (Time.time < nextBeatAt) return;
            body.React(CharacterMoment.SpeechBeat);
            nextBeatAt = Time.time + Random.Range(beatEvery.x, beatEvery.y);
        }

        private void StartLine(string line)
        {
            nextBeatAt = Time.time + firstBeatAfter;

            if (Time.time - lastSpokeAt > newConversationAfter && body.React(CharacterMoment.ConversationStarted))
                return;

            string trimmed = line != null ? line.TrimEnd() : string.Empty;
            if (trimmed.EndsWith("?")) body.React(CharacterMoment.QuestionAsked);
            else if (trimmed.EndsWith("!")) body.React(CharacterMoment.Exclaimed);
        }

        private void StopTalking()
        {
            if (!holding) return;
            holding = false;
            if (body != null) body.Release(talking);
        }

        private bool Says(Transform speaker) =>
            speaker != null && (speaker == transform || speaker.IsChildOf(transform));

        private void OnValidate()
        {
            if (beatEvery.x < 0.1f) beatEvery.x = 0.1f;
            if (beatEvery.y < beatEvery.x) beatEvery.y = beatEvery.x;
        }
    }
}
