using System;
using System.IO;
using System.Linq;
using System.Text;
using CouchGuys.Gameplay.Delivery;
using CouchGuys.ProceduralGeneration;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CouchGuys.Editor
{
    /// <summary>Checks house LODs, entrance clearance and deterministic delivery tier assignments.</summary>
    public static class EasyHouseValidation
    {
        private const string Request = "Library/EasyHouseValidation.request";
        private const string Report = "Library/EasyHouseValidation.txt";

        [InitializeOnLoadMethod]
        private static void RunRequested()
        {
            if (File.Exists(Request)) EditorApplication.delayCall += Run;
        }

        [MenuItem("Couch Guys/Validation/Validate Easy Delivery House")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (File.Exists(Request)) File.Delete(Request);
            StringBuilder results = new StringBuilder();
            try
            {
                HousePrefabBuilder.EnsureHousePrefabs();
                GameObject mansion = AssetDatabase.LoadAssetAtPath<GameObject>(HousePrefabBuilder.House03PrefabPath);
                Require(mansion != null, "Mansion prefab");
                Require(mansion.GetComponent<GeneratedProperty>().HasFrontDoorAnchor, "Mansion front-door anchor");
                Require(mansion.GetComponent<HouseExteriorModuleSet>().IsConfigured, "Mansion driveway modules");
                Require(AssetDatabase.GetDependencies(HousePrefabBuilder.House03PrefabPath)
                    .Contains(HousePrefabBuilder.House03ModelPath), "Mansion source dependency");
                Require(mansion.GetComponentsInChildren<BoxCollider>(true).Length > 0, "Mansion structural colliders");
                Require(mansion.GetComponentsInChildren<Renderer>(true).All(r => r.sharedMaterials.All(m => m != null)),
                    "Mansion has no missing materials");
                Require(mansion.GetComponentsInChildren<Renderer>(true).Any(r => r.sharedMaterials.Any(m =>
                    AssetDatabase.GetAssetPath(m) == HousePrefabBuilder.HouseModelPath)), "Mansion shares Medium palette");
                LOD[] mansionLods = mansion.GetComponentInChildren<LODGroup>(true).GetLODs();
                Require(mansionLods.Length == 4 && mansionLods.All(l => l.renderers.Length > 0), "Mansion four LODs");
                long mansionPrevious = long.MaxValue;
                for (int level = 0; level < mansionLods.Length; level++)
                {
                    LOD lod = mansionLods[level];
                    long triangles = lod.renderers.Sum(r => (long)r.GetComponent<MeshFilter>().sharedMesh.triangles.Length / 3);
                    Require(triangles <= mansionPrevious, "Mansion LOD triangle reduction");
                    mansionPrevious = triangles;
                    results.AppendLine($"Mansion LOD{level}: {triangles} triangles, {lod.renderers.Length} renderers");
                }
                GameObject waitingNpc = Resources.Load<GameObject>("DeliveryWaitingNPC");
                Require(waitingNpc != null && waitingNpc.GetComponentInChildren<Animator>() != null &&
                    waitingNpc.GetComponentInChildren<Animator>().runtimeAnimatorController != null &&
                    waitingNpc.GetComponentsInChildren<Collider>(true).Length == 0 &&
                    waitingNpc.GetComponentsInChildren<MonoBehaviour>(true).Length == 0,
                    "Waiting NPC reuses idle animation without gameplay components");
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HousePrefabBuilder.House02PrefabPath);
                Require(prefab != null && prefab.GetComponent<GeneratedProperty>().IsEasyDeliveryHouse,
                    "House 02 prefab and delivery identity");
                Require(AssetDatabase.GetDependencies(HousePrefabBuilder.House02PrefabPath)
                    .Contains(HousePrefabBuilder.House02ModelPath), "House 02 source dependency");
                Require(prefab.GetComponent<HouseExteriorModuleSet>().IsConfigured, "Exterior modules");
                LOD[] lods = prefab.GetComponentInChildren<LODGroup>(true).GetLODs();
                Require(lods.Length == 4, "Four LODs");
                long previous = long.MaxValue;
                for (int i = 0; i < lods.Length; i++)
                {
                    long triangles = lods[i].renderers.Sum(r => TriangleCount(r.GetComponent<MeshFilter>().sharedMesh));
                    Require(triangles > 0 && triangles < previous, $"LOD{i} genuinely reduces geometry");
                    results.AppendLine($"LOD{i}: {triangles} triangles, {lods[i].renderers.Length} renderers");
                    previous = triangles;
                }
                Require(prefab.GetComponentsInChildren<Collider>(true).All(c => c is BoxCollider), "Box colliders only");
                RenderLods(prefab);

                Scene scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/SampleScene.unity");
                try
                {
                    NeighbourhoodGenerator generator = scene.GetRootGameObjects()
                        .SelectMany(g => g.GetComponentsInChildren<NeighbourhoodGenerator>(true)).First();
                    RegionDefinition region = AssetDatabase.LoadAssetAtPath<RegionDefinition>(
                        "Assets/_Project/Settings/Regions/Region_Suburbs.asset");
                    DeliveryTierConfig[] tiers = new[] { "Easy", "Medium", "Hard" }.Select(t =>
                        AssetDatabase.LoadAssetAtPath<DeliveryTierConfig>($"Assets/_Project/Settings/Delivery/{t}Delivery.asset")).ToArray();
                    string firstSignature = null;
                    foreach (int seed in new[] { 10000, 10001, 12345, 10000 })
                    {
                        generator.Generate(region, seed);
                        Require(generator.HasGeneratedNeighbourhood && generator.IsDeliveryDifficultyReady,
                            $"Seed {seed} generates and analyses routes");
                        int[] counts = new int[3];
                        foreach (DeliveryDestination destination in generator.DeliveryDestinations)
                        {
                            bool easyHouse = destination.GetComponent<GeneratedProperty>().IsEasyDeliveryHouse;
                            Require(destination.GetComponent<GeneratedProperty>().HasFrontDoorAnchor, "Generated front-door anchor");
                            for (int t = 0; t < tiers.Length; t++)
                            {
                                if (!tiers[t].IsDestinationEligible(destination)) continue;
                                Require(easyHouse == (t == 0), "Tier exclusivity");
                                Require((destination.DestinationType == DeliveryDestinationType.Mansion) == (t == 2), "Hard exclusively uses mansion");
                                counts[t]++;
                                // The collection point must be outside structural collisions with
                                // room for a couch to approach at normal carry height.
                                Bounds couchSpace = new Bounds(destination.DropPosition.position + Vector3.up,
                                    new Vector3(1.4f, .8f, .7f));
                                foreach (Collider collider in destination.GetComponentsInChildren<Collider>())
                                    if (collider.name.StartsWith("Structure_"))
                                        Require(!IntersectsCurrentBox((BoxCollider)collider, couchSpace),
                                            $"Couch drop clearance seed={seed} tier={t} easyHouse={easyHouse} drop={destination.DropPosition.position} collider={collider.name} bounds={collider.bounds}");
                            }
                        }
                        Require(counts.All(n => n > 0), $"Seed {seed} has candidates for every tier");
                        results.AppendLine($"Seed {seed}: Easy={counts[0]}, Medium={counts[1]}, Hard={counts[2]}");
                        string signature = string.Join(";", generator.DeliveryDestinations.Select(d =>
                            $"{d.transform.position:F3}/{d.DropPosition.position:F3}/{d.GetComponent<GeneratedProperty>().IsEasyDeliveryHouse}"));
                        if (seed == 10000)
                        {
                            if (firstSignature != null) Require(firstSignature == signature, "Deterministic destination order and placement");
                            firstSignature = signature;
                        }
                    }
                }
                finally { EditorSceneManager.ClosePreviewScene(scene); }
                results.AppendLine("PASS: prefab, four LODs, tier exclusivity, generation, couch clearance and determinism.");
                results.AppendLine("Network host/client delivery completion requires a multiplayer Play Mode session.");
                Debug.Log(results.ToString());
            }
            catch (Exception exception)
            {
                results.AppendLine("FAIL: " + exception);
                Debug.LogException(exception);
            }
            File.WriteAllText(Report, results.ToString());
        }

        private static long TriangleCount(Mesh mesh)
        {
            long count = 0;
            for (int s = 0; s < mesh.subMeshCount; s++)
                count += mesh.lodCount > 0 ? mesh.GetLod(s, 0).indexCount / 3 : mesh.GetIndexCount(s) / 3;
            return count;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static bool IntersectsCurrentBox(BoxCollider collider, Bounds couch)
        {
            // Preview-scene physics bounds can lag behind procedural rescaling.
            // Transform the probe into the authored collider's current local space.
            Bounds local = new Bounds(collider.transform.InverseTransformPoint(couch.center), Vector3.zero);
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = couch.center + Vector3.Scale(couch.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                local.Encapsulate(collider.transform.InverseTransformPoint(corner));
            }
            return new Bounds(collider.center, collider.size).Intersects(local);
        }

        private static void RenderLods(GameObject prefab)
        {
            Scene scene = EditorSceneManager.NewPreviewScene();
            RenderTexture target = RenderTexture.GetTemporary(800, 600, 24);
            RenderTexture previous = RenderTexture.active;
            Texture2D pixels = new Texture2D(800, 600, TextureFormat.RGB24, false);
            try
            {
                GameObject house = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                LODGroup group = house.GetComponentInChildren<LODGroup>();
                GameObject cameraObject = new GameObject("House02 Preview", typeof(Camera));
                SceneManager.MoveGameObjectToScene(cameraObject, scene);
                Camera camera = cameraObject.GetComponent<Camera>();
                camera.scene = scene;
                camera.transform.position = new Vector3(24, 16, 31);
                camera.transform.LookAt(new Vector3(0, 3, 0));
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.4f,.65f,.8f);
                camera.targetTexture = target;
                GameObject sun = new GameObject("House02 Sun", typeof(Light));
                SceneManager.MoveGameObjectToScene(sun, scene);
                sun.GetComponent<Light>().type = LightType.Directional;
                sun.GetComponent<Light>().intensity = 1.5f;
                sun.transform.rotation = Quaternion.Euler(40,-30,0);
                for (int i = 0; i < 4; i++)
                {
                    group.ForceLOD(i);
                    camera.Render();
                    RenderTexture.active = target;
                    pixels.ReadPixels(new Rect(0,0,800,600),0,0);
                    pixels.Apply();
                    File.WriteAllBytes($"Library/House02_LOD{i}.png", pixels.EncodeToPNG());
                }
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.DestroyImmediate(pixels);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
