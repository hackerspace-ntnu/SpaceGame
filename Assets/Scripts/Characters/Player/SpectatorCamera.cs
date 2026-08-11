using UnityEngine;
using UnityEngine.InputSystem;
using SpaceGame.Presentation;

namespace SpaceGame.Characters
{
    // Free-fly camera activated on the local client when its player is eliminated
    // (out of lives, or killed in a no-respawn win condition) while the match
    // continues for everyone else. Destroyed when the player is restored or the
    // scene unloads; MatchResultUI simply renders on top of it at match end.
    //
    // Reads devices directly through the Input System rather than going through the
    // game's InputControls asset: spectating is a short-lived, self-contained mode
    // and doesn't warrant extending the gameplay action map. The project is set to
    // the new Input System only (activeInputHandler: 1), so the legacy Input class
    // is not an option here — it throws at runtime.
    public class SpectatorCamera : MonoBehaviour
    {
        [SerializeField] private float moveSpeed = 20f;
        [SerializeField] private float sprintMultiplier = 3f;
        [SerializeField] private float lookSensitivity = 0.1f;

        private float yaw;
        private float pitch;

        private void Start()
        {
            Vector3 euler = transform.eulerAngles;
            yaw = euler.y;
            pitch = euler.x;

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void Update()
        {
            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                Vector2 delta = mouse.delta.ReadValue();
                yaw += delta.x * lookSensitivity;
                pitch = Mathf.Clamp(pitch - delta.y * lookSensitivity, -89f, 89f);
                transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            Vector3 input = Vector3.zero;
            if (keyboard.wKey.isPressed) input.z += 1f;
            if (keyboard.sKey.isPressed) input.z -= 1f;
            if (keyboard.dKey.isPressed) input.x += 1f;
            if (keyboard.aKey.isPressed) input.x -= 1f;
            if (keyboard.spaceKey.isPressed) input.y += 1f;
            if (keyboard.leftCtrlKey.isPressed) input.y -= 1f;

            if (input == Vector3.zero) return;

            // Vertical stays world-aligned so looking down doesn't turn "up" into
            // "backwards" — standard free-cam behaviour.
            Vector3 direction = transform.right * input.x
                              + transform.forward * input.z
                              + Vector3.up * input.y;

            float speed = moveSpeed * (keyboard.leftShiftKey.isPressed ? sprintMultiplier : 1f);
            transform.position += direction.normalized * (speed * Time.deltaTime);
        }

        private void OnDestroy()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
