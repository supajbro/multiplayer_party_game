using CouchGuys.Input;
using UnityEditor;

namespace CouchGuys.Editor
{
    [InitializeOnLoad]
    internal static class CaptureAndPromoteIntegration
    {
        static CaptureAndPromoteIntegration()
        {
            // Keep the reusable plugin independent of the game's runtime assembly.
            // A type lookup also allows the plugin folder to be removed independently.
            bool available = System.Type.GetType("CaptureAndPromote.CaptureService, CaptureAndPromote.Editor") != null;
            ScreenshotHandler.EditorOwnsScreenshotHotkey = () => available;
            var api = System.Type.GetType("CaptureAndPromote.CaptureHighlight, CaptureAndPromote.Runtime");
            var priority = System.Type.GetType("CaptureAndPromote.HighlightPriority, CaptureAndPromote.Runtime");
            if (api != null && priority != null)
            {
                var mark = api.GetMethod("Mark");
                var isCapturing = (System.Func<bool>)System.Delegate.CreateDelegate(typeof(System.Func<bool>),
                    api.GetProperty("IsCapturing").GetMethod);
                object high = System.Enum.Parse(priority, "High");
                CouchGuys.Integrations.CaptureAndPromoteEvents.Receiver = (label, category) =>
                {
                    if (isCapturing() && EditorPrefs.GetBool(IntegrationKey, true))
                        mark.Invoke(null, new object[] { label, high, category, "" });
                };
            }
        }

        private const string IntegrationKey = "CaptureAndPromote.CouchGuysEvents";
        [MenuItem("Couch Guys/Capture/Enable Automatic Highlights")]
        private static void ToggleIntegration() => EditorPrefs.SetBool(IntegrationKey, !EditorPrefs.GetBool(IntegrationKey, true));

        [MenuItem("Couch Guys/Capture/Enable Automatic Highlights", true)]
        private static bool ValidateIntegration()
        {
            Menu.SetChecked("Couch Guys/Capture/Enable Automatic Highlights", EditorPrefs.GetBool(IntegrationKey, true));
            return true;
        }
    }
}
