using System;
using System.IO;
using CouchGuys.Tests.Editor;
using UnityEditor;
using UnityEngine;

namespace CouchGuys.Editor
{
    [InitializeOnLoad]
    public static class GetawayTruckPhysicsProbe
    {
        private const string Pending = "GetawayTruckPhysicsProbe.Pending";
        private const string ExitAfter = "GetawayTruckPhysicsProbe.ExitAfter";
        static GetawayTruckPhysicsProbe()
        {
            if (SessionState.GetBool(Pending, false)) EditorApplication.update += TryRunInPlay;
        }

        public static void Run()
        {
            SessionState.SetBool(Pending, true);
            SessionState.SetBool(ExitAfter, !EditorApplication.isPlaying);
            EditorApplication.update -= TryRunInPlay;
            EditorApplication.update += TryRunInPlay;
            if (!EditorApplication.isPlaying) EditorApplication.EnterPlaymode();
        }

        private static void TryRunInPlay()
        {
            if (!SessionState.GetBool(Pending, false) || !EditorApplication.isPlaying ||
                !Application.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            SessionState.SetBool(Pending, false);
            EditorApplication.update -= TryRunInPlay;
            RunInPlay();
        }

        private static void RunInPlay()
        {
            try
            {
                Debug.Log("Getaway physics probe started in Play mode.");
                new GetawayTruckMovementTests().RogueMotorDrivesActualTruckThroughTurnsWithoutTipping();
                File.AppendAllText("Temp/GetawayValidation.txt", "PASS PLAY: actual truck reached four goals including a close rearward escape target, turned and drove forward with bounded reversing, remained upright and grounded, and braked to rest. ALL TARGETED CHECKS PASSED.\n");
                Debug.Log("Getaway truck navigation and physics checks passed.");
            }
            catch (Exception error)
            {
                File.AppendAllText("Temp/GetawayValidation.txt", "FAIL PLAY: " + error + "\n");
                Debug.LogException(error);
            }
            finally
            {
                if (SessionState.GetBool(ExitAfter, false)) EditorApplication.ExitPlaymode();
            }
        }
    }
}
