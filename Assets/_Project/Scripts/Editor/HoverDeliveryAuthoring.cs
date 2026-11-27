using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CouchGuys.Gameplay.Weapons;
using CouchGuys.Networking;
using CouchGuys.Player;
using FishNet.Managing.Object;
using FishNet.Object;
using GameKit.Dependencies.Utilities;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CouchGuys.Editor
{
    // Explicit batch authoring. The saved variants can be edited normally in the Inspector.
    public static class HoverDeliveryAuthoring
    {
        private const string ModelPath = "Assets/_Project/Models/HoverDelivery/HoverDelivery.fbx";
        private const string MaterialFolder = "Assets/_Project/Models/HoverDelivery";
        private const string PlayerPath = "Assets/_Project/Prefabs/Players/Player.prefab";
        private const string PreviewPath = "Assets/_Project/Prefabs/UI/MainMenuRobotVisual.prefab";
        private const string DeliveryPlayerPath = "Assets/_Project/Prefabs/Players/HoverDeliveryPlayer.prefab";
        private const string DeliveryPreviewPath = "Assets/_Project/Prefabs/UI/HoverDeliveryMenuVisual.prefab";
        private const string CatalogPath = "Assets/_Project/Resources/PlayerCharacters.asset";
        private const string DefaultFaceSettingsPath = "Assets/_Project/Resources/RobotFaceDisplaySettings.asset";
        private const string FaceSettingsPath = MaterialFolder + "/HoverDeliveryFaceSettings.asset";
        private const string ValidationFolder = "Logs/HoverDelivery";
        private const string SelectionId = "hover-delivery";
        private static readonly string[] Registries =
        {
            "Assets/_Project/Settings/NetworkSpawnablePrefabs.asset",
            "Assets/DefaultPrefabObjects.asset"
        };

        public static void Install()
        {
            Require(AssetDatabase.LoadAssetAtPath<GameObject>(DeliveryPlayerPath) == null &&
                AssetDatabase.LoadAssetAtPath<GameObject>(DeliveryPreviewPath) == null,
                "The saved Hover Delivery prefabs already exist.");
            var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
            importer.SaveAndReimport();

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var parts = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Require(parts.Length == 11, "Expected 11 rigidly skinned delivery model parts.");
            var materials = CreateMaterials();
            if (AssetDatabase.LoadAssetAtPath<RobotFaceDisplaySettings>(FaceSettingsPath) == null)
            {
                var faceSettings = Object.Instantiate(AssetDatabase.LoadAssetAtPath<RobotFaceDisplaySettings>(DefaultFaceSettingsPath));
                AssetDatabase.CreateAsset(faceSettings, FaceSettingsPath);
                var faceData = new SerializedObject(faceSettings);
                faceData.FindProperty("m_screenUVRect").vector4Value = new Vector4(0, 0, 1, 1);
                faceData.FindProperty("m_rotateScreenUV").boolValue = false;
                faceData.ApplyModifiedPropertiesWithoutUndo();
            }

            var player = InstallVariant(PlayerPath, DeliveryPlayerPath, parts, materials, true);
            var preview = InstallVariant(PreviewPath, DeliveryPreviewPath, parts, materials, false);
            var networkObject = player.GetComponent<NetworkObject>();
            networkObject.SetAssetPathHash(new string((DeliveryPlayerPath + networkObject.name)
                .ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray()).GetStableHashU64());
            EditorUtility.SetDirty(networkObject);
            foreach (var path in Registries)
            {
                var registry = AssetDatabase.LoadAssetAtPath<SinglePrefabObjects>(path);
                if (!registry.Prefabs.Contains(networkObject))
                {
                    registry.AddObject(networkObject, false, false);
                    EditorUtility.SetDirty(registry);
                }
            }

            var catalog = AssetDatabase.LoadAssetAtPath<PlayerCharacterCatalog>(CatalogPath);
            var data = new SerializedObject(catalog);
            var entries = data.FindProperty("m_characters");
            entries.arraySize++;
            var entry = entries.GetArrayElementAtIndex(entries.arraySize - 1);
            entry.FindPropertyRelative("Id").stringValue = SelectionId;
            entry.FindPropertyRelative("PlayerPrefab").objectReferenceValue = networkObject;
            entry.FindPropertyRelative("MenuVisualPrefab").objectReferenceValue = preview;
            data.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            Validate();
        }

        private static GameObject InstallVariant(string sourcePath, string outputPath,
            SkinnedMeshRenderer[] parts, Dictionary<string, Material> materials, bool isPlayer)
        {
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
                var variant = (GameObject)PrefabUtility.InstantiatePrefab(source, scene);
                variant.name = Path.GetFileNameWithoutExtension(outputPath);
                var animator = isPlayer ? variant.GetComponent<CouchGuyAnimationDriver>().Animator : variant.GetComponent<Animator>();
                var visual = animator.transform;
                var rig = visual.Find("CouchGuy_Rig");
                Require(rig != null, "Original player rig is missing.");
                var hover = isPlayer
                    ? Reference<Transform>(variant.GetComponent<RobotHoverVFX>(), "m_hoverRing")
                    : visual.GetComponentsInChildren<Transform>(true).Single(t => t.name == "HoverAssembly");
                var bones = rig.GetComponentsInChildren<Transform>(true)
                    .Where(t => !InsideWeapon(t, rig))
                    .GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
                var held = isPlayer ? HeldObjects(variant).ToArray() : Array.Empty<Transform>();

                foreach (var renderer in visual.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer is ParticleSystemRenderer || InsideWeapon(renderer.transform, visual) ||
                        held.Any(item => renderer.transform == item || renderer.transform.IsChildOf(item))) continue;
                    if (renderer.transform.IsChildOf(hover))
                    {
                        // RobotHoverVFX enables its renderer list on startup.
                        renderer.gameObject.SetActive(false);
                        PrefabUtility.RecordPrefabInstancePropertyModifications(renderer.gameObject);
                    }
                    renderer.enabled = false;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                }

                Renderer face = null;
                Renderer identity = null;
                foreach (var part in parts)
                {
                    var boneName = PartBone(part.name);
                    int boneIndex = Array.FindIndex(part.bones, bone => bone != null && bone.name == boneName);
                    Require(boneIndex >= 0, $"Missing {boneName} bind pose for {part.name}.");
                    var bone = bones[boneName];
                    var parent = part.name == "DeliveryHover" ? hover : bone;
                    var local = parent.worldToLocalMatrix * bone.localToWorldMatrix * part.sharedMesh.bindposes[boneIndex];
                    var segment = new GameObject(part.name);
                    segment.layer = visual.gameObject.layer;
                    segment.transform.SetParent(parent, false);
                    segment.transform.SetLocalPositionAndRotation(local.GetColumn(3), local.rotation);
                    segment.transform.localScale = local.lossyScale;
                    segment.AddComponent<MeshFilter>().sharedMesh = part.sharedMesh;
                    var renderer = segment.AddComponent<MeshRenderer>();
                    renderer.sharedMaterials = part.sharedMaterials.Select(m => materials[m.name]).ToArray();
                    if (part.name == "DeliveryFace") face = renderer;
                    if (part.name == "DeliveryChest") identity = renderer;
                }

                SetReference(variant.GetComponent<RobotFaceExpressionController>(), "m_faceRenderer", face);
                SetReference(variant.GetComponent<RobotFaceExpressionController>(), "m_displaySettings",
                    AssetDatabase.LoadAssetAtPath<RobotFaceDisplaySettings>(FaceSettingsPath));
                if (isPlayer) SetReference(variant.GetComponent<NetworkPlayerOwnership>(), "m_playerRenderer", identity);
                return PrefabUtility.SaveAsPrefabAsset(variant, outputPath);
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        private static bool InsideWeapon(Transform item, Transform root)
        {
            for (var current = item; current != null && current != root; current = current.parent)
                if (current.name.StartsWith("Weapon_", StringComparison.Ordinal)) return true;
            return false;
        }

        private static IEnumerable<Transform> HeldObjects(GameObject player)
        {
            var data = new SerializedObject(player.GetComponent<PlayerWeaponController>());
            foreach (string field in new[] { "m_weaponModels", "m_healingModels" })
            {
                var items = data.FindProperty(field);
                for (int i = 0; i < items.arraySize; i++)
                    if (items.GetArrayElementAtIndex(i).objectReferenceValue is Transform item)
                        yield return item;
            }
        }

        private static string PartBone(string name) => name switch
        {
            "DeliveryHelmet" or "DeliveryFace" => "Head",
            "DeliveryChest" or "DeliveryBackpack" => "Torso",
            "DeliveryHover" => "Root",
            "DeliveryShoulder_L" => "UpperArm_L",
            "DeliveryShoulder_R" => "UpperArm_R",
            "DeliveryForearm_L" => "LowerArm_L",
            "DeliveryForearm_R" => "LowerArm_R",
            "DeliveryHand_L" => "Hand_L",
            "DeliveryHand_R" => "Hand_R",
            _ => throw new InvalidOperationException($"Unknown model part {name}.")
        };

        private static Dictionary<string, Material> CreateMaterials()
        {
            return new Dictionary<string, Material>
            {
                ["Porcelain"] = Lit("Porcelain", new Color(.97f,.95f,1), .08f,.76f),
                ["Ceramic Trim"] = Lit("Ceramic Trim", new Color(.79f,.81f,.89f),.23f,.68f),
                ["Graphite"] = Lit("Graphite", new Color(.157f,.180f,.242f),.32f,.65f),
                ["Joint Seals"] = Lit("Joint Seals", new Color(.071f,.087f,.118f),.1f,.50f),
                ["Anodised Blue"] = Lit("Anodised Blue", new Color(.08f,.31f,.51f),.45f,.69f),
                ["Cyan Light"] = Lit("PlayerGlow Hover Delivery Cyan", new Color(.004f,.77f,.94f),.05f,.74f,2f),
                ["Display Glass"] = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/RobotFace/RobotCRTDisplay.mat")
            };
        }

        private static Material Lit(string name, Color colour, float metal, float smooth, float emission = 0)
        {
            var path = MaterialFolder + "/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            material.SetColor("_BaseColor", colour);
            material.SetFloat("_Metallic", metal);
            material.SetFloat("_Smoothness", smooth);
            if (emission > 0)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", colour * emission);
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static T Reference<T>(Object target, string field) where T : Object =>
            new SerializedObject(target).FindProperty(field).objectReferenceValue as T;

        private static void SetReference(Object target, string field, Object value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(field).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.RecordPrefabInstancePropertyModifications(target);
        }

        private static void Require(bool ok, string message)
        {
            if (!ok) throw new InvalidOperationException(message);
        }

        // These project-native actions complement the actions in the source Blend.
        // The FBX copy is for offline editing; the playable prefab samples the original clips.
        public static void ExportNativeActions()
        {
            string animations = "Assets/_Project/Animations";
            string[] folders = { "BaseballBat", "Emotes", "MagneticCarry" };
            var scene = EditorSceneManager.NewPreviewScene();
            var lines = new List<string>();
            try
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
                var player = (GameObject)PrefabUtility.InstantiatePrefab(source, scene);
                var animator = player.GetComponent<CouchGuyAnimationDriver>().Animator.transform;
                var rig = animator.Find("CouchGuy_Rig");
                var names = new HashSet<string>
                {
                    "Root", "Torso", "Head", "Eye_L", "Eye_R", "UpperArm_L", "LowerArm_L",
                    "Hand_L", "UpperArm_R", "LowerArm_R", "Hand_R"
                };
                var bones = rig.GetComponentsInChildren<Transform>(true)
                    .Where(bone => names.Contains(bone.name)).ToArray();
                var transforms = new[] { rig }.Concat(bones).ToArray();
                var positions = transforms.Select(t => t.localPosition).ToArray();
                var rotations = transforms.Select(t => t.localRotation).ToArray();
                var scales = transforms.Select(t => t.localScale).ToArray();
                var rest = bones.Select(bone => bone.worldToLocalMatrix).ToArray();
                var definitions = AssetDatabase.FindAssets("t:EmoteDefinition")
                    .Select(guid => AssetDatabase.LoadAssetAtPath<EmoteDefinition>(AssetDatabase.GUIDToAssetPath(guid)))
                    .Where(d => d.Clip != null).ToArray();
                for (int i = 0; i < bones.Length; i++)
                    lines.Add($"rest {bones[i].name} {MatrixLine(bones[i].localToWorldMatrix)}");
                foreach (var folder in folders)
                {
                    foreach (var guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { animations + "/" + folder }))
                    {
                        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(guid));
                        string name = folder == "MagneticCarry" ? folder + "_" + clip.name : clip.name;
                        lines.Add("action " + name);
                        var emote = definitions.FirstOrDefault(d => d.Clip == clip);
                        int lastFrame = Mathf.RoundToInt(clip.length * 30f);
                        var visual = player.transform.Find("Visual");
                        Vector3 visualPosition = visual.localPosition;
                        Quaternion visualRotation = visual.localRotation;
                        for (int frame = 0; frame <= lastFrame; frame++)
                        {
                            visual.SetLocalPositionAndRotation(visualPosition, visualRotation);
                            for (int i = 0; i < transforms.Length; i++)
                            {
                                transforms[i].SetLocalPositionAndRotation(positions[i], rotations[i]);
                                transforms[i].localScale = scales[i];
                            }
                            float time = Mathf.Min(clip.length, frame / 30f);
                            clip.SampleAnimation(animator.gameObject, time);
                            if (emote != null)
                            {
                                var rotation = emote.Rotation(time);
                                Vector3 centre = new(0, 1.4f, 0);
                                visual.SetLocalPositionAndRotation(visualPosition + emote.Position(time) +
                                    visualRotation * (centre - rotation * centre), visualRotation * rotation);
                            }
                            for (int i = 0; i < bones.Length; i++)
                                lines.Add($"sample {frame + 1} {bones[i].name} {MatrixLine(bones[i].localToWorldMatrix * rest[i])}");
                        }
                        visual.SetLocalPositionAndRotation(visualPosition, visualRotation);
                    }
                }
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
            Directory.CreateDirectory(ValidationFolder);
            File.WriteAllLines(ValidationFolder + "/NativeActions.txt", lines);
        }

        private static string MatrixLine(Matrix4x4 matrix) =>
            string.Join(" ", Enumerable.Range(0, 4).SelectMany(row => Enumerable.Range(0, 4)
                .Select(column => matrix[row, column].ToString("R", System.Globalization.CultureInfo.InvariantCulture))));

        public static void Validate()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<PlayerCharacterCatalog>(CatalogPath);
            var character = catalog[catalog.FindIndex(SelectionId)];
            Require(character.Id == SelectionId, "Catalog selection is missing.");
            Require(catalog[0].PlayerPrefab == AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath).GetComponent<NetworkObject>(),
                "Original default selection changed.");
            foreach (var path in Registries)
                Require(AssetDatabase.LoadAssetAtPath<SinglePrefabObjects>(path).Prefabs.Contains(character.PlayerPrefab),
                    $"Missing registration in {path}.");

            var scene = EditorSceneManager.NewPreviewScene();
            var report = new List<string>();
            try
            {
                var original = (GameObject)PrefabUtility.InstantiatePrefab(catalog[0].PlayerPrefab.gameObject, scene);
                var delivery = (GameObject)PrefabUtility.InstantiatePrefab(character.PlayerPrefab.gameObject, scene);
                var preview = (GameObject)PrefabUtility.InstantiatePrefab(character.MenuVisualPrefab, scene);
                var baseline = original.GetComponent<CouchGuyAnimationDriver>().Animator;
                var animator = delivery.GetComponent<CouchGuyAnimationDriver>().Animator;
                Require(animator.runtimeAnimatorController == baseline.runtimeAnimatorController,
                    "The original Animator Controller must be reused.");
                CheckFace(delivery);
                CheckFace(preview);

                var parts = animator.GetComponentsInChildren<MeshFilter>(true)
                    .Where(m => m.name.StartsWith("Delivery", StringComparison.Ordinal)).ToArray();
                Require(parts.Length == 11, "Wrong number of saved model parts.");
                int triangles = parts.Sum(part => Enumerable.Range(0, part.sharedMesh.subMeshCount)
                    .Sum(i => (int)part.sharedMesh.GetIndexCount(i) / 3));
                foreach (var part in parts)
                    Require(part.GetComponent<MeshRenderer>().sharedMaterials.All(mat => mat != null),
                        $"A material is missing from {part.name}.");
                var hover = Reference<Transform>(delivery.GetComponent<RobotHoverVFX>(), "m_hoverRing");
                Require(hover.Find("DeliveryHover") != null, "The emission base is outside the hover effect pivot.");
                Require(hover.GetComponentsInChildren<Renderer>().Single().name == "DeliveryHover",
                    "An original hover renderer can be re-enabled by RobotHoverVFX.");
                var originalHeld = HeldObjects(original).ToArray();
                var deliveryHeld = HeldObjects(delivery).ToArray();
                Require(originalHeld.Length == deliveryHeld.Length, "Missing weapon or healing item mount.");
                for (int i = 0; i < originalHeld.Length; i++)
                {
                    var a = originalHeld[i];
                    var b = deliveryHeld[i];
                    Require(AnimationUtility.CalculateTransformPath(a, original.transform) ==
                        AnimationUtility.CalculateTransformPath(b, delivery.transform) &&
                        a.localPosition == b.localPosition && a.localRotation == b.localRotation,
                        $"Held item mount changed: {a.name}.");
                    Require(a.GetComponentsInChildren<Renderer>(true).Select(r => r.enabled)
                        .SequenceEqual(b.GetComponentsInChildren<Renderer>(true).Select(r => r.enabled)),
                        $"Held item renderer changed: {a.name}.");
                }

                var clips = animator.runtimeAnimatorController.animationClips.Distinct()
                    .OrderBy(clip => clip.name, StringComparer.Ordinal).ToArray();
                report.Add($"{parts.Length} rigid mesh sections, {triangles} triangles.");
                report.Add($"Shared Animator Controller: {clips.Length} distinct clips.");
                foreach (var clip in clips)
                {
                    var paths = AnimationUtility.GetCurveBindings(clip).Select(b => b.path).Distinct().ToArray();
                    foreach (string path in paths)
                        Require(animator.transform.Find(path) != null || path.Length == 0,
                            $"{clip.name} has an unresolved binding to {path}.");
                    for (int i = 0; i <= 8; i++)
                    {
                        float time = clip.length * i / 8;
                        clip.SampleAnimation(baseline.gameObject, time);
                        clip.SampleAnimation(animator.gameObject, time);
                        foreach (string path in paths)
                        {
                            var a = path.Length > 0 ? baseline.transform.Find(path) : baseline.transform;
                            var b = path.Length > 0 ? animator.transform.Find(path) : animator.transform;
                            Require((a.localPosition - b.localPosition).sqrMagnitude < .000001f &&
                                Quaternion.Angle(a.localRotation, b.localRotation) < .1f,
                                $"{clip.name} diverged at {path}, t={time:F3}.");
                        }
                    }

                    report.Add($"{clip.name} | {clip.length:F3}s | {paths.Length} bound transforms | 9 samples");
                }
                report.Add("FBX takes:");
                report.AddRange(AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>()
                    .Where(clip => !clip.name.StartsWith("__preview__", StringComparison.Ordinal))
                    .Select(clip => clip.name).OrderBy(name => name, StringComparer.Ordinal));
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }

            Directory.CreateDirectory(ValidationFolder);
            File.WriteAllLines(ValidationFolder + "/UnityValidation.txt", report);
            Debug.Log("Hover Delivery catalog, rig and animation bindings validated.");
        }

        private static void CheckFace(GameObject root)
        {
            var controller = root.GetComponent<RobotFaceExpressionController>();
            var face = Reference<Renderer>(controller, "m_faceRenderer");
            Require(face != null && face.name == "DeliveryFace" && face.transform.parent.name == "Head",
                $"{root.name} face display is missing or unbound.");
            var settings = Reference<RobotFaceDisplaySettings>(controller, "m_displaySettings");
            Require(settings != null && settings.Material != null && !settings.RotateScreenUV &&
                settings.ScreenUVRect == new Vector4(0, 0, 1, 1), $"{root.name} CRT UV configuration is invalid.");
        }
    }
}
