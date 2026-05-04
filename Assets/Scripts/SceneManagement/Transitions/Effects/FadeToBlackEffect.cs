using System.Collections;
using UnityEngine;

/// <summary>
/// Fade screen to black during the "out" phase, fade back during the "in" phase.
/// Runs on LetterboxOverlay (DontDestroyOnLoad), so it survives any scene unload
/// triggered by the destination.
///
/// Spacebar shortens the in-phase fade once End() has been called; before End(),
/// skip is ignored (the load is still running).
/// </summary>
[CreateAssetMenu(fileName = "Effect_FadeToBlack", menuName = "Scene Management/Effects/Fade To Black")]
public class FadeToBlackEffect : SceneTransitionEffect
{
    [SerializeField] private float fadeOut = 0.25f;
    [SerializeField] private float fadeIn = 0.35f;
    [SerializeField] private bool skippableWithSpacebar = true;

    public override TransitionChannel Channel => TransitionChannel.Screen;

    public override EffectHandle Begin()
    {
        var handle = new FadeHandle(fadeOut, fadeIn, skippableWithSpacebar);
        handle.StartOut();
        return handle;
    }

    private class FadeHandle : EffectHandle
    {
        private readonly float outDur;
        private readonly float inDur;
        private readonly bool skippable;

        private Coroutine inRoutine;
        private bool inDone;
        private bool ended;

        public FadeHandle(float outDur, float inDur, bool skippable)
        {
            this.outDur = outDur;
            this.inDur = inDur;
            this.skippable = skippable;
        }

        public void StartOut()
        {
            LetterboxOverlay.Instance.FadeToBlackAsync(outDur);
        }

        public override void End()
        {
            if (ended) return;
            ended = true;
            inRoutine = LetterboxOverlay.Instance.StartCoroutine(RunIn());
        }

        public override IEnumerator AwaitCompletion()
        {
            while (!inDone) yield return null;
        }

        private IEnumerator RunIn()
        {
            // Run our own timed fade so we can short-circuit it with spacebar
            // mid-animation. LetterboxOverlay.FadeFromBlackAsync would also work
            // but isn't interruptible from outside.
            float t = 0f;
            float dur = Mathf.Max(0.0001f, inDur);
            LetterboxOverlay.Instance.FadeFromBlackAsync(dur);

            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                if (skippable && Input.GetKeyDown(KeyCode.Space))
                {
                    LetterboxOverlay.Instance.SnapClear();
                    break;
                }
                yield return null;
            }

            inDone = true;
            inRoutine = null;
        }
    }
}
