using System;

namespace CaptureAndPromote
{
    public enum HighlightPriority { Low, Medium, High }

    /// <summary>Optional main-thread gameplay event reporting. Inactive captures cost no allocations.</summary>
    public static class CaptureHighlight
    {
#if UNITY_EDITOR
        public static Action<string, HighlightPriority, string, string> Receiver { get; set; }
        public static bool IsCapturing => Receiver != null;
#else
        public static bool IsCapturing => false;
#endif

        public static void Mark(string label, HighlightPriority priority = HighlightPriority.Medium,
            string category = "Gameplay", string description = "")
        {
#if UNITY_EDITOR
            try { Receiver?.Invoke(label, priority, category, description); }
            catch (Exception exception) { UnityEngine.Debug.LogWarning("Capture highlight failed. " + exception.Message); }
#endif
        }
    }
}
