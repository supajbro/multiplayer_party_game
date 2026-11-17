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
        }
    }
}
