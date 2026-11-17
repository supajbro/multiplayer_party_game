using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using UnityEditor;
using UnityEngine;

namespace CaptureAndPromote
{
    internal sealed class CaptureLibraryView : IDisposable
    {
        private sealed class Entry
        {
            public string path;
            public bool screenshot;
            public long size;
            public DateTime date;
            public RecordingMetadata metadata;
            public bool hasMetadata;
            public double duration;
            public int width, height, highlightCount;
            public double fps;
            public string error;
        }

        private readonly List<Entry> entries = new List<Entry>();
        private readonly List<Entry> filtered = new List<Entry>();
        private readonly Dictionary<string, Texture2D> thumbnails = new Dictionary<string, Texture2D>();
        private readonly Queue<string> thumbnailRequests = new Queue<string>();
        private readonly HashSet<string> queued = new HashSet<string>();
        private readonly Queue<string> thumbnailOrder = new Queue<string>();
        private readonly HighlightTimeline timeline = new HighlightTimeline();
        private IEnumerator<string> scan;
        private Entry selected;
        private Vector2 scroll;
        private int filter;
        private string message;
        private bool initialized;
        private readonly Action repaint;

        public CaptureLibraryView(Action repaint)
        {
            this.repaint = repaint;
            EditorApplication.update += Update;
        }

        public void Refresh()
        {
            scan?.Dispose();
            entries.Clear(); filtered.Clear(); selected = null; message = null;
            ClearThumbnails();
            scan = Enumerate().GetEnumerator();
            initialized = true;
        }

        private static IEnumerable<string> Enumerate()
        {
            var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                CaptureSettings.ResolveDirectory(CaptureSettings.instance.screenshotDirectory),
                CaptureSettings.ResolveDirectory(CaptureSettings.instance.videoDirectory)
            };
            foreach (string directory in directories)
                if (Directory.Exists(directory))
                    foreach (string file in Directory.EnumerateFiles(directory))
                        yield return file;
        }

