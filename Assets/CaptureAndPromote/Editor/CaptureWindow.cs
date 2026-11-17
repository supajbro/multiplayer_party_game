using System;
using UnityEditor;
using UnityEngine;

namespace CaptureAndPromote
{
    public sealed class CaptureWindow : EditorWindow
    {
        private Vector2 scroll;

        [MenuItem("Tools/Capture & Promote")]
        public static void Open() => GetWindow<CaptureWindow>("Capture & Promote");

        private void OnEnable()
        {
            minSize = new Vector2(390, 560);
            CaptureService.Changed += Repaint;
        }

        private void OnDisable() => CaptureService.Changed -= Repaint;
        private void OnInspectorUpdate() { if (CaptureService.IsRecording) Repaint(); }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            GUILayout.Space(12);
            GUILayout.Label("Capture & Promote", new GUIStyle(EditorStyles.boldLabel) { fontSize = 20 });
            GUILayout.Space(12);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                Color original = GUI.color;
                if (CaptureService.IsRecording) GUI.color = new Color(1f, 0.4f, 0.35f);
                GUILayout.Label(CaptureService.IsRecording ? "● RECORDING" : "READY", EditorStyles.boldLabel);
                GUI.color = original;
                GUILayout.Label(TimeSpan.FromSeconds(Math.Max(0, CaptureService.ElapsedSeconds)).ToString(@"hh\:mm\:ss"),
                    new GUIStyle(EditorStyles.largeLabel) { fontSize = 24 });
                GUILayout.Space(8);
                using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying))
                {
                    if (GUILayout.Button("Capture Screenshot", GUILayout.Height(32))) CaptureService.CaptureScreenshot();
                    using (new EditorGUI.DisabledScope(!CaptureService.RecorderAvailable && !CaptureService.IsRecording))
                    {
                        if (GUILayout.Button(CaptureService.IsRecording ? "Stop Recording" : "Start Recording", GUILayout.Height(32)))
                            CaptureService.ToggleRecording();
                    }
                }
                GUILayout.Label("F12 captures a PNG. F9 starts or stops recording.", EditorStyles.wordWrappedMiniLabel);
                GUILayout.Label("Shortcuts work during Play Mode. Rebind in Edit > Shortcuts.", EditorStyles.wordWrappedMiniLabel);
            }
            if (!EditorApplication.isPlaying)
                EditorGUILayout.HelpBox("Enter Play Mode to capture gameplay.", MessageType.Info);
            if (!CaptureService.RecorderAvailable)
            {
                EditorGUILayout.HelpBox("Video requires Unity Recorder 5.1. In Package Manager, choose Install package by name and enter com.unity.recorder. Screenshots remain available.", MessageType.Warning);
                if (GUILayout.Button("Open Package Manager")) UnityEditor.PackageManager.UI.Window.Open("com.unity.recorder");
            }

            var settings = CaptureSettings.instance;
            EditorGUI.BeginChangeCheck();
            GUILayout.Space(10);
            GUILayout.Label("Screenshot", EditorStyles.boldLabel);
            settings.screenshotMultiplier = EditorGUILayout.IntSlider("Resolution multiplier", settings.screenshotMultiplier, 1, 8);
            GUILayout.Label("1× uses the current rendered Game view resolution.", EditorStyles.wordWrappedMiniLabel);
            GUILayout.Space(10);
            GUILayout.Label("Video", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(CaptureService.IsRecording))
            {
                settings.videoWidth = EditorGUILayout.IntField("Width", settings.videoWidth);
                settings.videoHeight = EditorGUILayout.IntField("Height", settings.videoHeight);
                settings.videoFps = EditorGUILayout.IntField("Frames per second", settings.videoFps);
                settings.videoQuality = (CaptureQuality)EditorGUILayout.EnumPopup("Encoding quality", settings.videoQuality);
                settings.recordAudio = EditorGUILayout.Toggle("Include gameplay audio", settings.recordAudio);
            }
            GUILayout.Space(10);
            GUILayout.Label("Output folders", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(CaptureService.IsRecording))
            {
                settings.screenshotDirectory = EditorGUILayout.TextField("Screenshots", settings.screenshotDirectory);
                settings.videoDirectory = EditorGUILayout.TextField("Videos", settings.videoDirectory);
            }
            GUILayout.Label("Paths are relative to the project or absolute. Choose folders outside Assets. Custom folders need their own version-control ignore rule.", EditorStyles.wordWrappedMiniLabel);
            if (EditorGUI.EndChangeCheck()) settings.SaveSettings();
            GUILayout.Space(8);
            if (GUILayout.Button("Open Screenshots Folder")) OpenFolder(settings.screenshotDirectory);
            if (GUILayout.Button("Open Videos Folder")) OpenFolder(settings.videoDirectory);
            GUILayout.Space(10);
            EditorGUILayout.HelpBox(CaptureService.Status, CaptureService.HasError ? MessageType.Error : MessageType.Info);
            EditorGUILayout.EndScrollView();
        }

        private static void OpenFolder(string directory)
        {
            try { EditorUtility.RevealInFinder(CaptureSettings.PrepareDirectory(directory)); }
            catch (Exception exception) { CaptureService.SetStatus(exception.Message, true); }
        }
    }
}
