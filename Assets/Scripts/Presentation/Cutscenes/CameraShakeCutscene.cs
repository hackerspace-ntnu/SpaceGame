using System.Collections;
using UnityEngine;

// Short cinematic beat: small positional shake on the player camera, plus a tiny
// rotation jitter, decaying over `duration`. Restores the camera transform exactly
// on exit. Used for "the door is locked", "the ground rumbles", etc. — feedback
// moments where nothing changes in the world.
public class CameraShakeCutscene : Cutscene
{
    [SerializeField] private float duration = 1.0f;
    [Tooltip("Peak positional offset in meters. The actual offset decays over duration.")]
    [SerializeField] private float positionalAmplitude = 0.12f;
    [Tooltip("Peak rotational jitter in degrees.")]
    [SerializeField] private float rotationalAmplitude = 1.5f;
    [Tooltip("How fast the noise oscillates. Higher = jitterier.")]
    [SerializeField] private float frequency = 28f;

    public override IEnumerator Play(CutsceneContext ctx)
    {
        Camera cam = ctx.PlayerCamera;
        if (cam == null) yield break;

        Transform t = cam.transform;
        Vector3 startPos = t.localPosition;
        Quaternion startRot = t.localRotation;

        // Random per-axis seeds so each shake plays differently.
        float seedX = Random.value * 100f;
        float seedY = Random.value * 100f;
        float seedZ = Random.value * 100f;

        float elapsed = 0f;
        float dur = Mathf.Max(0.01f, duration);
        while (elapsed < dur)
        {
            elapsed += Time.unscaledDeltaTime;
            float falloff = 1f - Mathf.Clamp01(elapsed / dur);
            float fall2 = falloff * falloff; // ease-out the decay so it tapers fast

            float nx = (Mathf.PerlinNoise(seedX + elapsed * frequency, 0f) - 0.5f) * 2f;
            float ny = (Mathf.PerlinNoise(0f, seedY + elapsed * frequency) - 0.5f) * 2f;
            float nz = (Mathf.PerlinNoise(seedZ + elapsed * frequency, seedZ) - 0.5f) * 2f;

            t.localPosition = startPos + new Vector3(nx, ny, nz) * positionalAmplitude * fall2;
            t.localRotation = startRot * Quaternion.Euler(
                ny * rotationalAmplitude * fall2,
                nx * rotationalAmplitude * fall2,
                nz * rotationalAmplitude * fall2);

            yield return null;
        }

        t.localPosition = startPos;
        t.localRotation = startRot;
    }
}
