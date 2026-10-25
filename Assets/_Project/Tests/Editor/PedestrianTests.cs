using CouchGuys.Gameplay.Pedestrians;
using FishNet.Component.Transforming;
using FishNet.Managing.Object;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CouchGuys.Tests.Editor
{
    public sealed class PedestrianTests
    {
        private static Rect[] LoopWithDeadEnd => new[]
        {
            new Rect(0f, 0f, 12f, 2f), new Rect(0f, 10f, 12f, 2f),
            new Rect(0f, 2f, 2f, 8f), new Rect(10f, 2f, 2f, 8f),
            new Rect(12f, 5f, 4f, 2f)
        };

        [Test]
        public void SidewalkLoop_ContinuesWithoutReversingOrEnteringDeadEnd()
        {
            var graph = new SidewalkGraph();
            int samples = 0;
            graph.Build(LoopWithDeadEnd, point => { samples++; point.y = point.x * 0.1f; return point; });
            Assert.That(graph.SpawnNodes.Count, Is.GreaterThan(10));
            Assert.That(samples, Is.GreaterThan(10));
            Random.State savedRandom = Random.state;
            try
            {
                Random.InitState(731);
                foreach (int start in graph.SpawnNodes)
                {
                    int previous = -1, current = start;
                    for (int step = 0; step < 250; step++)
                    {
                        int next = graph.ChooseNext(current, previous);
                        Assert.That(next, Is.GreaterThanOrEqualTo(0));
                        Assert.That(next, Is.Not.EqualTo(previous));
                        Vector3 point = graph.Nodes[next].Position;
                        Assert.That(point.x, Is.LessThanOrEqualTo(12f), "Entered the dead-end spur.");
                        Assert.That(point.y, Is.EqualTo(point.x * 0.1f).Within(0.0001f));
                        if (previous >= 0)
                        {
                            Vector3 incoming = Vector3.ProjectOnPlane(graph.Nodes[current].Position -
                                graph.Nodes[previous].Position, Vector3.up).normalized;
                            Vector3 outgoing = Vector3.ProjectOnPlane(point - graph.Nodes[current].Position, Vector3.up).normalized;
                            Assert.That(Vector3.Dot(incoming, outgoing), Is.GreaterThanOrEqualTo(-0.011f));
                        }
                        previous = current;
                        current = next;
                    }
                }
            }
            finally { Random.state = savedRandom; }
        }

        [Test]
        public void DisconnectedSidewalks_DoNotBridgeRoadsOrProduceDeadEndSpawns()
        {
            var graph = new SidewalkGraph();
            graph.Build(new[] { new Rect(0f, 0f, 10f, 2f), new Rect(0f, 12f, 10f, 2f) }, p => p);
            Assert.That(graph.SpawnNodes, Is.Empty);
        }

        [Test]
        public void Characters_AreRegisteredWithServerTransformsAnimationAndRagdolls()
        {
            var settings = AssetDatabase.LoadAssetAtPath<PedestrianSettings>(
                "Assets/_Project/Resources/Pedestrians/PedestrianSettings.asset");
            var collection = AssetDatabase.LoadAssetAtPath<SinglePrefabObjects>(
                "Assets/_Project/Settings/NetworkSpawnablePrefabs.asset");
            Assert.That(settings, Is.Not.Null);
            Assert.That(settings.Characters.Length, Is.EqualTo(10));
            foreach (var character in settings.Characters)
            {
                Assert.That(collection.Prefabs, Does.Contain(character));
                Assert.That(character.GetComponent<PedestrianController>(), Is.Not.Null);
                Assert.That(character.GetComponent<CapsuleCollider>().isTrigger, Is.True);
                var nt = new SerializedObject(character.GetComponent<NetworkTransform>());
                Assert.That(nt.FindProperty("_clientAuthoritative").boolValue, Is.False);
                Assert.That(nt.FindProperty("_synchronizeScale").boolValue, Is.False);
                Assert.That(nt.FindProperty("_interval").intValue, Is.EqualTo(3));
                Assert.That(nt.FindProperty("_interpolation").intValue, Is.GreaterThan(0));
                Animator rig = character.GetComponentInChildren<Animator>();
                Assert.That(rig.avatar.isHuman, Is.True);
                Assert.That(rig.applyRootMotion, Is.False);
                Assert.That(rig.runtimeAnimatorController.animationClips.Length, Is.EqualTo(4));
                var controller = (UnityEditor.Animations.AnimatorController)rig.runtimeAnimatorController;
                var dance = System.Array.Find(controller.layers[0].stateMachine.states,
                    state => state.state.name == "Dance").state;
                Assert.That(dance, Is.Not.Null);
                Assert.That(dance.motion, Is.TypeOf<AnimationClip>());
                Assert.That(((AnimationClip)dance.motion).isLooping, Is.True);
                Assert.That(dance.transitions, Is.Empty, "Completed recipients must keep dancing.");
                Assert.That(character.GetComponentInChildren<PedestrianRagdoll>(), Is.Not.Null);
            }
        }

        [Test]
        public void PedestrianHitbox_IsShootableWithoutStoppingVehiclePhysics()
        {
            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/_Project/Resources/Pedestrians/Pedestrian_01.prefab");
                GameObject npc = Object.Instantiate(prefab);
                SceneManager.MoveGameObjectToScene(npc, scene);
                npc.transform.position = Vector3.zero;
                Physics.SyncTransforms();
                PhysicsScene physics = scene.GetPhysicsScene();
                Assert.That(physics, Is.Not.EqualTo(Physics.defaultPhysicsScene), "Physics regression must use an isolated scene.");
                Assert.That(physics.Raycast(new Vector3(0f, 0.9f, -3f), Vector3.forward,
                    out RaycastHit hit, 6f, ~0, QueryTriggerInteraction.Collide), Is.True);
                Assert.That(PedestrianController.IsBulletHit(hit.collider), Is.True);

                GameObject truck = new GameObject("Vehicle physics probe");
                SceneManager.MoveGameObjectToScene(truck, scene);
                truck.transform.position = new Vector3(-3f, 0.9f, 0f);
                truck.AddComponent<BoxCollider>();
                Rigidbody body = truck.AddComponent<Rigidbody>();
                body.mass = 1000f;
                body.useGravity = false;
                body.linearDamping = 0f;
                body.linearVelocity = Vector3.right * 10f;
                Physics.SyncTransforms();
                for (int i = 0; i < 30; i++) physics.Simulate(0.02f);
                Assert.That(body.position.x, Is.GreaterThan(2.5f));
                Assert.That(body.linearVelocity.x, Is.EqualTo(10f).Within(0.01f));
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        [Test]
        public void BulletHitFilter_StillIgnoresUnrelatedTriggers()
        {
            GameObject obj = new GameObject("Unrelated trigger");
            try
            {
                var collider = obj.AddComponent<BoxCollider>();
                collider.isTrigger = true;
                Assert.That(PedestrianController.IsBulletHit(collider), Is.False);
                collider.isTrigger = false;
                Assert.That(PedestrianController.IsBulletHit(collider), Is.True);
            }
            finally { Object.DestroyImmediate(obj); }
        }
    }
}
