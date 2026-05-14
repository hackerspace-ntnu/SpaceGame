using System.Collections;
using UnityEngine;

// Spawns a temporary third-person camera behind+above+to-the-side of the player, drifting
// away from the player, while the player walks toward and through the door.
// Player's main camera is disabled for the duration; restored (and temp cam destroyed) on exit.
public class ThirdPersonWalkThroughCutscene : Cutscene
{
    [Header("Targets")]
    [Tooltip("Where the player ends up at the end of the cutscene (just past the door).")]
    [SerializeField] private Transform throughPoint;

    [Header("Camera framing (offset is in player-local space)")]
    [Tooltip("Local-space offset from the player at the START of the cutscene (right, up, back).")]
    [SerializeField] private Vector3 startOffset = new Vector3(2.5f, 2.5f, -3.5f);
    [Tooltip("Local-space offset from the player at the END of the cutscene. Larger negative Z = further behind.")]
    [SerializeField] private Vector3 endOffset = new Vector3(3.5f, 3f, -6f);

    [Header("Timing")]
    [SerializeField] private float duration = 3.0f;
    [Range(0f, 0.5f)]
    [SerializeField] private float easeFraction = 0.25f;

    public override IEnumerator Play(CutsceneContext ctx)
    {
        if (ctx.Player == null)
            yield break;

        Transform playerTr = ctx.Player.transform;
        Vector3 playerStart = playerTr.position;
        Quaternion playerRot = playerTr.rotation;
        Vector3 playerEnd = throughPoint != null ? throughPoint.position : playerStart + playerTr.forward * 3f;

        // Spawn the temp third-person camera. Disable the player camera so we can render with ours.
        Camera playerCam = ctx.PlayerCamera;
        bool playerCamPrevEnabled = playerCam != null && playerCam.enabled;
        if (playerCam != null) playerCam.enabled = false;

        AudioListener playerListener = playerCam != null ? playerCam.GetComponent<AudioListener>() : null;
        bool playerListenerPrev = playerListener != null && playerListener.enabled;
        if (playerListener != null) playerListener.enabled = false;

        var camGO = new GameObject("CutsceneTempCamera");
        var tempCam = camGO.AddComponent<Camera>();
        tempCam.fieldOfView = playerCam != null ? playerCam.fieldOfView : 60f;
        tempCam.nearClipPlane = playerCam != null ? playerCam.nearClipPlane : 0.1f;
        tempCam.farClipPlane = playerCam != null ? playerCam.farClipPlane : 1000f;
        camGO.AddComponent<AudioListener>();

        try
        {
            float elapsed = 0f;
            float dur = Mathf.Max(0.01f, duration);
            while (elapsed < dur)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / dur);
                float k = SmoothEase(t, easeFraction);

                // Move the player along the path so the body actually walks through.
                Vector3 playerPos = Vector3.Lerp(playerStart, playerEnd, k);
                MovePlayer(ctx.Player, playerPos, playerRot);

                // Camera offset eases from startOffset to endOffset, in the player's local frame
                // (which we anchor to playerRot — the rotation captured at start, so the framing is
                // stable even if some other system tries to rotate the player).
                Vector3 offsetLocal = Vector3.Lerp(startOffset, endOffset, k);
                Vector3 camPos = playerPos + playerRot * offsetLocal;

                tempCam.transform.position = camPos;
                tempCam.transform.rotation = Quaternion.LookRotation(playerPos + Vector3.up * 1.5f - camPos, Vector3.up);

                yield return null;
            }

            // Snap to final pose so the InteriorManager teleport receives a clean state.
            MovePlayer(ctx.Player, playerEnd, playerRot);
        }
        finally
        {
            if (camGO != null) Object.Destroy(camGO);
            if (playerCam != null) playerCam.enabled = playerCamPrevEnabled;
            if (playerListener != null) playerListener.enabled = playerListenerPrev;
        }
    }

    private static void MovePlayer(PlayerController player, Vector3 pos, Quaternion rot)
    {
        if (player == null) return;
        // Rigidbody-driven players: set kinematic-style position so physics doesn't fight us.
        if (player.TryGetComponent<Rigidbody>(out var rb))
        {
            rb.position = pos;
            rb.rotation = rot;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            return;
        }
        player.transform.SetPositionAndRotation(pos, rot);
    }

    private static float SmoothEase(float t, float easeFrac)
    {
        if (easeFrac <= 0f) return t;
        float a = Mathf.Clamp01(easeFrac);
        if (t < a)        return Mathf.SmoothStep(0f, 1f, t / a) * a;
        if (t > 1f - a)   return 1f - Mathf.SmoothStep(0f, 1f, (1f - t) / a) * a;
        return t;
    }
}
