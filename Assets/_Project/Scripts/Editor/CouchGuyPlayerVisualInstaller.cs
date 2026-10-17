using System;
using System.IO;
using CouchGuys.Gameplay.Weapons;
using CouchGuys.Networking;
using CouchGuys.Player;
using UnityEditor;
using UnityEngine;

namespace CouchGuys.Editor
{
    /// <summary>
    /// Installs the redesigned robot without rebuilding the gameplay prefab. The old visual's
    /// rendered height, ground contact and facing are measured first so replacement is stable.
    /// Hover effects intentionally use a combined maximum of 27 particles.
    /// </summary>
    [InitializeOnLoad]
    public static class CouchGuyPlayerVisualInstaller
    {
        private const string PlayerPrefabPath = "Assets/_Project/Prefabs/Player.prefab";
        private const string ModelPath = "Assets/_Project/Models/CouchGuy/CouchGuy_Redesigned.fbx";
        private const string ControllerPath = "Assets/_Project/Animations/CouchGuy.controller";
        private const string VfxFolder = "Assets/_Project/Materials/Player";
        private const string WispMaterialPath = VfxFolder + "/PlayerGlow_HoverWisps.mat";
        private const string RippleMaterialPath = VfxFolder + "/PlayerGlow_HoverRipple.mat";
        private const string GroundGlowMaterialPath = VfxFolder + "/PlayerGlow_Ground.mat";
        private const string RippleTexturePath = VfxFolder + "/HoverRipple.png";
        private const string GroundGlowTexturePath = VfxFolder + "/HoverGroundGlow.png";
        private const string SettingsFolder = "Assets/_Project/Resources";
        private const string SettingsPath = SettingsFolder + "/RobotHoverVFXSettings.asset";

        static CouchGuyPlayerVisualInstaller()
        {
            EditorApplication.delayCall += InstallIfRequired;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                EditorApplication.delayCall += InstallIfRequired;
            }
        }

