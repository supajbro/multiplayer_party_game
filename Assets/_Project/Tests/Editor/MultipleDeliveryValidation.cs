using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace CouchGuys.Editor
{
    public static class MultipleDeliveryValidation
    {
        private static TestRunnerApi s_runner;

        [InitializeOnLoadMethod]
        private static void CheckRequest()
        {
            EditorApplication.delayCall += () =>
            {
                const string request = "Temp/RunMultipleDeliveryChecks";
                if (!File.Exists(request) || EditorApplication.isPlayingOrWillChangePlaymode) return;
                File.Delete(request);
                Run();
            };
        }

        [MenuItem("Couch Guys/Delivery/Validate Multiple Deliveries")]
        public static void Run()
        {
            s_runner = ScriptableObject.CreateInstance<TestRunnerApi>();
            s_runner.RegisterCallbacks(new Results());
            s_runner.Execute(new ExecutionSettings(new Filter
            {
                testMode = TestMode.EditMode,
                testNames = new[] { "CouchGuys.Tests.Editor.MultipleDeliveryTests", "CouchGuys.Tests.Editor.TruckCargoTests" }
            }));
        }

        private sealed class Results : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun) { }
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result) { }
            public void RunFinished(ITestResultAdaptor result)
            {
                TestRunnerApi.SaveResultToFile(result, "Temp/MultipleDeliveryTests.xml");
                Debug.Log($"Multiple delivery checks: {result.PassCount} passed, {result.FailCount} failed.");
                Object.DestroyImmediate(s_runner);
            }
        }
    }
}
