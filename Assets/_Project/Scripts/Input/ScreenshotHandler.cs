using System;
using System.Collections;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CouchGuys.Input
{
    /// <summary>Local screen captures, independent of player ownership and scene lifetime.</summary>
    [DisallowMultipleComponent]
    public sealed class ScreenshotHandler : MonoBehaviour
    {
        private DateTime m_lastTimestamp;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
#if !UNITY_SERVER
            if (Application.isBatchMode || FindFirstObjectByType<ScreenshotHandler>() != null) return;
            var handler = new GameObject("ScreenshotHandler").AddComponent<ScreenshotHandler>();
            DontDestroyOnLoad(handler.gameObject);
#endif
        }

        private void Update()
        {
            if (Keyboard.current?.f12Key.wasPressedThisFrame ?? false)
                StartCoroutine(Capture());
        }

        private IEnumerator Capture()
        {
            // Include all cameras and overlay UI, even while gameplay is paused.
            yield return new WaitForEndOfFrame();

            string path = null;
            try
            {
                string directory = GetWritableDirectory();
                DateTime timestamp = DateTime.Now;
                if (timestamp <= m_lastTimestamp) timestamp = m_lastTimestamp.AddMilliseconds(1);
                do
                {
                    path = Path.Combine(directory, "CouchGuys_" +
                        timestamp.ToString("yyyy-MM-dd_HH-mm-ss-fff", CultureInfo.InvariantCulture) + ".png");
                    timestamp = timestamp.AddMilliseconds(1);
                } while (File.Exists(path));
                m_lastTimestamp = timestamp;

                // PNG is lossless; supersize 1 preserves the native screen resolution.
                ScreenCapture.CaptureScreenshot(path, 1);
            }
            catch (Exception exception)
            {
                Debug.LogError($"Screenshot capture failed{(path == null ? string.Empty : $" ({path})")}: {exception}");
                path = null;
            }

            if (path == null) yield break;
            // Allow the native capture to finish before reporting success. Use real time so
            // a paused game cannot prevent a failed capture from timing out.
            float deadline = Time.realtimeSinceStartup + 10f;
            while (!File.Exists(path) && Time.realtimeSinceStartup < deadline)
                yield return null;

            try
            {
                if (!File.Exists(path) || new FileInfo(path).Length == 0)
                    throw new IOException("ScreenCapture did not produce a non-empty PNG within 10 seconds.");
                Debug.Log($"Screenshot saved: {path}");
            }
            catch (Exception exception)
            {
                Debug.LogError($"Screenshot capture failed ({path}): {exception}");
            }
        }

        private static string GetWritableDirectory()
        {
            // In the Editor dataPath is <Project>/Assets; on Windows it is <Game>/<Name>_Data.
            string preferred = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots");
            try
            {
                EnsureWritable(preferred);
                return preferred;
            }
            catch (Exception exception) when (!Application.isEditor &&
                (exception is IOException || exception is UnauthorizedAccessException ||
                 exception is System.Security.SecurityException))
            {
                string fallback = Path.Combine(Application.persistentDataPath, "Screenshots");
                Debug.LogWarning($"Screenshot folder '{preferred}' is not writable: {exception.Message} " +
                    $"Using '{fallback}'.");
                EnsureWritable(fallback);
                return fallback;
            }
        }

        private static void EnsureWritable(string directory)
        {
            Directory.CreateDirectory(directory);
            // Existing folders can still deny file creation. Probe before calling the native API,
            // whose write errors may only be logged by Unity rather than thrown as exceptions.
            string probe = Path.Combine(directory, $".write-test-{Guid.NewGuid():N}.tmp");
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                1, FileOptions.DeleteOnClose)) { }
        }
    }
}
