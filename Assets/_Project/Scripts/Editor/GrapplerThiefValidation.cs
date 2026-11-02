#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace CouchGuys.Editor
{
    public static class GrapplerThiefValidation
    {
        private static TestRunnerApi s_runner;
        [MenuItem("Couch Guys/Grappler Thief/Validate")]
        public static void Run()
        {
            s_runner = ScriptableObject.CreateInstance<TestRunnerApi>();
            s_runner.RegisterCallbacks(new Results());
            s_runner.Execute(new ExecutionSettings(new Filter
            {
                testMode = TestMode.EditMode,
                testNames = new[] { "CouchGuys.Tests.Editor.GrapplerThiefTests", "CouchGuys.Tests.Editor.GrappleGunTests",
                    "CouchGuys.Tests.Editor.ThiefRagdollTests", "CouchGuys.Tests.Editor.ThiefSettingsTests" }
            }));
        }
        private sealed class Results : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun) { }
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result) { }
            public void RunFinished(ITestResultAdaptor result)
            {
                TestRunnerApi.SaveResultToFile(result, "Temp/GrapplerThiefTestResults.xml");
                Debug.Log($"Grappler checks: {result.PassCount} passed, {result.FailCount} failed.");
                Object.DestroyImmediate(s_runner);
            }
        }
    }
}
#endif
