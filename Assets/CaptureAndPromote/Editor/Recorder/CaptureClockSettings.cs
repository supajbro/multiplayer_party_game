#if CAPTURE_PROMOTE_RECORDER
using System;
using System.Collections.Generic;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Input;

namespace CaptureAndPromote
{
    [RecorderSettings(typeof(CaptureClockRecorder), "Capture & Promote Frame Clock", "movie_16")]
    public sealed class CaptureClockSettings : RecorderSettings
    {
        [NonSerialized] public Action<long> FrameRecorded;
        public override IEnumerable<RecorderInputSettings> InputsSettings => Array.Empty<RecorderInputSettings>();
        protected override string Extension => "clock";
    }

    // A metadata-only Recorder shares the movie's frame scheduling through the supported
    // custom Recorder API. No image buffers, scene polling or wall-clock estimation are used.
    public sealed class CaptureClockRecorder : GenericRecorder<CaptureClockSettings>
    {
        protected override void RecordFrame(RecordingSession session) => Settings.FrameRecorded?.Invoke(session.frameIndex + 1L);
    }
}
#endif
