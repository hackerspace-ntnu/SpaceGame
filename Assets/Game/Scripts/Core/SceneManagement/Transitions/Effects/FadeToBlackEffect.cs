using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using SpaceGame.Diagnostics;
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
                // The owner is the overlay, not this: a FadeHandle is a plain object and the
                // routine borrows the (DontDestroyOnLoad) overlay's coroutines.
                //
                // FinishOut is the teardown because AwaitOutPhase spins on that one flag. An
                // out-phase that dies without setting it hangs the whole transition on a black
                // screen with the destination never loading and nothing in the console.
                outRoutine = LetterboxOverlay.Instance.StartCoroutine(Fault.Coroutine(
                    LetterboxOverlay.Instance, "FadeToBlack.Out", RunOut(), FinishOut));
            }

            public override IEnumerator AwaitOutPhase()
            {
                while (!outDone) yield return null;
            }

            public override void End()
            {
                if (ended) return;
                ended = true;
                // AbortIn rather than FinishIn: a fade-in that dies leaves the black it was
                // fading out of, which is a permanently black screen, AND leaves AwaitCompletion
                // spinning on a routine that is not coming back.
                inRoutine = LetterboxOverlay.Instance.StartCoroutine(Fault.Coroutine(
                    LetterboxOverlay.Instance, "FadeToBlack.In", RunIn(), AbortIn));
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
                FinishOut();
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

                FinishIn();
            }

            /// <summary>Releases anyone in <see cref="AwaitOutPhase"/>. The tail of RunOut, and its teardown.</summary>
            private void FinishOut()
            {
                outDone = true;
                outRoutine = null;
            }

            /// <summary>Releases anyone in <see cref="AwaitCompletion"/>. The tail of RunIn.</summary>
            private void FinishIn()
            {
                inDone = true;
                inRoutine = null;
            }

            /// <summary>
            /// The ending the spacebar skip already takes: clear the screen, then release the
            /// waiters. RunIn's teardown, because a dead fade-in has to give back the black.
            /// </summary>
            private void AbortIn()
            {
                LetterboxOverlay.Instance.SnapClear();
                FinishIn();
            }
        }
    }
}
