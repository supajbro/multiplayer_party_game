using System.Collections.Generic;
using System.Reflection;
using CouchGuys.Gameplay.Couch;
using CouchGuys.Gameplay.Delivery;
using CouchGuys.Gameplay.Sled;
using CouchGuys.Gameplay.Vehicles;
using CouchGuys.Player;
using CouchGuys.ProceduralGeneration;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace CouchGuys.Tests.Editor
{
    public sealed class MinimapNavigationTests
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private readonly List<GameObject> m_objects = new();
        private static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, Flags).GetValue(target);
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, Flags).SetValue(target, value);
        private static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Flags).Invoke(target, args);

        private T Create<T>() where T : Component
        {
            var go = new GameObject(typeof(T).Name);
            go.SetActive(false);
            m_objects.Add(go);
            return go.AddComponent<T>();
        }

        private void SpawnForQuery(NetworkBehaviour behaviour, int id)
        {
            var obj = behaviour.GetComponent<NetworkObject>();
            typeof(NetworkObject).GetProperty("ObjectId", Flags).SetValue(obj, id);
            typeof(NetworkObject).GetProperty("IsDeinitializing", Flags).SetValue(obj, false);
            typeof(NetworkBehaviour).GetField("_networkObjectCache", Flags).SetValue(behaviour, obj);
        }

        private NeighbourhoodMap Map()
        {
            var map = Create<NeighbourhoodMap>();
            Set(map, "m_carrier", map.gameObject.AddComponent<PlayerCouchCarrier>());
            Set(map, "m_truckInteractor", map.gameObject.AddComponent<PlayerTruckInteractor>());
            Set(map, "m_hoverboardInteractor", map.gameObject.AddComponent<PlayerHoverboardInteractor>());
            return map;
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = m_objects.Count - 1; i >= 0; i--)
                if (m_objects[i] != null) Object.DestroyImmediate(m_objects[i]);
            m_objects.Clear();
        }

        [Test]
        public void StaticBakeConnectsRoadTilesAndLeavesMiscellaneousPropsAsGrass()
        {
            var map = Map();
            var generator = Create<NeighbourhoodGenerator>();
            Set(map, "m_generator", generator);
            Set(map, "m_mapBounds", new Bounds(Vector3.zero, new Vector3(40, 1, 40)));
            Set(map, "m_textureResolution", 256);
            var roads = (List<GeneratedRoad>)generator.GeneratedRoads;
            for (int i = 0; i < 2; i++)
            {
                var road = Create<GeneratedRoad>();
                road.transform.position = new Vector3(i * 10, 0, 0);
                road.Initialise(GridCoordinate.Zero, RoadConnections.East | RoadConnections.West);
                roads.Add(road);
            }
            var prop = new GameObject("Landmark_Statue");
            m_objects.Add(prop);
            prop.transform.SetParent(generator.transform);
            prop.transform.position = new Vector3(0, 0, 15);
            Call(map, "BuildStaticTexture");
            Texture2D raster = Get<Texture2D>(map, "m_mapTexture");
            var rt = RenderTexture.GetTemporary(256, 256, 0, RenderTextureFormat.ARGB32);
            var oldActive = RenderTexture.active;
            var copy = new Texture2D(256, 256);
            try
            {
                Graphics.Blit(raster, rt);
                RenderTexture.active = rt;
                copy.ReadPixels(new Rect(0, 0, 256, 256), 0, 0);
                copy.Apply();
                Color seam = copy.GetPixel(Mathf.RoundToInt(25f / 40 * 255), Mathf.RoundToInt(22f / 40 * 255));
                Color asphalt = copy.GetPixel(Mathf.RoundToInt(20f / 40 * 255), Mathf.RoundToInt(22f / 40 * 255));
                Assert.That(Vector4.Distance(seam, asphalt), Is.LessThan(0.02f), "No grass/sidewalk seam between connected tiles.");
                Color grass = copy.GetPixel(128, Mathf.RoundToInt(35f / 40 * 255));
                Assert.That(grass.g, Is.GreaterThan(grass.r * 1.4f));
                Assert.That(grass.g, Is.GreaterThan(grass.b * 1.4f));
            }
            finally
            {
                RenderTexture.active = oldActive;
                RenderTexture.ReleaseTemporary(rt);
                Object.DestroyImmediate(copy);
                Object.DestroyImmediate(raster);
                Set(map, "m_mapTexture", null);
            }
        }

        [Test]
        public void SavedArtworkIsSmallTransparentReadableAndWiredOnEveryHouse()
        {
            var player = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Player.prefab");
            var map = player.GetComponent<NeighbourhoodMap>();
            Assert.That(Get<MinimapPlayerMarker>(map, "m_playerMarkerPrefab"), Is.Not.Null);
            var textures = new HashSet<Texture2D>();
            foreach (string name in new[] { "House_01", "House_02", "House_03", "medium_house_02", "small_house_02" })
            {
                var house = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/_Project/Prefabs/Houses/{name}.prefab");
                var texture = house.GetComponent<GeneratedProperty>().MinimapHouseIcon;
                Assert.That(texture, Is.Not.Null, name);
                Assert.That(texture.width, Is.InRange(128, 256));
                Assert.That(texture.height, Is.InRange(128, 256));
                Assert.That(texture.isReadable, Is.True);
                var pixels = texture.GetPixels32(); // Actual imported compression must support the static CPU bake.
                Assert.That(System.Array.Exists(pixels, c => c.a == 0), Is.True);
                Assert.That(System.Array.Exists(pixels, c => c.a > 200), Is.True);
                textures.Add(texture);
            }
            Assert.That(textures.Count, Is.EqualTo(3));
        }

        [Test]
        public void HeadingRotatesOnlyTriangleAndPreservesPlayerTint()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/UI/MinimapPlayerMarker.prefab");
            var marker = Object.Instantiate(prefab).GetComponent<MinimapPlayerMarker>();
            m_objects.Add(marker.gameObject);
            Image head = Get<Image>(marker, "m_head");
            Image direction = Get<Image>(marker, "m_direction");
            marker.SetAppearance(Color.magenta, -90f);
            Assert.That(head.color, Is.EqualTo(Color.magenta));
            Assert.That(direction.color, Is.EqualTo(Color.magenta));
            Assert.That(Quaternion.Angle(head.transform.localRotation, Quaternion.identity), Is.LessThan(0.01f));
            Assert.That(Quaternion.Angle(direction.transform.localRotation, Quaternion.Euler(0, 0, -90)), Is.LessThan(0.01f));
            Assert.That(head.sprite, Is.Not.Null);
            Assert.That(direction.sprite, Is.Not.Null);
        }

        [TestCase(0f)]
        [TestCase(90f)]
        [TestCase(180f)]
        [TestCase(270f)]
        public void HouseFrontFollowsWorldRotationAndTransparentPixelsPreserveGrass(float yaw)
        {
            var map = Map();
            Set(map, "m_mapBounds", new Bounds(Vector3.zero, new Vector3(20, 1, 20)));
            var house = new GameObject("House");
            m_objects.Add(house);
            house.transform.rotation = Quaternion.Euler(0, yaw, 0);
            var icon = new Texture2D(3, 3);
            try
            {
                var source = new Color32[9];
                source[1] = new Color32(255, 0, 0, 255); // Bottom/front of sprite.
                var pixels = new Color32[101 * 101];
                System.Array.Fill(pixels, new Color32(0, 100, 0, 255));
                Call(map, "DrawHouseIcon", pixels, 101, Vector3.zero, house.transform,
                    new Vector2(10, 10), icon, source);
                Vector3 front = house.transform.forward * 3.6f;
                int x = Mathf.RoundToInt((front.x + 10) * 5);
                int y = Mathf.RoundToInt((front.z + 10) * 5);
                Assert.That(pixels[y * 101 + x].r, Is.EqualTo(255));
                Assert.That(pixels[50 * 101 + 50].g, Is.EqualTo(100));
            }
            finally { Object.DestroyImmediate(icon); }
        }

        [Test]
        public void PickupDropAndDespawnUseReplicatedCarryState()
        {
            var map = Map();
            var couch = Create<CouchCarryController>();
            SpawnForQuery(couch, 1);
            Assert.That(Call(map, "IsTransportedByLocalPlayer", couch), Is.False);
            var carried = Get<SyncVar<NetworkObject>>(Get<PlayerCouchCarrier>(map, "m_carrier"), "m_carriedCouch");
            carried.Value = couch.GetComponent<NetworkObject>();
            Assert.That(Call(map, "IsTransportedByLocalPlayer", couch), Is.True);
            carried.Value = null;
            Assert.That(Call(map, "IsTransportedByLocalPlayer", couch), Is.False);
            carried.Value = couch.GetComponent<NetworkObject>();
            typeof(NetworkObject).GetProperty("IsDeinitializing", Flags).SetValue(carried.Value, true);
            Assert.That(Call(map, "IsTransportedByLocalPlayer", couch), Is.False);
        }

        [TestCase("truck")]
        [TestCase("sled")]
        [TestCase("hoverboard")]
        public void AttachmentRequiresLocalOccupancyAndUpdatesOnUnload(string transport)
        {
            var map = Map();
            var other = Map();
            var couch = Create<CouchCarryController>();
            SpawnForQuery(couch, 1);
            SyncVar<NetworkObject> attachment;
            if (transport == "truck")
            {
                var truck = Create<TruckController>();
                SpawnForQuery(truck, 2);
                attachment = Get<SyncVar<NetworkObject>>(couch, "m_loadedTruck");
                attachment.Value = truck.GetComponent<NetworkObject>();
                Get<SyncVar<NetworkObject>>(Get<PlayerTruckInteractor>(map, "m_truckInteractor"), "m_currentTruckObject").Value = attachment.Value;
            }
            else if (transport == "sled")
            {
                var sled = Create<SledController>();
                attachment = Get<SyncVar<NetworkObject>>(couch, "m_mountedSled");
                attachment.Value = sled.GetComponent<NetworkObject>();
                Get<SyncVar<NetworkObject>>(Get<PlayerCouchCarrier>(map, "m_carrier"), "m_carriedCouch").Value = attachment.Value;
            }
            else
            {
                var board = Create<MagneticHoverboardController>();
                SpawnForQuery(board, 2);
                attachment = Get<SyncVar<NetworkObject>>(couch, "m_mountedHoverboard");
                attachment.Value = board.GetComponent<NetworkObject>();
                Get<SyncVar<NetworkObject>>(Get<PlayerHoverboardInteractor>(map, "m_hoverboardInteractor"), "m_board").Value = attachment.Value;
            }
            Assert.That(Call(map, "IsTransportedByLocalPlayer", couch), Is.True);
            Assert.That(Call(other, "IsTransportedByLocalPlayer", couch), Is.False);
            attachment.Value = null;
            Assert.That(Call(map, "IsTransportedByLocalPlayer", couch), Is.False);
        }

        [Test]
        public void PrimaryDefaultsToEarliestRetainsChoiceCyclesAndRecoversAfterRemoval()
        {
            var first = Map();
            var second = Map();
            foreach (var map in new[] { first, second })
            {
                Get<List<int>>(map, "m_navigationIds").AddRange(new[] { 1, 2, 3 });
                Call(map, "SelectPrimaryDestination", 2, false);
                Assert.That(Get<int>(map, "m_primaryDeliveryId"), Is.EqualTo(2));
            }
            Call(first, "SelectPrimaryDestination", 1, true);
            Assert.That(Get<int>(first, "m_primaryDeliveryId"), Is.EqualTo(3));
            Assert.That(Get<int>(second, "m_primaryDeliveryId"), Is.EqualTo(2));
            Call(first, "SelectPrimaryDestination", 1, false);
            Assert.That(Get<int>(first, "m_primaryDeliveryId"), Is.EqualTo(3));
            Get<List<int>>(first, "m_navigationIds").Remove(3);
            Call(first, "SelectPrimaryDestination", 1, false);
            Assert.That(Get<int>(first, "m_primaryDeliveryId"), Is.EqualTo(1));
            Get<List<int>>(first, "m_navigationIds").Clear();
            Call(first, "SelectPrimaryDestination", -1, false);
            Assert.That(Get<int>(first, "m_primaryDeliveryId"), Is.EqualTo(-1));
        }

        [Test]
        public void SavedMarkerIsWiredAndPrimaryUsesOnlyExistingNavigationIndicator()
        {
            var player = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Player.prefab");
            var template = Get<RectTransform>(player.GetComponent<NeighbourhoodMap>(), "m_deliveryMarkerPrefab");
            Assert.That(template, Is.Not.Null);
            Assert.That(template.GetComponentInChildren<Text>(true), Is.Not.Null);
            var marker = Object.Instantiate(template);
            m_objects.Add(marker.gameObject);
            var map = Map();
            Get<List<int>>(map, "m_navigationIds").AddRange(new[] { 10, 20 });
            var label = marker.GetComponentInChildren<Text>(true);
            Call(map, "StyleDeliveryMarker", marker, label, 20, true, false);
            Assert.That(label.text, Is.EqualTo("2"));
            Assert.That(marker.GetComponent<Image>().enabled, Is.True);
            float secondarySize = marker.sizeDelta.x;
            Call(map, "StyleDeliveryMarker", marker, label, 20, true, true);
            Assert.That(marker.sizeDelta.x, Is.GreaterThan(secondarySize));
            Assert.That(marker.GetComponent<Image>().enabled, Is.False);
        }

        [Test]
        public void CollinearDestinationsKeepDistinctNumbersInsideMinimap()
        {
            var map = Map();
            var positions = Get<List<Vector2>>(map, "m_destinationMarkerPositions");
            positions.Add(new Vector2(90f, 0f));
            var second = (Vector2)Call(map, "SeparateDestinationMarker", new Vector2(90f, 0f), 100f);
            var third = (Vector2)Call(map, "SeparateDestinationMarker", new Vector2(90f, 0f), 100f);
            Assert.That(second.magnitude, Is.LessThanOrEqualTo(100.01f));
            Assert.That(third.magnitude, Is.LessThanOrEqualTo(100.01f));
            Assert.That(Vector2.Distance(second, third), Is.GreaterThan(22f));
            Assert.That(Vector2.Distance(second, positions[0]), Is.GreaterThan(22f));
        }
    }
}
