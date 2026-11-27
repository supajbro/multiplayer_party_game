using System;
using System.IO;
using System.Linq;
using CouchGuys.Player;
using CouchGuys.Networking;
using CouchGuys.Gameplay.Weapons;
using FishNet.Object;
using FishNet.Managing.Object;
using GameKit.Dependencies.Utilities;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace CouchGuys.Editor
{
    // Explicit authoring only. Saved prefabs remain independently editable in the Inspector.
    public static class PlayerCharacterAuthoring
    {
        public const string WheeliePath = "Assets/_Project/Prefabs/Players/WheeliePlayer.prefab";
        public const string PreviewPath = "Assets/_Project/Prefabs/UI/WheelieMenuVisual.prefab";
        public const string CatalogPath = "Assets/_Project/Resources/PlayerCharacters.asset";
        private const string PlayerPath = "Assets/_Project/Prefabs/Players/Player.prefab";
        private const string MenuPath = "Assets/_Project/Prefabs/UI/MainMenuUI.prefab";

        [MenuItem("Couch Guys/Main Menu/Install Player Character Selection")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode first.");
            if (!AssetDatabase.IsValidFolder("Assets/_Project/Prefabs/Players"))
                AssetDatabase.CreateFolder("Assets/_Project/Prefabs", "Players");
            if (AssetDatabase.LoadAssetAtPath<GameObject>(WheeliePath) == null) CreateWheelie();
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PreviewPath) == null) CreatePreview();
            var catalog = AssetDatabase.LoadAssetAtPath<PlayerCharacterCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<PlayerCharacterCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
                var data = new SerializedObject(catalog);var list = data.FindProperty("m_characters");list.arraySize = 2;
                string[] ids = { "couch-guy", "wheelie" };
                string[] players = { PlayerPath, WheeliePath };
                string[] previews = { "Assets/_Project/Prefabs/UI/MainMenuRobotVisual.prefab", PreviewPath };
                for (int i = 0; i < 2; i++)
                {
                    var entry = list.GetArrayElementAtIndex(i);
                    entry.FindPropertyRelative("Id").stringValue = ids[i];
                    entry.FindPropertyRelative("PlayerPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(players[i]).GetComponent<NetworkObject>();
                    entry.FindPropertyRelative("MenuVisualPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(previews[i]);
                }
                data.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(catalog);
            }
            var wheelie = AssetDatabase.LoadAssetAtPath<GameObject>(WheeliePath).GetComponent<NetworkObject>();
            wheelie.SetAssetPathHash(new string((WheeliePath + wheelie.name).ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray()).GetStableHashU64());
            EditorUtility.SetDirty(wheelie);
            foreach (string path in new[] { "Assets/_Project/Settings/NetworkSpawnablePrefabs.asset", "Assets/DefaultPrefabObjects.asset" })
            {
                var registry = AssetDatabase.LoadAssetAtPath<SinglePrefabObjects>(path);
                if (registry != null && !registry.Prefabs.Contains(wheelie))
                { registry.AddObject(wheelie, false, false);EditorUtility.SetDirty(registry); }
            }
            foreach (string path in new[] { "Assets/_Project/Resources/SoloNetworkBootstrap.prefab", "Assets/_Project/Resources/SteamMultiplayerBootstrap.prefab" })
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    Set(root.GetComponentInChildren<NeighbourhoodPlayerSpawner>(true), "m_characterCatalog", catalog);
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            var menu = PrefabUtility.LoadPrefabContents(MenuPath);
            try
            {
                var group = menu.transform.Find("Customise Arrows");
                var buttons = group.GetComponentsInChildren<Button>(true);
                foreach (var button in buttons)
                {
                    button.interactable = true;
                    // Only the saved target artwork catches clicks; labels and stencil masks stay passive.
                    button.targetGraphic.raycastTarget = true;
                    var navigation = button.navigation;navigation.mode = Navigation.Mode.Explicit;
                    navigation.selectOnLeft = buttons.First(b => b != button);
                    navigation.selectOnRight = navigation.selectOnLeft;button.navigation = navigation;
                }
                var cg = group.GetComponent<CanvasGroup>();cg.ignoreParentGroups = true;
                cg.alpha = 0;cg.interactable = false;cg.blocksRaycasts = false;
                PrefabUtility.SaveAsPrefabAsset(menu, MenuPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(menu); }
            AssetDatabase.SaveAssets();
            Validate();
        }

        private static void CreateWheelie()
        {
            var root = PrefabUtility.LoadPrefabContents(PlayerPath);
            var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/AI/Wheelie.prefab");
            try
            {
                root.name = "WheeliePlayer";
                var visual = root.transform.Find("Visual");
                var animation = root.GetComponent<CouchGuyAnimationDriver>();
                var oldModel = animation.Animator.gameObject;
                if (PrefabUtility.IsPartOfPrefabInstance(oldModel))
                    PrefabUtility.UnpackPrefabInstance(PrefabUtility.GetOutermostPrefabInstanceRoot(oldModel), PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                var wheelieVisual = Object.Instantiate(source.transform.Find("Visual").gameObject, visual, false);
                wheelieVisual.name = "Wheelie Model";
                Transform Bone(string name) => wheelieVisual.GetComponentsInChildren<Transform>(true).First(t => t.name == name);
                // The AI's pre-authored model already has correctly fitted materials and a grounded rig.
                foreach (var mount in wheelieVisual.GetComponentsInChildren<Transform>(true).Where(t => t.name == "Weapon Mount").ToArray())
                    Object.DestroyImmediate(mount.gameObject);
                var weapon = root.GetComponent<PlayerWeaponController>();var weapons = new SerializedObject(weapon);
                var mountRoot = new GameObject("Player Item Mount").transform;mountRoot.SetParent(Bone("Hand.R"), false);
                mountRoot.position = Bone("WeaponSocket.R").position;
                mountRoot.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
                foreach (string field in new[] { "m_weaponModels", "m_healingModels" })
                {
                    var models = weapons.FindProperty(field);
                    for (int i = 0; i < models.arraySize; i++)
                    {
                        var model = models.GetArrayElementAtIndex(i).objectReferenceValue as Transform;
                        if (model == null) continue;
                        model.SetParent(mountRoot, true);
                        model.localPosition = Vector3.zero;model.localRotation = Quaternion.identity;model.localScale = Vector3.one;
                        if (field == "m_weaponModels")
                        {
                            var muzzle = weapons.FindProperty("m_muzzles").GetArrayElementAtIndex(i).objectReferenceValue as Transform;
                            Vector3 barrel = muzzle != null ? model.InverseTransformPoint(muzzle.position).normalized : Vector3.forward;
                            if (barrel.sqrMagnitude > .01f)
                                model.localRotation = Quaternion.Inverse(Quaternion.LookRotation(barrel, Mathf.Abs(Vector3.Dot(barrel,Vector3.up)) > .95f ? Vector3.forward : Vector3.up));
                        }
                        var renderers = model.GetComponentsInChildren<Renderer>(true);
                        if (renderers.Length > 0)
                        {
                            Bounds bounds = renderers[0].bounds;foreach (var r in renderers) bounds.Encapsulate(r.bounds);
                            float length = field == "m_healingModels" ? .22f : i == 0 ? .28f : i == 3 ? .65f : .5f;
                            model.localScale *= length / Mathf.Max(.001f, bounds.size.x, bounds.size.y, bounds.size.z);
                        }
                        model.gameObject.SetActive(false);
                    }
                }
                // No CouchGuy animation is applied to the differently named Wheelie skeleton.
                animation.enabled = false;Set(animation, "m_animator", null);
                Set(root.GetComponent<PlayerEmotePlayback>(), "m_animator", null);
                Object.DestroyImmediate(oldModel);
                var capsule = visual.Find("Capsule");if (capsule != null) Object.DestroyImmediate(capsule.gameObject);
                var hover = root.GetComponent<RobotHoverVFX>();if (hover != null) hover.enabled = false;
                Set(root.GetComponent<NetworkPlayerOwnership>(), "m_playerRenderer", Bone("Wheelie_Body").GetComponent<Renderer>());
                Set(root.GetComponent<RobotFaceExpressionController>(), "m_faceRenderer", Bone("FaceDisplay").GetComponent<Renderer>());
                var presentation = root.AddComponent<WheeliePlayerPresentation>();
                Set(presentation, "m_wheel", Bone("Wheel"), "m_waist", Bone("Waist"));
                ConfigurePlayerPresentation(root);
                PrefabUtility.SaveAsPrefabAsset(root, WheeliePath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void CreatePreview()
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(WheeliePath);
            var visual = Object.Instantiate(source.transform.Find("Visual").Find("Wheelie Model").gameObject);
            try
            {
                visual.name = "Wheelie Menu Visual";
                foreach (var mount in visual.GetComponentsInChildren<Transform>(true).Where(t => t.name == "Player Item Mount").ToArray()) Object.DestroyImmediate(mount.gameObject);
                var face = visual.AddComponent<RobotFaceExpressionController>();
                Set(face, "m_faceRenderer", visual.GetComponentsInChildren<Renderer>(true).First(r => r.name == "FaceDisplay"),
                    "m_displaySettings", AssetDatabase.LoadAssetAtPath<RobotFaceDisplaySettings>("Assets/_Project/Resources/RobotFaceDisplaySettings.asset"));
                PrefabUtility.SaveAsPrefabAsset(visual, PreviewPath);
            }
            finally { Object.DestroyImmediate(visual); }
        }

        public static void ConfigurePlayerPresentation(GameObject root)
        {
            var model = root.transform.Find("Visual/Wheelie Model");
            Transform Bone(string name) => model.GetComponentsInChildren<Transform>(true).First(t => t.name == name);
            Set(root.GetComponent<WheeliePlayerPresentation>(),
                "m_weapon", root.GetComponent<PlayerWeaponController>(), "m_health", root.GetComponent<PlayerHealth>(),
                "m_carrier", root.GetComponent<PlayerCouchCarrier>(), "m_camera", root.GetComponentInChildren<Camera>(true),
                "m_rightUpperArm", Bone("UpperArm.R"), "m_rightElbow", Bone("Forearm.R"),
                "m_rightForearm", Bone("Forearm.R"), "m_rightWrist", Bone("Wrist.R"),
                "m_leftUpperArm", Bone("UpperArm.L"), "m_leftElbow", Bone("Forearm.L"),
                "m_leftForearm", Bone("Forearm.L"), "m_leftWrist", Bone("Wrist.L"));
            var playback = new SerializedObject(root.GetComponent<PlayerEmotePlayback>());
            playback.FindProperty("m_bones").arraySize = 0;playback.ApplyModifiedPropertiesWithoutUndo();
            foreach (string name in new[] { "GroundGlow", "HoverVFX" })
            { var child = root.transform.Find(name);if (child != null) Object.DestroyImmediate(child.gameObject); }
            foreach (var behaviour in root.GetComponents<MonoBehaviour>())
                if (behaviour.GetType().Name == "HoverAudioEmitter") behaviour.enabled = false;
        }

        [MenuItem("Couch Guys/Main Menu/Validate Player Character Selection")]
        public static void Validate()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<PlayerCharacterCatalog>(CatalogPath);
            if (catalog == null || catalog.Count < 2) throw new InvalidOperationException("Character catalog is missing.");
            foreach (var character in Enumerable.Range(0,catalog.Count).Select(i => catalog[i]))
            {
                if (character.PlayerPrefab == null || character.MenuVisualPrefab == null) throw new InvalidOperationException("Character references are missing.");
                if (character.PlayerPrefab.GetComponent<ThirdPersonPlayerController>() == null || character.PlayerPrefab.GetComponent<NetworkPlayerOwnership>() == null)
                    throw new InvalidOperationException("Character must be a playable network prefab.");
            }
            var wheelie = catalog[1].PlayerPrefab;
            if (wheelie.GetComponent<WheelieController>() != null) throw new InvalidOperationException("Player must not run AI behaviour.");
            if (wheelie.GetComponent<WheeliePlayerPresentation>() == null) throw new InvalidOperationException("Wheel presentation is missing.");
            File.WriteAllText("Temp/PlayerCharacterSelection.result", "PASS: playable Wheelie prefab, saved menu preview, character catalog, network registration and active arrow artwork.\n");
        }

        private static void Set(Object target, params object[] values)
        {
            var data = new SerializedObject(target);
            for (int i = 0; i < values.Length; i += 2) data.FindProperty((string)values[i]).objectReferenceValue = values[i + 1] as Object;
            data.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
