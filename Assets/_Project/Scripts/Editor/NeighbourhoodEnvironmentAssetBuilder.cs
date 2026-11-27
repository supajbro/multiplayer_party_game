using System;
using CouchGuys.ProceduralGeneration;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace CouchGuys.Editor
{
    /// <summary>Configures the saved environment prefabs and atmosphere for Suburbs.</summary>
    public static class NeighbourhoodEnvironmentAssetBuilder
    {
        private const string TreePrefabFolder = "Assets/_Project/Prefabs/Environment/Trees";
        private const string MaterialFolder = "Assets/_Project/Materials/Environment";
        private const string SettingsFolder = "Assets/_Project/Settings/Environment";
        private const string RegionPath = "Assets/_Project/Settings/Regions/Region_Suburbs.asset";
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";

        [MenuItem("Couch Guys/World/Create or Refresh Neighbourhood Environment")]
        public static void BuildAll()
        {
            EnsureFolder(MaterialFolder);
            EnsureFolder(SettingsFolder);
            CreateMaterials();
            StreetLightPrefabBuilder.EnsurePrefab();
            ConfigureSuburbs();
            ConfigureGameplayScene();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Configured the Couch Guys neighbourhood environment.");
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
        }

        private static void ConfigureSuburbs()
        {
            RegionDefinition region = AssetDatabase.LoadAssetAtPath<RegionDefinition>(RegionPath);
            if (region == null) throw new InvalidOperationException($"Missing region asset at {RegionPath}.");
            RegionEnvironmentSettings settings = RegionEnvironmentSettings.SuburbsDefault;
            settings.Parks.MinimumParks = 2;
            settings.DrivewayMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                $"{MaterialFolder}/Driveway_Pebbles.mat");
            EnvironmentPrefabLibrary library = region.Environment.Prefabs;
            GameObject fountain = AssetDatabase.LoadAssetAtPath<GameObject>(WaterFountainAuthoring.PrefabPath);
            if (fountain == null)
                throw new InvalidOperationException("Create the saved water fountain prefab using Tools > Couch Guys > Water Fountain first.");
            GameObject jumpingPillow = AssetDatabase.LoadAssetAtPath<GameObject>(JumpingPillowAuthoring.PrefabPath);
            if (jumpingPillow == null)
                throw new InvalidOperationException("Create the saved jumping pillow prefab first.");

            library.SmallPark = jumpingPillow;
            library.MediumPark = jumpingPillow;
            library.LargePark = jumpingPillow;
            library.Fountain = fountain;
            library.TreePrefabs = new[]
            {
                AssetDatabase.LoadAssetAtPath<GameObject>($"{TreePrefabFolder}/tree_01.prefab"),
                AssetDatabase.LoadAssetAtPath<GameObject>($"{TreePrefabFolder}/tree_02.prefab"),
                AssetDatabase.LoadAssetAtPath<GameObject>($"{TreePrefabFolder}/tree_03.prefab")
            };
            settings.Prefabs = library;
            settings.Lighting.SkyboxMaterial = CreateOrUpdateSkybox();
            settings.Lighting.PostProcessingProfile = CreateOrUpdatePostProfile();
            settings.Streetlights.Prefab = AssetDatabase.LoadAssetAtPath<GameObject>(StreetLightPrefabBuilder.PrefabPath);
            settings.Landmarks.Prefabs = new[]
            {
                new WeightedEnvironmentPrefab { Prefab = library.Fountain, Weight = 1.2f, AllowedInParks = true },
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
