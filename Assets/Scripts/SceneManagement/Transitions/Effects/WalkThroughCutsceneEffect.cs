using System.Collections;
using UnityEngine;

/// <summary>
/// Plays a sibling <see cref="Cutscene"/> MonoBehaviour during the out-phase of a
/// transition. The destination teleport is delayed until the cutscene finishes, so
/// the player visibly walks through the door before the scene loads.
///
/// Per-door config (target points, camera offsets, …) lives on the Cutscene
/// component itself — this SO is just the "play whichever Cutscene is on the
/// triggering GameObject" wiring.
/// </summary>
[CreateAssetMenu(fileName = "Effect_WalkThroughCutscene", menuName = "Scene Management/Effects/Walk Through Cutscene")]
public class WalkThroughCutsceneEffect : SceneTransitionEffect
{
    public override TransitionChannel Channel => TransitionChannel.Camera;

    public override EffectHandle Begin(SceneTransition host)
    {
        Cutscene cutscene = host != null ? host.GetComponent<Cutscene>() : null;
        var handle = new CutsceneHandle(cutscene);
        handle.StartOut();
        return handle;
    }

    private class CutsceneHandle : EffectHandle
    {
        private readonly Cutscene cutscene;
        private bool outDone;

        public CutsceneHandle(Cutscene cutscene)
        {
            this.cutscene = cutscene;
        }

        public void StartOut()
        {
            if (cutscene == null || CutsceneDirector.Instance == null)
            {
                outDone = true;
                return;
            }
            CutsceneDirector.Instance.StartCoroutine(RunOut());
        }

        private IEnumerator RunOut()
        {
            yield return CutsceneRunner.PlayAndAwait(cutscene);
            outDone = true;
        }

        public override IEnumerator AwaitOutPhase()
        {
            while (!outDone) yield return null;
        }

        public override void End() { }

        public override IEnumerator AwaitCompletion() { yield break; }
    }
}
