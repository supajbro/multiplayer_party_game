using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace CouchGuys.Tests.Editor
{
    [InitializeOnLoad]
    public static class EasyCouchRegressionRunner
    {
        private static TestRunnerApi s_runner;
        static EasyCouchRegressionRunner() { EditorApplication.update += CheckRequest; }
        private static void CheckRequest()
        {
            const string request = "Temp/EasyCouches.Tests.request";
            if (!File.Exists(request) || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            File.Delete(request); Run();
        }
        [MenuItem("Couch Guys/Couches/Run Couch Interaction Regression Tests")]
        public static void Run()
        {
            s_runner = ScriptableObject.CreateInstance<TestRunnerApi>();
            s_runner.RegisterCallbacks(new Results());
            s_runner.Execute(new ExecutionSettings(new Filter { testMode = TestMode.EditMode, testNames = new[] {
                "CouchGuys.Tests.Editor.CouchPhysicsTests", "CouchGuys.Tests.Editor.MultipleDeliveryTests",
                "CouchGuys.Tests.Editor.TruckCargoTests", "CouchGuys.Tests.Editor.SledPhysicsTests",
                "CouchGuys.Tests.Editor.GetawayTruckTests", "CouchGuys.Tests.Editor.EasyCouchAssetTests"
            }}));
        }
        private sealed class Results : ICallbacks
        {
            public void RunStarted(ITestAdaptor test) { }
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result) { }
            public void RunFinished(ITestResultAdaptor result)
            {
                const string folder = "Tools/Blender/Exports/EasyCouches/";
                TestRunnerApi.SaveResultToFile(result, folder + "interaction_tests.xml");
                File.WriteAllText(folder + "interaction_tests.txt", $"{result.PassCount} passed, {result.FailCount} failed, {result.SkipCount} skipped.\n");
                Object.DestroyImmediate(s_runner);
            }
        }
    }
}
