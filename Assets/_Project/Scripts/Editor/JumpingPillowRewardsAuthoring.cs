using System;
using System.IO;
using System.Linq;
using CouchGuys.Player;
using CouchGuys.ProceduralGeneration;
using CouchGuys.Testing;
using FishNet.Managing.Object;
using FishNet.Object;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CouchGuys.Editor
{
    public static class JumpingPillowRewardsAuthoring
    {
        public const string CubePath = "Assets/_Project/Prefabs/AI/WheelieRewardCube.prefab";
        [MenuItem("Couch Guys/Wheelie/Configure Pillow Rewards %#F8")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            try
            {
                WheelieAuthoring.CreateAssets();
                var settings = AssetDatabase.LoadAssetAtPath<WheelieSettings>(WheelieAuthoring.SettingsPath);
                var cube = AssetDatabase.LoadAssetAtPath<GameObject>(CubePath);
                if (cube == null)
                {
                    var root = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    try
                    {
                        root.name = "WheelieRewardCube"; root.transform.localScale = Vector3.one * .65f;
                        root.GetComponent<MeshRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                            "Assets/_Project/Models/Wheelie/Materials/Wheelie_Cyan.mat");
                        root.GetComponent<BoxCollider>().isTrigger = true;
                        var body = root.AddComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false;
                        root.AddComponent<NetworkObject>();
                        var reward = root.AddComponent<WheelieSquadReward>();
                        var data = new SerializedObject(reward); data.FindProperty("m_settings").objectReferenceValue = settings;
                        data.ApplyModifiedPropertiesWithoutUndo();
                        cube = PrefabUtility.SaveAsPrefabAsset(root, CubePath);
                    }
                    finally { Object.DestroyImmediate(root); }
                }
                var pillow = PrefabUtility.LoadPrefabContents(JumpingPillowAuthoring.PrefabPath);
                try
                {
                    var rewards = pillow.GetComponent<JumpingPillowRewards>();
                    if (rewards == null) rewards = pillow.AddComponent<JumpingPillowRewards>();
                    var data = new SerializedObject(rewards);
                    if (data.FindProperty("m_cubePrefab").objectReferenceValue == null)
                    {
                        data.FindProperty("m_cubePrefab").objectReferenceValue = cube.GetComponent<WheelieSquadReward>();
                        data.ApplyModifiedPropertiesWithoutUndo();
                    }
                    PrefabUtility.SaveAsPrefabAsset(pillow, JumpingPillowAuthoring.PrefabPath);
                }
                finally { PrefabUtility.UnloadPrefabContents(pillow); }
                Register("Assets/_Project/Settings/NetworkSpawnablePrefabs.asset", cube.GetComponent<NetworkObject>());
                ConfigureValidation(cube.GetComponent<WheelieSquadReward>());
                AssetDatabase.SaveAssets();
                Validate();
                File.WriteAllText("Temp/WheeliePillowAuthoring.txt", "PASS: saved cube trigger, shared Wheelie configuration, pillow rarity settings, network registration and test scene configured.\n");
            }
            catch (Exception e) { File.WriteAllText("Temp/WheeliePillowAuthoring.txt", e.ToString()); Debug.LogException(e); }
        }

        private static void ConfigureValidation(WheelieSquadReward cube)
        {
            const string folder = "Assets/_Project/Tests/Wheelie";
            string path = folder + "/WheelieValidation.unity";
            if (!File.Exists(path)) return;
            Scene original = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                var probe = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<WheelieValidationProbe>(true)).Single();
                var pillow = scene.GetRootGameObjects().Select(g => g.GetComponent<JumpingPillowRewards>()).FirstOrDefault(p => p != null);
                if (pillow == null)
                {
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(JumpingPillowAuthoring.PrefabPath), scene);
                    instance.name = "Reward Validation Pillow"; instance.transform.position = new Vector3(-15f, 0f, 0f);
                    pillow = instance.GetComponent<JumpingPillowRewards>();
                    var data = new SerializedObject(pillow);
                    data.FindProperty("m_spawnChancePercent").floatValue = 100f;
                    data.FindProperty("m_highJumpChanceMultiplier").floatValue = 1f;
                    data.FindProperty("m_horizontalSpawnRadius").floatValue = .6f;
                    data.ApplyModifiedPropertiesWithoutUndo();
                }
                var probeData = new SerializedObject(probe);
                probeData.FindProperty("m_rewardPillow").objectReferenceValue = pillow;
                probeData.ApplyModifiedPropertiesWithoutUndo();
                Register(folder + "/Spawnables.asset", cube.GetComponent<NetworkObject>());
                var surface = scene.GetRootGameObjects().Select(g => g.GetComponent<NavMeshSurface>()).First(s => s != null);
                string navigationPath = folder + "/NavigationWithRewards.asset";
                var navigation = AssetDatabase.LoadAssetAtPath<UnityEngine.AI.NavMeshData>(navigationPath);
                if (navigation == null) { surface.BuildNavMesh(); navigation = surface.navMeshData; AssetDatabase.CreateAsset(navigation, navigationPath); }
                surface.navMeshData = navigation;
                EditorSceneManager.SaveScene(scene);
            }
            finally { SceneManager.SetActiveScene(original); EditorSceneManager.CloseScene(scene, true); }
        }

        private static void Register(string path, NetworkObject prefab)
        {
            var asset = AssetDatabase.LoadAssetAtPath<SinglePrefabObjects>(path);
            var data = new SerializedObject(asset); var list = data.FindProperty("_prefabs");
            for (int i = 0; i < list.arraySize; i++) if (list.GetArrayElementAtIndex(i).objectReferenceValue == prefab) return;
            int index = list.arraySize++; list.GetArrayElementAtIndex(index).objectReferenceValue = prefab;
            data.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(asset);
        }

        [MenuItem("Couch Guys/Wheelie/Validate Pillow Rewards %#F9")]
        public static void Validate()
        {
            var cube = AssetDatabase.LoadAssetAtPath<GameObject>(CubePath);
            var pillow = AssetDatabase.LoadAssetAtPath<GameObject>(JumpingPillowAuthoring.PrefabPath).GetComponent<JumpingPillowRewards>();
            if (cube == null || pillow == null || !cube.GetComponent<BoxCollider>().isTrigger ||
                !cube.GetComponent<Rigidbody>().isKinematic || cube.GetComponent<Rigidbody>().useGravity ||
                pillow.CubePrefab != cube.GetComponent<WheelieSquadReward>() || cube.GetComponent<WheelieSquadReward>().Settings == null)
                throw new InvalidOperationException("Saved pillow/cube references or trigger physics are invalid.");
            if (!Mathf.Approximately(pillow.ChanceForHeight(.5f), 0f) ||
                pillow.ChanceForHeight(4f) < pillow.ChanceForHeight(1.25f) || pillow.ChanceForHeight(100f) > 100f)
                throw new InvalidOperationException("Height-dependent probability is invalid.");
            foreach (string path in new[] { "Assets/_Project/Settings/NetworkSpawnablePrefabs.asset", "Assets/_Project/Tests/Wheelie/Spawnables.asset" })
            {
                var registry = AssetDatabase.LoadAssetAtPath<SinglePrefabObjects>(path);
                if (registry == null) continue;
                int count = 0;
                for (int i = 0; i < registry.GetObjectCount(); i++) if (registry.GetObject(true, i) == cube.GetComponent<NetworkObject>()) count++;
                if (count != 1) throw new InvalidOperationException("Cube must have exactly one network registration: " + path);
            }
            WheelieValidation.ValidateAssets();
            File.WriteAllText("Temp/WheeliePillowAssetValidation.txt", "PASS: trigger physics, saved references, probability threshold/bonus/clamp, and centralized Wheelie weapon configuration.\n");
        }
    }
}
