using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using SpaceGame.Presentation;

namespace SpaceGame.Core
{
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

        public override EffectHandle Begin(SceneTransition host)
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

            private Coroutine outRoutine;
            private Coroutine inRoutine;
            private bool outDone;
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
                outRoutine = LetterboxOverlay.Instance.StartCoroutine(RunOut());
            }

            public override IEnumerator AwaitOutPhase()
            {
                while (!outDone) yield return null;
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

            private IEnumerator RunOut()
            {
                // Drive the fade-out ourselves so the orchestrator can await its completion
                // before kicking off the (potentially main-thread-stalling) destination load.
                // Without this gate the load freeze can swallow the entire fade.
                yield return LetterboxOverlay.Instance.FadeToBlackAsync(outDur);
                outDone = true;
                outRoutine = null;
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
                    if (skippable && Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
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
}
