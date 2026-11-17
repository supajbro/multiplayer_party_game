using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CaptureAndPromote
{
    public interface IVideoCapture : IDisposable
    {
        bool IsRecording { get; }
        string OutputPath { get; }
        long RecordedFrames { get; }
        void Start(CaptureSettings settings, string outputBasePath);
        void Stop();
    }

    [InitializeOnLoad]
    public static class CaptureService
    {
        public static Func<IVideoCapture> VideoFactory { private get; set; }
        public static bool RecorderAvailable => VideoFactory != null;
        public static bool IsRecording => video != null;
        public static double ElapsedSeconds => IsRecording ? EditorApplication.timeSinceStartup - startedAt : elapsed;
        public static string Status { get; private set; } = "Ready";
        public static bool HasError { get; private set; }
        public static event Action Changed;
        public static RecordingMetadata ActiveMetadata { get; private set; }
        public static string ActiveVideoPath => video == null ? null : videoPath;
        public static double MediaSeconds => video == null ? 0 : video.RecordedFrames / ActiveMetadata.frameRate;
        public static string HighlightConfirmation { get; private set; }
        public static double HighlightConfirmationUntil { get; private set; }
        private static readonly Dictionary<(string, string), double> lastEvents = new Dictionary<(string, string), double>();
        private static bool metadataDirty;
        private static double nextMetadataSave;
        private static int automaticCount;
        private static int mainThread;
        private static IVideoCapture video;
        private static double startedAt;
        private static double elapsed;
        private static string videoPath;
        private static string screenshotPath;
        private static double screenshotDeadline;
        private static bool stopping;
        private const string ActivePathKey = "CaptureAndPromote.ActiveVideo";

        static CaptureService()
        {
            EditorApplication.update += Update;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload += StopRecording;
            EditorApplication.quitting += StopRecording;
            mainThread = System.Threading.Thread.CurrentThread.ManagedThreadId;
            string interrupted = SessionState.GetString(ActivePathKey, "");
            if (!string.IsNullOrEmpty(interrupted))
            {
                SetStatus("Previous recording was interrupted. Check the output file at " + interrupted, true);
                SessionState.EraseString(ActivePathKey);
            }
        }

        public static void CaptureScreenshot()
        {
            if (!EditorApplication.isPlaying || EditorApplication.isCompiling)
            {
                SetStatus("Enter Play Mode to capture the Game view.", true);
                return;
            }
            if (screenshotPath != null) return;
            try
            {
                var settings = CaptureSettings.instance;
                if (settings.screenshotMultiplier < 1 || settings.screenshotMultiplier > 8)
                    throw new ArgumentException("Screenshot multiplier must be between 1 and 8.");
                screenshotPath = CaptureSettings.NewOutputPath(settings.screenshotDirectory, ".png");
                screenshotDeadline = EditorApplication.timeSinceStartup + 15;
                // Unity schedules a capture of the rendered Game view at the end of the frame.
                ScreenCapture.CaptureScreenshot(screenshotPath, settings.screenshotMultiplier);
                EditorApplication.QueuePlayerLoopUpdate();
                SetStatus("Saving screenshot...");
            }
            catch (Exception exception)
            {
                screenshotPath = null;
                SetStatus("Screenshot failed. " + exception.Message, true);
            }
        }

        public static void ToggleRecording()
        {
            if (IsRecording) StopRecording();
            else StartRecording();
        }

        public static void StartRecording()
        {
            if (IsRecording || stopping) return;
            try
            {
                if (!EditorApplication.isPlaying || EditorApplication.isCompiling)
                    throw new InvalidOperationException("Enter Play Mode before recording.");
                if (!RecorderAvailable)
                    throw new InvalidOperationException("Install Recorder 5.1 from Window > Package Management > Package Manager, using package name com.unity.recorder.");
                var settings = CaptureSettings.instance;
                if (settings.videoWidth < 16 || settings.videoHeight < 16 ||
                    settings.videoWidth > 4096 || settings.videoHeight > 2160 ||
                    settings.videoWidth % 2 != 0 || settings.videoHeight % 2 != 0)
                    throw new ArgumentException("Video dimensions must be even, from 16x16 through 4096x2160.");
                if (settings.videoFps < 1 || settings.videoFps > 120)
                    throw new ArgumentException("Video FPS must be between 1 and 120.");
                videoPath = CaptureSettings.NewOutputPath(settings.videoDirectory, "");
                SessionState.SetString(ActivePathKey, videoPath);
                video = VideoFactory();
                startedAt = EditorApplication.timeSinceStartup;
                video.Start(settings, videoPath);
                videoPath = video.OutputPath;
                SessionState.SetString(ActivePathKey, videoPath);
                ActiveMetadata = new RecordingMetadata
                {
                    recordingFilename = Path.GetFileName(videoPath),
                    startedAtUtc = DateTime.UtcNow.ToString("O"),
                    width = settings.videoWidth, height = settings.videoHeight, frameRate = settings.videoFps
                };
                ActiveMetadata.Save(videoPath);
                lastEvents.Clear();
                automaticCount = 0;
                nextMetadataSave = EditorApplication.timeSinceStartup + 2;
                CaptureHighlight.Receiver = OnGameplayHighlight;
                elapsed = 0;
                SetStatus("Recording Game view");
            }
            catch (Exception exception)
            {
                StopRecording("interrupted");
                SetStatus("Recording failed. " + exception.Message, true);
            }
        }

        public static void StopRecording() => StopRecording("complete");

        private static void StopRecording(string completionState)
        {
            if (video == null || stopping) return;
            stopping = true;
            CaptureHighlight.Receiver = null;
            elapsed = EditorApplication.timeSinceStartup - startedAt;
            var current = video;
            try
            {
                current.Stop();
                FinishMetadata(completionState, current.RecordedFrames);
                if (!File.Exists(videoPath) || new FileInfo(videoPath).Length == 0)
                    throw new IOException("Recorder did not produce a non-empty video file.");
                SetStatus("Recording finalised at " + videoPath);
                SessionState.EraseString(ActivePathKey);
            }
            catch (Exception exception)
            {
                try { FinishMetadata("interrupted", current.RecordedFrames); }
                catch (Exception metadataException) { Debug.LogException(metadataException); }
                SetStatus("Recording could not be finalised. Keep files at " + videoPath + ". " + exception.Message, true);
            }
            finally
            {
                try { current.Dispose(); }
                catch (Exception exception) { Debug.LogException(exception); }
                video = null;
                ActiveMetadata = null;
                lastEvents.Clear();
                stopping = false;
                Changed?.Invoke();
            }
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode) StopRecording();
        }

        private static void Update()
        {
            if (video != null && !stopping)
            {
                try
                {
                    if (ActiveMetadata != null)
                    {
                        long count = video.RecordedFrames;
                        if (count != ActiveMetadata.frameCount)
                        {
                            if (ActiveMetadata.frameCount == 0) ActiveMetadata.startedAtUtc = DateTime.UtcNow.ToString("O");
                            ActiveMetadata.frameCount = count;
                            ActiveMetadata.durationSeconds = count / ActiveMetadata.frameRate;
                            metadataDirty = true;
                        }
                        if (metadataDirty && EditorApplication.timeSinceStartup >= nextMetadataSave)
                        {
                            nextMetadataSave = EditorApplication.timeSinceStartup + 2;
                            try { ActiveMetadata.Save(videoPath); metadataDirty = false; }
                            catch (Exception exception) { SetStatus("Metadata checkpoint failed. " + exception.Message, true); }
                        }
                    }
                    if (!video.IsRecording)
                    {
                        StopRecording("interrupted");
                        SetStatus("Recorder stopped unexpectedly. Check the Console and output files.", true);
                    }
                }
                catch (Exception exception)
                {
                    StopRecording("interrupted");
                    SetStatus("Recorder failed. " + exception.Message, true);
                }
            }
            if (screenshotPath == null) return;
            try
            {
                if (File.Exists(screenshotPath) && new FileInfo(screenshotPath).Length > 0)
                {
                    SetStatus("Screenshot saved at " + screenshotPath);
                    screenshotPath = null;
                }
                else if (EditorApplication.timeSinceStartup > screenshotDeadline)
                {
                    screenshotPath = null;
                    SetStatus("Screenshot timed out. Keep the Game view visible and unpause Play Mode, then retry.", true);
                }
            }
            catch (Exception exception)
            {
                screenshotPath = null;
                SetStatus("Screenshot verification failed. " + exception.Message, true);
            }
        }

        public static void SetStatus(string message, bool error = false)
        {
            Status = message;
            HasError = error;
            if (error) Debug.LogError("[Capture & Promote] " + message);
            else Debug.Log("[Capture & Promote] " + message);
            Changed?.Invoke();
        }

        public static bool IsActiveMedia(string path) => video != null &&
            string.Equals(Path.GetFullPath(path), videoPath, Application.platform == RuntimePlatform.WindowsEditor
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

        public static void Bookmark()
        {
            if (video == null || ActiveMetadata == null || stopping) return;
            AddHighlight("Highlight " + (ActiveMetadata.highlights.Count + 1), HighlightPriority.Medium, "Manual", "", "manual");
        }

        private static void OnGameplayHighlight(string label, HighlightPriority priority, string category, string description)
        {
            if (System.Threading.Thread.CurrentThread.ManagedThreadId != mainThread || video == null ||
                ActiveMetadata == null || stopping || !CaptureSettings.instance.automaticHighlights || string.IsNullOrWhiteSpace(label)) return;
            var settings = CaptureSettings.instance;
            if (automaticCount >= Math.Max(0, settings.maximumAutomaticHighlights)) return;
            double now = MediaSeconds;
            var key = (category ?? "Gameplay", label);
            if (lastEvents.TryGetValue(key, out double last) && now - last < Math.Max(0, settings.eventCooldownSeconds)) return;
            lastEvents[key] = now;
            automaticCount++;
            AddHighlight(label, priority, category, description, "event");
        }

        private static void AddHighlight(string label, HighlightPriority priority, string category, string description, string source)
        {
            ActiveMetadata.highlights.Add(new HighlightMarker
            {
                timestampSeconds = MediaSeconds, frame = video.RecordedFrames,
                label = label, priority = priority.ToString(), category = category ?? "Gameplay",
                description = description ?? "", source = source
            });
            metadataDirty = true;
            HighlightConfirmation = "Bookmarked " + label + " at " + MediaSeconds.ToString("F3") + "s";
            HighlightConfirmationUntil = EditorApplication.timeSinceStartup + 3;
            Changed?.Invoke();
        }

        public static void SaveMetadata(string path, RecordingMetadata data)
        {
            if (IsActiveMedia(path)) metadataDirty = true;
            else data.Save(path);
            Changed?.Invoke();
        }

        private static void FinishMetadata(string state, long frames)
        {
            if (ActiveMetadata == null) return;
            ActiveMetadata.frameCount = frames;
            ActiveMetadata.durationSeconds = frames / ActiveMetadata.frameRate;
            ActiveMetadata.endedAtUtc = DateTime.UtcNow.ToString("O");
            ActiveMetadata.state = state;
            ActiveMetadata.Save(videoPath);
            metadataDirty = false;
        }
    }
}
