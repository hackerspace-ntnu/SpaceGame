using System.Collections;
using UnityEngine;
using SpaceGame.Gameplay.Objectives;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// A character addresses the player: the camera turns to them, their lines type out one after
    /// another, and the camera turns back — the witch, home when the crew arrive.
    ///
    /// <para>
    /// The speaker is found by <see cref="QuestActor"/> id when the cutscene starts, because the
    /// usual speaker was spawned a moment ago and no scene can reference them. A spawned speaker can
    /// reach a client a little after the step that spawned them, so the cutscene waits up to
    /// <see cref="speakerWait"/> for them — a bounded wait, never a player held on an empty view.
    /// </para>
    /// <para>
    /// Presentation only, on the local machine (see Cutscenes.md § Multiplayer): whatever the speaker
    /// DOES meanwhile is held by the server, not by this.
    /// </para>
    /// </summary>
    public class DialogueCutscene : Cutscene
    {
        [Tooltip("The QuestActor id of who speaks.")]
        [SerializeField] private string speakerActorId;

        [Tooltip("Shown in bold before every line.")]
        [SerializeField] private string speakerName;

        [SerializeField, TextArea(2, 5)] private string[] lines = System.Array.Empty<string>();

        [Tooltip("Shortest time a line stays up, seconds. Longer lines stay as long as they type.")]
        [SerializeField, Min(0.5f)] private float secondsPerLine = 4f;

        [Tooltip("Pause between lines, seconds.")]
        [SerializeField, Min(0f)] private float gapSeconds = 0.3f;

        [Tooltip("Height above the speaker's feet the camera looks at, metres.")]
        [SerializeField, Min(0f)] private float lookHeight = 1.6f;

        [Tooltip("Longest the cutscene waits for the speaker to exist on this machine, seconds.")]
        [SerializeField, Min(0f)] private float speakerWait = 3f;

        [SerializeField, Min(0f)] private float blendInDuration = 0.6f;
        [SerializeField, Min(0f)] private float blendOutDuration = 0.6f;

        public override IEnumerator Play(CutsceneContext ctx)
        {
            if (ctx.PlayerCamera == null) yield break;

            QuestActor speaker = null;
            float waited = 0f;
            while (!QuestActor.TryFind(speakerActorId, out speaker) && waited < speakerWait)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            if (speaker == null)
            {
                Debug.LogWarning($"[Cutscene] '{name}' never found its speaker '{speakerActorId}'; skipping it.", this);
                yield break;
            }

            Transform cam = ctx.PlayerCamera.transform;
            Quaternion startRot = cam.rotation;
            Vector3 lookAt = speaker.transform.position + Vector3.up * lookHeight;
            Quaternion lookRot = Quaternion.LookRotation(lookAt - cam.position);

            yield return LookAtCutscene.BlendRotation(cam, startRot, lookRot, blendInDuration);

            NpcDialogPopupUI popup = NpcDialogPopupUI.Instance;
            foreach (string line in lines)
            {
                if (popup == null || string.IsNullOrWhiteSpace(line)) continue;

                string shown = string.IsNullOrEmpty(speakerName) ? line : $"<b>{speakerName}</b>  {line}";
                popup.Show(shown, secondsPerLine, speaker.transform);
                while (popup.IsVisible) yield return null;
                yield return new WaitForSecondsRealtime(gapSeconds);
            }

            yield return LookAtCutscene.BlendRotation(cam, lookRot, startRot, blendOutDuration);
        }
    }
}
