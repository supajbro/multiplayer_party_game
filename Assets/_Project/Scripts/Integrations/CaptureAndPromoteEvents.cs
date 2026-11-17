using System;
using System.Diagnostics;

namespace CouchGuys.Integrations
{
    /// <summary>Optional Editor capture hooks. Calls are removed from standalone players.</summary>
    public static class CaptureAndPromoteEvents
    {
#if UNITY_EDITOR
        public static Action<string, string> Receiver;
#endif
        [Conditional("UNITY_EDITOR")]
        public static void Report(string label, string category)
        {
#if UNITY_EDITOR
            try { Receiver?.Invoke(label, category); }
            catch (Exception exception) { UnityEngine.Debug.LogWarning("Capture event hook failed. " + exception.Message); }
#endif
        }
    }
}