        private void Update()
        {
            if (scan != null)
            {
                var budget = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    while (budget.ElapsedMilliseconds < 6)
                    {
                        if (!scan.MoveNext())
                        {
                            scan.Dispose(); scan = null;
                            entries.Sort((a, b) => b.date.CompareTo(a.date));
                            ApplyFilter(); break;
                        }
                        string path = scan.Current;
                        string extension = Path.GetExtension(path).ToLowerInvariant();
                        if (extension != ".png" && extension != ".mp4" && extension != ".webm" && extension != ".mov") continue;
                        var info = new FileInfo(path);
                        var entry = new Entry { path = path, screenshot = extension == ".png", size = info.Length, date = info.LastWriteTime };
                        if (!entry.screenshot)
                        {
                            try
                            {
                                var metadata = CaptureService.IsActiveMedia(path) ? CaptureService.ActiveMetadata : RecordingMetadata.Load(path);
                                if (metadata != null)
                                {
                                    entry.hasMetadata = true;
                                    entry.duration = metadata.durationSeconds;
                                    entry.width = metadata.width; entry.height = metadata.height; entry.fps = metadata.frameRate;
                                    entry.highlightCount = metadata.highlights.Count;
                                    if (DateTime.TryParse(metadata.startedAtUtc, CultureInfo.InvariantCulture,
                                        DateTimeStyles.RoundtripKind, out var recordedDate)) entry.date = recordedDate.ToLocalTime();
                                }
                            }
                            catch (Exception exception) { entry.error = exception.Message; }
                        }
                        entries.Add(entry);
                        if (Matches(entry)) filtered.Add(entry);
                    }
                }
                catch (Exception exception) { message = exception.Message; scan?.Dispose(); scan = null; }
                repaint();
            }
            if (thumbnailRequests.Count == 0) return;
            string request = thumbnailRequests.Dequeue();
            Texture2D thumbnail = null;
            try { thumbnail = CreateThumbnail(request); }
            catch (Exception) { /* Unreadable/oversized images retain the file icon. */ }
            thumbnails[request] = thumbnail;
            thumbnailOrder.Enqueue(request);
            while (thumbnailOrder.Count > 24)
            {
                string oldest = thumbnailOrder.Dequeue();
                if (thumbnails.TryGetValue(oldest, out var texture) && texture != null) UnityEngine.Object.DestroyImmediate(texture);
                thumbnails.Remove(oldest); queued.Remove(oldest);
            }
            repaint();
        }

        public void Draw()
        {
            if (!initialized) Refresh();
            using (new EditorGUILayout.HorizontalScope())
            {
                int next = EditorGUILayout.Popup(filter, new[] { "All captures", "Screenshots", "Videos", "With highlights" });
                if (next != filter) { filter = next; ApplyFilter(); scroll = Vector2.zero; }
                if (GUILayout.Button("Refresh", GUILayout.Width(70))) Refresh();
            }
            GUILayout.Label(filtered.Count + " captures" + (scan != null ? " • Scanning..." : ""), EditorStyles.miniLabel);
            if (message != null) EditorGUILayout.HelpBox(message, MessageType.Warning);
            const float height = 66;
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.Height(240));
            Rect rows = GUILayoutUtility.GetRect(0, filtered.Count * height);
            int first = Mathf.Max(0, (int)(scroll.y / height));
            int end = Mathf.Min(filtered.Count, first + 5);
            for (int i = first; i < end; i++)
            {
                Entry entry = filtered[i];
                Rect row = new Rect(rows.x, rows.y + i * height, rows.width, height - 3);
                if (GUI.Button(row, GUIContent.none, entry == selected ? EditorStyles.helpBox : GUI.skin.box)) Select(entry);
                Rect preview = new Rect(row.x + 4, row.y + 4, 80, 50);
                if (entry.screenshot)
                {
                    if (thumbnails.TryGetValue(entry.path, out var texture) && texture != null) GUI.DrawTexture(preview, texture, ScaleMode.ScaleToFit);
                    else
                    {
                        GUI.Label(preview, EditorGUIUtility.IconContent("Texture Icon"));
                        if (queued.Add(entry.path)) thumbnailRequests.Enqueue(entry.path);
                    }
                }
                else GUI.Label(preview, EditorGUIUtility.IconContent("MovieTexture Icon"));
                string details = !entry.hasMetadata ? "" : entry.duration.ToString("F2") + "s • " +
                    entry.width + "×" + entry.height + " • " + entry.fps + " FPS";
                GUI.Label(new Rect(row.x + 90, row.y + 3, row.width - 94, 20), Path.GetFileName(entry.path), EditorStyles.boldLabel);
                GUI.Label(new Rect(row.x + 90, row.y + 23, row.width - 94, 18), entry.date.ToString("g") + " • " + EditorUtility.FormatBytes(entry.size));
                GUI.Label(new Rect(row.x + 90, row.y + 41, row.width - 94, 18), details, EditorStyles.miniLabel);
            }
            EditorGUILayout.EndScrollView();
            if (selected == null) return;
            GUILayout.Label(Path.GetFileName(selected.path), EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Open File"))
                {
                    try { EditorUtility.OpenWithDefaultApp(selected.path); }
                    catch (Exception exception) { message = exception.Message; }
                }
                if (GUILayout.Button("Reveal File")) EditorUtility.RevealInFinder(selected.path);
            }
            if (selected.error != null) EditorGUILayout.HelpBox(selected.error, MessageType.Warning);
            if (!selected.screenshot)
                timeline.Draw(selected.path, CaptureService.IsActiveMedia(selected.path) ? CaptureService.ActiveMetadata : selected.metadata);
        }

        private void Select(Entry entry)
        {
            if (selected != null) selected.metadata = null;
            selected = entry;
            if (entry.screenshot) return;
            try { entry.metadata = CaptureService.IsActiveMedia(entry.path) ? CaptureService.ActiveMetadata : RecordingMetadata.Load(entry.path); }
            catch (Exception exception) { entry.error = exception.Message; }
        }

        private bool Matches(Entry entry) => filter == 0 || (filter == 1 && entry.screenshot) ||
            (filter == 2 && !entry.screenshot) || (filter == 3 && entry.highlightCount > 0);

        private void ApplyFilter()
        {
            filtered.Clear();
            foreach (var entry in entries) if (Matches(entry)) filtered.Add(entry);
        }

        private static Texture2D CreateThumbnail(string path)
        {
            var info = new FileInfo(path);
            if (info.Length > 32 * 1024 * 1024) return null;
            using (var stream = File.OpenRead(path))
            {
                var header = new byte[24];
                if (stream.Read(header, 0, header.Length) != header.Length) return null;
                uint width = ((uint)header[16] << 24) | ((uint)header[17] << 16) | ((uint)header[18] << 8) | header[19];
                uint height = ((uint)header[20] << 24) | ((uint)header[21] << 16) | ((uint)header[22] << 8) | header[23];
                if ((long)width * height > 16777216) return null;
            }
            var source = new Texture2D(2, 2);
            RenderTexture target = null;
            RenderTexture previous = RenderTexture.active;
            try
            {
                if (!source.LoadImage(File.ReadAllBytes(path))) return null;
                float scale = Mathf.Min(256f / source.width, 144f / source.height);
                int width = Mathf.Max(1, Mathf.RoundToInt(source.width * scale));
                int height = Mathf.Max(1, Mathf.RoundToInt(source.height * scale));
                target = RenderTexture.GetTemporary(width, height, 0);
                Graphics.Blit(source, target);
                RenderTexture.active = target;
                var result = new Texture2D(width, height, TextureFormat.RGB24, false) { hideFlags = HideFlags.HideAndDontSave };
                result.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                result.Apply();
                return result;
            }
            finally
            {
                RenderTexture.active = previous;
                if (target != null) RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.DestroyImmediate(source);
            }
        }

        private void ClearThumbnails()
        {
            foreach (var texture in thumbnails.Values) if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
            thumbnails.Clear(); thumbnailRequests.Clear(); thumbnailOrder.Clear(); queued.Clear();
        }

        public void Dispose() { EditorApplication.update -= Update; scan?.Dispose(); ClearThumbnails(); }
    }
}
