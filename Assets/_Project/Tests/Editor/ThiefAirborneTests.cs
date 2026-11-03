using System;
using System.IO;
using System.Reflection;
using CouchGuys.Gameplay.Enemies;
using CouchGuys.Gameplay.Pedestrians;
using FishNet.Object.Synchronizing;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace CouchGuys.Tests.Editor
{
    public sealed class ThiefAirborneTests
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        [Test]
        public void GroundProbeDetectsLostSupportAndIgnoresTriggersAndSelf()
        {
            var root = new GameObject("Airborne support test");
            root.transform.position = new Vector3(20000, 0, 20000);
            var body = root.AddComponent<CapsuleCollider>(); body.center = Vector3.up; body.height = 2; body.radius = .3f;
            var health = root.AddComponent<ThiefHealth>();
            typeof(ThiefHealth).GetField("m_collider", Fields).SetValue(health, body);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.position = root.transform.position + Vector3.down * .5f;
            floor.transform.localScale = new Vector3(4, 1, 4);
            bool Supported() => (bool)typeof(ThiefHealth).GetMethod("HasGroundSupport", Fields).Invoke(health, null);
            try
            {
                Physics.SyncTransforms(); Assert.That(Supported(), Is.True, "Standing on a floor.");
                root.transform.position += Vector3.up;
                Physics.SyncTransforms(); Assert.That(Supported(), Is.False, "Lifted thief must be airborne.");
                root.transform.position -= Vector3.up;
                floor.GetComponent<Collider>().isTrigger = true;
                Physics.SyncTransforms(); Assert.That(Supported(), Is.False, "Trigger volumes cannot support a thief.");
                floor.SetActive(false);
                Physics.SyncTransforms(); Assert.That(Supported(), Is.False, "Own collider cannot count as support.");
                floor.SetActive(true); floor.GetComponent<Collider>().isTrigger = false;
                Physics.SyncTransforms(); Assert.That(Supported(), Is.True, "Restored platform is detected.");
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(floor); }
        }

        [TestCase("Thief_Pistol")]
        [TestCase("GrapplerThief")]
        public void AirborneTransitionDisablesAnimationAndActivatesBonePhysics(string name)
        {
            var root = PrefabUtility.LoadPrefabContents($"Assets/_Project/Prefabs/Enemies/{name}.prefab");
            try
            {
                var health = root.GetComponent<ThiefHealth>();
                var ragdoll = root.GetComponentInChildren<PedestrianRagdoll>(true);
                Assert.That(ragdoll, Is.Not.Null);
                Vector3 launch = new Vector3(12, 8, 0);
                typeof(ThiefHealth).GetField("m_knockbackVelocity", Fields).SetValue(health, launch);
                typeof(ThiefHealth).GetMethod("BeginAirborneRagdoll", Fields).Invoke(health, null);
                Assert.That(ragdoll.IsFallen, Is.True);
                Assert.That(ragdoll.Body.isKinematic, Is.False);
                Assert.That(new SerializedObject(ragdoll).FindProperty("m_animator").objectReferenceValue is Animator animator && !animator.enabled, Is.True);
                var impulse = (SyncVar<Vector3>)typeof(ThiefHealth).GetField("m_airborneImpulse", Fields).GetValue(health);
                Assert.That(impulse.Value, Is.EqualTo(launch), "Initial launch velocity must replicate with ragdoll state.");
                var spin = (SyncVar<Vector3>)typeof(ThiefHealth).GetField("m_airborneSpin", Fields).GetValue(health);
                Assert.That(spin.Value.magnitude, Is.InRange(7f - .001f, 11f + .001f));
                Assert.That(Mathf.Abs(spin.Value.normalized.y), Is.LessThan(.5f), "Tumble should favour flips over upright spinning.");
                Assert.That(ragdoll.Body.angularVelocity.magnitude, Is.GreaterThan(6.9f));
                Assert.That(ragdoll.Body.angularDamping, Is.LessThanOrEqualTo(.15f));
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        [Test]
        public void LandedThiefCanRecoverOffNavMeshButCannotStandInMidairOrUnderObstacles()
        {
            var root = new GameObject("Recovery fallback test");
            root.transform.position = new Vector3(20000, 0, 20000);
            var capsule = root.AddComponent<CapsuleCollider>();
            capsule.center = Vector3.up; capsule.height = 2; capsule.radius = .3f;
            var health = root.AddComponent<ThiefHealth>();
            typeof(ThiefHealth).GetField("m_collider", Fields).SetValue(health, capsule);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.position = root.transform.position + Vector3.down * .5f;
            floor.transform.localScale = new Vector3(20, 1, 20);
            var ceiling = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ceiling.transform.position = root.transform.position + Vector3.up * 1.2f;
            ceiling.transform.localScale = new Vector3(20, .2f, 20);
            ceiling.SetActive(false);
            bool FindStanding(out Vector3 position)
            {
                object[] args = { root.transform.position, Vector3.zero };
                bool result = (bool)typeof(ThiefHealth).GetMethod("TryFindRecoveryPosition", Fields).Invoke(health, args);
                position = (Vector3)args[1];
                return result;
            }
            try
            {
                typeof(ThiefHealth).GetField("m_groundedSince", Fields).SetValue(health, Time.time);
                Physics.SyncTransforms();
                Assert.That(FindStanding(out _), Is.False, "Wait for the normal recovery path before using the off-NavMesh fallback.");
                typeof(ThiefHealth).GetField("m_groundedSince", Fields).SetValue(health, Time.time - 2f);
                Assert.That(FindStanding(out Vector3 position), Is.True, "A supported living thief must not require a baked NavMesh to stand.");
                Assert.That(position.y, Is.EqualTo(0f).Within(.01f));
                ceiling.SetActive(true); Physics.SyncTransforms();
                Assert.That(FindStanding(out _), Is.False, "Do not stand through a low ceiling.");
                ceiling.SetActive(false); floor.SetActive(false); Physics.SyncTransforms();
                Assert.That(FindStanding(out _), Is.False, "Do not stand without physical ground.");
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(floor); Object.DestroyImmediate(ceiling); }
        }

        [TestCase(2.5f)]
        [TestCase(5f)]
        public void GroundedRecoveryWaitsForConfiguredDuration(float seconds)
        {
            var root = new GameObject("Recovery delay test");
            try
            {
                var health = root.AddComponent<ThiefHealth>();
                typeof(ThiefHealth).GetField("m_groundedRecoveryDuration", Fields).SetValue(health, seconds);
                var grounded = typeof(ThiefHealth).GetField("m_groundedSince", Fields);
                var ready = typeof(ThiefHealth).GetMethod("HasFinishedGroundedRecovery", Fields);
                grounded.SetValue(health, Time.time - seconds + .1f);
                Assert.That((bool)ready.Invoke(health, null), Is.False, "Cannot get up early.");
                grounded.SetValue(health, Time.time - seconds - .1f);
                Assert.That((bool)ready.Invoke(health, null), Is.True, "Configured delay must eventually finish.");
            }
            finally { Object.DestroyImmediate(root); }
        }

        public void RecoveryRetriesAttachmentAndResumesPathsAcrossPhysicsNavMeshHeightDifference()
        {
            Vector3 center = new Vector3(20000, 0, 20000);
            var root = new GameObject("Navigation recovery test"); root.SetActive(false);
            var agent = root.AddComponent<NavMeshAgent>(); agent.enabled = false;
            var capsule = root.AddComponent<CapsuleCollider>(); capsule.height = 2; capsule.center = Vector3.up; capsule.radius = .3f;
            var health = root.AddComponent<ThiefHealth>();
            var navigator = root.AddComponent<ThiefNavigator>();
            void Set(object target, string name, object value) => target.GetType().GetField(name, Fields).SetValue(target, value);
            Set(health,"m_agent",agent); Set(health,"m_collider",capsule); Set(health,"m_navigator",navigator);
            Set(navigator,"m_agent",agent); Set(navigator,"m_health",health); Set(navigator,"m_path",new NavMeshPath());
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            NavMeshData data = null; NavMeshDataInstance instance = default;
            try
            {
                var settings = NavMesh.GetSettingsByIndex(0);
                agent.agentTypeID = settings.agentTypeID;
                var sources = new System.Collections.Generic.List<NavMeshBuildSource> {
                    new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box, area = 0,
                        transform = Matrix4x4.TRS(center + Vector3.down*.1f, Quaternion.identity, Vector3.one), size = new Vector3(8,.2f,8) }
                };
                data = NavMeshBuilder.BuildNavMeshData(settings, sources, new Bounds(center, new Vector3(20,10,20)), Vector3.zero, Quaternion.identity);
                Assert.That(data, Is.Not.Null);
                instance = NavMesh.AddNavMeshData(data);
                var filter = new NavMeshQueryFilter { agentTypeID = settings.agentTypeID, areaMask = NavMesh.AllAreas };
                Assert.That(NavMesh.SamplePosition(center, out NavMeshHit onMesh, 2, filter), Is.True);
                float physicalGround = onMesh.position.y - .22f; // Reproduces the old .35 versus .15 mismatch.
                floor.transform.position = new Vector3(center.x, physicalGround-.5f, center.z);
                floor.transform.localScale = new Vector3(40,1,40);
                root.transform.position = new Vector3(center.x+15,physicalGround,center.z);
                root.SetActive(true); Physics.SyncTransforms();
                var retry = typeof(ThiefHealth).GetMethod("RetryNavigationAfterRecovery", Fields);
                retry.Invoke(health,null);
                Assert.That(agent.enabled, Is.False, "Remain safe when no nearby NavMesh exists.");
                root.transform.position = new Vector3(center.x+5,physicalGround,center.z);
                Physics.SyncTransforms();
                Set(health,"m_nextNavigationAttachAttempt",0f);
                Set(navigator,"m_hasDestination",true); Set(navigator,"m_nextPathTime",9999f); Set(navigator,"m_nextRepositionTime",9999f);
                object[] candidateArgs = { root.transform.position, 4f, Vector3.zero };
                bool candidateValid = (bool)typeof(ThiefHealth).GetMethod("TryGetNavigationPosition",Fields).Invoke(health,candidateArgs);
                bool sampled = NavMesh.SamplePosition(root.transform.position,out NavMeshHit sampledHit,4f,filter);
                Assert.That(candidateValid, Is.True, $"Recovery candidate: sampled={sampled}, root={root.transform.position}, nav={sampledHit.position}, floor={physicalGround}, time={Time.time}");
                retry.Invoke(health,null);
                Assert.That(agent.enabled && agent.isOnNavMesh, Is.True, $"Retry must attach the formerly disabled agent. time={Time.time}, next={typeof(ThiefHealth).GetField("m_nextNavigationAttachAttempt",Fields).GetValue(health)}, resume={typeof(ThiefHealth).GetField("m_resumeNavigationAt",Fields).GetValue(health)}");
                Assert.That((bool)typeof(ThiefHealth).GetMethod("HasGroundSupport",Fields).Invoke(health,null), Is.True,
                    "A valid NavMesh height offset must not immediately re-trigger airborne ragdoll.");
                Assert.That((bool)typeof(ThiefNavigator).GetField("m_hasDestination",Fields).GetValue(navigator), Is.False);
                Assert.That((float)typeof(ThiefNavigator).GetField("m_nextPathTime",Fields).GetValue(navigator), Is.EqualTo(0));
                Vector3 goal = onMesh.position - Vector3.right;
                var path = new NavMeshPath();
                Assert.That(agent.CalculatePath(goal,path), Is.True);
                Assert.That(path.status, Is.EqualTo(NavMeshPathStatus.PathComplete));
                typeof(ThiefNavigator).GetMethod("MoveTo",Fields).Invoke(navigator,new object[]{goal,.25f});
                Assert.That((bool)typeof(ThiefNavigator).GetField("m_hasDestination",Fields).GetValue(navigator), Is.True);
                Assert.That(agent.isStopped, Is.False, "Recovered agent should accept a fresh movement request.");
                Assert.That(agent.destination.x, Is.EqualTo(goal.x).Within(.1f));
            }
            finally
            {
                Object.DestroyImmediate(root); Object.DestroyImmediate(floor);
                if(instance.valid) instance.Remove(); if(data!=null) Object.DestroyImmediate(data);
            }
        }

        [MenuItem("Couch Guys/Thieves/Test Airborne Ragdoll %#&i")]
        public static void RunTargetedChecks()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode before Edit-mode checks.");
            Directory.CreateDirectory("Temp/ThiefAirborne");
            try
            {
                var tests = new ThiefAirborneTests();
                tests.GroundProbeDetectsLostSupportAndIgnoresTriggersAndSelf();
                tests.AirborneTransitionDisablesAnimationAndActivatesBonePhysics("Thief_Pistol");
                tests.AirborneTransitionDisablesAnimationAndActivatesBonePhysics("GrapplerThief");
                tests.LandedThiefCanRecoverOffNavMeshButCannotStandInMidairOrUnderObstacles();
                tests.GroundedRecoveryWaitsForConfiguredDuration(2.5f);
                tests.GroundedRecoveryWaitsForConfiguredDuration(5f);
                File.WriteAllText("Temp/ThiefAirborne/results.txt", "PASS EDIT: ground support, airborne ragdolls/flips, safe off-NavMesh recovery; configurable 2.5/5-second landing waits.\nSTART PLAY: actual NavMeshAgent attachment and path requests.\n");
                CouchGuys.Editor.ThiefNavigationRecoveryProbe.Run();
            }
            catch (Exception error)
            {
                File.WriteAllText("Temp/ThiefAirborne/results.txt", "FAIL: " + error);
                throw;
            }
        }
    }
}
