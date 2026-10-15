using System;
using UnityEngine;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// A camera of the cutscene's own, standing in for the player's for as long as it is held: the
    /// player's camera and its <see cref="AudioListener"/> are switched off, a temporary camera with
    /// the same lens and a listener of its own takes over, and <see cref="Dispose"/> puts everything
    /// back. For a shot the first-person camera cannot frame — over the shoulder, round a subject.
    /// Hold it in a <c>try/finally</c> (or a <c>using</c>) so a cutscene that throws still gives the
    /// player their view back.
    /// </summary>
    public sealed class CutsceneCamera : IDisposable
    {
        private readonly Camera playerCamera;
        private readonly bool playerCameraWasEnabled;
        private readonly AudioListener playerListener;
        private readonly bool playerListenerWasEnabled;
        private GameObject temporary;

        public Camera Camera { get; }

        public CutsceneCamera(Camera player)
        {
            playerCamera = player;
            playerCameraWasEnabled = player != null && player.enabled;
            if (player != null) player.enabled = false;

            playerListener = player != null ? player.GetComponent<AudioListener>() : null;
            playerListenerWasEnabled = playerListener != null && playerListener.enabled;
            if (playerListener != null) playerListener.enabled = false;

            temporary = new GameObject("CutsceneTempCamera");
            Camera = temporary.AddComponent<Camera>();
            Camera.fieldOfView = player != null ? player.fieldOfView : 60f;
            Camera.nearClipPlane = player != null ? player.nearClipPlane : 0.1f;
            Camera.farClipPlane = player != null ? player.farClipPlane : 1000f;
            temporary.AddComponent<AudioListener>();
        }

        public void Dispose()
        {
            if (temporary != null) UnityEngine.Object.Destroy(temporary);
            temporary = null;

            if (playerCamera != null) playerCamera.enabled = playerCameraWasEnabled;
            if (playerListener != null) playerListener.enabled = playerListenerWasEnabled;
        }
    }
}
