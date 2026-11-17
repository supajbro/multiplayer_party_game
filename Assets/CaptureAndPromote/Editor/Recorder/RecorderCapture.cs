#if CAPTURE_PROMOTE_RECORDER
using System;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Encoder;
using UnityEditor.Recorder.Input;
using UnityEngine;

namespace CaptureAndPromote
{
    [InitializeOnLoad]
    internal sealed class RecorderCapture : IVideoCapture
    {
        private RecorderController controller;
        private RecorderControllerSettings controllerSettings;
        private MovieRecorderSettings movie;
        public string OutputPath { get; private set; }

        static RecorderCapture() => CaptureService.VideoFactory = () => new RecorderCapture();
        public bool IsRecording => controller != null && controller.IsRecording();

        public void Start(CaptureSettings settings, string outputBasePath)
        {
            foreach (var recorder in Resources.FindObjectsOfTypeAll<UnityEditor.Recorder.Recorder>())
                if (recorder.Recording) throw new InvalidOperationException("Stop the active Unity Recorder session first.");

            controllerSettings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
            controllerSettings.hideFlags = HideFlags.HideAndDontSave;
            controllerSettings.SetRecordModeToManual();
            controllerSettings.FrameRate = settings.videoFps;
            controllerSettings.FrameRatePlayback = FrameRatePlayback.Constant;
            controllerSettings.CapFrameRate = true;
            movie = ScriptableObject.CreateInstance<MovieRecorderSettings>();
            movie.hideFlags = HideFlags.HideAndDontSave;
            movie.name = "Capture & Promote";
            movie.Enabled = true;
            movie.OutputFile = outputBasePath;
            movie.EncoderSettings = new CoreEncoderSettings
            {
                EncodingQuality = (CoreEncoderSettings.VideoEncodingQuality)settings.videoQuality
            };
            OutputPath = outputBasePath + "." + movie.EncoderSettings.Extension;
            movie.ImageInputSettings = new GameViewInputSettings
            {
                OutputWidth = settings.videoWidth,
                OutputHeight = settings.videoHeight
            };
            bool audioSupported = AudioSettings.speakerMode == AudioSpeakerMode.Mono ||
                AudioSettings.speakerMode == AudioSpeakerMode.Stereo;
            movie.AudioInputSettings.PreserveAudio = settings.recordAudio && audioSupported;
            if (settings.recordAudio && !audioSupported)
                Debug.LogWarning("[Capture & Promote] Recorder audio requires Mono or Stereo speaker mode. Recording video without audio.");
            controllerSettings.AddRecorderSettings(movie);
            controller = new RecorderController(controllerSettings);
            controller.PrepareRecording();
            if (!controller.StartRecording())
                throw new InvalidOperationException("Unity Recorder could not start. See the Console for encoder or audio errors.");
        }

        public void Stop() => controller?.StopRecording();

        public void Dispose()
        {
            try { Stop(); }
            finally
            {
                if (movie != null) UnityEngine.Object.DestroyImmediate(movie);
                if (controllerSettings != null) UnityEngine.Object.DestroyImmediate(controllerSettings);
                movie = null;
                controllerSettings = null;
                controller = null;
            }
        }
    }
}
#endif
