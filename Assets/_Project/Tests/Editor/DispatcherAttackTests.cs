using System.Reflection;
using CouchGuys.Gameplay.Delivery;
using CouchGuys.Gameplay.Weapons;
using CouchGuys.Player;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CouchGuys.Tests.Editor
{
    public sealed class DispatcherAttackTests
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        private static object Call(object target, string name, params object[] args) =>
            target.GetType().GetMethod(name, Flags).Invoke(target, args);
        private static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, Flags).GetValue(target);
        private static void Begin(DeliveryNPC npc, float force, float lift = 0f) =>
            Call(npc, "BeginReaction", Vector3.right, force, lift);
        private static void Step(DeliveryNPC npc, float dt = .02f) => Call(npc, "TickAttackReactionServer", dt);

        [Test]
        public void SavedDispatcherKeepsItsModelAndHasSeparateEditableAttackSettings()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/DeliveryNPC.prefab");
            var npc = prefab.GetComponent<DeliveryNPC>();
            var data = new SerializedObject(npc);
            Assert.That(data.FindProperty("m_angryExpressionDuration").floatValue, Is.EqualTo(3f));
            Assert.That(data.FindProperty("m_aggressiveLines").arraySize, Is.EqualTo(7));
            foreach (string setting in new[] { "m_attackKnockbackMultiplier", "m_rocketBounceForce",
                "m_grappleRetaliationForce", "m_maximumReactionDistance", "m_aggressiveDialogueCooldown" })
                Assert.That(data.FindProperty(setting).floatValue, Is.GreaterThan(0f), setting);
            Assert.That(new SerializedObject(prefab.GetComponent<DispatcherIdleBanter>())
                .FindProperty("m_lines").arraySize, Is.EqualTo(10), "Normal dialogue remains separate.");
            Assert.That(prefab.GetComponentsInChildren<PlayerHealth>(true), Is.Empty);
            Assert.That(prefab.GetComponentsInChildren<Rigidbody>(true), Is.Empty, "No tipping or competing rigidbody simulation.");
            Assert.That(prefab.GetComponentInChildren<LODGroup>().GetLODs().Length, Is.EqualTo(3));
            var instance = Object.Instantiate(prefab);
            try
            {
                Assert.That(RocketProjectile.FindDamageTarget(instance.GetComponent<Collider>()),
                    Is.SameAs(instance.GetComponent<DeliveryNPC>()));
            }
            finally { Object.DestroyImmediate(instance); }
            var screens = new SerializedObject(prefab.GetComponent<DispatcherScreen>()).FindProperty("m_screens");
            Assert.That(screens.arraySize, Is.EqualTo(3));
            for (int i = 0; i < screens.arraySize; i++)
            {
                var screen = (Renderer)screens.GetArrayElementAtIndex(i).objectReferenceValue;
                Assert.That(screen.sharedMaterial.HasProperty("_Anger"), Is.True);
                var mesh = screen.GetComponent<MeshFilter>().sharedMesh;
                var vertices = mesh.vertices;
                var uv = mesh.uv;
                Assert.That(uv.Length, Is.EqualTo(vertices.Length));
                float averageY = 0f, averageV = 0f;
                for (int v = 0; v < vertices.Length; v++)
                { averageY += screen.transform.TransformPoint(vertices[v]).y; averageV += uv[v].y; }
                averageY /= vertices.Length; averageV /= vertices.Length;
                float covariance = 0f;
                for (int v = 0; v < vertices.Length; v++)
                    covariance += (screen.transform.TransformPoint(vertices[v]).y - averageY) * (uv[v].y - averageV);
                Assert.That(covariance, Is.GreaterThan(0f), "The angry brows must render above the mouth on the existing model.");
            }
        }

        [Test]
        public void ComboForceIsStrongerAndRepeatedHitsNeverDriftTheRecoveryAnchor()
        {
            var root = new GameObject("Dispatcher recoil test");
            root.transform.position = new Vector3(17000, 0, 17000);
            var npc = root.AddComponent<DeliveryNPC>();
            Vector3 anchor = root.transform.position;
            try
            {
                Begin(npc, 2.5f); Step(npc);
                float jab = (root.transform.position - anchor).magnitude;
                for (int i = 0; i < 140; i++) Step(npc);
                Assert.That(root.transform.position, Is.EqualTo(anchor));
                Begin(npc, 7.5f); Step(npc);
                Assert.That((root.transform.position - anchor).magnitude, Is.GreaterThan(jab * 2.9f));
                for (int i = 0; i < 100; i++)
                {
                    Begin(npc, 50f); Step(npc, .05f);
                    Assert.That(Get<Vector3>(npc, "m_reactionAnchor"), Is.EqualTo(anchor));
                    Assert.That((root.transform.position - anchor).magnitude, Is.LessThanOrEqualTo(2.501f));
                    Assert.That(Vector3.Dot(root.transform.up, Vector3.up), Is.GreaterThan(.9999f));
                }
                for (int i = 0; i < 140; i++) Step(npc);
                Assert.That(root.transform.position, Is.EqualTo(anchor));
                Assert.That((bool)Call(npc, "TickAttackReactionServer", .02f), Is.False, "Roaming must resume.");
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void RocketsBounceUprightAndAlwaysRecoverAtDifferentFrameRates()
        {
            foreach (float dt in new[] { .01f, .033f, .1f })
            {
                var root = new GameObject("Dispatcher bounce test");
                root.transform.SetPositionAndRotation(new Vector3(17100, 0, 17100), Quaternion.Euler(0, 37, 0));
                var npc = root.AddComponent<DeliveryNPC>();
                Vector3 anchor = root.transform.position;
                Quaternion rotation = root.transform.rotation;
                float highest = 0f;
                bool falling = false, bounced = false;
                try
                {
                    Begin(npc, 24f, 8f);
                    for (int i = 0; i < Mathf.CeilToInt(3f / dt); i++)
                    {
                        Step(npc, dt);
                        float height = root.transform.position.y - anchor.y;
                        highest = Mathf.Max(highest, height);
                        float verticalSpeed = Get<Vector3>(npc, "m_reactionVelocity").y;
                        if (falling && verticalSpeed > .1f) bounced = true;
                        if (verticalSpeed < -.5f) falling = true;
                        Assert.That(Vector3.Dot(root.transform.up, Vector3.up), Is.GreaterThan(.9999f));
                    }
                    Assert.That(highest, Is.GreaterThan(1f));
                    Assert.That(bounced, Is.True, "Expected a secondary bounce at dt=" + dt);
                    Assert.That(root.transform.position, Is.EqualTo(anchor));
                    Assert.That(Quaternion.Angle(root.transform.rotation, rotation), Is.LessThan(.01f));
                }
                finally { Object.DestroyImmediate(root); }
            }
        }

        [Test]
        public void OutwardImpulseRespectsWallsAndCeilings()
        {
            var root = new GameObject("Dispatcher obstruction test");
            root.transform.position = new Vector3(17200, 0, 17200);
            var npc = root.AddComponent<DeliveryNPC>();
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var ceiling = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.position = root.transform.position + new Vector3(.85f, 1.1f, 0);
            wall.transform.localScale = new Vector3(.1f, 4f, 5f);
            ceiling.transform.position = root.transform.position + Vector3.up * 2.6f;
            ceiling.transform.localScale = new Vector3(5, .1f, 5);
            Vector3 anchor = root.transform.position;
            try
            {
                Physics.SyncTransforms();
                Begin(npc, 24f, 8f);
                for (int i = 0; i < 140; i++)
                {
                    Step(npc);
                    Assert.That(root.transform.position.x - anchor.x, Is.LessThan(.33f));
                    Assert.That(root.transform.position.y - anchor.y, Is.LessThan(.4f));
                }
                Assert.That(root.transform.position, Is.EqualTo(anchor));
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(wall); Object.DestroyImmediate(ceiling); }
        }
    }
}
