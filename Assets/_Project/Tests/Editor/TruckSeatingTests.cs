using System.Linq;
using CouchGuys.Gameplay.Vehicles;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CouchGuys.Tests.Editor
{
    public sealed class TruckSeatingTests
    {
        private static readonly string[] PrefabNames = { "LightTruck", "GoodTruck", "BestTruck" };
        private static readonly string[] ModelMarkerNames =
        {
            "SeatAnchor_Front_Right",
            "SeatAnchor_Front_Left",
            "SeatAnchor_Rear_Right",
            "SeatAnchor_Rear_Left"
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

                Transform importedTruck = Find(prefab.transform, "ImportedTruckModel");
                Assert.That(importedTruck, Is.Not.Null, $"{prefabName} truck visual");
                Assert.That(importedTruck.localScale, Is.EqualTo(Vector3.one * 100f));

                Renderer[] renderers = importedTruck.GetComponentsInChildren<Renderer>(true);
                Assert.That(renderers, Is.Not.Empty, prefabName);
                Bounds truckBounds = renderers[0].bounds;
                foreach (Renderer renderer in renderers.Skip(1)) truckBounds.Encapsulate(renderer.bounds);
                Assert.That(truckBounds.size.x, Is.InRange(2.5f, 3.5f), $"{prefabName} width");
                Assert.That(truckBounds.size.y, Is.InRange(3.5f, 4.5f), $"{prefabName} height");
                Assert.That(truckBounds.size.z, Is.InRange(7f, 9f), $"{prefabName} length");

                TruckController controller = prefab.GetComponent<TruckController>();
                Assert.That(controller, Is.Not.Null, prefabName);
                SerializedProperty seats = new SerializedObject(controller).FindProperty("m_seatAnchors");
                Assert.That(seats.arraySize, Is.EqualTo(4), prefabName);

                for (int seatIndex = 0; seatIndex < seats.arraySize; seatIndex++)
                {
                    Transform seat = seats.GetArrayElementAtIndex(seatIndex).objectReferenceValue as Transform;
                    Transform marker = Find(importedTruck, ModelMarkerNames[seatIndex]);
                    Assert.That(seat, Is.Not.Null, $"{prefabName} seat {seatIndex}");
                    Assert.That(marker, Is.Not.Null, $"{prefabName} marker {ModelMarkerNames[seatIndex]}");
                    Assert.That(seat, Is.EqualTo(marker), "Use the original imported seat anchor");
                    Assert.That((seat.position + controller.SeatedRootOffset).y, Is.InRange(.7f, .9f));
                    Transform door = controller.GetDoorPoint(seatIndex);
                    Assert.That(door, Is.Not.Null);
                    string suffix = ModelMarkerNames[seatIndex].Replace("SeatAnchor_", "");
                    string[] parts = suffix.Split('_');
                    Transform surface = Find(importedTruck, $"Cab_{parts[1]}_{parts[0]}_DoorSurface");
                    Assert.That(door.position.z, Is.EqualTo(surface.GetComponent<Renderer>().bounds.center.z).Within(.02f));
                    Assert.That(Mathf.Abs(door.position.x - surface.GetComponent<Renderer>().bounds.center.x), Is.InRange(.5f, .8f));
                    Assert.That(controller.FindEntrySeat(door.position), Is.EqualTo(seatIndex));
                    Assert.That(Vector3.Dot(door.forward, Vector3.right * Mathf.Sign(door.position.x)), Is.GreaterThan(.99f));
                }

                Object[] distinctSeats = Enumerable.Range(0, seats.arraySize)
                    .Select(index => seats.GetArrayElementAtIndex(index).objectReferenceValue).ToArray();
                Assert.That(distinctSeats.Distinct().Count(), Is.EqualTo(4), prefabName);
                Assert.That(controller.FindEntrySeat(prefab.transform.position), Is.EqualTo(-1), "Truck centre is not an entry");
                Assert.That(new SerializedObject(controller).FindProperty("m_shutterSlats").arraySize, Is.EqualTo(15));
            }
        }

        private static Transform Find(Transform root, string name)
        {
            return root.GetComponentsInChildren<Transform>(true).FirstOrDefault(value => value.name == name);
        }
    }
}
