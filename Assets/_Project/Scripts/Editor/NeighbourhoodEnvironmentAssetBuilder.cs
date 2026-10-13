using System;
using System.Collections.Generic;
using CouchGuys.ProceduralGeneration;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace CouchGuys.Editor
{
    /// <summary>Builds the lightweight cube-only environment library and wires it into Suburbs.</summary>
    public static class NeighbourhoodEnvironmentAssetBuilder
    {
        private const string PrefabFolder = "Assets/_Project/Prefabs/Environment/Placeholders";
        private const string MaterialFolder = "Assets/_Project/Materials/Environment";
        private const string SettingsFolder = "Assets/_Project/Settings/Environment";
        private const string RegionPath = "Assets/_Project/Settings/Regions/Region_Suburbs.asset";
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";

        private static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();

        [InitializeOnLoadMethod]
        private static void BuildMissingAssetsAfterImport()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode ||
                    AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabFolder}/StreetLight_Placeholder.prefab") != null)
                    return;
                BuildAll();
            };
        }

        [MenuItem("Couch Guys/World/Create or Refresh Neighbourhood Environment")]
        public static void BuildAll()
        {
            EnsureFolder(PrefabFolder);
            EnsureFolder(MaterialFolder);
            EnsureFolder(SettingsFolder);
            CreateMaterials();
            EnvironmentPrefabLibrary library = CreatePrefabLibrary();
            ConfigureSuburbs(library);
            ConfigureGameplayScene();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Created and wired the Couch Guys cube-placeholder environment library.");
        }

        public static void BuildFromCommandLine() => BuildAll();

        private static void CreateMaterials()
        {
            Shader shader = Shader.Find(ToonMaterialWorkflow.ShaderName) ?? Shader.Find("Universal Render Pipeline/Lit");
            CreateMaterial("GrassBright", new Color(0.24f, 0.68f, 0.24f), shader);
            CreateMaterial("GrassDark", new Color(0.12f, 0.45f, 0.16f), shader);
            CreateMaterial("Wood", new Color(0.42f, 0.22f, 0.09f), shader);
            CreateMaterial("MetalDark", new Color(0.12f, 0.15f, 0.2f), shader);
            CreateMaterial("Concrete", new Color(0.66f, 0.69f, 0.72f), shader);
            CreateMaterial("Path", new Color(0.78f, 0.66f, 0.49f), shader);
            CreateMaterial("Water", new Color(0.16f, 0.62f, 0.95f), shader, new Color(0.03f, 0.15f, 0.32f));
            CreateMaterial("WarmGlow", new Color(1f, 0.63f, 0.18f), shader, new Color(1.8f, 0.72f, 0.16f));
            CreateMaterial("Red", new Color(0.86f, 0.16f, 0.13f), shader);
            CreateMaterial("Yellow", new Color(0.98f, 0.72f, 0.12f), shader);
            CreateMaterial("Blue", new Color(0.15f, 0.47f, 0.88f), shader);
            CreateMaterial("Purple", new Color(0.64f, 0.26f, 0.82f), shader);
            CreateMaterial("Pink", new Color(1f, 0.32f, 0.62f), shader);
            CreateMaterial("White", new Color(0.95f, 0.96f, 0.91f), shader);
        }

        private static void CreateMaterial(string name, Color colour, Shader shader, Color? emission = null)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", colour);
            else material.color = colour;
            if (material.HasProperty("_Saturation")) material.SetFloat("_Saturation", 1.12f);
            if (material.HasProperty("_ShadowStrength")) material.SetFloat("_ShadowStrength", 0.55f);
            if (material.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor", emission ?? Color.black);
            EditorUtility.SetDirty(material);
            Materials[name] = material;
        }

        private static EnvironmentPrefabLibrary CreatePrefabLibrary()
        {
            GameObject streetlight = CreatePrefab("StreetLight_Placeholder", new Vector2(1.1f, 1.1f), 0.35f, root =>
            {
                Cube(root, "Pole", new Vector3(0f, 3.1f, 0f), new Vector3(0.28f, 6.2f, 0.28f), "MetalDark");
                Cube(root, "Arm", new Vector3(0f, 5.95f, 0.72f), new Vector3(0.25f, 0.25f, 1.65f), "MetalDark");
                Cube(root, "LampHousing", new Vector3(0f, 5.78f, 1.48f), new Vector3(0.7f, 0.35f, 0.75f), "MetalDark");
                GameObject bulb = Cube(root, "Bulb", new Vector3(0f, 5.55f, 1.48f), new Vector3(0.48f, 0.16f, 0.5f), "WarmGlow");
                Light light = bulb.AddComponent<Light>();
                light.type = LightType.Point;
                light.enabled = false;
                light.color = new Color(1f, 0.68f, 0.28f);
                light.intensity = 2.2f;
                light.range = 11f;
                root.AddComponent<StreetlightPlaceholder>();
            });
            GameObject bin = CreatePrefab("Bin_Placeholder", new Vector2(0.75f, 0.75f), 0.2f, root =>
            {
                Cube(root, "Body", new Vector3(0f, 0.55f, 0f), new Vector3(0.68f, 1.1f, 0.62f), "GrassDark");
                Cube(root, "Lid", new Vector3(0f, 1.13f, -0.02f), new Vector3(0.76f, 0.12f, 0.68f), "Yellow");
                Cube(root, "Handle", new Vector3(0f, 1.24f, 0.16f), new Vector3(0.35f, 0.08f, 0.1f), "MetalDark");
            });
            GameObject publicBin = CreatePrefab("PublicBin_Placeholder", new Vector2(0.9f, 0.9f), 0.25f, root =>
            {
                Cube(root, "Body", new Vector3(0f, 0.6f, 0f), new Vector3(0.78f, 1.2f, 0.72f), "Blue");
                Cube(root, "Top", new Vector3(0f, 1.24f, 0f), new Vector3(0.88f, 0.16f, 0.82f), "MetalDark");
            });
            GameObject mailbox = CreatePrefab("Mailbox_Placeholder", new Vector2(0.8f, 0.65f), 0.25f, root =>
            {
                Cube(root, "Post", new Vector3(0f, 0.75f, 0f), new Vector3(0.18f, 1.5f, 0.18f), "Wood");
                Cube(root, "Box", new Vector3(0f, 1.48f, 0.08f), new Vector3(0.65f, 0.48f, 0.85f), "White");
                Cube(root, "Flag", new Vector3(0.39f, 1.6f, 0f), new Vector3(0.08f, 0.5f, 0.08f), "Red");
            });
            GameObject flowers = CreatePrefab("FlowerCluster_Placeholder", new Vector2(1.6f, 1.1f), 0.2f, root =>
            {
                Cube(root, "Bed", new Vector3(0f, 0.12f, 0f), new Vector3(1.6f, 0.24f, 1.05f), "Wood");
                for (int i = 0; i < 7; i++)
                {
                    float x = -0.62f + (i % 4) * 0.4f;
                    float z = -0.25f + (i / 4) * 0.5f;
                    Cube(root, $"Stem_{i}", new Vector3(x, 0.38f, z), new Vector3(0.09f, 0.38f, 0.09f), "GrassDark");
                    Cube(root, $"Flower_{i}", new Vector3(x, 0.62f, z), Vector3.one * 0.22f, i % 3 == 0 ? "Yellow" : i % 3 == 1 ? "Pink" : "Purple");
                }
            });
            GameObject bush = CreatePrefab("Bush_Placeholder", new Vector2(1.7f, 1.5f), 0.25f, root =>
            {
                Cube(root, "BushLeft", new Vector3(-0.42f, 0.55f, 0f), new Vector3(1f, 1.05f, 1.15f), "GrassDark");
                Cube(root, "BushRight", new Vector3(0.4f, 0.62f, 0.08f), new Vector3(1.05f, 1.2f, 1.25f), "GrassBright");
            });
            GameObject tree = CreatePrefab("Tree_Placeholder", new Vector2(2.2f, 2.2f), 0.45f, root =>
            {
                Cube(root, "Trunk", new Vector3(0f, 1.4f, 0f), new Vector3(0.6f, 2.8f, 0.6f), "Wood");
                Cube(root, "CanopyLower", new Vector3(0f, 3.35f, 0f), new Vector3(2.6f, 1.7f, 2.5f), "GrassDark");
                Cube(root, "CanopyUpper", new Vector3(0.15f, 4.55f, 0f), new Vector3(2.05f, 1.45f, 2f), "GrassBright");
            });
            GameObject bench = CreatePrefab("Bench_Placeholder", new Vector2(2.4f, 0.95f), 0.35f, root =>
            {
                Cube(root, "Seat", new Vector3(0f, 0.55f, 0f), new Vector3(2.35f, 0.22f, 0.72f), "Wood");
                Cube(root, "Back", new Vector3(0f, 1.02f, 0.3f), new Vector3(2.35f, 0.7f, 0.18f), "Wood");
                Cube(root, "LegL", new Vector3(-0.82f, 0.27f, 0f), new Vector3(0.2f, 0.55f, 0.55f), "MetalDark");
                Cube(root, "LegR", new Vector3(0.82f, 0.27f, 0f), new Vector3(0.2f, 0.55f, 0.55f), "MetalDark");
            });
            GameObject fountain = CreatePrefab("Fountain_Placeholder", new Vector2(5.5f, 5.5f), 1f, root =>
            {
                Cube(root, "Basin", new Vector3(0f, 0.35f, 0f), new Vector3(5.5f, 0.7f, 5.5f), "Concrete");
                Cube(root, "Water", new Vector3(0f, 0.73f, 0f), new Vector3(4.7f, 0.12f, 4.7f), "Water");
                Cube(root, "Centre", new Vector3(0f, 1.25f, 0f), new Vector3(1.15f, 1.7f, 1.15f), "Concrete");
                Cube(root, "Jet", new Vector3(0f, 2.45f, 0f), new Vector3(0.22f, 1.1f, 0.22f), "Water");
            });
            GameObject statue = CreatePrefab("Statue_Placeholder", new Vector2(3.2f, 3.2f), 0.8f, root =>
            {
                Cube(root, "Pedestal", new Vector3(0f, 0.65f, 0f), new Vector3(2.7f, 1.3f, 2.7f), "Concrete");
                Cube(root, "Statue", new Vector3(0f, 2.65f, 0f), new Vector3(1.15f, 2.8f, 1.15f), "Blue");
                Cube(root, "Head", new Vector3(0f, 4.35f, 0f), Vector3.one * 0.85f, "Blue");
            });
            GameObject playground = CreatePrefab("Playground_Placeholder", new Vector2(8f, 7f), 1f, root =>
            {
                Cube(root, "Mat", new Vector3(0f, 0.08f, 0f), new Vector3(8f, 0.16f, 7f), "Path");
                Cube(root, "Tower", new Vector3(-1.8f, 1.5f, 0.5f), new Vector3(2.2f, 3f, 2.2f), "Yellow");
                Cube(root, "Slide", new Vector3(0f, 0.9f, -0.8f), new Vector3(1.1f, 0.25f, 4.3f), "Blue");
                Cube(root, "SwingBar", new Vector3(2.6f, 2.1f, 0.7f), new Vector3(3.2f, 0.25f, 0.25f), "Red");
                Cube(root, "SwingPostL", new Vector3(1.25f, 1.05f, 0.7f), new Vector3(0.25f, 2.1f, 0.25f), "Red");
                Cube(root, "SwingPostR", new Vector3(3.95f, 1.05f, 0.7f), new Vector3(0.25f, 2.1f, 0.25f), "Red");
            });
            GameObject gazebo = CreatePrefab("Gazebo_Placeholder", new Vector2(6f, 6f), 0.8f, root =>
            {
                Cube(root, "Floor", new Vector3(0f, 0.18f, 0f), new Vector3(5.8f, 0.36f, 5.8f), "Wood");
                for (int i = 0; i < 4; i++)
                {
                    float x = i % 2 == 0 ? -2.25f : 2.25f;
                    float z = i < 2 ? -2.25f : 2.25f;
                    Cube(root, $"Post_{i}", new Vector3(x, 1.8f, z), new Vector3(0.3f, 3.3f, 0.3f), "Wood");
                }
                Cube(root, "Roof", new Vector3(0f, 3.65f, 0f), new Vector3(6.4f, 0.55f, 6.4f), "Red");
            });
            GameObject busStop = CreatePrefab("BusStop_Placeholder", new Vector2(4f, 1.8f), 0.6f, root =>
            {
                Cube(root, "Back", new Vector3(0f, 1.45f, 0.72f), new Vector3(4f, 2.9f, 0.18f), "Blue");
                Cube(root, "Roof", new Vector3(0f, 3f, 0f), new Vector3(4.3f, 0.25f, 1.8f), "Yellow");
                Cube(root, "Seat", new Vector3(0f, 0.6f, 0.18f), new Vector3(2.8f, 0.24f, 0.65f), "Wood");
            });
            GameObject parkSign = SignPrefab("ParkSign_Placeholder", "GrassDark", new Vector2(2.3f, 0.65f));
            GameObject noticeBoard = SignPrefab("NoticeBoard_Placeholder", "Yellow", new Vector2(2.8f, 0.9f));
            GameObject picnic = CreatePrefab("PicnicArea_Placeholder", new Vector2(4.2f, 3.2f), 0.65f, root =>
            {
                Cube(root, "Table", new Vector3(0f, 0.85f, 0f), new Vector3(3.2f, 0.25f, 1.35f), "Wood");
                Cube(root, "BenchL", new Vector3(-1.85f, 0.5f, 0f), new Vector3(0.55f, 0.2f, 3.2f), "Wood");
                Cube(root, "BenchR", new Vector3(1.85f, 0.5f, 0f), new Vector3(0.55f, 0.2f, 3.2f), "Wood");
            });
            GameObject plaza = CreatePrefab("Plaza_Placeholder", new Vector2(8f, 8f), 0.8f, root =>
            {
                Cube(root, "Paving", new Vector3(0f, 0.08f, 0f), new Vector3(8f, 0.16f, 8f), "Concrete");
                Cube(root, "PlanterL", new Vector3(-2.8f, 0.35f, 0f), new Vector3(1.2f, 0.7f, 4.8f), "Wood");
                Cube(root, "PlanterR", new Vector3(2.8f, 0.35f, 0f), new Vector3(1.2f, 0.7f, 4.8f), "Wood");
            });
            GameObject hydrant = CreatePrefab("FireHydrant_Placeholder", new Vector2(0.8f, 0.8f), 0.25f, root =>
            {
                Cube(root, "Body", new Vector3(0f, 0.55f, 0f), new Vector3(0.55f, 1.1f, 0.55f), "Red");
                Cube(root, "Cap", new Vector3(0f, 1.16f, 0f), new Vector3(0.72f, 0.22f, 0.72f), "Yellow");
                Cube(root, "Side", new Vector3(0.42f, 0.7f, 0f), new Vector3(0.4f, 0.35f, 0.35f), "Red");
            });
            GameObject streetSign = SignPrefab("StreetSign_Placeholder", "Blue", new Vector2(1.5f, 0.6f));
            GameObject utility = CreatePrefab("UtilityBox_Placeholder", new Vector2(1.3f, 0.9f), 0.3f, root =>
                Cube(root, "Box", new Vector3(0f, 0.65f, 0f), new Vector3(1.25f, 1.3f, 0.85f), "GrassDark"));
            GameObject planting = CreatePrefab("PlantingBed_Placeholder", new Vector2(3f, 1.4f), 0.25f, root =>
            {
                Cube(root, "Bed", new Vector3(0f, 0.16f, 0f), new Vector3(3f, 0.32f, 1.4f), "Wood");
                Cube(root, "Plants", new Vector3(0f, 0.48f, 0f), new Vector3(2.5f, 0.55f, 0.95f), "GrassBright");
            });
            GameObject path = CreatePrefab("ParkPath_Placeholder", new Vector2(2.2f, 1f), 0f, root =>
                Cube(root, "Path", new Vector3(0f, 0.035f, 0f), new Vector3(2.2f, 0.07f, 1f), "Path"), false);
            GameObject boundary = CreatePrefab("ParkBoundary_Placeholder", new Vector2(0.35f, 2f), 0f, root =>
                Cube(root, "Boundary", new Vector3(0f, 0.28f, 0f), new Vector3(0.35f, 0.56f, 2f), "Wood"), false);
            GameObject smallPark = ParkRoot("SmallPark_Placeholder", new Vector2(20f, 20f));
            GameObject mediumPark = ParkRoot("MediumPark_Placeholder", new Vector2(40f, 40f));
            GameObject largePark = ParkRoot("LargePark_Placeholder", new Vector2(60f, 60f));

            return new EnvironmentPrefabLibrary
            {
                SmallPark = smallPark, MediumPark = mediumPark, LargePark = largePark,
                Tree = tree, Bench = bench, Bin = bin, PublicBin = publicBin, Mailbox = mailbox,
                FlowerCluster = flowers, Bush = bush, Fountain = fountain, Statue = statue,
                Playground = playground, Gazebo = gazebo, BusStop = busStop, ParkSign = parkSign,
                ParkPath = path, ParkBoundary = boundary, NoticeBoard = noticeBoard, PicnicArea = picnic,
                Plaza = plaza, FireHydrant = hydrant, StreetSign = streetSign, UtilityBox = utility,
                PlantingBed = planting
            };
        }

        private static GameObject SignPrefab(string name, string panelMaterial, Vector2 footprint) =>
            CreatePrefab(name, footprint, 0.25f, root =>
            {
                Cube(root, "PostL", new Vector3(-footprint.x * 0.32f, 0.85f, 0f), new Vector3(0.16f, 1.7f, 0.16f), "Wood");
                Cube(root, "PostR", new Vector3(footprint.x * 0.32f, 0.85f, 0f), new Vector3(0.16f, 1.7f, 0.16f), "Wood");
                Cube(root, "Panel", new Vector3(0f, 1.65f, 0f), new Vector3(footprint.x, 0.9f, 0.2f), panelMaterial);
            });

        private static GameObject ParkRoot(string name, Vector2 footprint) =>
            CreatePrefab(name, footprint, 0f, root =>
                Cube(root, "ReservedParkLawn", new Vector3(0f, -0.035f, 0f),
                    new Vector3(footprint.x - 1f, 0.07f, footprint.y - 1f), "GrassBright"), false);

        private static GameObject CreatePrefab(
            string name,
            Vector2 footprint,
            float clearance,
            Action<GameObject> build,
            bool blocksPlacement = true)
        {
            GameObject root = new GameObject(name);
            EnvironmentPlacementMetadata metadata = root.AddComponent<EnvironmentPlacementMetadata>();
            metadata.Configure(footprint, clearance, true, blocksPlacement);
            build(root);
            string path = $"{PrefabFolder}/{name}.prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);
            return prefab;
        }

        private static GameObject Cube(
            GameObject root,
            string name,
            Vector3 localPosition,
            Vector3 localScale,
            string material)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(root.transform, false);
            cube.transform.localPosition = localPosition;
            cube.transform.localScale = localScale;
            UnityEngine.Object.DestroyImmediate(cube.GetComponent<Collider>());
            cube.GetComponent<Renderer>().sharedMaterial = Materials[material];
            return cube;
        }

        private static void ConfigureSuburbs(EnvironmentPrefabLibrary library)
        {
            RegionDefinition region = AssetDatabase.LoadAssetAtPath<RegionDefinition>(RegionPath);
            if (region == null) throw new InvalidOperationException($"Missing region asset at {RegionPath}.");
            RegionEnvironmentSettings settings = RegionEnvironmentSettings.SuburbsDefault;
            settings.Prefabs = library;
            settings.Lighting.SkyboxMaterial = CreateOrUpdateSkybox();
            settings.Lighting.PostProcessingProfile = CreateOrUpdatePostProfile();
            settings.Streetlights.Prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabFolder}/StreetLight_Placeholder.prefab");
            settings.Landmarks.Prefabs = new[]
            {
                new WeightedEnvironmentPrefab { Prefab = library.Fountain, Weight = 1.2f, AllowedInParks = true },
                new WeightedEnvironmentPrefab { Prefab = library.Statue, Weight = 0.8f, AllowedInParks = true },
                new WeightedEnvironmentPrefab { Prefab = library.Gazebo, Weight = 0.75f, AllowedInParks = true },
                new WeightedEnvironmentPrefab { Prefab = library.Playground, Weight = 0.9f, AllowedInParks = true },
                new WeightedEnvironmentPrefab { Prefab = library.BusStop, Weight = 0.7f, AllowedRoadside = true },
                new WeightedEnvironmentPrefab { Prefab = library.NoticeBoard, Weight = 0.5f, AllowedRoadside = true },
                new WeightedEnvironmentPrefab { Prefab = library.Plaza, Weight = 0.35f, AllowedInParks = true }
            };
            region.ConfigureEnvironment(settings);
            EditorUtility.SetDirty(region);
        }

        private static void ConfigureGameplayScene()
        {
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            if (scene.IsValid() && scene.isLoaded && scene.isDirty)
            {
                Debug.LogWarning("Gameplay scene has unsaved user changes. Atmosphere will be applied by the generator at runtime; scene authoring was skipped to preserve those changes.");
                return;
            }
            bool openedTemporarily = !scene.IsValid() || !scene.isLoaded;
            if (openedTemporarily) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                Light sun = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (Light light in root.GetComponentsInChildren<Light>(true))
                        if (light.type == LightType.Directional) { sun = light; break; }
                    if (sun != null) break;
                }
                if (sun == null)
                {
                    GameObject sunObject = new GameObject("Cartoon Sun", typeof(Light));
                    SceneManager.MoveGameObjectToScene(sunObject, scene);
                    sun = sunObject.GetComponent<Light>();
                }
                CartoonLightingSettings lighting = CartoonLightingSettings.Default;
                sun.type = LightType.Directional;
                sun.transform.rotation = Quaternion.Euler(lighting.SunEulerAngles);
                sun.color = lighting.SunColour;
                sun.intensity = lighting.SunIntensity;
                sun.shadows = LightShadows.Soft;
                sun.shadowStrength = 0.72f;
                RenderSettings.sun = sun;
                RenderSettings.ambientMode = AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = lighting.AmbientSkyColour;
                RenderSettings.ambientEquatorColor = lighting.AmbientEquatorColour;
                RenderSettings.ambientGroundColor = lighting.AmbientGroundColour;
                RenderSettings.ambientIntensity = lighting.AmbientIntensity;
                RenderSettings.fog = lighting.FogEnabled;
                RenderSettings.fogMode = FogMode.Linear;
                RenderSettings.fogColor = lighting.FogColour;
                RenderSettings.fogStartDistance = lighting.FogStartDistance;
                RenderSettings.fogEndDistance = lighting.FogEndDistance;
                RenderSettings.skybox = CreateOrUpdateSkybox();
                ConfigurePostProcessing(scene);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            finally
            {
                if (openedTemporarily) EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static Material CreateOrUpdateSkybox()
        {
            string path = $"{MaterialFolder}/CartoonProceduralSky.mat";
            Material sky = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find("Skybox/Procedural");
            if (sky == null)
            {
                sky = new Material(shader) { name = "Cartoon Procedural Sky" };
                AssetDatabase.CreateAsset(sky, path);
            }
            sky.shader = shader;
            sky.SetFloat("_SunSize", 0.045f);
            sky.SetFloat("_SunSizeConvergence", 7f);
            sky.SetFloat("_AtmosphereThickness", 0.78f);
            sky.SetColor("_SkyTint", new Color(0.34f, 0.67f, 1f));
            sky.SetColor("_GroundColor", new Color(0.48f, 0.60f, 0.64f));
            sky.SetFloat("_Exposure", 1.12f);
            RenderSettings.skybox = sky;
            EditorUtility.SetDirty(sky);
            return sky;
        }

        private static VolumeProfile CreateOrUpdatePostProfile()
        {
            string profilePath = $"{SettingsFolder}/NeighbourhoodVolumeProfile.asset";
            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, profilePath);
            }
            if (!profile.TryGet(out ColorAdjustments colour)) colour = profile.Add<ColorAdjustments>(true);
            colour.postExposure.Override(0.08f);
            colour.contrast.Override(7f);
            colour.saturation.Override(8f);
            if (!profile.TryGet(out Bloom bloom)) bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(1.25f);
            bloom.intensity.Override(0.12f);
            bloom.scatter.Override(0.55f);
            if (!profile.TryGet(out Tonemapping tonemapping)) tonemapping = profile.Add<Tonemapping>(true);
            tonemapping.mode.Override(TonemappingMode.Neutral);
            EditorUtility.SetDirty(profile);
            return profile;
        }

        private static void ConfigurePostProcessing(Scene scene)
        {
            VolumeProfile profile = CreateOrUpdatePostProfile();

            Volume volume = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                Volume candidate = root.GetComponentInChildren<Volume>(true);
                if (candidate != null && candidate.name == "Neighbourhood Post Processing") { volume = candidate; break; }
            }
            if (volume == null)
            {
                GameObject volumeObject = new GameObject("Neighbourhood Post Processing", typeof(Volume));
                SceneManager.MoveGameObjectToScene(volumeObject, scene);
                volume = volumeObject.GetComponent<Volume>();
            }
            volume.isGlobal = true;
            volume.priority = 10f;
            volume.sharedProfile = profile;
        }

        private static void EnsureFolder(string path)
        {
            string[] segments = path.Split('/');
            string current = segments[0];
            for (int index = 1; index < segments.Length; index++)
            {
                string next = $"{current}/{segments[index]}";
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, segments[index]);
                current = next;
            }
        }
    }
}
