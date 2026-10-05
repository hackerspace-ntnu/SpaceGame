// "Press the key you want": captures the next button the player presses, for rebinding.
//
// Built on InputSystem.onAnyButtonPress rather than PerformInteractiveRebinding because the voice
// keys are not bindings on an asset to override — they are plain paths in GameSettings — and
// because a voice key must be able to become UNBOUND, which an interactive rebind has no idea of.
//
// Escape cancels. Backspace or Delete clears, where clearing is allowed. The left mouse button is
// never taken: it is what clicked the button that started listening, and binding it would make
// every click in every menu a key press.
using System;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;

namespace SpaceGame.Presentation
{
    /// <summary>One capture at a time, for whichever settings row asked.</summary>
    public static class KeyCapture
    {
        private const string Escape = "<Keyboard>/escape";
        private const string Backspace = "<Keyboard>/backspace";
        private const string Delete = "<Keyboard>/delete";
        private const string LeftClick = "<Mouse>/leftButton";

        private static IDisposable listener;
        private static Action<string> onBound;
        private static Action onEnded;
        private static bool clearable;

        public static bool IsActive => listener != null;

        /// <summary>
        /// Starts listening. <paramref name="bound"/> receives the new binding path — or an empty
        /// string for "cleared" — and is not called at all on a cancel. <paramref name="ended"/>
        /// always runs, once, however the capture finishes.
        /// </summary>
        public static void Begin(bool allowClear, Action<string> bound, Action ended)
        {
            Cancel();

            clearable = allowClear;
            onBound = bound;
            onEnded = ended;
            listener = InputSystem.onAnyButtonPress.Call(OnPress);
        }

        /// <summary>Stops listening without changing anything. Safe when nothing is being captured.</summary>
        public static void Cancel() => Finish(null);

        /// <summary>
        /// The binding path for a control, device-agnostic within its layout: "/Keyboard1/v" on a
        /// second keyboard is still "&lt;Keyboard&gt;/v", so the binding follows the key rather than
        /// the particular device that happened to press it.
        /// </summary>
        public static string BindingPathOf(string deviceLayout, string devicePath, string controlPath)
        {
            if (string.IsNullOrEmpty(deviceLayout) || string.IsNullOrEmpty(controlPath)) return string.Empty;

            string relative = !string.IsNullOrEmpty(devicePath) &&
                              controlPath.StartsWith(devicePath, StringComparison.Ordinal)
                ? controlPath.Substring(devicePath.Length)
                : controlPath;

            return "<" + deviceLayout + ">" + relative;
        }

        /// <summary>
        /// What a binding reads as on a button: "V", "LEFT SHIFT", "BACK [MOUSE]". Keyboard keys
        /// drop the device, which is obvious; anything else keeps it, because "BACK" alone reads
        /// like Backspace.
        /// </summary>
        public static string Describe(string path)
        {
            if (string.IsNullOrEmpty(path)) return "UNBOUND";

            var options = path.StartsWith("<Keyboard>", StringComparison.Ordinal)
                ? InputControlPath.HumanReadableStringOptions.OmitDevice
                : InputControlPath.HumanReadableStringOptions.None;

            string text = InputControlPath.ToHumanReadableString(path, options);
            return string.IsNullOrEmpty(text) ? path : text.ToUpperInvariant();
        }

        private static void OnPress(InputControl control)
        {
            if (control?.device == null) return;

            string path = BindingPathOf(control.device.layout, control.device.path, control.path);

            if (path == Escape)
            {
                Finish(null);
                return;
            }

            if (path == Backspace || path == Delete)
            {
                Finish(clearable ? string.Empty : null);
                return;
            }

            // Keep listening: this press is the click that started the capture, or its twin.
            if (path == LeftClick) return;

            Finish(path);
        }

        private static void Finish(string path)
        {
            if (listener == null) return;

            listener.Dispose();
            listener = null;

            Action<string> bound = onBound;
            Action ended = onEnded;
            onBound = null;
            onEnded = null;

            if (path != null) bound?.Invoke(path);
            ended?.Invoke();
        }
    }
}
