using System.Collections.Generic;
using System.Reflection;
using CouchGuys.CameraSystem;
using CouchGuys.Gameplay.Couch;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CouchGuys.Tests.Editor
{
    public sealed class CameraCollisionTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly Vector3 Origin = new(15000f, 1000f, 15000f);
        private readonly List<GameObject> m_objects = new();
        private ThirdPersonCameraController m_camera;

        [SetUp]
        public void SetUp()
        {
            GameObject rig = Create("Camera collision test", Origin);
            rig.SetActive(false);
            m_camera = rig.AddComponent<ThirdPersonCameraController>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject instance in m_objects) Object.DestroyImmediate(instance);
            m_objects.Clear();
        }

        [TestCase(-35f)]
        [TestCase(12f)]
        [TestCase(65f)]
        public void Orbit_StartingInsideCouch_KeepsCameraSphereOutside(float pitch)
        {
            BoxCollider couch = CreateBox(Origin, new Vector3(3f, 2f, 3f));
            typeof(ThirdPersonCameraController).GetField("m_cameraDistance", Private).SetValue(m_camera, 1f);
            Physics.SyncTransforms();

            Vector3 result = Invoke("CalculateCollisionPosition", Origin, Quaternion.Euler(pitch, 0f, 0f));

            Assert.That(Vector3.Distance(result, couch.ClosestPoint(result)), Is.GreaterThanOrEqualTo(0.199f));
        }

        [Test]
        public void SmoothedPosition_InsideFurniture_IsClampedBeforeSurface()
        {
            BoxCollider couch = CreateBox(Origin + Vector3.back * 2f, Vector3.one);
            Physics.SyncTransforms();

            Vector3 result = Invoke("ResolveCollisionPosition", Origin, couch.transform.position);

            Assert.That(result.z - Origin.z, Is.InRange(-1.21f, -1.19f));
            Assert.That(Vector3.Distance(result, couch.ClosestPoint(result)), Is.GreaterThan(0.29f));
        }

        [Test]
        public void Pivot_IgnoresPlayerAndTriggers_ButClearsFurniture()
        {
            BoxCollider player = CreateBox(Origin, Vector3.one * 10f);
            typeof(ThirdPersonCameraController).GetField("m_playerRoot", Private).SetValue(m_camera, player.transform);
            CreateBox(Origin, Vector3.one * 20f).isTrigger = true;
            Physics.SyncTransforms();
            Assert.That(Invoke("GetClearCollisionPivot", Origin), Is.EqualTo(Origin));

            BoxCollider couch = CreateBox(Origin, Vector3.one * 2f);
            Physics.SyncTransforms();
            Vector3 pivot = Invoke("GetClearCollisionPivot", Origin);
            Assert.That(pivot.y, Is.GreaterThan(couch.bounds.max.y + 0.2f));
        }

        [Test]
        public void ClearBoom_PreservesConfiguredDistance()
        {
            Physics.SyncTransforms();
            Vector3 result = Invoke("CalculateCollisionPosition", Origin, Quaternion.identity);
            Assert.That(Vector3.Distance(result, Origin + new Vector3(0.65f, 0f, -4.5f)), Is.LessThan(0.002f));
        }

        [Test]
        public void HeldCouch_AnimatedVisualOutsideCollider_BlocksSmoothedCamera()
        {
            WithCouch(couch =>
            {
                Transform visual = couch.transform.Find("Visual");
                visual.localRotation = Quaternion.Euler(12f, 0f, 14f);
                visual.localScale = new Vector3(1.3f, 1.1f, 1.2f);
                UpdateVisualBounds(couch);
                Bounds bounds = VisualBounds(couch);
                // Sweep above the collider but through the animated visual.
                Vector3 destination = new(bounds.center.x, bounds.max.y - 0.02f, bounds.center.z);
                Vector3 pivot = destination + Vector3.forward * 3f;
                Physics.SyncTransforms();
                typeof(ThirdPersonCameraController).GetField("m_hasCarriedCouchBounds", Private).SetValue(m_camera, false);
                Vector3 unprotected = Invoke("ResolveCollisionPosition", pivot, destination);
                Assert.That(bounds.Contains(unprotected), Is.True, "Reproduce the physics-only visual clipping.");
                UpdateVisualBounds(couch);
                Vector3 result = Invoke("ResolveCollisionPosition", pivot, destination);
                Assert.That(bounds.Contains(result), Is.False);
                Assert.That(Vector3.Distance(result, bounds.ClosestPoint(result)), Is.GreaterThan(0.29f));
            });
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void HeldCouch_AllOrbitAnglesAndEmbeddedPivots_KeepCameraOutsideVisual(int variant)
        {
            WithCouch(couch =>
            {
                // Visual protection must survive physics filtering and work at the
                // rendered pose even when the query world has not been synchronized.
                typeof(ThirdPersonCameraController).GetField("m_collisionLayers", Private)
                    .SetValue(m_camera, (LayerMask)0);
                foreach (float roll in new[] { -45f, 0f, 45f, 90f })
                {
                    couch.transform.rotation = Quaternion.Euler(20f, 30f, roll);
                    couch.transform.Find("Visual").localRotation = Quaternion.Euler(12f, 0f, 14f);
                    UpdateVisualBounds(couch);
                    Bounds bounds = VisualBounds(couch);
                    foreach (Vector3 pivot in new[] { bounds.center, bounds.center + Vector3.forward * 2f })
                    for (int yaw = 0; yaw < 360; yaw += 15)
                    foreach (float pitch in new[] { -35f, 12f, 65f })
                    {
                        Vector3 result = Invoke("CalculateCollisionPosition", pivot, Quaternion.Euler(pitch, yaw, 0f));
                        Assert.That(Vector3.Distance(result, bounds.ClosestPoint(result)), Is.GreaterThan(0.299f),
                            $"roll={roll}, yaw={yaw}, pitch={pitch}, pivot={pivot}");
                        // A stale, smoothed destination can be anywhere inside the couch.
                        result = Invoke("ResolveCollisionPosition", pivot, bounds.center);
                        Assert.That(Vector3.Distance(result, bounds.ClosestPoint(result)), Is.GreaterThan(0.299f));
                    }
                }
            }, variant);
        }

        [Test]
        public void HeldCouch_WideNearPlaneAndChildOffset_RemainOutsideVisual()
        {
            WithCouch(couch =>
            {
                Camera lens = Create("Offset camera", Origin).AddComponent<Camera>();
                lens.transform.SetParent(m_camera.transform, false);
                lens.transform.localPosition = new Vector3(0.4f, 0.1f, 0.2f);
                lens.nearClipPlane = 0.5f;
                lens.fieldOfView = 90f;
                lens.aspect = 2.4f;
                typeof(ThirdPersonCameraController).GetField("m_camera", Private).SetValue(m_camera, lens);
                UpdateVisualBounds(couch);
                Bounds bounds = VisualBounds(couch);
                Vector3 result = Invoke("ResolveCollisionPosition", bounds.center + Vector3.forward * 4f, bounds.center);
                m_camera.transform.position = result;
                Vector3[] corners = new Vector3[4];
                lens.CalculateFrustumCorners(new Rect(0f, 0f, 1f, 1f), lens.nearClipPlane,
                    Camera.MonoOrStereoscopicEye.Mono, corners);
                foreach (Vector3 corner in corners)
                    Assert.That(bounds.Contains(lens.transform.TransformPoint(corner)), Is.False);
            });
        }

        [Test]
        public void HeldCouch_TallerVisual_DoesNotLiftOrbitPivot()
        {
            WithCouch(couch =>
            {
                couch.transform.Find("Visual").localScale = Vector3.one * 3f;
                Bounds bounds = VisualBounds(couch);
                // Start below the visual guard. An unrelated overlapping collider
                // lifts the pivot into it during the physics clearance pass.
                Vector3 pivot = bounds.center + Vector3.down * (bounds.extents.y + 0.8f);
                CreateBox(pivot, Vector3.one * 0.8f);
                UpdateVisualBounds(couch);
                Physics.SyncTransforms();
                Vector3 result = Invoke("GetClearCollisionPivot", pivot);
                Assert.That(result.y, Is.LessThan(bounds.max.y), "Only world geometry should lift the query pivot.");
            });
        }

        [Test]
        public void AlternateCameraBlend_InsideHeldCouch_IsCorrected()
        {
            WithCouch(couch =>
            {
                UpdateVisualBounds(couch);
                Bounds bounds = VisualBounds(couch);
                m_camera.transform.position = bounds.center;
                typeof(ThirdPersonCameraController).GetMethod("ResolveCameraVisualOverlap", Private)
                    .Invoke(m_camera, null);
                Assert.That(Vector3.Distance(m_camera.transform.position, bounds.ClosestPoint(m_camera.transform.position)),
                    Is.GreaterThan(0.299f));
            });
        }

        [Test]
        public void ReleasedCouch_ClearsVisualGuard()
        {
            WithCouch(couch =>
            {
                typeof(ThirdPersonCameraController).GetField("m_collisionLayers", Private)
                    .SetValue(m_camera, (LayerMask)0);
                UpdateVisualBounds(couch);
                UpdateVisualBounds(null);
                Vector3 result = Invoke("CalculateCollisionPosition", Origin, Quaternion.identity);
                Assert.That(Vector3.Distance(result, Origin + new Vector3(0.65f, 0f, -4.5f)), Is.LessThan(0.002f));
            });
        }

        [Test]
        public void HeldCouch_AcrossSightLine_DoesNotShortenClearBoomOrLiftPivot()
        {
            WithCouch(couch =>
            {
                couch.transform.position += Vector3.back * 2f;
                UpdateVisualBounds(couch);
                Physics.SyncTransforms();
                Vector3 desired = Origin + Vector3.back * 8f;
                Assert.That(Invoke("GetClearCollisionPivot", Origin), Is.EqualTo(Origin));
                Assert.That(Vector3.Distance(Invoke("ResolveCollisionPosition", Origin, desired), desired), Is.LessThan(0.002f));
                Bounds bounds = VisualBounds(couch);
                Assert.That(Invoke("GetClearCollisionPivot", bounds.center), Is.EqualTo(bounds.center));
            });
        }

        [Test]
        public void CarryDistance_GrabAndReleaseBlendTowardConfiguredDistances()
        {
            WithCouch(couch =>
            {
                UpdateVisualBounds(couch);
                StepPreferredDistance(1f / 60f);
                float first = Field<float>("m_preferredDistance");
                Assert.That(first, Is.InRange(4.5f, 5.625f));
                for (int i = 0; i < 120; i++) StepPreferredDistance(1f / 60f);
                Assert.That(Field<float>("m_preferredDistance"), Is.EqualTo(5.625f).Within(0.002f));
                UpdateVisualBounds(null);
                StepPreferredDistance(1f / 60f);
                Assert.That(Field<float>("m_preferredDistance"), Is.InRange(4.51f, 5.624f));
                for (int i = 0; i < 120; i++) StepPreferredDistance(1f / 60f);
                Assert.That(Field<float>("m_preferredDistance"), Is.EqualTo(4.5f).Within(0.002f));
            });
        }

        [TestCase(30)]
        [TestCase(60)]
        [TestCase(120)]
        public void CollisionDamping_FastCompressionSlowRecoveryAndBriefGapHold(int fps)
        {
            float dt = 1f / fps;
            SmoothBoom(5f, dt);
            float inward = 5f - Vector3.Distance(Origin, SmoothBoom(2f, dt));
            // Settle against an obstacle, then repeatedly expose a single clear frame.
            for (int i = 0; i < fps; i++) SmoothBoom(2f, dt);
            for (int i = 0; i < 8; i++)
            {
                float before = Field<float>("m_smoothedBoomDistance");
                SmoothBoom(5f, dt);
                Assert.That(Field<float>("m_smoothedBoomDistance"), Is.LessThanOrEqualTo(before + 0.001f));
                SmoothBoom(1.95f, dt);
            }
            float outward = 0f;
            for (int i = 0; i < fps && outward <= 0f; i++)
            {
                float start = Field<float>("m_smoothedBoomDistance");
                outward = Vector3.Distance(Origin, SmoothBoom(5f, dt)) - start;
            }
            Assert.That(outward, Is.GreaterThan(0f).And.LessThan(inward));
        }

        [Test]
        public void TightCorner_MovingAndRotatingBoomStaysOutsideWalls()
        {
            BoxCollider backWall = CreateBox(Origin + Vector3.back * 3f, new Vector3(12f, 6f, 0.2f));
            BoxCollider sideWall = CreateBox(Origin + Vector3.right * 3f, new Vector3(0.2f, 6f, 12f));
            Physics.SyncTransforms();
            for (int frame = 0; frame < 360; frame++)
            {
                Vector3 pivot = Origin + new Vector3(Mathf.Sin(frame * 0.02f), 0f, Mathf.Cos(frame * 0.02f));
                Quaternion rotation = Quaternion.Euler(12f, frame * 2f, 0f);
                Vector3 safe = Invoke("CalculateCollisionPosition", pivot, rotation);
                Vector3 damped = Invoke("SmoothCollisionPosition", pivot, safe, 1f / 60f);
                Vector3 result = Invoke("ResolveCollisionPosition", pivot, damped);
                Assert.That(Vector3.Distance(result, backWall.ClosestPoint(result)), Is.GreaterThanOrEqualTo(0.199f));
                Assert.That(Vector3.Distance(result, sideWall.ClosestPoint(result)), Is.GreaterThanOrEqualTo(0.199f));
            }
        }

        private T Field<T>(string name) => (T)typeof(ThirdPersonCameraController).GetField(name, Private).GetValue(m_camera);

        private void StepPreferredDistance(float dt) => typeof(ThirdPersonCameraController)
            .GetMethod("UpdatePreferredDistance", Private).Invoke(m_camera, new object[] { dt });

        private Vector3 SmoothBoom(float distance, float dt) =>
            Invoke("SmoothCollisionPosition", Origin, Origin + Vector3.back * distance, dt);

        [Test]
        public void ObstructedBoom_ReproducesFaceClipping_ThenKeepsCameraOutsidePlayer()
        {
            Renderer head = CreatePlayerHead();
            CreateBox(Origin + Vector3.back * 0.85f, new Vector3(3f, 2.4f, 0.2f));
            Physics.SyncTransforms();
            Vector3 desired = Origin + Vector3.back * 4.5f;
            Vector3 unprotected = Invoke("CastCameraBoom", Origin, desired);
            Assert.That(head.bounds.Contains(unprotected), Is.True, "Reproduce the camera being pushed into the face.");

            Vector3 result = Invoke("ResolveCollisionPosition", Origin, desired);
            Assert.That(Vector3.Distance(result, head.bounds.ClosestPoint(result)), Is.GreaterThan(0.299f));
            foreach (Collider obstacle in Physics.OverlapSphere(result, 0.199f))
                Assert.Fail("The recovered camera overlaps " + obstacle.name);
        }

        [Test]
        public void PlayerVisualGuard_ClearBoomPreservesNormalFraming()
        {
            CreatePlayerHead();
            Physics.SyncTransforms();
            Vector3 desired = Origin + new Vector3(0.65f, 0f, -4.5f);
            Assert.That(Vector3.Distance(Invoke("ResolveCollisionPosition", Origin, desired), desired), Is.LessThan(0.002f));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CompressedCamera_RecoveryKeepsRobotInViewAndClearOfHeldCouch(bool carrying)
        {
            Renderer head = CreatePlayerHead();
            Camera lens = m_camera.gameObject.AddComponent<Camera>();
            lens.fieldOfView = 65f;
            lens.nearClipPlane = .1f;
            lens.aspect = 16f / 9f;
            typeof(ThirdPersonCameraController).GetField("m_camera", Private).SetValue(m_camera, lens);
            CreateBox(Origin + Vector3.back * .85f, new Vector3(3f, 2.4f, .2f));

            void CheckRecovery(CouchCarryController couch)
            {
                Physics.SyncTransforms();
                Vector3 result = Invoke("ResolveCollisionPosition", Origin,
                    Origin + new Vector3(.65f, 0f, -4.5f));
                m_camera.transform.position = result;
                m_camera.transform.rotation = (Quaternion)typeof(ThirdPersonCameraController)
                    .GetMethod("CorrectPlayerFraming", Private).Invoke(m_camera, new object[] { Quaternion.identity });
                Vector3 screen = lens.WorldToViewportPoint(head.bounds.center);
                Assert.That(screen.z, Is.GreaterThan(lens.nearClipPlane));
                Assert.That(screen.x, Is.InRange(.1f, .9f));
                Assert.That(screen.y, Is.InRange(.1f, .9f));
                Assert.That(Vector3.Distance(result, head.bounds.ClosestPoint(result)), Is.GreaterThan(.299f));
                if (couch != null)
                    Assert.That(Vector3.Distance(result, VisualBounds(couch).ClosestPoint(result)), Is.GreaterThan(.299f));
                foreach (Collider obstacle in Physics.OverlapSphere(result, .199f))
                    Assert.Fail("Recovered camera overlaps " + obstacle.name);
            }

            if (carrying)
                WithCouch(couch =>
                {
                    couch.transform.position += Vector3.forward * 2f;
                    UpdateVisualBounds(couch);
                    CheckRecovery(couch);
                });
            else CheckRecovery(null);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void HeldCouchAndPlayer_EmbeddedSmoothedCameraStaysOutsideBoth(int variant)
        {
            Renderer head = CreatePlayerHead();
            WithCouch(couch =>
            {
                couch.transform.position += Vector3.back * 2f;
                couch.transform.Find("Visual").localRotation = Quaternion.Euler(12f, 0f, 14f);
                UpdateVisualBounds(couch);
                Bounds couchBounds = VisualBounds(couch);
                Physics.SyncTransforms();
                for (int yaw = 0; yaw < 360; yaw += 15)
                {
                    Vector3 destination = Origin + Quaternion.Euler(0f, yaw, 0f) * Vector3.back * 0.2f;
                    Vector3 result = Invoke("ResolveCollisionPosition", Origin, destination);
                    Assert.That(Vector3.Distance(result, head.bounds.ClosestPoint(result)), Is.GreaterThan(0.299f));
                    Assert.That(Vector3.Distance(result, couchBounds.ClosestPoint(result)), Is.GreaterThan(0.299f));
                }
            }, variant);
        }

        [Test]
        public void PlayerVisualGuard_ProtectsWideNearPlaneDuringCameraBlend()
        {
            Renderer head = CreatePlayerHead();
            Camera lens = Create("Offset player camera", Origin).AddComponent<Camera>();
            lens.transform.SetParent(m_camera.transform, false);
            lens.transform.localPosition = new Vector3(0.2f, -0.1f, 0.1f);
            lens.nearClipPlane = 0.4f;
            lens.fieldOfView = 90f;
            lens.aspect = 2.4f;
            typeof(ThirdPersonCameraController).GetField("m_camera", Private).SetValue(m_camera, lens);
            m_camera.transform.position = Origin;
            typeof(ThirdPersonCameraController).GetMethod("ResolveCameraVisualOverlap", Private).Invoke(m_camera, null);
            Vector3[] corners = new Vector3[4];
            lens.CalculateFrustumCorners(new Rect(0f, 0f, 1f, 1f), lens.nearClipPlane,
                Camera.MonoOrStereoscopicEye.Mono, corners);
            foreach (Vector3 corner in corners)
                Assert.That(head.bounds.Contains(lens.transform.TransformPoint(corner)), Is.False);
        }

        [Test]
        public void SavedPlayerPrefab_ReferencesItsOwnVisualForCameraProtection()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Player.prefab");
            var controller = prefab.GetComponentInChildren<ThirdPersonCameraController>(true);
            Assert.That(new SerializedObject(controller).FindProperty("m_playerVisualRoot").objectReferenceValue,
                Is.EqualTo(prefab.transform.Find("Visual/CouchGuy")));
            int cameraOrder = typeof(ThirdPersonCameraController).GetCustomAttribute<DefaultExecutionOrder>().order;
            int hoverOrder = typeof(CouchGuys.Player.RobotHoverVFX).GetCustomAttribute<DefaultExecutionOrder>().order;
            Assert.That(cameraOrder, Is.GreaterThan(hoverOrder), "Camera must sample the final player hover pose.");
        }

        [Test]
        public void SavedRobotVisual_ObstructedCameraRemainsOutsideBodyAndFace()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Player.prefab");
            GameObject body = Object.Instantiate(prefab.transform.Find("Visual").gameObject);
            m_objects.Add(body);
            body.transform.position = Origin;
            Transform robot = body.transform.Find("CouchGuy");
            Assert.That(robot.GetComponentsInChildren<Renderer>().Length, Is.GreaterThan(0));
            typeof(ThirdPersonCameraController).GetField("m_playerVisualRoot", Private).SetValue(m_camera, robot);
            typeof(ThirdPersonCameraController).GetMethod("UpdatePlayerVisualBounds", Private).Invoke(m_camera, null);
            Bounds bounds = (Bounds)typeof(ThirdPersonCameraController).GetField("m_playerVisualBounds", Private).GetValue(m_camera);
            Assert.That(bounds.size.sqrMagnitude, Is.GreaterThan(0.1f));
            Vector3 pivot = bounds.center;
            CreateBox(pivot + Vector3.back * (bounds.extents.z + 0.25f), new Vector3(bounds.size.x + 2f, bounds.size.y + 2f, 0.1f));
            Physics.SyncTransforms();
            Vector3 destination = pivot + Vector3.back * 4.5f;
            Assert.That(bounds.Contains(Invoke("CastCameraBoom", pivot, destination)), Is.True,
                "Reproduce camera compression inside the saved robot model.");
            Vector3 result = Invoke("ResolveCollisionPosition", pivot, destination);
            Assert.That(Vector3.Distance(result, bounds.ClosestPoint(result)), Is.GreaterThan(0.299f));
        }

        private Renderer CreatePlayerHead()
        {
            GameObject head = GameObject.CreatePrimitive(PrimitiveType.Cube);
            head.name = "Player head visual";
            head.transform.position = Origin;
            Object.DestroyImmediate(head.GetComponent<Collider>());
            m_objects.Add(head);
            typeof(ThirdPersonCameraController).GetField("m_playerVisualRoot", Private).SetValue(m_camera, head.transform);
            typeof(ThirdPersonCameraController).GetMethod("UpdatePlayerVisualBounds", Private).Invoke(m_camera, null);
            return head.GetComponent<Renderer>();
        }

        [Test]
        public void CrowdedBoom_DoesNotLoseNearestColliderWhenHitBufferFills()
        {
            for (int i = 0; i < 40; i++)
                CreateBox(Origin + Vector3.back * (1f + i * 0.2f), new Vector3(2f, 2f, 0.05f));
            Physics.SyncTransforms();
            Vector3 result = Invoke("ResolveCollisionPosition", Origin, Origin + Vector3.back * 10f);
            Assert.That(result.z - Origin.z, Is.InRange(-0.68f, -0.67f));
            Assert.That(((RaycastHit[])typeof(ThirdPersonCameraController).GetField("m_collisionHits", Private)
                .GetValue(m_camera)).Length, Is.GreaterThanOrEqualTo(40));
        }

        private void WithCouch(System.Action<CouchCarryController> test, int variant = 0)
        {
            GameObject root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Couch.prefab"));
            m_objects.Add(root);
            root.transform.position = Origin;
            CouchCarryController couch = root.GetComponent<CouchCarryController>();
            // Match the model attachment performed during network spawn; the old
            // primitive placeholders in this saved prefab are intentionally inactive.
            typeof(CouchCarryController).GetMethod("ApplyModelVariant", Private).Invoke(couch, new object[] { variant });
            Assert.That(couch.GetComponentsInChildren<Renderer>(), Is.Not.Empty, "Saved couch model must be loaded.");
            test(couch);
        }

        private void UpdateVisualBounds(CouchCarryController couch)
        {
            typeof(ThirdPersonCameraController).GetMethod("UpdateCarriedCouchBounds", Private)
                .Invoke(m_camera, new object[] { couch });
        }

        private static Bounds VisualBounds(CouchCarryController couch)
        {
            Renderer[] renderers = couch.GetComponentsInChildren<Renderer>();
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer visual in renderers) bounds.Encapsulate(visual.bounds);
            return bounds;
        }

        private Vector3 Invoke(string method, params object[] arguments)
        {
            return (Vector3)typeof(ThirdPersonCameraController).GetMethod(method, Private).Invoke(m_camera, arguments);
        }

        private GameObject Create(string name, Vector3 position)
        {
            GameObject instance = new(name);
            instance.transform.position = position;
            m_objects.Add(instance);
            return instance;
        }

        private BoxCollider CreateBox(Vector3 position, Vector3 size)
        {
            BoxCollider collider = Create("Camera obstacle", position).AddComponent<BoxCollider>();
            collider.size = size;
            return collider;
        }
    }
}
