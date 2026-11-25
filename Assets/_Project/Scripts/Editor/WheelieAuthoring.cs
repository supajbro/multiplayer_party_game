using System;
using System.IO;
using System.Linq;
using CouchGuys.Gameplay.Weapons;
using CouchGuys.Networking;
using CouchGuys.Player;
using FishNet.Component.Transforming;
using FishNet.Managing.Object;
using FishNet.Object;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace CouchGuys.Editor
{
    /// <summary>Explicit authoring only. Never generates or overwrites assets on reload/import/play.</summary>
    public static class WheelieAuthoring
    {
        public const string SettingsPath = "Assets/_Project/Settings/AI/Wheelie.asset";
        public const string PrefabPath = "Assets/_Project/Prefabs/AI/Wheelie.prefab";
        private const string PlayerPath = "Assets/_Project/Prefabs/Player.prefab";

        [MenuItem("Couch Guys/Wheelie/Create Saved Assets %#&w")]
        public static void CreateAssets()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode before authoring Wheelie.");
            try
            {
                Directory.CreateDirectory("Assets/_Project/Settings/AI"); Directory.CreateDirectory("Assets/_Project/Prefabs/AI");
                AssetDatabase.Refresh();
                var settings = AssetDatabase.LoadAssetAtPath<WheelieSettings>(SettingsPath);
                if (settings == null)
                { settings = ScriptableObject.CreateInstance<WheelieSettings>(); settings.name = "Wheelie"; AssetDatabase.CreateAsset(settings, SettingsPath); }
                var settingsData = new SerializedObject(settings);
                if (!settings.HasConfiguredWeaponDefinitions)
                {
                    var source = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath).GetComponent<PlayerWeaponController>();
                    settingsData.CopyFromSerializedProperty(new SerializedObject(source).FindProperty("m_weapons"));
                    settingsData.FindProperty("m_weaponDefinitionsConfigured").boolValue = true;
                    settingsData.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(settings);
                }
                // Existing Inspector edits are preserved; this command fills only missing assets/references.
                var saved = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                if (saved == null) saved = BuildPrefab(settings);
                var contents = PrefabUtility.LoadPrefabContents(PrefabPath);
                try
                {
                    var gunData = new SerializedObject(contents.GetComponent<PlayerWeaponController>());
                    if (gunData.FindProperty("m_weapons").arraySize > 0)
                    {
                        gunData.FindProperty("m_weapons").arraySize = 0;
                        gunData.ApplyModifiedPropertiesWithoutUndo(); PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
                    }
                }
                finally { PrefabUtility.UnloadPrefabContents(contents); }
                if (settings.Prefab == null) { settings.Prefab = saved.GetComponent<NetworkObject>(); EditorUtility.SetDirty(settings); }
                Register(saved.GetComponent<NetworkObject>());
                var player = PrefabUtility.LoadPrefabContents(PlayerPath);
                try
                {
                    var ownership = new SerializedObject(player.GetComponent<NetworkPlayerOwnership>());
                    if (ownership.FindProperty("m_wheelieSettings").objectReferenceValue == null)
                    {
                        ownership.FindProperty("m_wheelieSettings").objectReferenceValue = settings;
                        ownership.ApplyModifiedPropertiesWithoutUndo(); PrefabUtility.SaveAsPrefabAsset(player, PlayerPath);
                    }
                }
                finally { PrefabUtility.UnloadPrefabContents(player); }
                AssetDatabase.SaveAssets();
                File.WriteAllText("Temp/WheelieAuthoring.txt", "PASS: saved Wheelie settings, prefab, weapon references, face display, player debug reference and network registration.\n");
                Debug.Log("Wheelie saved assets configured. F2 > Spawnables > Spawn Wheelie Squad.");
            }
            catch (Exception e) { File.WriteAllText("Temp/WheelieAuthoring.txt", e.ToString()); Debug.LogException(e); }
        }

        private static GameObject BuildPrefab(WheelieSettings settings)
        {
            var player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Models/Wheelie/Wheelie.fbx");
            if (model == null || player == null) throw new InvalidOperationException("Wheelie FBX and player weapon source must exist.");
            var root = new GameObject("Wheelie");
            try
            {
                root.AddComponent<NetworkObject>();
                var capsule = root.AddComponent<CapsuleCollider>();
                capsule.radius = settings.AgentRadius; capsule.height = settings.AgentHeight; capsule.center = Vector3.up * settings.AgentHeight * .5f;
                var agent = root.AddComponent<NavMeshAgent>(); agent.enabled = false;
                var health = root.AddComponent<PlayerHealth>();
                root.AddComponent<PlayerWeaponEffects>(); root.AddComponent<WeaponAudioEmitter>();
                var weapon = root.AddComponent<PlayerWeaponController>();
                var controller = root.AddComponent<WheelieController>();
                var face = root.AddComponent<RobotFaceExpressionController>();
                var presentation = root.AddComponent<WheeliePresentation>();
                var net = root.AddComponent<NetworkTransform>();
                var netData = new SerializedObject(net);
                netData.FindProperty("_clientAuthoritative").boolValue = false;
                netData.FindProperty("_synchronizeScale").boolValue = false;
                netData.FindProperty("_interval").intValue = 2;
                netData.ApplyModifiedPropertiesWithoutUndo();

                var visual = new GameObject("Visual"); visual.transform.SetParent(root.transform, false);
                var robot = (GameObject)PrefabUtility.InstantiatePrefab(model); robot.transform.SetParent(visual.transform, false);
                PrefabUtility.UnpackPrefabInstance(robot, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                robot.name = "Wheelie_Model";
                var body = robot.GetComponentsInChildren<SkinnedMeshRenderer>().First(r => r.name == "Wheelie_Body");
                var screen = robot.GetComponentsInChildren<SkinnedMeshRenderer>().First(r => r.name == "FaceDisplay");
                Vector3 front = Vector3.ProjectOnPlane(screen.bounds.center - body.bounds.center, Vector3.up);
                if (front.sqrMagnitude > .0001f) robot.transform.rotation = Quaternion.FromToRotation(front.normalized, Vector3.forward);
                var bounds = new Bounds(); bool first = true;
                foreach (Renderer r in robot.GetComponentsInChildren<Renderer>())
                { if (first) { bounds = r.bounds; first = false; } else bounds.Encapsulate(r.bounds); }
                robot.transform.localScale *= 1.131692f / bounds.size.y;
                bounds = body.bounds; bounds.Encapsulate(screen.bounds);
                robot.transform.position -= new Vector3(bounds.center.x, bounds.min.y, 0f);
                foreach (var animator in robot.GetComponentsInChildren<Animator>()) Object.DestroyImmediate(animator);
                foreach (var renderer in robot.GetComponentsInChildren<SkinnedMeshRenderer>()) renderer.updateWhenOffscreen = true;
                Transform Bone(string name) => robot.GetComponentsInChildren<Transform>(true).First(t => t.name == name);

                Set(health, "m_wheelieSettings", settings, "m_visualRoot", visual.transform);
                Set(weapon, "m_wheelieSettings", settings, "m_health", health);
                Set(controller, "m_settings", settings, "m_agent", agent, "m_health", health, "m_weapon", weapon);
                Set(face, "m_displaySettings", AssetDatabase.LoadAssetAtPath<RobotFaceDisplaySettings>("Assets/_Project/Resources/RobotFaceDisplaySettings.asset"), "m_faceRenderer", screen);
                Set(presentation, "m_controller", controller, "m_health", health, "m_weapon", weapon,
                    "m_wheel", Bone("Wheel"), "m_waist", Bone("Waist"), "m_torso", Bone("Torso"), "m_head", Bone("Head"),
                    "m_rightUpperArm", Bone("UpperArm.R"), "m_rightForearm", Bone("Forearm.R"),
                    "m_rightElbow", Bone("Forearm.R"), "m_rightWrist", Bone("Wrist.R"),
                    "m_leftUpperArm", Bone("UpperArm.L"), "m_leftForearm", Bone("Forearm.L"),
                    "m_leftElbow", Bone("Forearm.L"), "m_leftWrist", Bone("Wrist.L"));

                var sourceWeapons = new SerializedObject(player.GetComponent<PlayerWeaponController>());
                var targetWeapons = new SerializedObject(weapon);
                targetWeapons.CopyFromSerializedProperty(sourceWeapons.FindProperty("m_weapons"));
                var mount = new GameObject("Weapon Mount"); mount.transform.SetParent(Bone("Hand.R"), false);
                mount.transform.position = Bone("WeaponSocket.R").position;
                mount.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
                string[] names = { "Weapon_Pistol", "Weapon_AssaultRifle", "Weapon_Shotgun" };
                float[] lengths = { .24f, .46f, .5f };
                for (int i = 0; i < names.Length; i++)
                {
                    var original = sourceWeapons.FindProperty("m_weaponModels").GetArrayElementAtIndex(i).objectReferenceValue as Transform;
                    if (original == null) throw new InvalidOperationException("Player weapon source missing: " + names[i]);
                    var clone = Object.Instantiate(original.gameObject, mount.transform, false); clone.name = names[i]; clone.SetActive(true);
                    clone.transform.localPosition = Vector3.zero; clone.transform.localRotation = Quaternion.identity; clone.transform.localScale = Vector3.one;
                    Transform muzzle = clone.GetComponentsInChildren<Transform>(true).First(t => t.name == "Muzzle" || t.name.StartsWith("Muzzle."));
                    Vector3 barrel = clone.transform.InverseTransformPoint(muzzle.position).normalized;
                    Vector3 up = original.InverseTransformDirection(Vector3.up);
                    if (Mathf.Abs(Vector3.Dot(up.normalized, barrel)) > .95f) up = Vector3.forward;
                    clone.transform.localRotation = Quaternion.Inverse(Quaternion.LookRotation(barrel, up));
                    Bounds weaponBounds = new Bounds(); bool have = false;
                    foreach (var r in clone.GetComponentsInChildren<Renderer>())
                    { if (!have) { weaponBounds = r.bounds; have = true; } else weaponBounds.Encapsulate(r.bounds); }
                    clone.transform.localScale *= lengths[i] / Mathf.Max(weaponBounds.size.x, weaponBounds.size.y, weaponBounds.size.z);
                    foreach (var c in clone.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
                    targetWeapons.FindProperty("m_weaponModels").GetArrayElementAtIndex(i).objectReferenceValue = clone.transform;
                    targetWeapons.FindProperty("m_muzzles").GetArrayElementAtIndex(i).objectReferenceValue = muzzle;
                    clone.SetActive(false);
                }
                targetWeapons.ApplyModifiedPropertiesWithoutUndo();
                // Reuse the player's configured impact effects without recreating effects or weapon meshes.
                var effects = new SerializedObject(root.GetComponent<PlayerWeaponEffects>());
                var sourceEffects = new SerializedObject(player.GetComponent<PlayerWeaponEffects>());
                foreach (string field in new[] { "m_terrainImpactPrefab", "m_wallImpactPrefab", "m_metalImpactPrefab", "m_enemyImpactPrefab" })
                    effects.CopyFromSerializedProperty(sourceEffects.FindProperty(field));
                effects.ApplyModifiedPropertiesWithoutUndo();
                var saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                return saved;
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static void Set(Object target, params object[] values)
        {
            var data = new SerializedObject(target);
            for (int i = 0; i < values.Length; i += 2)
            {
                var property = data.FindProperty((string)values[i]);
                if (property == null) throw new InvalidOperationException(target.GetType().Name + ": missing " + values[i]);
                property.objectReferenceValue = values[i + 1] as Object;
            }
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Register(NetworkObject prefab)
        {
            var registry = AssetDatabase.LoadAssetAtPath<SinglePrefabObjects>("Assets/_Project/Settings/NetworkSpawnablePrefabs.asset");
            var data = new SerializedObject(registry); var list = data.FindProperty("_prefabs");
            for (int i = 0; i < list.arraySize; i++) if (list.GetArrayElementAtIndex(i).objectReferenceValue == prefab) return;
            int index = list.arraySize++; list.GetArrayElementAtIndex(index).objectReferenceValue = prefab;
            data.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(registry);
        }
    }
}
