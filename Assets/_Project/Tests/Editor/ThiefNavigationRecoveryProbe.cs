using System;
using System.IO;
using CouchGuys.Tests.Editor;
using UnityEditor;
using UnityEngine;

namespace CouchGuys.Editor
{
    // Agent activation is a Play-mode operation; the static NavMesh queries in
    // Edit mode alone cannot prove a disabled agent has resumed navigation.
    [InitializeOnLoad]
    public static class ThiefNavigationRecoveryProbe
    {
        private const string Pending = "ThiefNavigationRecovery.Pending";
        private const string Result = "Temp/ThiefAirborne/results.txt";
        static ThiefNavigationRecoveryProbe() { EditorApplication.playModeStateChanged += OnStateChanged; }

        public static void Run()
        {
            SessionState.SetBool(Pending, true);
            EditorApplication.EnterPlaymode();
        }

        private static void OnStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
            SessionState.SetBool(Pending, false);
            EditorApplication.delayCall += RunInPlay;
        }

        private static void RunInPlay()
        {
            try
            {
                new ThiefAirborneTests().RecoveryRetriesAttachmentAndResumesPathsAcrossPhysicsNavMeshHeightDifference();
                File.AppendAllText(Result, "PASS PLAY: initially disabled off-NavMesh agent retries and attaches across a 0.22 m physics/NavMesh height difference; ground support remains valid; stale destination cleared; new complete path calculated and movement destination accepted.\nALL TARGETED CHECKS PASSED.\n");
                Debug.Log("Thief recovery and navigation regression checks PASS.");
            }
            catch (Exception error)
            {
                File.AppendAllText(Result, "FAIL PLAY: " + error + "\n");
                Debug.LogException(error);
            }
            finally { EditorApplication.ExitPlaymode(); }
        }
    }
}