        [MenuItem("Couch Guys/Player/Install Redesigned Robot + Hover VFX")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("Exit Play Mode before installing the Couch Guy visual.");
                return;
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (modelAsset == null)
            {
                throw new UnityException($"Redesigned Couch Guy model is missing at {ModelPath}.");
            }

            CouchGuyAnimatorControllerBuilder.BuildAnimatorController();
            RuntimeAnimatorController controller =
                AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
            if (controller == null)
            {
                throw new UnityException("The Couch Guy Animator Controller could not be rebuilt.");
            }

            Material wispMaterial = EnsureParticleMaterial(WispMaterialPath, null);
            Texture2D rippleTexture = EnsureRippleTexture();
            Material rippleMaterial = EnsureParticleMaterial(RippleMaterialPath, rippleTexture);
            Texture2D groundGlowTexture = EnsureGroundGlowTexture();
            Material groundGlowMaterial = EnsureParticleMaterial(GroundGlowMaterialPath, groundGlowTexture);
            RobotHoverVFXSettings settings = EnsureSettings();

            GameObject player = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                Transform visual = player.transform.Find("Visual");
                if (visual == null)
                {
                    throw new UnityException("Player prefab has no Visual transform.");
                }

                Transform oldModel = visual.Find("CouchGuy");
                Animator oldAnimator = oldModel != null
                    ? oldModel.GetComponentInChildren<Animator>(true)
                    : null;
                Vector3 preservedLocalPosition = oldModel != null
                    ? oldModel.localPosition
                    : Vector3.zero;
                Quaternion preservedLocalRotation = oldModel != null
                    ? oldModel.localRotation
                    : Quaternion.identity;
                Vector3 preservedLocalScale = oldModel != null
                    ? oldModel.localScale
                    : Vector3.one;
                if (oldAnimator != null && oldAnimator.runtimeAnimatorController != null)
                {
                    controller = oldAnimator.runtimeAnimatorController;
                }

                if (oldModel != null)
                {
                    UnityEngine.Object.DestroyImmediate(oldModel.gameObject);
                }

                Transform oldVfx = visual.Find("HoverVFX");
                if (oldVfx != null)
                {
                    UnityEngine.Object.DestroyImmediate(oldVfx.gameObject);
                }

                GameObject model = PrefabUtility.InstantiatePrefab(modelAsset, visual) as GameObject;
                if (model == null)
                {
                    throw new UnityException("Failed to instantiate the redesigned Couch Guy model.");
                }

                model.name = "CouchGuy";
                model.transform.SetLocalPositionAndRotation(
                    preservedLocalPosition,
                    preservedLocalRotation);
                model.transform.localScale = preservedLocalScale;
                SetWeaponsInactive(model.transform);

                Animator animator = model.GetComponent<Animator>();
                if (animator == null)
                {
                    animator = model.AddComponent<Animator>();
                }
                animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

                Transform hoverRing = FindDescendant(model.transform, "HoverRing");
                if (hoverRing == null)
                {
                    throw new UnityException("The redesigned model contains no HoverRing transform.");
                }

                Renderer ringRenderer = hoverRing.GetComponent<Renderer>();
                float ringRadius = ringRenderer != null
                    ? Mathf.Max(ringRenderer.bounds.extents.x, ringRenderer.bounds.extents.z)
                    : 0.34f;

                GameObject hoverVfx = new("HoverVFX");
                hoverVfx.transform.SetParent(visual, false);
                hoverVfx.transform.position = ringRenderer != null
                    ? ringRenderer.bounds.center
                    : hoverRing.position;
                hoverVfx.tag = "Untagged";

                GameObject ringPivot = new("HoverRingPivot");
                ringPivot.transform.SetParent(hoverVfx.transform, false);
                GameObject ringEffects = new("RingEffects");
                ringEffects.transform.SetParent(ringPivot.transform, false);
                ParticleSystem wisps = CreateWisps(ringEffects.transform, ringRadius, wispMaterial);
                ParticleSystem ripples = CreateRipples(ringEffects.transform, ringRadius, rippleMaterial);
                Renderer groundGlow = CreateGroundGlow(
                    hoverVfx.transform,
                    ringRadius,
                    groundGlowMaterial);

                RobotHoverVFX hoverController = player.GetComponent<RobotHoverVFX>();
                if (hoverController == null) hoverController = player.AddComponent<RobotHoverVFX>();
                SetObjectReference(hoverController, "m_settings", settings);
                SetObjectReference(hoverController, "m_robotVisual", model.transform);
                SetObjectReference(hoverController, "m_hoverRing", hoverRing);
                SetObjectReference(hoverController, "m_ringPivot", ringPivot.transform);
                SetObjectReference(hoverController, "m_ringEffects", ringEffects.transform);
                SetObjectReference(hoverController, "m_energyRipples", ripples);
                SetObjectReference(hoverController, "m_floatingParticles", wisps);
                SetObjectReference(hoverController, "m_groundGlow", groundGlow);

                RewirePlayerReferences(player, animator, model.transform);
                PrefabUtility.SaveAsPrefabAsset(player, PlayerPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(player);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Installed CouchGuy_Redesigned on Player with optimised hover wisps and energy ripples.");
        }

        private static void InstallIfRequired()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (player == null || model == null)
            {
                return;
            }

            Transform couchGuy = player.transform.Find("Visual/CouchGuy");
            Transform vfx = player.transform.Find("Visual/HoverVFX");
            string sourcePath = couchGuy != null
                ? PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(couchGuy.gameObject)
                : string.Empty;
            bool hoverVfxComplete = vfx != null &&
                vfx.Find("HoverRingPivot/RingEffects/HoverEnergyWisps") != null &&
                vfx.Find("HoverRingPivot/RingEffects/HoverEnergyRipples") != null &&
                vfx.Find("GroundGlow") != null &&
                player.GetComponent<RobotHoverVFX>() != null;
            bool weaponBindingsComplete = AreWeaponReferencesComplete(player);
            bool hasMissingComponent = false;
            if (vfx != null)
            {
                foreach (Component component in vfx.GetComponents<Component>())
                {
                    if (component == null)
                    {
                        hasMissingComponent = true;
                        break;
                    }
                }
            }
            if (sourcePath == ModelPath && hoverVfxComplete &&
                weaponBindingsComplete && !hasMissingComponent)
            {
                return;
            }

            try
            {
                Install();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private static bool IsInsideWeapon(Transform transform, Transform modelRoot)
        {
            Transform current = transform;
            while (current != null && current != modelRoot)
            {
                if (current.name.StartsWith("Weapon_", StringComparison.Ordinal)) return true;
                current = current.parent;
            }
            return false;
        }

        private static void SetWeaponsInactive(Transform model)
        {
            foreach (string name in new[] { "Weapon_Pistol", "Weapon_AssaultRifle", "Weapon_Shotgun" })
            {
                Transform weapon = FindDescendant(model, name);
                if (weapon != null) weapon.gameObject.SetActive(false);
            }
        }

        private static void RewirePlayerReferences(GameObject player, Animator animator, Transform model)
        {
            CouchGuyAnimationDriver animationDriver = player.GetComponent<CouchGuyAnimationDriver>();
            SetObjectReference(animationDriver, "m_animator", animator);

            string[] weaponNames = { "Weapon_Pistol", "Weapon_AssaultRifle", "Weapon_Shotgun" };
            PlayerWeaponController weaponController = player.GetComponent<PlayerWeaponController>();
            SerializedObject weaponObject = new(weaponController);
            SerializedProperty weaponModels = weaponObject.FindProperty("m_weaponModels");
            SerializedProperty muzzles = weaponObject.FindProperty("m_muzzles");
            weaponModels.arraySize = weaponNames.Length;
            muzzles.arraySize = weaponNames.Length;
            for (int index = 0; index < weaponNames.Length; index++)
            {
                Transform weapon = FindDescendant(model, weaponNames[index]);
                weaponModels.GetArrayElementAtIndex(index).objectReferenceValue = weapon;
                muzzles.GetArrayElementAtIndex(index).objectReferenceValue =
                    weapon != null ? FindDescendantByPrefix(weapon, "Muzzle") : null;
            }
            weaponObject.ApplyModifiedPropertiesWithoutUndo();

            NetworkPlayerOwnership ownership = player.GetComponent<NetworkPlayerOwnership>();
            Renderer identityRenderer = FindIdentityRenderer(model);
            SetObjectReference(ownership, "m_playerRenderer", identityRenderer);
        }

        private static Renderer FindIdentityRenderer(Transform model)
        {
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is ParticleSystemRenderer || IsInsideWeapon(renderer.transform, model)) continue;
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material != null && material.name.StartsWith("PlayerGlow", StringComparison.Ordinal))
                    {
                        return renderer;
                    }
                }
            }
            return model.GetComponentInChildren<Renderer>(true);
        }

        private static ParticleSystem CreateWisps(Transform parent, float radius, Material material)
        {
            GameObject gameObject = new("HoverEnergyWisps");
            gameObject.transform.SetParent(parent, false);
            ParticleSystem particles = gameObject.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = particles.main;
            main.loop = true;
            main.playOnAwake = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.08f, 0.22f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.018f, 0.045f);
            main.startColor = new Color(0.08f, 0.72f, 1f, 0.82f);
            main.maxParticles = 24;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = 11f;
            ParticleSystem.ShapeModule shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = radius * 1.04f;
            shape.radiusThickness = 0.28f;
            shape.arcMode = ParticleSystemShapeMultiModeValue.Random;

            ParticleSystem.VelocityOverLifetimeModule velocity = particles.velocityOverLifetime;
            velocity.enabled = true;
            // Unity requires all three velocity axes to use the same curve mode.
            // A small constant lift keeps the wisps drifting without creating a
            // per-axis mode mismatch (or the associated runtime warning).
            velocity.y = 0.1f;
            ParticleSystem.NoiseModule noise = particles.noise;
            noise.enabled = true;
            noise.strength = 0.055f;
            noise.frequency = 0.55f;
            noise.scrollSpeed = 0.22f;
            noise.quality = ParticleSystemNoiseQuality.Low;

            ParticleSystem.ColorOverLifetimeModule colour = particles.colorOverLifetime;
            colour.enabled = true;
            colour.color = FadeGradient(new Color(0.05f, 0.68f, 1f, 1f));

            ParticleSystemRenderer renderer = gameObject.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = material;
            renderer.maxParticleSize = 0.08f;
            renderer.sortingFudge = -0.1f;
            return particles;
        }

        private static ParticleSystem CreateRipples(Transform parent, float radius, Material material)
        {
            GameObject gameObject = new("HoverEnergyRipples");
            gameObject.transform.SetParent(parent, false);
            gameObject.transform.localPosition = new Vector3(0f, -0.035f, 0f);
            ParticleSystem particles = gameObject.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = particles.main;
            main.loop = true;
            main.playOnAwake = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.1f);
            main.startSpeed = 0f;
            main.startSize = radius * 1.65f;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new Color(0.05f, 0.64f, 1f, 0.48f);
            main.maxParticles = 3;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = 1.35f;
            ParticleSystem.ShapeModule shape = particles.shape;
            shape.enabled = false;

            ParticleSystem.SizeOverLifetimeModule size = particles.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.72f), new Keyframe(1f, 1.42f)));
            ParticleSystem.ColorOverLifetimeModule colour = particles.colorOverLifetime;
            colour.enabled = true;
            colour.color = FadeGradient(new Color(0.04f, 0.62f, 1f, 0.62f));

            ParticleSystemRenderer renderer = gameObject.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            renderer.sharedMaterial = material;
            renderer.maxParticleSize = 0.5f;
            renderer.sortingFudge = -0.2f;
            return particles;
        }

        private static Renderer CreateGroundGlow(Transform parent, float radius, Material material)
        {
            GameObject gameObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
            gameObject.name = "GroundGlow";
            gameObject.transform.SetParent(parent, false);
            gameObject.transform.localScale = Vector3.one * (radius * 2.45f);
            Collider collider = gameObject.GetComponent<Collider>();
            if (collider != null) UnityEngine.Object.DestroyImmediate(collider);

            MeshRenderer renderer = gameObject.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            renderer.enabled = false;
            return renderer;
        }

        private static ParticleSystem.MinMaxGradient FadeGradient(Color colour)
        {
            Gradient gradient = new();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(colour, 0f),
                    new GradientColorKey(colour, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(colour.a, 0.16f),
                    new GradientAlphaKey(colour.a * 0.7f, 0.66f),
                    new GradientAlphaKey(0f, 1f)
                });
            return new ParticleSystem.MinMaxGradient(gradient);
        }

        private static Material EnsureParticleMaterial(string path, Texture2D texture)
        {
            EnsureFolder(VfxFolder);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool hasRenderPipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null;
            Shader shader = hasRenderPipeline
                ? Shader.Find("Universal Render Pipeline/Particles/Unlit")
                : Shader.Find("Particles/Standard Unlit");
            shader ??= Shader.Find("Particles/Standard Unlit") ??
                Shader.Find("Legacy Shaders/Particles/Alpha Blended");
            if (shader == null) throw new UnityException("No compatible unlit particle shader is available.");
            if (material == null)
            {
                material = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            Color cyan = new(0.02f, 0.55f, 1f, 0.72f);
            if (material.HasProperty("_Color")) material.SetColor("_Color", cyan);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", cyan);
            if (material.HasProperty("_TintColor")) material.SetColor("_TintColor", cyan);
            if (material.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor", cyan * 2.5f);
            if (texture != null && material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
            if (texture != null && material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);
            if (material.HasProperty("_Mode")) material.SetFloat("_Mode", 2f);
            if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", 5f);
            if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", 10f);
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = 3000;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Texture2D EnsureRippleTexture()
        {
            Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(RippleTexturePath);
            if (existing != null) return existing;
            EnsureFolder(VfxFolder);
            const int size = 64;
            Texture2D texture = new(size, size, TextureFormat.RGBA32, false, true);
            Color[] pixels = new Color[size * size];
            Vector2 center = new((size - 1) * 0.5f, (size - 1) * 0.5f);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x, y), center) / center.x;
                    float alpha = Mathf.Clamp01(1f - Mathf.Abs(distance - 0.78f) / 0.075f);
                    alpha *= Mathf.Clamp01((1f - distance) * 5f);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha * alpha);
                }
            }
            texture.SetPixels(pixels);
            texture.Apply(false, false);
            File.WriteAllBytes(RippleTexturePath, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(RippleTexturePath, ImportAssetOptions.ForceSynchronousImport);
            TextureImporter importer = AssetImporter.GetAtPath(RippleTexturePath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Default;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.textureCompression = TextureImporterCompression.Compressed;
                importer.maxTextureSize = size;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(RippleTexturePath);
        }

        private static Texture2D EnsureGroundGlowTexture()
        {
            Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(GroundGlowTexturePath);
            if (existing != null) return existing;
            EnsureFolder(VfxFolder);
            const int size = 64;
            Texture2D texture = new(size, size, TextureFormat.RGBA32, false, true);
            Color[] pixels = new Color[size * size];
            Vector2 centre = new((size - 1) * 0.5f, (size - 1) * 0.5f);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x, y), centre) / centre.x;
                    float alpha = Mathf.Pow(Mathf.Clamp01(1f - distance), 2.15f);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }
            texture.SetPixels(pixels);
            texture.Apply(false, false);
            File.WriteAllBytes(GroundGlowTexturePath, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(GroundGlowTexturePath, ImportAssetOptions.ForceSynchronousImport);
            TextureImporter importer = AssetImporter.GetAtPath(GroundGlowTexturePath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Default;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.textureCompression = TextureImporterCompression.Compressed;
                importer.maxTextureSize = size;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(GroundGlowTexturePath);
        }

        private static RobotHoverVFXSettings EnsureSettings()
        {
            EnsureFolder(SettingsFolder);
            RobotHoverVFXSettings settings =
                AssetDatabase.LoadAssetAtPath<RobotHoverVFXSettings>(SettingsPath);
            if (settings != null) return settings;

            settings = ScriptableObject.CreateInstance<RobotHoverVFXSettings>();
            settings.name = "RobotHoverVFXSettings";
            AssetDatabase.CreateAsset(settings, SettingsPath);
            return settings;
        }

        private static Transform FindDescendant(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform child in root)
            {
                Transform result = FindDescendant(child, name);
                if (result != null) return result;
            }
            return null;
        }

        private static Transform FindDescendantByPrefix(Transform root, string namePrefix)
        {
            if (root.name.StartsWith(namePrefix, StringComparison.Ordinal)) return root;
            foreach (Transform child in root)
            {
                Transform result = FindDescendantByPrefix(child, namePrefix);
                if (result != null) return result;
            }
            return null;
        }

        private static bool AreWeaponReferencesComplete(GameObject player)
        {
            PlayerWeaponController controller = player.GetComponent<PlayerWeaponController>();
            if (controller == null) return false;

            SerializedObject serializedController = new(controller);
            SerializedProperty weaponModels = serializedController.FindProperty("m_weaponModels");
            SerializedProperty muzzles = serializedController.FindProperty("m_muzzles");
            if (weaponModels == null || muzzles == null ||
                weaponModels.arraySize != 3 || muzzles.arraySize != 3)
            {
                return false;
            }

            for (int index = 0; index < 3; index++)
            {
                UnityEngine.Object weapon =
                    weaponModels.GetArrayElementAtIndex(index).objectReferenceValue;
                UnityEngine.Object muzzle =
                    muzzles.GetArrayElementAtIndex(index).objectReferenceValue;
                if (weapon == null || muzzle == null ||
                    !muzzle.name.StartsWith("Muzzle", StringComparison.Ordinal))
                {
                    return false;
                }
            }
            return true;
        }

        private static void SetObjectReference(UnityEngine.Object target, string propertyName, UnityEngine.Object value)
        {
            if (target == null) throw new UnityException($"Missing component for {propertyName}.");
            SerializedObject serialised = new(target);
            SerializedProperty property = serialised.FindProperty(propertyName);
            if (property == null) throw new UnityException($"Missing property {propertyName} on {target.GetType().Name}.");
            property.objectReferenceValue = value;
            serialised.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void EnsureFolder(string path)
        {
            string[] sections = path.Split('/');
            string current = sections[0];
            for (int index = 1; index < sections.Length; index++)
            {
                string next = current + "/" + sections[index];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, sections[index]);
                current = next;
            }
        }
    }

}
