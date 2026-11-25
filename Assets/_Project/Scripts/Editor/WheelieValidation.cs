using System;
using System.IO;
using System.Linq;
using CouchGuys.Gameplay.Enemies;
using CouchGuys.Gameplay.Weapons;
using CouchGuys.Networking;
using CouchGuys.Player;
using CouchGuys.Testing;
using FishNet.Component.Transforming;
using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Object;
using FishNet.Transporting.Tugboat;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CouchGuys.Editor
{
    public static class WheelieValidation
    {
        private const string Folder = "Assets/_Project/Tests/Wheelie";
        [MenuItem("Couch Guys/Wheelie/Preserve Scene Snapshot %#&s")]
        public static void PreserveSceneSnapshot()
        {
            var scene = SceneManager.GetActiveScene();
            string snapshot = "Temp/WheeliePreservedScene.unity";
            string original = scene.path;
            if (!EditorSceneManager.SaveScene(scene, snapshot, false)) throw new InvalidOperationException("Scene snapshot failed.");
            File.WriteAllText("Temp/WheelieSceneComparison.txt", "Original left unchanged: " + original + "\nPreserved open scene: " + snapshot);
        }
        [MenuItem("Couch Guys/Wheelie/Validate Saved Assets %#&v")]
        public static void ValidateAssets()
        {
            try
            {
                var settings = AssetDatabase.LoadAssetAtPath<WheelieSettings>(WheelieAuthoring.SettingsPath);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WheelieAuthoring.PrefabPath);
                Require(settings != null && prefab != null && settings.SquadSize == 4, "Saved settings/prefab and fixed squad size.");
                Require(prefab.GetComponent<WheelieController>().Settings == settings && settings.Prefab == prefab.GetComponent<NetworkObject>(), "Central configuration references.");
                Require(prefab.GetComponent<PlayerCouchCarrier>() == null && prefab.GetComponent<PlayerTruckInteractor>() == null &&
                    prefab.GetComponent<PlayerHoverboardInteractor>() == null && prefab.GetComponent<DebugCouchBotController>() == null &&
                    prefab.GetComponent<ThirdPersonPlayerController>() == null, "No carrier, delivery, player movement, or vehicle behaviours.");
                foreach (Object component in new Object[] { prefab.GetComponent<PlayerHealth>(), prefab.GetComponent<PlayerWeaponController>() })
                    Require(new SerializedObject(component).FindProperty("m_wheelieSettings").objectReferenceValue == settings, "Health/weapon share configuration.");
                var gun = new SerializedObject(prefab.GetComponent<PlayerWeaponController>());
                Require(gun.FindProperty("m_weapons").arraySize == 0 && settings.GetWeapon(0).BulletPrefab != null,
                    "Weapon balance/effects are centralized in the shared asset, not duplicated on each prefab.");
                for (int i = 0; i < 3; i++) Require(gun.FindProperty("m_weaponModels").GetArrayElementAtIndex(i).objectReferenceValue != null &&
                    gun.FindProperty("m_muzzles").GetArrayElementAtIndex(i).objectReferenceValue != null, "Weapon and muzzle references.");
                Require(prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).Any(r => r.name == "FaceDisplay" && r.sharedMesh.uv.Length > 0), "Separate UV screen.");
                Require(!new SerializedObject(prefab.GetComponent<NetworkTransform>()).FindProperty("_clientAuthoritative").boolValue, "Server movement authority.");
                var registry = AssetDatabase.LoadAssetAtPath<SinglePrefabObjects>("Assets/_Project/Settings/NetworkSpawnablePrefabs.asset");
                int count = 0; for (int i = 0; i < registry.GetObjectCount(); i++) if (registry.GetObject(true, i) == settings.Prefab) count++;
                Require(count == 1, "One network registration.");
                var player = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Player.prefab");
                Require(new SerializedObject(player.GetComponent<NetworkPlayerOwnership>()).FindProperty("m_wheelieSettings").objectReferenceValue == settings, "Debug reference configured.");
                Require(player.GetComponent<DebugCouchBotController>() != null &&
                    new SerializedObject(player.GetComponent<PlayerHealth>()).FindProperty("m_wheelieSettings").objectReferenceValue == null,
                    "Existing player and AI settings unchanged.");
                var copy = Object.Instantiate(settings);
                try
                {
                    copy.StartingWeapons = new[] { new WheelieSettings.StartingWeapon { Weapon = PlayerWeaponType.Pistol, Probability = 1 },
                        new WheelieSettings.StartingWeapon { Weapon = PlayerWeaponType.AssaultRifle, Probability = 2 },
                        new WheelieSettings.StartingWeapon { Weapon = PlayerWeaponType.Shotgun, Probability = 1 } };
                    Require(copy.TrySelectWeapon(.1f, out var a) && a == PlayerWeaponType.Pistol &&
                        copy.TrySelectWeapon(.5f, out var b) && b == PlayerWeaponType.AssaultRifle &&
                        copy.TrySelectWeapon(.9f, out var c) && c == PlayerWeaponType.Shotgun, "Weighted selection intervals.");
                    copy.StartingWeapons = new[] { new WheelieSettings.StartingWeapon { Weapon = PlayerWeaponType.RocketLauncher, Probability = 1 } };
                    Require(!copy.TrySelectWeapon(.5f, out _), "Invalid starting weapons cannot spawn.");
                }
                finally { Object.DestroyImmediate(copy); }
                File.WriteAllText("Temp/WheelieAssetValidation.txt", "PASS: saved references, restrictions, weapon models, face UVs, server NetworkTransform, registration, isolated settings, weighted selection and invalid-weapon rejection.\n");
            }
            catch (Exception e) { File.WriteAllText("Temp/WheelieAssetValidation.txt", e.ToString()); Debug.LogException(e); }
        }

        [MenuItem("Couch Guys/Wheelie/Build Multiplayer Validation %#&b")]
        public static void BuildValidation()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (File.Exists(Folder + "/WheelieValidation.unity")) { BuildSavedValidation(); return; }
            var original = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                Directory.CreateDirectory(Folder); AssetDatabase.Refresh();
                // Test fixture only: uses the actual damage receiver with inert hostile behaviour.
                var enemySettings = ScriptableObject.CreateInstance<ThiefSettings>(); enemySettings.Health.MaxHealth = 100000f;
                enemySettings.Health.WeaponDropChance = 0f; enemySettings.name = "WheelieValidationHostile";
                string configPath = Folder + "/Hostile.asset";
                var existing = AssetDatabase.LoadAssetAtPath<ThiefSettings>(configPath);
                if (existing == null) AssetDatabase.CreateAsset(enemySettings, configPath);
                else { Object.DestroyImmediate(enemySettings); enemySettings = existing; }
                var dummy = new GameObject("WheelieValidationHostile"); dummy.AddComponent<NetworkObject>();
                var collider = dummy.AddComponent<CapsuleCollider>(); collider.center = Vector3.up * .9f; collider.height = 1.8f; collider.radius = .4f;
                var health = dummy.AddComponent<ThiefHealth>(); var data = new SerializedObject(health);
                data.FindProperty("m_settings").objectReferenceValue = enemySettings;
                data.FindProperty("m_collider").objectReferenceValue = collider; data.ApplyModifiedPropertiesWithoutUndo();
                var targetPrefab = PrefabUtility.SaveAsPrefabAsset(dummy, "Assets/_Project/Prefabs/AI/WheelieValidationHostile.prefab"); Object.DestroyImmediate(dummy);

                var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "Validation ground";
                floor.transform.position = new Vector3(10f, -.25f, 10f); floor.transform.localScale = new Vector3(100f, .5f, 100f);
                var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name = "Navigation obstacle";
                wall.transform.position = new Vector3(0f, 1f, 4.5f); wall.transform.localScale = new Vector3(4f, 2f, .8f);
                var surface = new GameObject("Validation navigation").AddComponent<NavMeshSurface>();
                surface.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders; surface.BuildNavMesh();
                string navPath = Folder + "/Navigation.asset";
                if (AssetDatabase.LoadAssetAtPath<Object>(navPath) == null) AssetDatabase.CreateAsset(surface.navMeshData, navPath);
                else surface.navMeshData = AssetDatabase.LoadAssetAtPath<UnityEngine.AI.NavMeshData>(navPath);

                var networkRoot = new GameObject("Wheelie validation network"); var transport = networkRoot.AddComponent<Tugboat>(); transport.SetPort(18473);
                var network = networkRoot.AddComponent<NetworkManager>();
                var registry = ScriptableObject.CreateInstance<SinglePrefabObjects>();
                var registryData = new SerializedObject(registry); var prefabs = registryData.FindProperty("_prefabs"); prefabs.arraySize = 3;
                var player = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Player.prefab").GetComponent<NetworkObject>();
                var wheelie = AssetDatabase.LoadAssetAtPath<GameObject>(WheelieAuthoring.PrefabPath).GetComponent<NetworkObject>();
                prefabs.GetArrayElementAtIndex(0).objectReferenceValue = player;
                prefabs.GetArrayElementAtIndex(1).objectReferenceValue = wheelie;
                prefabs.GetArrayElementAtIndex(2).objectReferenceValue = targetPrefab.GetComponent<NetworkObject>();
                registryData.ApplyModifiedPropertiesWithoutUndo();
                string registryPath = Folder + "/Spawnables.asset";
                var oldRegistry = AssetDatabase.LoadAssetAtPath<SinglePrefabObjects>(registryPath);
                if (oldRegistry == null) AssetDatabase.CreateAsset(registry, registryPath);
                else { EditorUtility.CopySerialized(registry, oldRegistry); Object.DestroyImmediate(registry); registry = oldRegistry; }
                network.SpawnablePrefabs = registry;
                var probe = new GameObject("Wheelie Validation Probe").AddComponent<WheelieValidationProbe>();
                var probeData = new SerializedObject(probe);
                probeData.FindProperty("m_network").objectReferenceValue = network;
                probeData.FindProperty("m_playerPrefab").objectReferenceValue = player;
                probeData.FindProperty("m_hostilePrefab").objectReferenceValue = targetPrefab.GetComponent<NetworkObject>();
                probeData.FindProperty("m_settings").objectReferenceValue = AssetDatabase.LoadAssetAtPath<WheelieSettings>(WheelieAuthoring.SettingsPath);
                probeData.ApplyModifiedPropertiesWithoutUndo();
                var light = new GameObject("Validation light").AddComponent<Light>(); light.type = LightType.Directional; light.transform.rotation = Quaternion.Euler(45f, -25f, 0f);
                var camera = new GameObject("Validation camera").AddComponent<Camera>(); camera.transform.position = new Vector3(0f, 16f, -10f); camera.transform.rotation = Quaternion.Euler(45f, 0f, 0f); camera.gameObject.AddComponent<AudioListener>();
                string scenePath = Folder + "/WheelieValidation.unity"; EditorSceneManager.SaveScene(scene, scenePath);
                AssetDatabase.SaveAssets();
                Directory.CreateDirectory("Temp/WheelieValidationBuild");
                var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { scenePath },
                    locationPathName = "Temp/WheelieValidationBuild/WheelieValidation.exe", target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.Development });
                File.WriteAllText("Temp/WheelieValidationBuild.txt", result.summary.result + " errors=" + result.summary.totalErrors + "\n");
            }
            catch (Exception e) { File.WriteAllText("Temp/WheelieValidationBuild.txt", e.ToString()); Debug.LogException(e); }
            finally { SceneManager.SetActiveScene(original); EditorSceneManager.CloseScene(scene, true); }
        }
        private static void Require(bool condition, string detail) { if (!condition) throw new InvalidOperationException(detail); }

        private static void BuildSavedValidation()
        {
            Directory.CreateDirectory("Temp/WheelieValidationBuild");
            var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { Folder + "/WheelieValidation.unity" },
                locationPathName = "Temp/WheelieValidationBuild/WheelieValidation.exe", target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development });
            File.WriteAllText("Temp/WheelieValidationBuild.txt", result.summary.result + " errors=" + result.summary.totalErrors + "\n");
        }
    }
}
