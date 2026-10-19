using System.Linq;
using CouchGuys.Gameplay.Vehicles;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CouchGuys.Tests.Editor
{
    public sealed class VanSeatingTests
    {
        private static readonly string[] PrefabNames = { "LightVan", "GoodVan", "BestVan" };
        private static readonly string[] SeatNames =
        {
            "Seat_00_Driver",
            "Seat_01_FrontPassenger",
            "Seat_02_RearPassenger",
            "Seat_03_RearPassenger"
        };
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
            Assert.That(VanController.MaximumSeats, Is.EqualTo(4));

            foreach (string prefabName in PrefabNames)
            {
                string path = $"Assets/_Project/Prefabs/Vehicles/{prefabName}.prefab";
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.That(prefab, Is.Not.Null, path);

                Transform importedTruck = Find(prefab.transform, "ImportedTruckModel");
                Assert.That(importedTruck, Is.Not.Null, $"{prefabName} truck visual");
                Assert.That(Find(prefab.transform, "ImportedVanModel"), Is.Null, $"{prefabName} old visual");
                Assert.That(importedTruck.localScale, Is.EqualTo(Vector3.one * 100f));

                Renderer[] renderers = importedTruck.GetComponentsInChildren<Renderer>(true);
                Assert.That(renderers, Is.Not.Empty, prefabName);
                Bounds truckBounds = renderers[0].bounds;
                foreach (Renderer renderer in renderers.Skip(1)) truckBounds.Encapsulate(renderer.bounds);
                Assert.That(truckBounds.size.x, Is.InRange(2.5f, 3.5f), $"{prefabName} width");
                Assert.That(truckBounds.size.y, Is.InRange(3.5f, 4.5f), $"{prefabName} height");
                Assert.That(truckBounds.size.z, Is.InRange(7f, 9f), $"{prefabName} length");

                VanController controller = prefab.GetComponent<VanController>();
                Assert.That(controller, Is.Not.Null, prefabName);
                SerializedProperty seats = new SerializedObject(controller).FindProperty("m_seatAnchors");
                Assert.That(seats.arraySize, Is.EqualTo(4), prefabName);

                for (int seatIndex = 0; seatIndex < seats.arraySize; seatIndex++)
                {
                    Transform seat = seats.GetArrayElementAtIndex(seatIndex).objectReferenceValue as Transform;
                    Transform marker = Find(importedTruck, ModelMarkerNames[seatIndex]);
                    Assert.That(seat, Is.Not.Null, $"{prefabName} seat {seatIndex}");
                    Assert.That(seat.name, Is.EqualTo(SeatNames[seatIndex]));
                    Assert.That(marker, Is.Not.Null, $"{prefabName} marker {ModelMarkerNames[seatIndex]}");
                    Assert.That(Vector3.Distance(seat.position + Vector3.up * .8f, marker.position),
                        Is.LessThan(.001f),
                        $"{prefabName} seat {seatIndex} must compensate for the player visual offset");
                    Assert.That(seat.position.y, Is.InRange(.7f, .9f),
                        $"{prefabName} seat {seatIndex} root height");
                }

                Object[] distinctSeats = Enumerable.Range(0, seats.arraySize)
                    .Select(index => seats.GetArrayElementAtIndex(index).objectReferenceValue).ToArray();
                Assert.That(distinctSeats.Distinct().Count(), Is.EqualTo(4), prefabName);
            }
        }

        private static Transform Find(Transform root, string name)
        {
            return root.GetComponentsInChildren<Transform>(true).FirstOrDefault(value => value.name == name);
        }
    }
}
