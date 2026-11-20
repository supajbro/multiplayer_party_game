using System;
using System.IO;
using System.Linq;
using CouchGuys.Gameplay.Delivery;
using CouchGuys.Gameplay.Enemies;
using CouchGuys.Gameplay.Pedestrians;
using FishNet.Managing.Object;
using FishNet.Object;
using GameKit.Dependencies.Utilities;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CouchGuys.Editor
{
    /// <summary>Explicit installation only; never rebuilds the prefab on reload/import/play.</summary>
    [InitializeOnLoad]
    public static class SpeakerThiefAuthoring
    {
        public const string PrefabPath = "Assets/_Project/Prefabs/Enemies/SpeakerThief.prefab";
        public const string SettingsPath = "Assets/_Project/Settings/Thieves/SpeakerThiefSettings.asset";
        private const string SourcePrefab = "Assets/_Project/Prefabs/Enemies/Thief.prefab";
        static SpeakerThiefAuthoring() => EditorApplication.update += CheckRequest;
        private static void CheckRequest()
        {
            const string request = "Temp/SpeakerThief.request";
            if (!File.Exists(request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            string command = File.ReadAllText(request).Trim();File.Delete(request);
            try
            {
                if (command == "install") Install();
                else if (command == "fit")
                {
                    var root = PrefabUtility.LoadPrefabContents(PrefabPath);
                    try { FitShoulder(root);PrefabUtility.SaveAsPrefabAsset(root, PrefabPath); }
                    finally { PrefabUtility.UnloadPrefabContents(root); }
                    Preview();
                }
                else if (command == "preview") Preview();
                else if (command == "probe") { SpeakerThiefPlayProbe.Run(); return; }
                Validate();
                File.WriteAllText("Temp/SpeakerThief.result", "PASS: existing Character01 model/rig/materials, silent shoulder prop, shared health/ragdoll/networking and spawn rules.\n");
            }
            catch (Exception e) { File.WriteAllText("Temp/SpeakerThief.result", e.ToString());Debug.LogException(e); }
        }
        [MenuItem("Couch Guys/Speaker Thief/Install Existing Character Variant")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode first.");
            var settings = AssetDatabase.LoadAssetAtPath<ThiefSettings>(SettingsPath);
            if (settings == null)
            {
                settings = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ThiefSettings>("Assets/_Project/Settings/Thieves/PistolThiefSettings.asset"));
                settings.name = "SpeakerThiefSettings";
                settings.Combat.DamagePerPellet = 0;settings.Combat.KnockbackForce = 0;
                settings.Combat.RetaliationChance = 0;settings.Combat.RetaliationChanceVariation = 0;
                settings.Health.WeaponDropChance = 0;settings.Alerts.AlertChance = 0;
                AssetDatabase.CreateAsset(settings, SettingsPath);
            }
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null) CreatePrefab(settings);
            var no = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath).GetComponent<NetworkObject>();
            foreach (string path in new[] { "Assets/_Project/Settings/NetworkSpawnablePrefabs.asset", "Assets/DefaultPrefabObjects.asset" })
            {
                var list = AssetDatabase.LoadAssetAtPath<SinglePrefabObjects>(path);
                if (list != null && !list.Prefabs.Contains(no)) { list.AddObject(no, false, false);EditorUtility.SetDirty(list); }
            }
            string stable = new((PrefabPath + no.name).ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
            no.SetAssetPathHash(stable.GetStableHashU64());EditorUtility.SetDirty(no);
            foreach (string name in new[] { "Easy", "Medium", "Hard" })
            {
                var tier = AssetDatabase.LoadAssetAtPath<DeliveryTierConfig>($"Assets/_Project/Settings/Delivery/{name}Delivery.asset");
                var so = new SerializedObject(tier);
                if (so.FindProperty("m_speakerThiefPrefab").objectReferenceValue != null) continue;
                so.FindProperty("m_speakerThiefPrefab").objectReferenceValue = no;
                so.FindProperty("m_speakerThiefChance").floatValue = .15f;
                so.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(tier);
            }
            AssetDatabase.SaveAssets();Validate();Preview();
        }

        private static void CreatePrefab(ThiefSettings settings)
        {
            var root = PrefabUtility.LoadPrefabContents(SourcePrefab);
            try
            {
                root.name = "SpeakerThief";
                // Keep the existing Character01 model, skeleton, Animator and bone physics intact.
                var animator = root.GetComponentInChildren<Animator>(true);
                var shoulder = animator.GetComponentsInChildren<Transform>(true).First(t => t.name == "mixamorig:RightShoulder");
                root.AddComponent<SpeakerThief>();
                if (root.GetComponent<ThiefSpawnMember>() == null) root.AddComponent<ThiefSpawnMember>();
                var weapon = root.GetComponent<EnemyWeapon>();
                if (weapon != null) Object.DestroyImmediate(weapon);
                foreach (var component in root.GetComponentsInChildren<Component>(true))
                {
                    if (component == null) continue;
                    var so = new SerializedObject(component);var property = so.FindProperty("m_settings");
                    if (property != null && property.objectReferenceValue is ThiefSettings)
                    { property.objectReferenceValue = settings;so.ApplyModifiedPropertiesWithoutUndo(); }
                }
                var mount = new GameObject("ShoulderSpeaker");mount.transform.SetParent(shoulder, false);
                FitShoulder(root);
                var lods = new LOD[3];float[] thresholds = { .35f, .16f, .035f };
                for (int i = 0; i < 3; i++)
                {
                    var asset = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/_Project/Models/Props/Speaker/Speaker_LOD{i}.fbx");
                    var prop = (GameObject)PrefabUtility.InstantiatePrefab(asset, mount.transform);
                    PrefabUtility.UnpackPrefabInstance(prop, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                    prop.name = "Speaker_LOD" + i;prop.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);prop.transform.localScale = Vector3.one;
                    foreach (var autoLod in prop.GetComponentsInChildren<LODGroup>(true)) Object.DestroyImmediate(autoLod);
                    foreach (var unusedAnimator in prop.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(unusedAnimator);
                    lods[i] = new LOD(thresholds[i], prop.GetComponentsInChildren<Renderer>(true));
                }
                var group = mount.AddComponent<LODGroup>();group.SetLODs(lods);group.RecalculateBounds();
                // No AudioSource or music runtime is added in this version.
                var no = root.GetComponent<NetworkObject>();var behaviours = root.GetComponents<NetworkBehaviour>();
                var net = new SerializedObject(no);var array = net.FindProperty("NetworkBehaviours");array.arraySize = behaviours.Length;
                for (int i = 0; i < behaviours.Length; i++)
                {
                    array.GetArrayElementAtIndex(i).objectReferenceValue = behaviours[i];
                    var data = new SerializedObject(behaviours[i]);data.FindProperty("_componentIndexCache").intValue = i;
                    data.FindProperty("_addedNetworkObject").objectReferenceValue = no;data.FindProperty("_networkObjectCache").objectReferenceValue = no;data.ApplyModifiedPropertiesWithoutUndo();
                }
                net.ApplyModifiedPropertiesWithoutUndo();PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void FitShoulder(GameObject root)
        {
            var mount = root.GetComponentsInChildren<Transform>(true).First(t => t.name == "ShoulderSpeaker");
            var shoulder = mount.parent;
            mount.SetPositionAndRotation(shoulder.position - root.transform.right * .27f + root.transform.up * .045f - root.transform.forward * .035f,
                root.transform.rotation * Quaternion.Euler(0, -90, 0));
            // The existing FBX rig contains a centimetre conversion in its hierarchy.
            // Author the prop at metre scale without changing that model or skeleton.
            Vector3 scale = shoulder.lossyScale;
            mount.localScale = new Vector3(.45f / Mathf.Abs(scale.x), .45f / Mathf.Abs(scale.y), .45f / Mathf.Abs(scale.z));
        }

        public static void Validate()
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePrefab);
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (root == null) throw new Exception("Speaker variant prefab is missing.");
            var sourceSkin = source.GetComponentInChildren<SkinnedMeshRenderer>(true);
            var skin = root.GetComponentInChildren<SkinnedMeshRenderer>(true);
            Require(skin.sharedMesh == sourceSkin.sharedMesh && skin.sharedMaterials.SequenceEqual(sourceSkin.sharedMaterials), "Character01 mesh/materials were changed.");
            Require(root.GetComponentInChildren<Animator>(true).runtimeAnimatorController == source.GetComponentInChildren<Animator>(true).runtimeAnimatorController, "Existing animation controller must be reused.");
            Require(root.GetComponent<EnemyWeapon>() == null && root.GetComponent<GrapplerThief>() == null, "Speaker must have no attack component.");
            Require(root.GetComponent<ThiefHealth>() != null && root.GetComponent<ThiefNavigator>() != null && root.GetComponentInChildren<PedestrianRagdoll>(true) != null, "Shared thief lifecycle is missing.");
            var mount = root.GetComponentsInChildren<Transform>(true).First(t => t.name == "ShoulderSpeaker");
            Require(mount.parent.name == "mixamorig:RightShoulder" && mount.GetComponentsInChildren<AudioSource>(true).Length == 0, "Speaker must be silent and attached to the existing shoulder.");
            Require(mount.GetComponent<LODGroup>().GetLODs().Length == 3, "Missing speaker LODs.");
            var prefab = root.GetComponent<NetworkObject>();
            foreach (string path in new[] { "Assets/_Project/Settings/NetworkSpawnablePrefabs.asset", "Assets/DefaultPrefabObjects.asset" })
                Require(AssetDatabase.LoadAssetAtPath<SinglePrefabObjects>(path).Prefabs.Contains(prefab), "Missing network prefab registration.");
            int checks = 0;
            var regular = source.GetComponent<NetworkObject>();
            var special = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Enemies/GrapplerThief.prefab").GetComponent<NetworkObject>();
            var invalid = new GameObject("Invalid filtered spawn candidate");
            try
            {
                var invalidNo = invalid.AddComponent<NetworkObject>();
                foreach (string name in new[] { "Easy", "Medium", "Hard" })
                {
                    var tierAsset = AssetDatabase.LoadAssetAtPath<DeliveryTierConfig>($"Assets/_Project/Settings/Delivery/{name}Delivery.asset");
                    Require(tierAsset.SpeakerThiefPrefab == prefab, "Missing tier reference: " + name);
                    var tier = Object.Instantiate(tierAsset);
                    try
                    {
                        var so = new SerializedObject(tier);so.FindProperty("m_speakerThiefChance").floatValue = 1;so.ApplyModifiedPropertiesWithoutUndo();
                        foreach (var members in new[] { new NetworkObject[0], new[] { regular }, new[] { null, regular, invalidNo } })
                        { Require(SpeakerThief.SelectReplacement(members, tier, 0) == -1, "Filtered singleton became a speaker on " + name);checks++; }
                        Require(SpeakerThief.SelectReplacement(new[] { regular, regular }, tier, .999f) >= 0, "Eligible two-member group rejected on " + name);checks++;
                        Require(SpeakerThief.SelectReplacement(new[] { regular, prefab }, tier, 0) == -1, "Second speaker allowed.");checks++;
                        Require(SpeakerThief.SelectReplacement(new[] { special, regular }, tier, 0) == 1, "Special thief was replaced.");checks++;
                        so.FindProperty("m_speakerThiefChance").floatValue = 0;so.ApplyModifiedPropertiesWithoutUndo();
                        Require(SpeakerThief.SelectReplacement(new[] { regular, regular }, tier, 0) == -1, "Zero probability ignored.");checks++;
                    }
                    finally { Object.DestroyImmediate(tier); }
                }
            }
            finally { Object.DestroyImmediate(invalid); }
            File.WriteAllText("Tools/SpeakerThiefValidation.txt", $"PASS: {checks} spawn-rule cases across Easy/Medium/Hard; final filtering, minimum group size, max one, regular-only replacement, probability zero/one.\nPASS: original Character01 mesh/materials/controller, shared health/ragdoll, bone attachment, three speaker LODs, no attack components, no music, both FishNet prefab lists.\nEasy wave counts are unchanged (currently 1..1), so no speaker can appear on Easy until the existing wave settings allow two members.\n");
        }
        private static void Require(bool valid, string message) { if (!valid) throw new Exception(message); }

        [MenuItem("Couch Guys/Speaker Thief/Render Prefab Preview")]
        public static void Preview()
        {
            var utility = new PreviewRenderUtility();GameObject root = null;
            try
            {
                root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));utility.AddSingleGO(root);
                foreach (var lod in root.GetComponentsInChildren<LODGroup>(true)) lod.ForceLOD(0);
                utility.camera.transform.position = new Vector3(-3.2f, 2.1f, 4.8f);
                utility.camera.transform.LookAt(new Vector3(-.1f, .95f, 0));utility.camera.nearClipPlane = .01f;utility.camera.farClipPlane = 30;
                utility.camera.orthographic = true;utility.camera.orthographicSize = 1.15f;
                utility.lights[0].intensity = 1.4f;utility.lights[0].transform.rotation = Quaternion.Euler(35, 140, 0);
                utility.lights[1].intensity = 1.1f;utility.ambientColor = new Color(.4f, .4f, .4f);
                utility.BeginStaticPreview(new Rect(0, 0, 900, 900));utility.Render(true);
                var texture = utility.EndStaticPreview();File.WriteAllBytes("Tools/SpeakerThiefPreview.png", texture.EncodeToPNG());Object.DestroyImmediate(texture);
            }
            finally { utility.Cleanup();if (root != null) Object.DestroyImmediate(root); }
        }
    }
}
