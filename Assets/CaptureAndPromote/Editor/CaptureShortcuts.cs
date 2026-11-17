using System;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;

namespace CaptureAndPromote
{
    [InitializeOnLoad]
    public static class CaptureShortcuts
    {
        public const string ScreenshotId = "Capture & Promote/Screenshot";
        public const string RecordingId = "Capture & Promote/Toggle Recording";
        public const string HighlightId = "Capture & Promote/Bookmark Highlight";
        private static double lastHighlight = double.NegativeInfinity;
        private static readonly Dictionary<string, KeyCombination[]> bindings = new Dictionary<string, KeyCombination[]>();
        private static double lastScreenshot = double.NegativeInfinity;
        private static double lastRecording = double.NegativeInfinity;
        public static bool GameViewInputAvailable { get; set; }
        public static bool IsGameViewFocused => EditorWindow.focusedWindow != null &&
            EditorWindow.focusedWindow.GetType().Name == "GameView";

        static CaptureShortcuts()
        {
            var context = new CaptureContext();
            ShortcutManager.RegisterContext(context);
            ShortcutManager.instance.shortcutBindingChanged += _ => bindings.Clear();
            ShortcutManager.instance.activeProfileChanged += _ => bindings.Clear();
            AssemblyReloadEvents.beforeAssemblyReload += () => ShortcutManager.UnregisterContext(context);
#if ENABLE_LEGACY_INPUT_MANAGER && !ENABLE_INPUT_SYSTEM
            GameViewInputAvailable = true;
            EditorApplication.update += PollLegacyInput;
#endif
        }

        public sealed class CaptureContext : IShortcutContext
        {
            public bool active => EditorApplication.isPlaying;
        }

        [Shortcut(ScreenshotId, typeof(CaptureContext), KeyCode.F12)]
        private static void ScreenshotShortcut()
        {
            if (GameViewInputAvailable && IsGameViewFocused) return;
            Screenshot();
        }

        public static void Screenshot()
        {
            if (!Accept(ref lastScreenshot)) return;
            CaptureService.CaptureScreenshot();
        }

        [Shortcut(RecordingId, typeof(CaptureContext), KeyCode.F9)]
        private static void RecordingShortcut()
        {
            if (GameViewInputAvailable && IsGameViewFocused) return;
            Recording();
        }

        public static void Recording()
        {
            if (!Accept(ref lastRecording)) return;
            CaptureService.ToggleRecording();
        }

        [Shortcut(HighlightId, typeof(CaptureContext), KeyCode.F10)]
        private static void HighlightShortcut()
        {
            if (GameViewInputAvailable && IsGameViewFocused) return;
            Highlight();
        }

        public static void Highlight()
        {
            if (Accept(ref lastHighlight)) CaptureService.Bookmark();
        }

        private static bool Accept(ref double last)
        {
            double now = EditorApplication.timeSinceStartup;
            if (!EditorApplication.isPlaying || now - last < 0.25) return false;
            last = now;
            return true;
        }

        // Game view sends keys to the player and can bypass Editor shortcuts. Input adapters
        // only call this for a focused Game view and use the user's Shortcut Manager binding.
        public static KeyCombination[] Binding(string id)
        {
            if (!bindings.TryGetValue(id, out var binding))
            {
                binding = ShortcutManager.instance.GetShortcutBinding(id).keyCombinationSequence.ToArray();
                bindings.Add(id, binding);
            }
            return binding;
        }

#if ENABLE_LEGACY_INPUT_MANAGER && !ENABLE_INPUT_SYSTEM
        private static bool screenshotHeld;
        private static bool recordingHeld;
        private static bool highlightHeld;

        private static void PollLegacyInput()
        {
            if (!EditorApplication.isPlaying) return;
            bool screenshot = IsLegacyPressed(ScreenshotId);
            bool recording = IsLegacyPressed(RecordingId);
            bool highlight = IsLegacyPressed(HighlightId);
            bool focused = IsGameViewFocused;
            if (focused && screenshot && !screenshotHeld) Screenshot();
            if (focused && recording && !recordingHeld) Recording();
            if (focused && highlight && !highlightHeld) Highlight();
            screenshotHeld = screenshot;
            recordingHeld = recording;
            highlightHeld = highlight;
        }

        private static bool IsLegacyPressed(string id)
        {
            var binding = Binding(id);
            if (binding.Length != 1) return false;
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            bool action = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) ||
                Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand);
            bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            ShortcutModifiers modifiers = binding[0].modifiers;
            return Input.GetKey(binding[0].keyCode) && shift == ((modifiers & ShortcutModifiers.Shift) != 0) &&
                action == ((modifiers & ShortcutModifiers.Action) != 0) && alt == ((modifiers & ShortcutModifiers.Alt) != 0);
        }
#endif
    }
}
