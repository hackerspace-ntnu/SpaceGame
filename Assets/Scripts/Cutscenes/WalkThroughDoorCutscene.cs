using System.Collections;
using UnityEngine;

// Walks the player camera up to and through a target point, simulating a "stepping through
// the door" beat. Player is locked by CutsceneDirector while this runs. Camera position
// and rotation are restored on the way out so the InteriorManager teleport takes over cleanly.
public class WalkThroughDoorCutscene : Cutscene
{
    [Tooltip("Point the camera moves toward (typically just past the door).")]
    [SerializeField] private Transform throughPoint;

    [Tooltip("Total seconds the cutscene takes from current position to throughPoint.")]
    [SerializeField] private float duration = 2.5f;

    [Tooltip("How long to ease the camera in and out of the move (0..1 of duration).")]
    [Range(0f, 0.5f)]
    [SerializeField] private float easeFraction = 0.25f;

    public override IEnumerator Play(CutsceneContext ctx)
    {
        if (throughPoint == null || ctx.PlayerCamera == null)
            yield break;

        Transform cam = ctx.PlayerCamera.transform;
        Vector3 startPos = cam.position;
        Quaternion startRot = cam.rotation;

        Vector3 endPos = throughPoint.position;
        Quaternion endRot = Quaternion.LookRotation(throughPoint.forward, Vector3.up);

        float elapsed = 0f;
        float dur = Mathf.Max(0.01f, duration);
        while (elapsed < dur)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / dur);
            float k = SmoothEase(t, easeFraction);
            cam.position = Vector3.Lerp(startPos, endPos, k);
            cam.rotation = Quaternion.Slerp(startRot, endRot, k);
            yield return null;
        }

        cam.position = endPos;
        cam.rotation = endRot;
    }

    // S-curve with configurable ramp-up/ramp-down. easeFrac=0 → linear, 0.5 → full smoothstep.
    private static float SmoothEase(float t, float easeFrac)
    {
        if (easeFrac <= 0f) return t;
        float a = Mathf.Clamp01(easeFrac);
        if (t < a)             return Mathf.SmoothStep(0f, 1f, t / a) * a;
        if (t > 1f - a)        return 1f - Mathf.SmoothStep(0f, 1f, (1f - t) / a) * a;
        return t;
    }
}
