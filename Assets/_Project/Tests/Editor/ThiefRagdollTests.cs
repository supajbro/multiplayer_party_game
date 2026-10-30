using System.Reflection;
using System.Collections.Generic;
using CouchGuys.Gameplay.Pedestrians;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CouchGuys.Tests.Editor
{
    public sealed class ThiefRagdollTests
    {
        private static IEnumerable<TestCaseData> Cases()
        {
            foreach (string variant in new[] { "Thief", "Thief_Pistol", "Thief_AssaultRifle", "Thief_Shotgun" })
            foreach (bool authority in new[] { true, false })
            foreach (bool permanent in new[] { true, false })
                yield return new TestCaseData(variant, authority, permanent);
        }

        [TestCaseSource(nameof(Cases))]
        public void RepeatedRootTracking_PreservesFloorCollisions(string variant, bool authority, bool permanent)
        {
            Scene scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            GameObject root = null, floor = null;
            try
            {
                root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/_Project/Prefabs/Enemies/" + variant + ".prefab"));
                SceneManager.MoveGameObjectToScene(root, scene);
                root.GetComponent<Collider>().enabled = false;
                root.GetComponent<UnityEngine.AI.NavMeshAgent>().enabled = false;
                floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                SceneManager.MoveGameObjectToScene(floor, scene);
                floor.transform.position = Vector3.down * 0.05f;
                floor.transform.localScale = new Vector3(50f, 0.1f, 50f);
                var ragdoll = root.GetComponentInChildren<PedestrianRagdoll>(true);
                Invoke(ragdoll, "Awake");
                Vector3 visualLocalPosition = ragdoll.transform.localPosition;
                Quaternion visualLocalRotation = ragdoll.transform.localRotation;
                Physics.SyncTransforms();
                ragdoll.Fall(Vector3.right * 0.5f, permanent);
                PhysicsScene physics = scene.GetPhysicsScene();
                Assert.That(physics, Is.Not.EqualTo(Physics.defaultPhysicsScene));
                for (int step = 0; step < 180; step++)
                {
                    if (!authority)
                    {
                        root.transform.position = new Vector3(step * 0.003f, -Mathf.Min(step * 0.01f, 0.6f), 0f);
                        root.transform.rotation = Quaternion.Euler(0f, Mathf.Min(step * 3f, 45f), 0f);
                    }
                    ragdoll.FollowRoot(root.transform, authority);
                    physics.Simulate(0.02f);
                    foreach (Rigidbody bone in root.GetComponentsInChildren<Rigidbody>())
                        Assert.That(bone.position.y, Is.GreaterThan(-0.05f),
                            "Root synchronization moved a physical bone below the thin floor at tick " + step);
                }
                if (permanent)
                {
                    ragdoll.Recover();
                    Assert.That(ragdoll.IsFallen, Is.True, "Death must prevent recovery.");
                    return;
                }
                // Match server landing: move the network root without moving the physical pose.
                Vector3 hips = ragdoll.Body.position;
                ragdoll.RepositionRoot(root.transform, new Vector3(hips.x, 0f, hips.z));
                Physics.SyncTransforms();
                Assert.That(Vector3.Distance(hips, ragdoll.Body.position), Is.LessThan(0.002f));
                ragdoll.Recover();
                Assert.That(ragdoll.IsFallen, Is.False);
                Assert.That(Vector3.Distance(ragdoll.transform.localPosition, visualLocalPosition), Is.LessThan(0.002f),
                    "Recovery retained the accumulated visual offset from root tracking.");
                Assert.That(Quaternion.Angle(ragdoll.transform.localRotation, visualLocalRotation), Is.LessThan(0.01f));
                Invoke(ragdoll, "LateUpdate");
                Assert.That(Vector3.Distance(hips, ragdoll.Body.transform.position), Is.LessThan(0.02f),
                    "Recovery snapped the physical pose when restoring the visual parent.");
            }
            finally
            {
                if (root != null) Object.DestroyImmediate(root);
                if (floor != null) Object.DestroyImmediate(floor);
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private static void Invoke(PedestrianRagdoll target, string method) =>
            typeof(PedestrianRagdoll).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(target, null);
    }
}
