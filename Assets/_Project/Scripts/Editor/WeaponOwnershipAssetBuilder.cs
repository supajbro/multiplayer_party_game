using System;
using System.Linq;
using CouchGuys.Gameplay.Shop;
using CouchGuys.Gameplay.Weapons;
using FishNet.Component.Transforming;
using FishNet.Managing.Object;
using FishNet.Object;
using GameKit.Dependencies.Utilities;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace CouchGuys.Editor
{
    /// <summary>Installs pickup assets and an upper-body layer without replacing the player visual.</summary>
    public static class WeaponOwnershipAssetBuilder
    {
        private const string PickupFolder = "Assets/_Project/Resources/WeaponPickups";
        private const string PunchModel = "Assets/_Project/Models/CouchGuy/CouchGuy_WithPunches.fbx";

        [InitializeOnLoadMethod]
        private static void InstallMissing()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(CouchGuyAnimatorControllerBuilder.ControllerPath);
                if (AssetDatabase.LoadAssetAtPath<GameObject>($"{PickupFolder}/Pistol.prefab") == null ||
                    controller != null && !controller.layers.Any(layer => layer.name == "Punches")) Install();
            };
        }

        [MenuItem("Couch Guys/Weapons/Install Ownership and Punches")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            EnsureFolder(PickupFolder);
            var spawnables = AssetDatabase.LoadAssetAtPath<SinglePrefabObjects>(
                "Assets/_Project/Settings/NetworkSpawnablePrefabs.asset");
            if (spawnables == null) throw new UnityException("Network spawnable collection is missing.");
            GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Player.prefab");
            foreach (PlayerWeaponType type in Enum.GetValues(typeof(PlayerWeaponType)))
            {
                string path = $"{PickupFolder}/{type}.prefab";
                GameObject prefab = BuildPickup(type, path, player);
                NetworkObject no = prefab.GetComponent<NetworkObject>();
                if (!spawnables.Prefabs.Contains(no)) spawnables.AddObject(no, false, false);
                string stableName = new(($"{path}{prefab.name}".ToLowerInvariant()).Where(char.IsLetterOrDigit).ToArray());
                no.SetAssetPathHash(stableName.GetStableHashU64());
                EditorUtility.SetDirty(no);
            }
            EditorUtility.SetDirty(spawnables);
            InstallShop();
            InstallPunchLayer();
            AssetDatabase.SaveAssets();
            Debug.Log("Weapon ownership installed: registered pickups, shop weapons, masked punch animations.");
        }

        private static GameObject BuildPickup(PlayerWeaponType type, string path, GameObject player)
        {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(
                type == PlayerWeaponType.GrappleGun
                    ? "Assets/_Project/Resources/Weapons/GrappleGunModel.prefab"
                    : $"Assets/_Project/Models/CouchGuy/Weapons/{type}.fbx");
            if (model == null) throw new UnityException($"Missing exported weapon {type}.");
            var root = new GameObject(type.ToString());
            try
            {
                NetworkObject no = root.AddComponent<NetworkObject>();
                Rigidbody rb = root.AddComponent<Rigidbody>();
                rb.mass = 1.5f;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                NetworkTransform nt = root.AddComponent<NetworkTransform>();
                WeaponPickup pickup = root.AddComponent<WeaponPickup>();
                SetInt(pickup, "m_weaponType", (int)type);
                GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
                visual.name = "Visual";
                Renderer[] renderers = visual.GetComponentsInChildren<Renderer>();
                float visualScale = 1f;
                // Reuse the equipped renderer's exact Unity materials (including toon settings).
                foreach (Renderer renderer in renderers)
                {
                    string sourceName = renderer.name;
                    Renderer original = player.GetComponentsInChildren<Renderer>(true)
                        .FirstOrDefault(candidate => candidate.name == sourceName);
                    if (original != null)
                    {
                        renderer.sharedMaterials = original.sharedMaterials;
                        float sourceSize = Vector3.Scale(original.localBounds.size, original.transform.lossyScale).magnitude;
                        float pickupSize = Vector3.Scale(renderer.localBounds.size, renderer.transform.lossyScale).magnitude;
                        if (pickupSize > 0.0001f) visualScale = sourceSize / pickupSize;
                    }
                }
                visual.transform.localScale *= visualScale;
                Bounds bounds = renderers[0].bounds;
                foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
                visual.transform.localPosition -= bounds.center;
                BoxCollider collider = root.AddComponent<BoxCollider>();
                collider.size = Vector3.Max(bounds.size, Vector3.one * 0.12f);
                var transformSettings = new SerializedObject(nt);
                transformSettings.FindProperty("_clientAuthoritative").boolValue = false;
                transformSettings.FindProperty("_componentConfiguration").enumValueIndex =
                    (int)NetworkTransform.ComponentConfigurationType.Rigidbody;
                transformSettings.FindProperty("_synchronizeScale").boolValue = false;
                transformSettings.ApplyModifiedPropertiesWithoutUndo();
                var network = new SerializedObject(no);
                var behaviours = network.FindProperty("NetworkBehaviours");
                behaviours.arraySize = 2;
                behaviours.GetArrayElementAtIndex(0).objectReferenceValue = nt;
                behaviours.GetArrayElementAtIndex(1).objectReferenceValue = pickup;
                network.ApplyModifiedPropertiesWithoutUndo();
                ConfigureBehaviour(nt, no, 0);
                ConfigureBehaviour(pickup, no, 1);
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static void InstallShop()
        {
            ShopCatalogue catalogue = ShopAssetBuilder.EnsureDefaultCatalogue();
            var so = new SerializedObject(catalogue);
            SerializedProperty items = so.FindProperty("m_items");
            foreach (PlayerWeaponType type in Enum.GetValues(typeof(PlayerWeaponType)))
            {
                string id = $"weapon_{type.ToString().ToLowerInvariant()}";
                if (catalogue.Find(id) != null) continue;
                string folder = "Assets/_Project/Settings/Shop";
                var behaviour = ScriptableObject.CreateInstance<WeaponPurchaseBehaviour>();
                SetInt(behaviour, "m_weaponType", (int)type);
                AssetDatabase.CreateAsset(behaviour, $"{folder}/{id}_purchase.asset");
                var item = ScriptableObject.CreateInstance<ShopItemDefinition>();
                var data = new SerializedObject(item);
                data.FindProperty("m_itemId").stringValue = id;
                data.FindProperty("m_displayName").stringValue = type == PlayerWeaponType.GrappleGun ? "Grapple Gun" :
                    type == PlayerWeaponType.AssaultRifle ? "Assault Rifle" : type.ToString();
                data.FindProperty("m_description").stringValue = "Equips this weapon. Your previous weapon drops nearby.";
                data.FindProperty("m_price").intValue = type == PlayerWeaponType.Pistol ? 100 : type == PlayerWeaponType.AssaultRifle ? 300 : 250;
                data.FindProperty("m_category").enumValueIndex = (int)ShopItemCategory.Weapon;
                data.FindProperty("m_purchaseBehaviour").objectReferenceValue = behaviour;
                data.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.CreateAsset(item, $"{folder}/{id}.asset");
                int index = items.arraySize++;
                items.GetArrayElementAtIndex(index).objectReferenceValue = item;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalogue);
        }

        public static void InstallPunchLayer()
        {
            if (AssetImporter.GetAtPath(PunchModel) is not ModelImporter importer) return;
            if (importer.animationType != ModelImporterAnimationType.Generic || !importer.importAnimation)
            {
                importer.animationType = ModelImporterAnimationType.Generic;
                importer.importAnimation = true;
                importer.SaveAndReimport();
            }
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(CouchGuyAnimatorControllerBuilder.ControllerPath);
            if (controller == null) return;
            AnimatorControllerLayer existing = controller.layers.FirstOrDefault(layer => layer.name == "Punches");
            AnimationClip[] clips = AssetDatabase.LoadAllAssetsAtPath(PunchModel).OfType<AnimationClip>()
                .Where(clip => !clip.name.StartsWith("__preview__") && (clip.name.EndsWith("Punch_Left") || clip.name.EndsWith("Punch_Right"))).ToArray();
            if (clips.Length != 2) throw new UnityException("Both authored punch clips must be imported.");
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(CouchGuyAnimatorControllerBuilder.ModelPath);
            var mask = existing?.avatarMask ?? new AvatarMask { name = "Punch Upper Body" };
            mask.transformCount = 0;
            mask.AddTransformPath(model.transform, true);
            for (int i = 0; i < mask.transformCount; i++)
            {
                string bone = mask.GetTransformPath(i).Split('/').Last();
                mask.SetTransformActive(i, bone.StartsWith("UpperArm_") ||
                    bone.StartsWith("LowerArm_") || bone.StartsWith("Hand_"));
            }
            var machine = existing?.stateMachine ?? new AnimatorStateMachine { name = "Punches" };
            if (existing == null)
            {
                AssetDatabase.AddObjectToAsset(mask, controller);
                AssetDatabase.AddObjectToAsset(machine, controller);
                machine.AddState("Rest");
            }
            foreach (AnimationClip clip in clips)
            {
                string name = clip.name[(clip.name.LastIndexOf('|') + 1)..];
                AnimatorState state = machine.states.FirstOrDefault(child => child.state.name == name).state ?? machine.AddState(name);
                state.motion = clip;
                state.writeDefaultValues = false;
            }
            if (existing == null)
                controller.AddLayer(new AnimatorControllerLayer { name = "Punches", stateMachine = machine,
                    avatarMask = mask, defaultWeight = 0f, blendingMode = AnimatorLayerBlendingMode.Override });
            EditorUtility.SetDirty(mask);
            EditorUtility.SetDirty(controller);
        }

        private static void SetInt(UnityEngine.Object target, string property, int value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(property).intValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        private static void ConfigureBehaviour(NetworkBehaviour behaviour, NetworkObject no, int index)
        {
            var so = new SerializedObject(behaviour);
            so.FindProperty("_componentIndexCache").intValue = index;
            so.FindProperty("_addedNetworkObject").objectReferenceValue = no;
            so.FindProperty("_networkObjectCache").objectReferenceValue = no;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            EnsureFolder(path[..slash]);
            AssetDatabase.CreateFolder(path[..slash], path[(slash + 1)..]);
        }
    }
}
