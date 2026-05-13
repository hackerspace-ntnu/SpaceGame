using System.Collections;
using UnityEngine;

/// <summary>
/// Plays a <see cref="Cutscene"/> during the out-phase of a transition. The destination
/// teleport is delayed until the cutscene finishes, so the player visibly walks through
/// the door before the scene loads.
///
/// Cutscene binding is resolved in this order:
///   1. <see cref="cutsceneOverride"/> on the asset (rare — only useful if every door
///      using this effect plays the exact same cutscene).
///   2. A <c>Cutscene</c> MonoBehaviour on the SceneTransition GameObject (the common
///      case — per-door config lives on the door itself).
/// </summary>
[CreateAssetMenu(fileName = "Effect_WalkThroughCutscene", menuName = "Scene Management/Effects/Walk Through Cutscene")]
public class WalkThroughCutsceneEffect : SceneTransitionEffect
{
    [Tooltip("Optional. If set, this cutscene plays for every transition that uses this " +
             "effect asset. Leave null to use the Cutscene component on the SceneTransition " +
             "GameObject (the usual per-door pattern).")]
    [SerializeField] private Cutscene cutsceneOverride;

    public override TransitionChannel Channel => TransitionChannel.Camera;

    public override EffectHandle Begin(SceneTransition host)
    {
        Cutscene cutscene = cutsceneOverride;
        if (cutscene == null && host != null) cutscene = host.GetComponent<Cutscene>();

        var handle = new CutsceneHandle(cutscene, host != null ? host.LastInitiator : null);
        handle.StartOut();
        return handle;
    }

    private class CutsceneHandle : EffectHandle
    {
        private readonly Cutscene cutscene;
        private readonly GameObject initiator;
        private bool outDone;

        public CutsceneHandle(Cutscene cutscene, GameObject initiator)
        {
            this.cutscene = cutscene;
            this.initiator = initiator;
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
            yield return CutsceneRunner.PlayAndAwait(cutscene, initiator);
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
