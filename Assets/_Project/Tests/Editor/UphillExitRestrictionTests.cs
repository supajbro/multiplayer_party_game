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
        public void WalkingJumpingAndSweptGrappleMovementCannotAdvance(float heading)
        {
            Vector3 start = new Vector3(80f, 6f, -25f);
            Vector3 direction = Quaternion.Euler(0, heading, 0) * Vector3.forward;
            var restriction = Create(start, direction);
            try
            {
                Vector3 position = start - direction * .5f;
                Assert.That(restriction.ConstrainPlayerMotion(position, direction * 2f, .6f, .02f, out var destination), Is.True);
                Assert.That(Vector3.Dot(destination - position, direction), Is.LessThan(0f));
                Assert.That(restriction.ConstrainPlayerMotion(position + Vector3.up * 300f,
                    direction * 1000f + Vector3.up * 100f, .6f, .02f, out destination), Is.True);
                Assert.That(Vector3.Dot(destination - start, direction), Is.LessThan(0f));
                Vector3 across = Vector3.Cross(Vector3.up, direction);
                position = start - across * 100f - direction * 20f;
                Assert.That(restriction.ConstrainPlayerMotion(position,
                    across * 200f + direction * 300f, .6f, .02f, out destination), Is.True);
                Assert.That(Vector3.Dot(destination - start, direction), Is.LessThan(0f));
            }
            finally { Object.DestroyImmediate(restriction.gameObject); }
        }

        [Test]
        public void VehicleMomentumIsReversedAndAForcedSummitPlacementIsRecovered()
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
                Assert.That(body.position.z, Is.LessThan(0f));
                Assert.That(body.linearVelocity.z, Is.LessThan(0f));
                Assert.That(body.linearVelocity.y, Is.LessThanOrEqualTo(0f));
                Assert.That(body.linearVelocity.x, Is.EqualTo(4f).Within(.001f));
            }
            finally
            {
                Object.DestroyImmediate(vehicle);
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
                Assert.That(restriction.ConstrainPlayerMotion(position, movement, .6f, .02f, out var destination), Is.False);
                Assert.That(destination, Is.EqualTo(position + movement));
                restriction.SendMessage("OnEnable");
                UphillExitRestriction.RemoveGenerator(null);
                Assert.That(restriction.IntersectsRoute(Vector3.forward, Vector3.forward * 100f, .6f), Is.False);
            }
            finally { Object.DestroyImmediate(restriction.gameObject); }
        }
    }
}
