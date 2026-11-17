#if CAPTURE_PROMOTE_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
using System;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine.InputSystem;

namespace CaptureAndPromote
{
    [InitializeOnLoad]
    internal static class GameViewInput
    {
        private static bool screenshotHeld;
        private static bool recordingHeld;

        static GameViewInput()
        {
            CaptureShortcuts.GameViewInputAvailable = true;
            InputSystem.onAfterUpdate += OnInputUpdate;
            AssemblyReloadEvents.beforeAssemblyReload += () => InputSystem.onAfterUpdate -= OnInputUpdate;
        }

        private static void OnInputUpdate()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            bool screenshot = IsPressed(keyboard, CaptureShortcuts.ScreenshotId);
            bool recording = IsPressed(keyboard, CaptureShortcuts.RecordingId);
            bool focused = EditorApplication.isPlaying && CaptureShortcuts.IsGameViewFocused;
            // Queue onto the Editor update. Recorder can modify input/session objects during startup.
            if (focused && screenshot && !screenshotHeld) EditorApplication.delayCall += CaptureShortcuts.Screenshot;
            if (focused && recording && !recordingHeld) EditorApplication.delayCall += CaptureShortcuts.Recording;
            screenshotHeld = screenshot;
            recordingHeld = recording;
        }

        private static bool IsPressed(Keyboard keyboard, string id)
        {
            var binding = CaptureShortcuts.Binding(id);
            if (binding.Length != 1) return false;
            if (!Enum.TryParse(binding[0].keyCode.ToString(), true, out Key key) || key == Key.None) return false;
            bool shift = keyboard.shiftKey.isPressed;
            bool action = keyboard.ctrlKey.isPressed || keyboard.leftMetaKey.isPressed || keyboard.rightMetaKey.isPressed;
            bool alt = keyboard.altKey.isPressed;
            ShortcutModifiers modifiers = binding[0].modifiers;
            return keyboard[key].isPressed && shift == ((modifiers & ShortcutModifiers.Shift) != 0) &&
                action == ((modifiers & ShortcutModifiers.Action) != 0) && alt == ((modifiers & ShortcutModifiers.Alt) != 0);
        }
    }
}
#endif
