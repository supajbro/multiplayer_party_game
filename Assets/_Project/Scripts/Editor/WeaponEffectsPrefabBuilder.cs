using CouchGuys.Gameplay.Weapons;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CouchGuys.Editor
{
    /// <summary>Builds the reusable placeholder projectiles and cartoon weapon particles.</summary>
    public static class WeaponEffectsPrefabBuilder
    {
        private const string Root = "Assets/_Project/Prefabs/Weapons";
        private const string ProjectileRoot = Root + "/Projectiles";
        private const string EffectRoot = Root + "/Effects";
        private const string MaterialRoot = Root + "/Materials";
        private const string PlayerPrefabPath = "Assets/_Project/Prefabs/Player.prefab";

        [MenuItem("Couch Guys/Build Weapon Projectiles and Effects %#e")]
        public static void Build()
        {
            EnsureFolder("Assets/_Project/Prefabs", "Weapons");
            EnsureFolder(Root, "Projectiles");
            EnsureFolder(Root, "Effects");
            EnsureFolder(Root, "Materials");

            Material yellow = CreateMaterial("Bullet_Yellow", new Color(1f, 0.66f, 0.08f, 1f), true);
            Material orange = CreateMaterial("Bullet_Orange", new Color(1f, 0.3f, 0.04f, 1f), true);
            Material smoke = CreateMaterial("Particle_Smoke", new Color(0.72f, 0.76f, 0.82f, 0.72f), false);
            Material dirt = CreateMaterial("Particle_Dirt", new Color(0.38f, 0.2f, 0.08f, 1f), false);
            Material grass = CreateMaterial("Particle_Grass", new Color(0.28f, 0.72f, 0.16f, 1f), false);
            Material dust = CreateMaterial("Particle_Dust", new Color(0.82f, 0.65f, 0.43f, 1f), false);
            Material cyan = CreateMaterial("Particle_Hit", new Color(0.15f, 0.95f, 1f, 1f), true);

            GameObject pistolBullet = CreateBullet("Pistol_Bullet", yellow);
            GameObject rifleBullet = CreateBullet("AssaultRifle_Bullet", orange);
            GameObject shotgunBullet = CreateBullet("Shotgun_Pellet", yellow);

            GameObject pistolMuzzle = CreateMuzzle("Pistol_MuzzleFlash", yellow, smoke, 7, 0.16f, 0.3f);
            GameObject rifleMuzzle = CreateMuzzle("AssaultRifle_MuzzleFlash", orange, smoke, 5, 0.11f, 0.18f);
            GameObject shotgunMuzzle = CreateMuzzle("Shotgun_MuzzleFlash", yellow, smoke, 18, 0.28f, 0.65f);
            GameObject pistolCasing = CreateCasing("Pistol_Casing", yellow, 1);
            GameObject rifleCasing = CreateCasing("AssaultRifle_Casing", orange, 1);
            GameObject shotgunCasing = CreateCasing("Shotgun_Shell", orange, 2);

            GameObject terrainImpact = CreateLayeredImpact("Impact_TerrainGrass", dirt, grass, 9, 0.12f, 2.8f);
            GameObject wallImpact = CreateLayeredImpact("Impact_WallDust", dust, smoke, 10, 0.14f, 2.2f);
            GameObject metalImpact = CreateLayeredImpact("Impact_MetalSparks", yellow, orange, 12, 0.055f, 5.5f);
            GameObject enemyImpact = CreateLayeredImpact("Impact_EnemyHit", cyan, yellow, 9, 0.16f, 2.4f);

            ConfigurePlayer(pistolBullet, rifleBullet, shotgunBullet,
                pistolMuzzle, rifleMuzzle, shotgunMuzzle,
                pistolCasing, rifleCasing, shotgunCasing,
                terrainImpact, wallImpact, metalImpact, enemyImpact);
            VerifyProjectile(pistolBullet);
            VerifyProjectile(rifleBullet);
            VerifyProjectile(shotgunBullet);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Built and assigned Couch Guys weapon projectile and particle prefabs.");
        }

        public static void BuildFromCommandLine() => Build();

        private static void VerifyProjectile(GameObject prefab)
        {
            if (prefab == null || prefab.GetComponent<WeaponProjectileVisual>() == null ||
                prefab.GetComponent<Collider>() != null)
            {
                throw new UnityException(
                    $"Weapon projectile validation failed for {(prefab != null ? prefab.name : "missing prefab")}.");
            }
        }

        private static GameObject CreateBullet(string name, Material material)
        {
            GameObject root = GameObject.CreatePrimitive(PrimitiveType.Cube);
            root.name = name;
            Object.DestroyImmediate(root.GetComponent<Collider>());
            MeshRenderer renderer = root.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            TrailRenderer trail = root.AddComponent<TrailRenderer>();
            trail.time = 0.09f;
            trail.minVertexDistance = 0.03f;
            trail.sharedMaterial = material;
            trail.textureMode = LineTextureMode.Stretch;
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.receiveShadows = false;
            root.AddComponent<WeaponProjectileVisual>();
            return SaveAndDestroy(root, ProjectileRoot + "/" + name + ".prefab");
        }

        private static GameObject CreateMuzzle(string name, Material flashMaterial, Material smokeMaterial,
            int flashCount, float flashSize, float smokeSize)
        {
            GameObject root = CreateParticleObject(name, flashMaterial, flashCount, 0.07f, 3.8f,
                flashSize, 16f, new Color(1f, 0.72f, 0.1f, 1f));
            GameObject puff = CreateParticleObject("SmokePuff", smokeMaterial,
                Mathf.Max(3, flashCount / 2), 0.3f, 0.65f, smokeSize, 32f,
                new Color(0.82f, 0.85f, 0.9f, 0.7f));
            puff.transform.SetParent(root.transform, false);
            ParticleSystem.ShapeModule shape = root.GetComponent<ParticleSystem>().shape;
            shape.angle = 18f;
            ParticleSystem.ShapeModule smokeShape = puff.GetComponent<ParticleSystem>().shape;
            smokeShape.angle = 35f;
            return SaveAndDestroy(root, EffectRoot + "/" + name + ".prefab");
        }

        private static GameObject CreateCasing(string name, Material material, int count)
        {
            GameObject root = CreateParticleObject(name, material, count, 0.75f, 2.2f,
                0.07f, 720f, Color.white);
            ParticleSystem particles = root.GetComponent<ParticleSystem>();
            ParticleSystem.MainModule main = particles.main;
            main.gravityModifier = 1.4f;
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(-3f, 3f);
            main.startRotationY = new ParticleSystem.MinMaxCurve(-3f, 3f);
            main.startRotationZ = new ParticleSystem.MinMaxCurve(-3f, 3f);
            ParticleSystem.ShapeModule shape = particles.shape;
            shape.angle = 12f;
            root.transform.localRotation = Quaternion.Euler(0f, 70f, 0f);
            return SaveAndDestroy(root, EffectRoot + "/" + name + ".prefab");
        }

        private static GameObject CreateLayeredImpact(string name, Material primary, Material secondary,
            int count, float size, float speed)
        {
            GameObject root = CreateParticleObject(name, primary, count, 0.24f, speed, size, 360f, Color.white);
            ParticleSystem.ShapeModule shape = root.GetComponent<ParticleSystem>().shape;
            shape.angle = 42f;
            GameObject layer = CreateParticleObject("SecondaryBurst", secondary,
                Mathf.Max(3, count / 2), 0.34f, speed * 0.55f, size * 1.6f, 180f, Color.white);
            layer.transform.SetParent(root.transform, false);
            ParticleSystem.ShapeModule layerShape = layer.GetComponent<ParticleSystem>().shape;
            layerShape.angle = 55f;
            return SaveAndDestroy(root, EffectRoot + "/" + name + ".prefab");
        }

        private static GameObject CreateParticleObject(string name, Material material, int burstCount,
            float lifetime, float speed, float size, float rotationSpeed, Color colour)
        {
            GameObject root = new(name);
            ParticleSystem particles = root.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = particles.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = 0.12f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.65f, lifetime);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.65f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.55f, size);
            main.startRotation = new ParticleSystem.MinMaxCurve(-Mathf.PI, Mathf.PI);
            main.startColor = colour;
            main.maxParticles = Mathf.Max(16, burstCount * 2);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)burstCount) });
            ParticleSystem.ShapeModule shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.radius = 0.02f;
            shape.angle = 30f;
            ParticleSystem.RotationOverLifetimeModule rotation = particles.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-rotationSpeed * Mathf.Deg2Rad,
                rotationSpeed * Mathf.Deg2Rad);
            ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return root;
        }

        private static void ConfigurePlayer(GameObject pistolBullet, GameObject rifleBullet, GameObject shotgunBullet,
            GameObject pistolMuzzle, GameObject rifleMuzzle, GameObject shotgunMuzzle,
            GameObject pistolCasing, GameObject rifleCasing, GameObject shotgunCasing,
            GameObject terrainImpact, GameObject wallImpact, GameObject metalImpact, GameObject enemyImpact)
        {
            GameObject player = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                PlayerWeaponController controller = player.GetComponent<PlayerWeaponController>();
                PlayerWeaponEffects effects = player.GetComponent<PlayerWeaponEffects>();
                if (controller == null || effects == null) throw new UnityException("Player weapon components are missing.");

                SerializedObject controllerObject = new(controller);
                SerializedProperty weapons = controllerObject.FindProperty("m_weapons");
                GameObject[] bullets = { pistolBullet, rifleBullet, shotgunBullet };
                GameObject[] muzzles = { pistolMuzzle, rifleMuzzle, shotgunMuzzle };
                GameObject[] casings = { pistolCasing, rifleCasing, shotgunCasing };
                for (int i = 0; i < 3; i++)
                {
                    SerializedProperty weapon = weapons.GetArrayElementAtIndex(i);
                    weapon.FindPropertyRelative("m_bulletPrefab").objectReferenceValue = bullets[i];
                    weapon.FindPropertyRelative("m_muzzleFlashPrefab").objectReferenceValue = muzzles[i];
                    weapon.FindPropertyRelative("m_shellEjectionPrefab").objectReferenceValue = casings[i];
                }
                SetFloat(weapons, 0, "m_bulletSpeed", 75f); SetFloat(weapons, 0, "m_bulletLifetime", 0.75f);
                SetVector(weapons, 0, "m_bulletSize", new Vector3(0.065f, 0.065f, 0.24f));
                SetFloat(weapons, 0, "m_trailWidth", 0.025f); SetFloat(weapons, 0, "m_recoilDistance", 0.035f);
                SetFloat(weapons, 0, "m_cameraShake", 0.045f); SetFloat(weapons, 0, "m_impactScale", 1f);
                SetFloat(weapons, 1, "m_bulletSpeed", 105f); SetFloat(weapons, 1, "m_bulletLifetime", 0.65f);
                SetVector(weapons, 1, "m_bulletSize", new Vector3(0.045f, 0.045f, 0.34f));
                SetFloat(weapons, 1, "m_trailWidth", 0.018f); SetFloat(weapons, 1, "m_recoilDistance", 0.018f);
                SetFloat(weapons, 1, "m_cameraShake", 0.022f); SetFloat(weapons, 1, "m_impactScale", 0.8f);
                SetFloat(weapons, 2, "m_bulletSpeed", 62f); SetFloat(weapons, 2, "m_bulletLifetime", 0.55f);
                SetVector(weapons, 2, "m_bulletSize", new Vector3(0.055f, 0.055f, 0.18f));
                SetFloat(weapons, 2, "m_trailWidth", 0.03f); SetFloat(weapons, 2, "m_recoilDistance", 0.075f);
                SetFloat(weapons, 2, "m_cameraShake", 0.1f); SetFloat(weapons, 2, "m_impactScale", 1.5f);
                controllerObject.ApplyModifiedPropertiesWithoutUndo();

                SerializedObject effectsObject = new(effects);
                effectsObject.FindProperty("m_terrainImpactPrefab").objectReferenceValue = terrainImpact;
                effectsObject.FindProperty("m_wallImpactPrefab").objectReferenceValue = wallImpact;
                effectsObject.FindProperty("m_metalImpactPrefab").objectReferenceValue = metalImpact;
                effectsObject.FindProperty("m_enemyImpactPrefab").objectReferenceValue = enemyImpact;
                effectsObject.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(player, PlayerPrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(player); }
        }

        private static void SetFloat(SerializedProperty array, int index, string name, float value) =>
            array.GetArrayElementAtIndex(index).FindPropertyRelative(name).floatValue = value;
        private static void SetVector(SerializedProperty array, int index, string name, Vector3 value) =>
            array.GetArrayElementAtIndex(index).FindPropertyRelative(name).vector3Value = value;

        private static Material CreateMaterial(string name, Color colour, bool emissive)
        {
            string path = MaterialRoot + "/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            else material.shader = shader;
            material.color = colour;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", colour);
            if (emissive && material.HasProperty("_EmissionColor"))
                material.SetColor("_EmissionColor", colour * 1.8f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static GameObject SaveAndDestroy(GameObject root, string path)
        {
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        private static void EnsureFolder(string parent, string child)
        {
            string path = parent + "/" + child;
            if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, child);
        }
    }
}
