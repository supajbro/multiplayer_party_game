using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace CouchGuys.Editor
{
    /// <summary>Focused editor checks for the minimap transport navigation.</summary>
    public static class MinimapNavigationValidationRunner
    {
        private static TestRunnerApi s_runner;

        [MenuItem("Couch Guys/Validate Minimap Navigation")]
        public static void Run()
        {
            s_runner = ScriptableObject.CreateInstance<TestRunnerApi>();
            s_runner.RegisterCallbacks(new Results());
            s_runner.Execute(new ExecutionSettings(new Filter
            {
                testMode = TestMode.EditMode,
                testNames = new[] { "CouchGuys.Tests.Editor.MinimapNavigationTests" }
            }));
        }

        private sealed class Results : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun) { }
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result) { }
            public void RunFinished(ITestResultAdaptor result)
            {
                TestRunnerApi.SaveResultToFile(result, "Temp/MinimapNavigationTestResults.xml");
                Debug.Log($"Minimap navigation checks: {result.PassCount} passed, {result.FailCount} failed.");
                Object.DestroyImmediate(s_runner);
            }
        }
    }
}
