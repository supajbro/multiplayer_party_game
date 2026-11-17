using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace CaptureAndPromote
{
    [Serializable]
    public sealed class HighlightMarker
    {
        public string id = Guid.NewGuid().ToString("N");
        public double timestampSeconds;
        public long frame;
        public string label = "Highlight";
        public string category = "Manual";
        public string priority = "Medium";
        public string description = "";
        public string source = "manual";
    }

    [Serializable]
    public sealed class RecordingMetadata
    {
        public int schemaVersion = 1;
        public string recordingId = Guid.NewGuid().ToString("N");
        public string recordingFilename;
        public string startedAtUtc;
        public string endedAtUtc;
        public string state = "recording";
        public string timebase = "constant-frame-rate";
        public double durationSeconds;
        public long frameCount;
        public int width;
        public int height;
        public double frameRate;
        public List<HighlightMarker> highlights = new List<HighlightMarker>();

        public static string SidecarPath(string mediaPath) => mediaPath + ".capture.json";

        public static RecordingMetadata Load(string mediaPath)
        {
            string path = SidecarPath(mediaPath);
            if (!File.Exists(path)) return null;
            RecordingMetadata data;
            try { data = JsonUtility.FromJson<RecordingMetadata>(File.ReadAllText(path)); }
            catch (Exception exception) when (exception is IOException || exception is ArgumentException)
            {
                // Atomic writes retain a previous valid snapshot for recovery.
                if (!File.Exists(path + ".bak")) throw;
                data = JsonUtility.FromJson<RecordingMetadata>(File.ReadAllText(path + ".bak"));
            }
            if (data == null || data.schemaVersion != 1 || string.IsNullOrEmpty(data.recordingId))
                throw new InvalidDataException("Unsupported or invalid capture metadata. Preserve this sidecar for recovery.");
            data.highlights ??= new List<HighlightMarker>();
            if (data.state == "recording" && !CaptureService.IsActiveMedia(mediaPath)) data.state = "interrupted";
            return data;
        }

        public void Save(string mediaPath)
        {
            string path = SidecarPath(mediaPath);
            string temporary = path + ".tmp";
            byte[] bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(this, true));
            using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                file.Write(bytes, 0, bytes.Length);
                file.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
            else File.Move(temporary, path);
        }
    }
}
