using CouchGuys.Gameplay.Vehicles;
using System.Reflection;
using CouchGuys.ProceduralGeneration;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CouchGuys.Tests.Editor
{
    public sealed class UphillExitRestrictionTests
    {
        private static UphillExitRestriction Create(Vector3 start, Vector3 direction)
        {
            var configuration = UphillExitRestrictionConfiguration.Load();
            Assert.That(configuration, Is.Not.Null);
            Assert.That(configuration.Prefab, Is.Not.Null);
            var restriction = Object.Instantiate(configuration.Prefab);
            restriction.Initialise(null, start, start + direction * 240f + Vector3.up * 100f, 26f);
            return restriction;
        }

        [TestCase(0f)]
        [TestCase(90f)]
        [TestCase(180f)]
        [TestCase(270f)]
        public void InitialClimbTurnsIntoASlowSlideWithoutSnapping(float heading)
        {
            Vector3 start = new Vector3(80f, 6f, -25f);
            Vector3 direction = Quaternion.Euler(0, heading, 0) * Vector3.forward;
            var restriction = Create(start, direction);
            try
            {
                foreach (float inputSpeed in new[] { 3.5f, 6f, 1000f })
                {
                    Vector3 position = start;
                    float limit = float.PositiveInfinity;
                    float maximumProgress = 0f;
                    bool slidDown = false;
                    for (int i = 0; i < 120; i++)
                    {
                        if (!restriction.ConstrainPlayerMotion(position, direction * inputSpeed * .02f,
                            .6f, .02f, ref limit, out var destination)) break;
                        float step = Vector3.Dot(destination - position, direction);
                        Assert.That(Mathf.Abs(step), Is.LessThanOrEqualTo(.121f), "No position snaps");
                        slidDown |= step < 0f;
                        position = destination;
                        maximumProgress = Mathf.Max(maximumProgress, Vector3.Dot(position - start, direction));
                    }
                    Assert.That(maximumProgress, Is.GreaterThan(.1f), "Players can initially climb");
                    Assert.That(maximumProgress, Is.LessThan(12f));
                    Assert.That(slidDown, Is.True, "Sprint and grapple momentum cannot prevent sliding");
                }
            }
            finally { Object.DestroyImmediate(restriction.gameObject); }
        }

        [Test]
        public void PlacementHighOnHillSlidesLocallyWithoutSnappingToEntrance()
        {
            var restriction = Create(Vector3.zero, Vector3.forward);
            var vehicle = new GameObject("Restricted vehicle physics test");
            try
            {
                vehicle.AddComponent<BoxCollider>();
                var body = vehicle.AddComponent<Rigidbody>();
                body.position = new Vector3(0f, 100f, 200f);
                body.linearVelocity = new Vector3(4f, 50f, 1000f);
                Assert.That(restriction.ConstrainBody(body, .02f), Is.True);
                Assert.That(body.position, Is.EqualTo(new Vector3(0f, 100f, 200f)));
                Assert.That(body.linearVelocity.z, Is.LessThan(0f));
                Assert.That(body.linearVelocity.y, Is.LessThanOrEqualTo(0f));
                Assert.That(body.linearVelocity.x, Is.EqualTo(4f).Within(.001f));
                float limit = float.PositiveInfinity;
                Assert.That(restriction.ConstrainPlayerMotion(body.position, new Vector3(0f, 100f, 1000f),
                    .6f, .02f, ref limit, out var destination), Is.True);
                Assert.That(destination.z, Is.EqualTo(199.95f).Within(.001f));
                Assert.That(destination.y, Is.EqualTo(100f), "No terrain teleport");
            }
            finally
            {
                Object.DestroyImmediate(vehicle);
                Object.DestroyImmediate(restriction.gameObject);
            }
        }

        [Test]
        public void VehicleTractionGraduallyTurnsIntoDownhillVelocity()
        {
            var restriction = Create(Vector3.zero, Vector3.forward);
            var vehicle = new GameObject("Vehicle traction test");
            try
            {
                vehicle.AddComponent<BoxCollider>();
                var body = vehicle.AddComponent<Rigidbody>();
                body.position = Vector3.forward;
                float previous = 6f;
                for (int i = 0; i < 120; i++)
                {
                    body.linearVelocity = Vector3.forward * 1000f;
                    Assert.That(restriction.ConstrainBody(body, .02f), Is.True);
                    Assert.That(body.position, Is.EqualTo(Vector3.forward));
                    Assert.That(body.linearVelocity.z, Is.LessThanOrEqualTo(previous));
                    Assert.That(body.linearVelocity.z, Is.GreaterThan(previous - .3f));
                    previous = body.linearVelocity.z;
                }
                Assert.That(previous, Is.EqualTo(-3f).Within(.001f));
            }
            finally
            {
                Object.DestroyImmediate(vehicle);
                Object.DestroyImmediate(restriction.gameObject);
            }
        }

        [Test]
        public void LongFramesAndReentryHigherUpCannotSkipTheSlipperySection()
        {
            var restriction = Create(Vector3.zero, Vector3.forward);
            try
            {
                float limit = float.PositiveInfinity;
                restriction.ConstrainPlayerMotion(Vector3.zero, Vector3.forward * 1000f,
                    .6f, 10f, ref limit, out var destination);
                Assert.That(destination.z, Is.LessThan(12f));
                limit = float.PositiveInfinity;
                restriction.ConstrainPlayerMotion(Vector3.forward * 20f, Vector3.forward * 1000f,
                    .6f, .02f, ref limit, out destination);
                Assert.That(destination.z, Is.EqualTo(19.95f).Within(.001f));
                float otherPlayer = float.PositiveInfinity;
                restriction.ConstrainPlayerMotion(Vector3.zero, Vector3.forward * .12f,
                    .6f, .02f, ref otherPlayer, out destination);
                Assert.That(destination.z, Is.GreaterThan(0f), "Slide state is per player");
            }
            finally { Object.DestroyImmediate(restriction.gameObject); }
        }

        [Test]
        public void LeavingTheRouteRestoresTractionForTheNextClimb()
        {
            var restriction = Create(Vector3.zero, Vector3.forward);
            var player = new GameObject("Slide reset test");
            try
            {
                restriction.SendMessage("OnEnable");
                float limit = -2.5f;
                player.transform.position = Vector3.back * 10f;
                Assert.That(UphillExitRestriction.ConstrainPlayer(player.transform, Vector3.zero, .6f,
                    .02f, ref limit, out _), Is.False);
                Assert.That(limit, Is.EqualTo(float.PositiveInfinity));
                player.transform.position = Vector3.zero;
                Assert.That(UphillExitRestriction.ConstrainPlayer(player.transform, Vector3.forward * .12f, .6f,
                    .02f, ref limit, out var destination), Is.True);
                Assert.That(destination.z, Is.GreaterThan(0f));
            }
            finally
            {
                Object.DestroyImmediate(player);
                Object.DestroyImmediate(restriction.gameObject);
            }
        }

        [Test]
        public void CrossTrafficBelowTheEntranceKeepsMoving()
        {
            var restriction = Create(Vector3.zero, Vector3.forward);
            var vehicle = new GameObject("Cross traffic test");
            try
            {
                vehicle.AddComponent<BoxCollider>().size = new Vector3(10f, 1f, 2f);
                var body = vehicle.AddComponent<Rigidbody>();
                body.position = new Vector3(0f, .5f, -4f);
                body.linearVelocity = Vector3.right * 10f;
                Physics.SyncTransforms();
                Assert.That(restriction.ConstrainBody(body, .02f), Is.False);
                Assert.That(body.linearVelocity.x, Is.EqualTo(10f));
            }
            finally
            {
                Object.DestroyImmediate(vehicle);
                Object.DestroyImmediate(restriction.gameObject);
            }
        }

        [Test]
        public void OnlyTheSavedFlamecouchIdentityIsExempt()
        {
            var restriction = Create(Vector3.zero, Vector3.forward);
            var flame = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/_Project/Prefabs/Vehicles/Flamecouch.prefab"));
            var truck = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/_Project/Prefabs/Vehicles/Truck.prefab"));
            try
            {
                truck.name = "Flamecouch";
                Assert.That(restriction.AllowsVehicle(truck.GetComponent<TruckController>()), Is.False);
                Assert.That(restriction.AllowsVehicle(flame.GetComponent<TruckController>()), Is.True);
                Assert.That(UphillExitRestriction.AllowsExitVehicle(truck.GetComponent<TruckController>()), Is.False);
                Assert.That(UphillExitRestriction.AllowsExitVehicle(flame.GetComponent<TruckController>()), Is.True);
                var body = flame.GetComponent<Rigidbody>();
                body.position = new Vector3(0f, 40f, 100f);
                body.linearVelocity = Vector3.forward * 12f;
                Assert.That(restriction.ConstrainBody(body, .02f), Is.False);
                Assert.That(body.position.z, Is.EqualTo(100f));
                Assert.That(body.linearVelocity.z, Is.EqualTo(12f));
            }
            finally
            {
                Object.DestroyImmediate(truck);
                Object.DestroyImmediate(flame);
                Object.DestroyImmediate(restriction.gameObject);
            }
        }

        [Test]
        public void GeneratorInstantiatesTheSavedRestrictionForItsSummitRoute()
        {
            var host = new GameObject("Generated uphill restriction test");
            try
            {
                var generator = host.AddComponent<NeighbourhoodGenerator>();
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var type = typeof(NeighbourhoodGenerator);
                var region = AssetDatabase.LoadAssetAtPath<RegionDefinition>("Assets/_Project/Settings/Regions/Region_Suburbs.asset");
                type.GetMethod("ApplyRegionConfiguration", flags).Invoke(generator, new object[] { region });
                type.GetMethod("GenerateGuaranteedCentralRoad", flags).Invoke(generator, null);
                type.GetMethod("GenerateUphillExitRoad", flags).Invoke(generator, null);
                var generated = new GameObject("Test generated root");
                generated.transform.SetParent(host.transform);
                type.GetField("m_generatedRoot", flags).SetValue(generator, generated.transform);
                type.GetProperty("IsGenerationReady").SetValue(generator, true);
                type.GetMethod("CreateUphillExitRestriction", flags).Invoke(generator, null);
                var restriction = generated.GetComponentInChildren<UphillExitRestriction>();
                Assert.That(restriction, Is.Not.Null);
                restriction.SendMessage("OnEnable");
                Vector3 summit = generator.SummitExitPortal.WorldPosition;
                Assert.That(restriction.IntersectsRoute(summit, summit, .6f), Is.True);
                UphillExitRestriction.RemoveGenerator(generator);
                Assert.That(restriction.IntersectsRoute(summit, summit, .6f), Is.False);
            }
            finally { Object.DestroyImmediate(host); }
        }

        [Test]
        public void StreetsOutsideTheExitRemainAccessibleAndClearedRoutesStopRestricting()
        {
            var restriction = Create(Vector3.zero, Vector3.forward);
            try
            {
                Vector3 position = new Vector3(100f, 0f, 10f);
                Vector3 movement = Vector3.forward;
                float limit = float.PositiveInfinity;
                Assert.That(restriction.ConstrainPlayerMotion(position, movement, .6f, .02f, ref limit, out var destination), Is.False);
                Assert.That(destination, Is.EqualTo(position + movement));
                restriction.SendMessage("OnEnable");
                UphillExitRestriction.RemoveGenerator(null);
                Assert.That(restriction.IntersectsRoute(Vector3.forward, Vector3.forward * 100f, .6f), Is.False);
            }
            finally { Object.DestroyImmediate(restriction.gameObject); }
        }
    }
}
