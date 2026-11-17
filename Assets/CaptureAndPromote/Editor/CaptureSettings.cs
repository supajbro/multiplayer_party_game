using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CaptureAndPromote
{
    public enum CaptureQuality { Low, Medium, High }

    [FilePath("UserSettings/CaptureAndPromote.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class CaptureSettings : ScriptableSingleton<CaptureSettings>
    {
        public int screenshotMultiplier = 1;
        public int videoWidth = 1920;
        public int videoHeight = 1080;
        public int videoFps = 60;
        public CaptureQuality videoQuality = CaptureQuality.High;
        public bool recordAudio = true;
        public string screenshotDirectory = "Captures/Screenshots";
        public string videoDirectory = "Captures/Videos";

        public void SaveSettings() => Save(true);

        public static string ResolveDirectory(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException("Choose an output directory.");
            string project = Directory.GetParent(Application.dataPath).FullName;
            string full = Path.GetFullPath(Path.IsPathRooted(directory) ? directory : Path.Combine(project, directory));
            string assets = Path.GetFullPath(Application.dataPath);
            StringComparison comparison = Application.platform == RuntimePlatform.WindowsEditor
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (full.Equals(assets, comparison) || full.StartsWith(assets + Path.DirectorySeparatorChar, comparison))
                throw new ArgumentException("Choose an output directory outside Assets.");
            return full;
        }

        public static string PrepareDirectory(string directory)
        {
            string full = ResolveDirectory(directory);
            Directory.CreateDirectory(full);
            string probe = Path.Combine(full, ".capture-write-test-" + Guid.NewGuid().ToString("N"));
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                1, FileOptions.DeleteOnClose)) { }
            return full;
        }

        public static string NewOutputPath(string directory, string extension)
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff",
                System.Globalization.CultureInfo.InvariantCulture);
            return Path.Combine(PrepareDirectory(directory), "Capture_" + timestamp + "_" +
                Guid.NewGuid().ToString("N").Substring(0, 8) + extension);
        }
    }
}
