using System.Linq;
using CouchGuys.Gameplay.Vehicles;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CouchGuys.Tests.Editor
{
    public sealed class TruckSeatingTests
    {
        private static readonly string[] PrefabNames = { "Truck" };
        private static readonly string[] ModelMarkerNames =
        {
            "SeatAnchor_0",
            "SeatAnchor_1",
            "SeatAnchor_2",
            "SeatAnchor_3"
        };

        [Test]
        public void VehiclePrefabsUseTruckModelAndFourModelAlignedSeats()
        {
            Assert.That(TruckController.MaximumSeats, Is.EqualTo(4));

            foreach (string prefabName in PrefabNames)
            {
                string path = $"Assets/_Project/Prefabs/Vehicles/{prefabName}.prefab";
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.That(prefab, Is.Not.Null, path);

                Transform importedTruck = Find(prefab.transform, "ImportedHoverTruckModel");
                Assert.That(importedTruck, Is.Not.Null, $"{prefabName} truck visual");

                Renderer[] renderers = importedTruck.GetComponentsInChildren<Renderer>(true);
                Assert.That(renderers, Is.Not.Empty, prefabName);
                Bounds truckBounds = renderers[0].bounds;
                foreach (Renderer renderer in renderers.Skip(1)) truckBounds.Encapsulate(renderer.bounds);
                Assert.That(truckBounds.size.x, Is.InRange(3.5f, 4.2f), $"{prefabName} width");
                Assert.That(truckBounds.size.y, Is.InRange(2f, 2.6f), $"{prefabName} height");
                Assert.That(truckBounds.size.z, Is.InRange(9f, 10.5f), $"{prefabName} length");

                TruckController controller = prefab.GetComponent<TruckController>();
                Assert.That(controller, Is.Not.Null, prefabName);
                Assert.That(controller.IsHoverTruck, Is.True);
                SerializedProperty seats = new SerializedObject(controller).FindProperty("m_seatAnchors");
                Assert.That(seats.arraySize, Is.EqualTo(4), prefabName);

                for (int seatIndex = 0; seatIndex < seats.arraySize; seatIndex++)
                {
                    Transform seat = seats.GetArrayElementAtIndex(seatIndex).objectReferenceValue as Transform;
                    Transform marker = Find(prefab.transform, ModelMarkerNames[seatIndex]);
                    Assert.That(seat, Is.Not.Null, $"{prefabName} seat {seatIndex}");
                    Assert.That(marker, Is.Not.Null, $"{prefabName} marker {ModelMarkerNames[seatIndex]}");
                    Assert.That(seat, Is.EqualTo(marker), "Use the saved model-aligned seat anchor");
                    Assert.That((seat.position + controller.SeatedRootOffset).y, Is.InRange(.45f, .75f));
                    Transform door = controller.GetDoorPoint(seatIndex);
                    Assert.That(door, Is.Not.Null);
                    Assert.That(door.position.z, Is.EqualTo(seat.position.z).Within(.02f));
                    Assert.That(Mathf.Abs(door.position.x), Is.GreaterThan(2f));
                    Assert.That(controller.FindEntrySeat(door.position), Is.EqualTo(seatIndex));
                    Assert.That(Vector3.Dot(door.forward, Vector3.right * Mathf.Sign(door.position.x)), Is.GreaterThan(.99f));
                }

                Object[] distinctSeats = Enumerable.Range(0, seats.arraySize)
                    .Select(index => seats.GetArrayElementAtIndex(index).objectReferenceValue).ToArray();
                Assert.That(distinctSeats.Distinct().Count(), Is.EqualTo(4), prefabName);
                Assert.That(controller.FindEntrySeat(prefab.transform.position), Is.EqualTo(-1), "Truck centre is not an entry");
                Assert.That(controller.GetSeatAnchor(0).position.z, Is.EqualTo(controller.GetSeatAnchor(1).position.z).Within(.01f));
                Assert.That(controller.GetSeatAnchor(2).position.z, Is.EqualTo(controller.GetSeatAnchor(3).position.z).Within(.01f));
                Assert.That(controller.GetSeatAnchor(0).position.z, Is.GreaterThan(controller.GetSeatAnchor(2).position.z));
                Assert.That(new SerializedObject(controller).FindProperty("m_turretYaw").arraySize, Is.EqualTo(3));
                for (int seatIndex = 1; seatIndex < 4; seatIndex++)
                {
                    Assert.That(controller.GetTurretLeftGrip(seatIndex), Is.Not.Null);
                    Assert.That(controller.GetTurretRightGrip(seatIndex), Is.Not.Null);
                    Assert.That(controller.GetTurretAuraTarget(seatIndex), Is.Not.Null);
                    Assert.That(controller.GetTurretLeftGrip(seatIndex).position.x,
                        Is.LessThan(controller.GetTurretRightGrip(seatIndex).position.x));
                }
                Assert.That(controller.GetTurretAuraTarget(0), Is.Null);
            }
        }

        [Test]
        public void RearTurretForwardShotsClearTheFrontChairs()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Vehicles/Truck.prefab");
            var truck = prefab.GetComponent<TruckController>();
            var muzzles = new SerializedObject(truck).FindProperty("m_turretMuzzles");
            for (int turret = 1; turret < 3; turret++)
            {
                var muzzle = (Transform)muzzles.GetArrayElementAtIndex(turret).objectReferenceValue;
                var shot = new Ray(muzzle.position, prefab.transform.forward);
                for (int seat = 0; seat < 2; seat++)
                    foreach (var renderer in Find(prefab.transform, "Seat_" + seat).GetComponentsInChildren<Renderer>())
                        Assert.That(renderer.bounds.IntersectRay(shot), Is.False, "Rear turret shot crosses front chair " + seat);
            }
        }

        private static Transform Find(Transform root, string name)
        {
            return root.GetComponentsInChildren<Transform>(true).FirstOrDefault(value => value.name == name);
        }
    }
}
