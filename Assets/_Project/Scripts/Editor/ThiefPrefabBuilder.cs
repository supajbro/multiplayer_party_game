using CouchGuys.Gameplay.Enemies;
using FishNet.Component.Transforming;
using FishNet.Managing.Object;
using FishNet.Object;
using GameKit.Dependencies.Utilities;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace CouchGuys.Editor
{
    /// <summary>Builds the three network-ready animated thief enemies.</summary>
    public static class ThiefPrefabBuilder
    {
        public const string PistolPrefabPath = "Assets/_Project/Prefabs/Enemies/Thief_Pistol.prefab";
        public const string AssaultRiflePrefabPath = "Assets/_Project/Prefabs/Enemies/Thief_AssaultRifle.prefab";
        public const string ShotgunPrefabPath = "Assets/_Project/Prefabs/Enemies/Thief_Shotgun.prefab";
        public const string PrefabPath = PistolPrefabPath;
        public const string PistolModelPath = "Assets/_Project/Models/Thieves/thief_pistol.fbx";
        public const string AssaultRifleModelPath = "Assets/_Project/Models/Thieves/thief_assault_rifle.fbx";
        public const string ShotgunModelPath = "Assets/_Project/Models/Thieves/thief_shotgun.fbx";
        public const string PistolControllerPath = "Assets/_Project/Animations/Thieves/Thief_Pistol.controller";
        public const string AssaultRifleControllerPath = "Assets/_Project/Animations/Thieves/Thief_AssaultRifle.controller";
        public const string ShotgunControllerPath = "Assets/_Project/Animations/Thieves/Thief_Shotgun.controller";
        private const string SpawnablesPath = "Assets/_Project/Settings/NetworkSpawnablePrefabs.asset";

        private readonly struct WeaponSpec
        {
            public readonly EnemyWeaponType Type;
            public readonly string Name;
            public readonly string Path;
            public readonly string ModelPath;
            public readonly string ControllerPath;
            public readonly float FireInterval;
            public readonly float Range;
            public readonly int Pellets;
            public readonly float Spread;
            public readonly float Damage;
            public readonly float Knockback;
            public readonly float PreferredRange;

            public WeaponSpec(
                EnemyWeaponType type, string name, string path, string modelPath, string controllerPath,
                float fireInterval, float range, int pellets, float spread,
                float damage, float knockback, float preferredRange)
            {
                Type = type;
                Name = name;
                Path = path;
                ModelPath = modelPath;
                ControllerPath = controllerPath;
                FireInterval = fireInterval;
                Range = range;
                Pellets = pellets;
                Spread = spread;
                Damage = damage;
                Knockback = knockback;
                PreferredRange = preferredRange;
            }
        }

        private static readonly WeaponSpec[] Specs =
        {
            new(EnemyWeaponType.Pistol, "Thief_Pistol", PistolPrefabPath,
                PistolModelPath, PistolControllerPath, 0.8f, 28f, 1, 2f, 20f, 5f, 5f),
            new(EnemyWeaponType.AssaultRifle, "Thief_AssaultRifle", AssaultRiflePrefabPath,
                AssaultRifleModelPath, AssaultRifleControllerPath, 0.12f, 34f, 1, 1.5f, 9f, 3.5f, 7f),
            new(EnemyWeaponType.Shotgun, "Thief_Shotgun", ShotgunPrefabPath,
                ShotgunModelPath, ShotgunControllerPath, 1.4f, 20f, 8, 7f, 8f, 9f, 3.5f)
        };

        [InitializeOnLoadMethod]
        private static void BuildMissingPrefabsAfterImport()
        {
            EditorApplication.delayCall += () => EnsureEnemyPrefabs();
        }

        [MenuItem("Couch Guys/Build Enemy Weapon Prefabs")]
        public static void BuildEnemyWeaponPrefabs()
        {
            Selection.objects = EnsureEnemyPrefabs();
        }

        [MenuItem("Couch Guys/Build Thief Prefab")]
        private static void BuildLegacyMenuAlias()
        {
            BuildEnemyWeaponPrefabs();
        }

        public static GameObject EnsureThiefPrefab()
        {
            return EnsureEnemyPrefabs()[0];
        }

        public static GameObject[] EnsureEnemyPrefabs()
        {
            EnsureFolder("Assets/_Project/Prefabs", "Enemies");
            ThiefAnimatorControllerBuilder.EnsureAll();
            GameObject[] result = new GameObject[Specs.Length];
            for (int index = 0; index < Specs.Length; index++)
            {
                result[index] = EnsureEnemyPrefab(Specs[index]);
            }

            AssetDatabase.SaveAssets();
            return result;
        }

        private static GameObject EnsureEnemyPrefab(WeaponSpec spec)
        {
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(spec.Path);
            if (IsValid(existing, spec.Type))
            {
                RegisterNetworkPrefab(existing.GetComponent<NetworkObject>());
                return existing;
            }

            GameObject root = new GameObject(spec.Name);
            try
            {
                NetworkObject networkObject = root.AddComponent<NetworkObject>();
                networkObject.SetAssetPathHash(CalculateAssetPathHash(spec.Path, spec.Name));
                NetworkTransform networkTransform = root.AddComponent<NetworkTransform>();
                NavMeshAgent agent = root.AddComponent<NavMeshAgent>();
                CapsuleCollider collider = root.AddComponent<CapsuleCollider>();
                ThiefNavigator navigator = root.AddComponent<ThiefNavigator>();
                EnemyWeapon weapon = root.AddComponent<EnemyWeapon>();
                ThiefAnimationDriver animationDriver = root.AddComponent<ThiefAnimationDriver>();

                agent.radius = 0.35f;
                agent.height = 1.8f;
                agent.baseOffset = 0f;
                agent.speed = 4.5f;
                agent.angularSpeed = 720f;
                agent.acceleration = 16f;
                agent.stoppingDistance = spec.PreferredRange;
                agent.autoBraking = true;

                collider.radius = 0.35f;
                collider.height = 1.8f;
                collider.center = new Vector3(0f, 0.9f, 0f);

                Animator animator = CreateAnimatedVisual(root.transform, spec, out Transform muzzle);

                ConfigureNetworkTransform(networkTransform);
                SetObjectReference(navigator, "m_agent", agent);
                SetObjectReference(animationDriver, "m_animator", animator);
                ConfigureWeapon(weapon, muzzle, spec);
                ConfigureNetworkBehaviours(networkObject, networkTransform, navigator, weapon);

                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, spec.Path);
                if (saved == null)
                {
                    throw new UnityException($"Failed to save enemy prefab at {spec.Path}.");
                }

                RegisterNetworkPrefab(saved.GetComponent<NetworkObject>());
                return saved;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static Animator CreateAnimatedVisual(
            Transform parent, WeaponSpec spec, out Transform muzzle)
        {
            GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(spec.ModelPath);
            if (modelAsset == null)
            {
                throw new UnityException($"Missing thief model at {spec.ModelPath}.");
            }

            GameObject model = PrefabUtility.InstantiatePrefab(modelAsset, parent) as GameObject;
            if (model == null)
            {
                throw new UnityException($"Failed to instantiate thief model at {spec.ModelPath}.");
            }

            model.name = "ThiefModel";
            model.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            model.transform.localScale = Vector3.one;
            FitVisualToHeight(model, 1.8f);

            Animator animator = model.GetComponentInChildren<Animator>(true);
            if (animator == null)
            {
                animator = model.AddComponent<Animator>();
            }
            animator.applyRootMotion = false;
            animator.runtimeAnimatorController =
                AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(spec.ControllerPath);
            if (animator.runtimeAnimatorController == null)
            {
                throw new UnityException($"Missing thief Animator Controller at {spec.ControllerPath}.");
            }

            muzzle = FindDescendant(model.transform, "Muzzle");
            if (muzzle == null)
            {
                throw new UnityException($"The thief model at {spec.ModelPath} has no Muzzle transform.");
            }
            return animator;
        }

        private static void FitVisualToHeight(GameObject model, float targetHeight)
        {
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                throw new UnityException($"The model {model.name} contains no renderers.");
            }

            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }
            if (bounds.size.y <= 0.001f)
            {
                throw new UnityException($"The model {model.name} has invalid vertical bounds.");
            }

            model.transform.localScale = Vector3.one * (targetHeight / bounds.size.y);
            bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }
            model.transform.localPosition += Vector3.up * (0.02f - bounds.min.y);
        }

        private static Transform FindDescendant(Transform root, string name)
        {
            foreach (Transform descendant in root.GetComponentsInChildren<Transform>(true))
            {
                if (descendant.name == name)
                {
                    return descendant;
                }
            }
            return null;
        }

        private static void ConfigureWeapon(EnemyWeapon weapon, Transform muzzle, WeaponSpec spec)
        {
            SerializedObject serialised = new SerializedObject(weapon);
            serialised.FindProperty("m_weaponType").enumValueIndex = (int)spec.Type;
            serialised.FindProperty("m_muzzle").objectReferenceValue = muzzle;
            serialised.FindProperty("m_fireInterval").floatValue = spec.FireInterval;
            serialised.FindProperty("m_range").floatValue = spec.Range;
            serialised.FindProperty("m_pelletsPerShot").intValue = spec.Pellets;
            serialised.FindProperty("m_spreadAngle").floatValue = spec.Spread;
            serialised.FindProperty("m_damagePerPellet").floatValue = spec.Damage;
            serialised.FindProperty("m_knockbackForce").floatValue = spec.Knockback;
            serialised.FindProperty("m_preferredRange").floatValue = spec.PreferredRange;
            serialised.FindProperty("m_tracerDuration").floatValue = 0.16f;
            serialised.FindProperty("m_tracerWidth").floatValue = 0.035f;
            serialised.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureNetworkTransform(NetworkTransform networkTransform)
        {
            SerializedObject serialised = new SerializedObject(networkTransform);
            serialised.FindProperty("_componentConfiguration").enumValueIndex =
                (int)NetworkTransform.ComponentConfigurationType.Disabled;
            serialised.FindProperty("_clientAuthoritative").boolValue = false;
            serialised.FindProperty("_synchronizePosition").boolValue = true;
            serialised.FindProperty("_synchronizeRotation").boolValue = true;
            serialised.FindProperty("_synchronizeScale").boolValue = false;
            serialised.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureNetworkBehaviours(
            NetworkObject networkObject, NetworkTransform networkTransform,
            ThiefNavigator navigator, EnemyWeapon weapon)
        {
            NetworkBehaviour[] behaviours = { networkTransform, navigator, weapon };
            SerializedObject serialisedNetworkObject = new SerializedObject(networkObject);
            SerializedProperty property = serialisedNetworkObject.FindProperty("NetworkBehaviours");
            property.arraySize = behaviours.Length;
            for (int index = 0; index < behaviours.Length; index++)
            {
                property.GetArrayElementAtIndex(index).objectReferenceValue = behaviours[index];
                SetNetworkBehaviourReferences(behaviours[index], networkObject, index);
            }

            serialisedNetworkObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetNetworkBehaviourReferences(
            NetworkBehaviour behaviour, NetworkObject networkObject, int componentIndex)
        {
            SerializedObject serialised = new SerializedObject(behaviour);
            serialised.FindProperty("_componentIndexCache").intValue = componentIndex;
            serialised.FindProperty("_addedNetworkObject").objectReferenceValue = networkObject;
            serialised.FindProperty("_networkObjectCache").objectReferenceValue = networkObject;
            serialised.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetObjectReference(Object target, string propertyName, Object value)
        {
            SerializedObject serialised = new SerializedObject(target);
            serialised.FindProperty(propertyName).objectReferenceValue = value;
            serialised.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void RegisterNetworkPrefab(NetworkObject networkObject)
        {
            SinglePrefabObjects spawnables = AssetDatabase.LoadAssetAtPath<SinglePrefabObjects>(SpawnablesPath);
            if (spawnables == null || networkObject == null)
            {
                return;
            }

            spawnables.AddObject(networkObject, true, false);
            EditorUtility.SetDirty(spawnables);
        }

        private static ulong CalculateAssetPathHash(string assetPath, string objectName)
        {
            string pathAndName = $"{assetPath}{objectName}".Trim().ToLowerInvariant();
            StringBuilder builder = new StringBuilder(pathAndName.Length);
            foreach (char character in pathAndName)
            {
                if ((character >= 'a' && character <= 'z') ||
                    (character >= '0' && character <= '9'))
                {
                    builder.Append(character);
                }
            }

            return builder.ToString().GetStableHashU64();
        }

        private static bool IsValid(GameObject prefab, EnemyWeaponType type)
        {
            return prefab != null && prefab.GetComponent<NetworkObject>() != null &&
                   prefab.GetComponent<NetworkObject>().AssetPathHash != 0 &&
                   prefab.GetComponent<NetworkTransform>() != null &&
                   prefab.GetComponent<NavMeshAgent>() != null &&
                   prefab.GetComponent<CapsuleCollider>() != null &&
                   prefab.GetComponent<ThiefNavigator>() != null &&
                   prefab.GetComponent<ThiefAnimationDriver>() != null &&
                   prefab.GetComponentInChildren<Animator>(true)?.runtimeAnimatorController != null &&
                   FindDescendant(prefab.transform, "Muzzle") != null &&
                   prefab.TryGetComponent(out EnemyWeapon weapon) && weapon.WeaponType == type;
        }

        private static void EnsureFolder(string parent, string child)
        {
            string path = $"{parent}/{child}";
            if (!AssetDatabase.IsValidFolder(path))
            {
                AssetDatabase.CreateFolder(parent, child);
            }
        }
    }
}
