using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CouchGuys.Editor
{
    /// <summary>
    /// Creates reusable toon presets and non-destructively converts prefab material
    /// assignments to generated copies. Imported FBX materials are never modified.
    /// </summary>
    public static class ToonMaterialWorkflow
    {
        public const string ShaderName = "Couch Guys/Toon Lit";
        public const string MaterialFolder = "Assets/_Project/Materials/Toon";
        private const string GeneratedFolder = MaterialFolder + "/Generated";

        private enum Profile
        {
            Houses,
            Roads,
            Sidewalks,
            Grass,
            Foliage,
            Props,
            StreetLights
        }

        private readonly struct Preset
        {
            public Preset(string name, Profile profile, Color colour)
            {
                Name = name;
                Profile = profile;
                Colour = colour;
            }

            public string Name { get; }
            public Profile Profile { get; }
            public Color Colour { get; }
        }

        private static readonly Preset[] Presets =
        {
            new Preset("Toon_Houses", Profile.Houses, new Color(0.90f, 0.52f, 0.32f)),
            new Preset("Toon_Roads", Profile.Roads, new Color(0.075f, 0.085f, 0.105f)),
            new Preset("Toon_Sidewalks", Profile.Sidewalks, new Color(0.58f, 0.60f, 0.62f)),
            new Preset("Toon_Grass", Profile.Grass, new Color(0.25f, 0.62f, 0.20f)),
            new Preset("Toon_Foliage", Profile.Foliage, new Color(0.18f, 0.52f, 0.16f)),
            new Preset("Toon_Props", Profile.Props, new Color(0.28f, 0.62f, 0.82f)),
            new Preset("Toon_StreetLights", Profile.StreetLights, new Color(0.95f, 0.72f, 0.22f))
        };

        private static readonly (string Path, Profile Profile)[] Prefabs =
        {
            ("Assets/_Project/Prefabs/Houses/House_01.prefab", Profile.Houses),
            ("Assets/_Project/Prefabs/Houses/House_02.prefab", Profile.Houses),
            ("Assets/_Project/Prefabs/Roads/Road_Straight.prefab", Profile.Roads),
            ("Assets/_Project/Prefabs/Roads/Road_Corner.prefab", Profile.Roads),
            ("Assets/_Project/Prefabs/Roads/Road_T.prefab", Profile.Roads),
            ("Assets/_Project/Prefabs/Roads/Road_Cross.prefab", Profile.Roads),
            ("Assets/_Project/Prefabs/Roads/Road_End.prefab", Profile.Roads),
            ("Assets/_Project/Prefabs/Couch.prefab", Profile.Props),
            ("Assets/_Project/Prefabs/DeliveryNPC.prefab", Profile.Props),
            ("Assets/_Project/Prefabs/Player.prefab", Profile.Props),
            ("Assets/_Project/Prefabs/Enemies/Thief.prefab", Profile.Props),
            ("Assets/_Project/Prefabs/Enemies/Thief_Pistol.prefab", Profile.Props),
            ("Assets/_Project/Prefabs/Enemies/Thief_Shotgun.prefab", Profile.Props),
            ("Assets/_Project/Prefabs/Enemies/Thief_AssaultRifle.prefab", Profile.Props),
            ("Assets/_Project/Prefabs/Vehicles/LightTruck.prefab", Profile.Props),
            ("Assets/_Project/Prefabs/Vehicles/GoodTruck.prefab", Profile.Props),
            ("Assets/_Project/Prefabs/Vehicles/BestTruck.prefab", Profile.Props)
        };

        [InitializeOnLoadMethod]
        private static void ApplyMissingWorkflowAfterImport()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                if (AssetDatabase.LoadAssetAtPath<Material>(
                        $"{MaterialFolder}/Toon_Houses.mat") == null)
                {
                    ApplyToProjectPrefabs();
                    ValidateProjectMaterials();
                }
            };
        }

        [MenuItem("Couch Guys/Art/Create or Refresh Toon Presets")]
        public static void CreateOrRefreshPresets()
        {
            Shader shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogError($"Shader '{ShaderName}' is unavailable. Let Unity finish importing, then try again.");
                return;
            }

            EnsureFolder(MaterialFolder);
            foreach (Preset preset in Presets)
            {
                string path = $"{MaterialFolder}/{preset.Name}.mat";
                Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(shader) { name = preset.Name };
                    AssetDatabase.CreateAsset(material, path);
                }
                else
                {
                    material.shader = shader;
                }

                material.SetColor("_BaseColor", preset.Colour);
                Configure(material, preset.Profile);
                EditorUtility.SetDirty(material);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"Created or refreshed {Presets.Length} toon material presets in '{MaterialFolder}'.");
        }

        [MenuItem("Couch Guys/Art/Apply Toon Workflow to Project Prefabs")]
        public static void ApplyToProjectPrefabs()
        {
            CreateOrRefreshPresets();
            Shader shader = Shader.Find(ShaderName);
            if (shader == null) return;

            EnsureFolder(GeneratedFolder);
            Dictionary<Material, Material> converted = new Dictionary<Material, Material>();
            int prefabCount = 0;
            int assignmentCount = 0;

            foreach ((string path, Profile profile) in Prefabs)
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) continue;
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                bool changed = false;
                try
                {
                    foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                    {
                        Material[] materials = renderer.sharedMaterials;
                        for (int index = 0; index < materials.Length; index++)
                        {
                            Material source = materials[index];
                            if (source == null || IsParticleShader(source.shader)) continue;
                            Profile materialProfile = ResolveProfile(profile, renderer.name, source.name);
                            Material toon = GetOrCreateCopy(source, materialProfile, shader, converted);
                            if (toon == null || toon == source) continue;
                            materials[index] = toon;
                            assignmentCount++;
                            changed = true;
                        }

                        if (changed) renderer.sharedMaterials = materials;
                    }

                    if (changed)
                    {
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                        prefabCount++;
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Applied the toon workflow to {assignmentCount} renderer slots across {prefabCount} prefabs. Original imported materials were preserved.");
        }

        public static void ApplyFromCommandLine()
        {
            ApplyToProjectPrefabs();
        }

        [MenuItem("Couch Guys/Art/Validate Toon Materials")]
        public static void ValidateProjectMaterials()
        {
            Shader shader = Shader.Find(ShaderName);
            int rendererSlots = 0;
            int toonSlots = 0;
            int missingMaterials = 0;
            int unsupportedShaders = 0;

            foreach ((string path, Profile _) in Prefabs)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;
                foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
                {
                    foreach (Material material in renderer.sharedMaterials)
                    {
                        rendererSlots++;
                        if (material == null)
                        {
                            missingMaterials++;
                            continue;
                        }

                        if (material.shader == shader) toonSlots++;
                        if (material.shader == null || !material.shader.isSupported) unsupportedShaders++;
                    }
                }
            }

            bool valid = shader != null && shader.isSupported &&
                         missingMaterials == 0 && unsupportedShaders == 0;
            string message =
                $"Toon material validation {(valid ? "passed" : "failed")}: " +
                $"shader supported={shader != null && shader.isSupported}, " +
                $"toon slots={toonSlots}/{rendererSlots}, missing materials={missingMaterials}, " +
                $"unsupported shaders={unsupportedShaders}.";
            if (valid) Debug.Log(message);
            else Debug.LogError(message);

            if (valid)
            {
                string previewPath = Path.GetFullPath("Temp/ToonWorkflowValidation.png");
                if (TryCapturePreview(previewPath))
                    Debug.Log($"Captured toon workflow validation frame at '{previewPath}'.");
            }
        }

        private static bool TryCapturePreview(string path)
        {
            Camera camera = Camera.main != null
                ? Camera.main
                : UnityEngine.Object.FindFirstObjectByType<Camera>(FindObjectsInactive.Exclude);
            if (camera == null)
            {
                return TryCapturePrefabPreview(path);
            }

            return RenderCameraToFile(camera, path);
        }

        private static bool TryCapturePrefabPreview(string path)
        {
            GameObject housePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/_Project/Prefabs/Houses/House_01.prefab");
            if (housePrefab == null) return false;

            Scene previewScene = EditorSceneManager.NewPreviewScene();
            try
            {
                GameObject house = PrefabUtility.InstantiatePrefab(housePrefab, previewScene) as GameObject;
                if (house == null) return false;
                Renderer[] renderers = house.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0) return false;

                Bounds bounds = renderers[0].bounds;
                for (int index = 1; index < renderers.Length; index++) bounds.Encapsulate(renderers[index].bounds);

                GameObject lightObject = new GameObject("Preview Sun", typeof(Light));
                SceneManager.MoveGameObjectToScene(lightObject, previewScene);
                Light light = lightObject.GetComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.25f;
                light.color = new Color(1f, 0.88f, 0.72f);
                lightObject.transform.rotation = Quaternion.Euler(48f, -32f, 0f);

                GameObject cameraObject = new GameObject("Preview Camera", typeof(Camera));
                SceneManager.MoveGameObjectToScene(cameraObject, previewScene);
                Camera previewCamera = cameraObject.GetComponent<Camera>();
                previewCamera.clearFlags = CameraClearFlags.SolidColor;
                previewCamera.backgroundColor = new Color(0.42f, 0.70f, 0.90f);
                previewCamera.fieldOfView = 35f;
                previewCamera.aspect = 16f / 9f;
                float distance = Mathf.Max(5f, bounds.extents.magnitude * 2.8f);
                previewCamera.transform.position = bounds.center + new Vector3(1f, 0.65f, -1f).normalized * distance;
                previewCamera.transform.LookAt(bounds.center + Vector3.up * bounds.extents.y * 0.08f);
                return RenderCameraToFile(previewCamera, path);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(previewScene);
            }
        }

        private static bool RenderCameraToFile(Camera camera, string path)
        {

            RenderTexture renderTexture = RenderTexture.GetTemporary(1280, 720, 24, RenderTextureFormat.ARGB32);
            Texture2D image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture previousTarget = camera.targetTexture;
            try
            {
                camera.targetTexture = renderTexture;
                camera.Render();
                RenderTexture.active = renderTexture;
                image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Toon workflow preview capture failed: {exception.Message}");
                return false;
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(renderTexture);
                UnityEngine.Object.DestroyImmediate(image);
            }
        }

        private static Material GetOrCreateCopy(
            Material source,
            Profile profile,
            Shader shader,
            IDictionary<Material, Material> cache)
        {
            if (source.shader == shader)
            {
                Configure(source, profile);
                EditorUtility.SetDirty(source);
                return source;
            }
            if (cache.TryGetValue(source, out Material cached)) return cached;

            Color baseColour = ReadColour(source);
            Color emission = source.HasProperty("_EmissionColor")
                ? source.GetColor("_EmissionColor")
                : Color.black;
            Texture baseMap = ReadTexture(source, "_BaseMap", "_MainTex");
            Texture emissionMap = ReadTexture(source, "_EmissionMap");
            bool alphaClip = source.HasProperty("_AlphaClip") && source.GetFloat("_AlphaClip") > 0.5f;

            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out string guid, out long localId);
            string safeName = MakeSafeFileName(source.name);
            string identity = string.IsNullOrEmpty(guid)
                ? Math.Abs(source.GetInstanceID()).ToString()
                : $"{guid.Substring(0, 8)}_{Math.Abs(localId)}";
            string path = $"{GeneratedFolder}/{profile}_{safeName}_{identity}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = $"{source.name} (Toon)" };
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = shader;
            }

            material.SetColor("_BaseColor", baseColour);
            if (baseMap != null) material.SetTexture("_BaseMap", baseMap);
            if (emissionMap != null) material.SetTexture("_EmissionMap", emissionMap);
            material.SetColor("_EmissionColor", emission);
            material.SetFloat("_AlphaClip", alphaClip ? 1f : 0f);
            if (source.HasProperty("_Cutoff")) material.SetFloat("_Cutoff", source.GetFloat("_Cutoff"));
            Configure(material, profile);
            EditorUtility.SetDirty(material);
            cache[source] = material;
            return material;
        }

        private static void Configure(Material material, Profile profile)
        {
            material.SetColor("_ShadowColor", new Color(0.40f, 0.48f, 0.62f, 1f));
            material.SetFloat("_ShadowStrength", 0.62f);
            material.SetFloat("_ShadowThreshold", 0.38f);
            material.SetFloat("_BandSoftness", 0.055f);
            material.SetFloat("_Saturation", 1.12f);
            material.SetFloat("_AdditionalLightStrength", 0.65f);
            material.SetFloat("_RimEnabled", 1f);
            material.SetColor("_RimColor", new Color(0.46f, 0.72f, 1f, 1f));
            material.SetFloat("_RimPower", 3.2f);
            material.SetFloat("_RimIntensity", 0.16f);
            material.SetColor("_EmissionColor", Color.black);
            material.SetFloat("_SpecularEnabled", 0f);
            material.SetFloat("_SpecularIntensity", 0.3f);

            switch (profile)
            {
                case Profile.Roads:
                    material.SetFloat("_Saturation", 1f);
                    material.SetFloat("_ShadowStrength", 0.48f);
                    material.SetFloat("_RimEnabled", 0f);
                    break;
                case Profile.Sidewalks:
                    material.SetFloat("_Saturation", 0.92f);
                    material.SetFloat("_RimIntensity", 0.06f);
                    break;
                case Profile.Grass:
                case Profile.Foliage:
                    material.SetFloat("_Saturation", 1.22f);
                    material.SetFloat("_BandSoftness", 0.08f);
                    material.SetFloat("_RimIntensity", 0.12f);
                    break;
                case Profile.StreetLights:
                    material.SetColor("_EmissionColor", new Color(1.6f, 1.05f, 0.35f, 1f));
                    material.SetFloat("_RimIntensity", 0.28f);
                    break;
                case Profile.Props:
                    material.SetFloat("_SpecularEnabled", 1f);
                    material.SetFloat("_SpecularSize", 0.14f);
                    material.SetFloat("_SpecularSoftness", 0.035f);
                    break;
            }
        }

        private static Profile ResolveProfile(Profile fallback, string rendererName, string materialName)
        {
            string combined = (rendererName + " " + materialName).ToLowerInvariant();
            if (combined.Contains("leaf") || combined.Contains("foliage") || combined.Contains("tree")) return Profile.Foliage;
            if (combined.Contains("grass") || combined.Contains("lawn")) return Profile.Grass;
            if (combined.Contains("sidewalk") || combined.Contains("pavement")) return Profile.Sidewalks;
            if (combined.Contains("lamp") || combined.Contains("headlight") ||
                combined.Contains("brakelight") || combined.Contains("window") ||
                combined.Contains("glow") || combined.Contains("emissive"))
                return Profile.StreetLights;
            return fallback;
        }

        private static bool IsParticleShader(Shader shader) =>
            shader != null && shader.name.IndexOf("Particle", StringComparison.OrdinalIgnoreCase) >= 0;

        private static Color ReadColour(Material material)
        {
            if (material.HasProperty("_BaseColor")) return material.GetColor("_BaseColor");
            if (material.HasProperty("_Color")) return material.GetColor("_Color");
            return Color.white;
        }

        private static Texture ReadTexture(Material material, params string[] names)
        {
            foreach (string name in names)
                if (material.HasProperty(name) && material.GetTexture(name) != null)
                    return material.GetTexture(name);
            return null;
        }

        private static string MakeSafeFileName(string value)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '_');
            return string.IsNullOrWhiteSpace(value) ? "Material" : value;
        }

        private static void EnsureFolder(string folder)
        {
            string[] pieces = folder.Split('/');
            string current = pieces[0];
            for (int index = 1; index < pieces.Length; index++)
            {
                string next = $"{current}/{pieces[index]}";
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, pieces[index]);
                current = next;
            }
        }
    }
}
