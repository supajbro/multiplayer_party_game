using System;
using System.IO;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
internal static class PlayerCharacterRequest
{
    static PlayerCharacterRequest() => EditorApplication.update += Poll;
    private static void Poll()
    {
        const string request = "Temp/PlayerCharacterSelection.request";
        if (!File.Exists(request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        string command = File.ReadAllText(request).Trim();File.Delete(request);
        try
        {
            if (command == "probe") CouchGuys.Editor.PlayerCharacterSelectionProbe.Run();
            else if (command == "finish")
            {
                var root = PrefabUtility.LoadPrefabContents(CouchGuys.Editor.PlayerCharacterAuthoring.WheeliePath);
                try
                {
                    CouchGuys.Editor.PlayerCharacterAuthoring.ConfigurePlayerPresentation(root);
                    PrefabUtility.SaveAsPrefabAsset(root, CouchGuys.Editor.PlayerCharacterAuthoring.WheeliePath);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
                CouchGuys.Editor.PlayerCharacterAuthoring.Validate();
            }
            else CouchGuys.Editor.PlayerCharacterAuthoring.Install();
        }
        catch (Exception e) { File.WriteAllText("Temp/PlayerCharacterSelection.result", e.ToString());Debug.LogException(e); }
    }
}
