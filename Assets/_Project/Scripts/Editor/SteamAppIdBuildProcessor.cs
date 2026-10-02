using System.IO;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace CouchGuys.Editor
{
    /// <summary>
    /// Copies the repository Steam development configuration beside desktop builds.
    /// </summary>
    public sealed class SteamAppIdBuildProcessor : IPostprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPostprocessBuild(BuildReport report)
        {
            string sourcePath = Path.Combine(Directory.GetCurrentDirectory(), "steam_appid.txt");
            if (!File.Exists(sourcePath))
            {
                Debug.LogWarning("steam_appid.txt is missing from the project root; Steam development builds may not initialise.");
                return;
            }

            string buildDirectory = Path.GetDirectoryName(report.summary.outputPath);
            if (string.IsNullOrEmpty(buildDirectory))
            {
                Debug.LogWarning("Could not determine the build directory for steam_appid.txt.");
                return;
            }

            File.Copy(sourcePath, Path.Combine(buildDirectory, "steam_appid.txt"), true);
        }
    }
}
