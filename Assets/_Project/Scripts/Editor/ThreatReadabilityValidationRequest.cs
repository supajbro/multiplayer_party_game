using System;
using System.IO;
using UnityEditor;

// Temporary local test launcher; removed after verification. Never authors assets.
[InitializeOnLoad]
internal static class ThreatReadabilityValidationRequest
{
    static ThreatReadabilityValidationRequest() => EditorApplication.update += Poll;
    private static void Poll()
    {
        const string request = "Temp/ThreatReadability.test-request";
        if (!File.Exists(request) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(request);
        try { CouchGuys.Tests.Editor.ThreatReadabilityPlayProbe.Run(); }
        catch (Exception error) { File.WriteAllText("Tools/ThreatReadabilityValidation.txt", "FAIL: " + error); }
    }
}
