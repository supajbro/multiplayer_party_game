using CouchGuys.Gameplay.Weapons;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace CouchGuys.Editor
{
    public static class WeaponReticleAssetBuilder
    {
        public const string ReticlePrefabPath = "Assets/_Project/Prefabs/UI/WeaponReticle.prefab";
        private const string SettingsRoot = "Assets/_Project/Settings/Reticles";
        private const string PlayerPrefabPath = "Assets/_Project/Prefabs/Player.prefab";

        [MenuItem("Couch Guys/Build Weapon Reticles %#t")]
        public static void Build()
        {
            EnsureFolder("Assets/_Project/Settings", "Reticles");
            EnsureFolder("Assets/_Project/Prefabs", "UI");

            ReticleSettings pistol = CreateSettings("PistolReticle", ReticleStyle.Crosshair,
                28f, 3.2f, 12f, 7f, 4f, 11f, 160f, 55f, false, 1.4f, 18f, 0.18f);
            ReticleSettings rifle = CreateSettings("AssaultRifleReticle", ReticleStyle.Crosshair,
                20f, 2.5f, 8f, 5f, 3f, 5f, 145f, 38f, true, 2.6f, 17f, 0.18f);
            ReticleSettings shotgun = CreateSettings("ShotgunReticle", ReticleStyle.Circle,
                68f, 4f, 15f, 0f, 5f, 20f, 180f, 45f, false, 1.6f, 22f, 0.22f);
            WeaponReticleView viewPrefab = BuildReticlePrefab();
            ConfigurePlayer(pistol, rifle, shotgun, viewPrefab);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Verify(pistol, rifle, shotgun, viewPrefab);
            Debug.Log("Built and assigned Couch Guys weapon reticles.");
        }

        public static void BuildFromCommandLine() => Build();

        private static ReticleSettings CreateSettings(string name, ReticleStyle style,
            float size, float thickness, float length, float gap, float dot,
            float expansion, float expansionSpeed, float recoverySpeed, bool sustained,
            float maximumMultiplier, float hitSize, float hitDuration)
        {
            string path = $"{SettingsRoot}/{name}.asset";
            ReticleSettings settings = AssetDatabase.LoadAssetAtPath<ReticleSettings>(path);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<ReticleSettings>();
                settings.name = name;
                AssetDatabase.CreateAsset(settings, path);
            }

            SerializedObject serialised = new(settings);
            Set(serialised, "m_style", (int)style);
            Set(serialised, "m_defaultSize", size);
            Set(serialised, "m_lineThickness", thickness);
            Set(serialised, "m_lineLength", length);
            Set(serialised, "m_centreGap", gap);
            Set(serialised, "m_centreDotSize", dot);
            Set(serialised, "m_shootingExpansion", expansion);
            Set(serialised, "m_expansionSpeed", expansionSpeed);
            Set(serialised, "m_recoverySpeed", recoverySpeed);
            serialised.FindProperty("m_sustainedFireExpansion").boolValue = sustained;
            Set(serialised, "m_maximumSustainedMultiplier", maximumMultiplier);
            Set(serialised, "m_hitMarkerSize", hitSize);
            Set(serialised, "m_hitMarkerDuration", hitDuration);
            serialised.FindProperty("m_normalColour").colorValue = Color.white;
            serialised.FindProperty("m_enemyColour").colorValue = new Color(1f, 0.14f, 0.1f, 1f);
            serialised.FindProperty("m_teammateColour").colorValue = new Color(0.18f, 1f, 0.32f, 1f);
            serialised.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(settings);
            return settings;
        }

        private static WeaponReticleView BuildReticlePrefab()
        {
            GameObject root = new("WeaponReticle", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(CanvasGroup), typeof(WeaponReticleView));
            try
            {
                Canvas canvas = root.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 75;
                CanvasScaler scaler = root.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 0.5f;
                CanvasGroup group = root.GetComponent<CanvasGroup>();
                group.interactable = false;
                group.blocksRaycasts = false;

                Sprite rounded = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
                Sprite circle = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
                Image dot = CreateImage("CentreDot", root.transform, circle);
                Image[] crosshair = new Image[4];
                string[] crossNames = { "Right", "Left", "Up", "Down" };
                for (int i = 0; i < crosshair.Length; i++)
                    crosshair[i] = CreateImage("Crosshair_" + crossNames[i], root.transform, rounded);

                Image[] segments = new Image[8];
                for (int i = 0; i < segments.Length; i++)
                    segments[i] = CreateImage($"CircleSegment_{i + 1}", root.transform, rounded);

                GameObject hitRoot = new("HitMarker", typeof(RectTransform), typeof(CanvasGroup));
                hitRoot.transform.SetParent(root.transform, false);
                Centre(hitRoot.GetComponent<RectTransform>());
                CanvasGroup hitGroup = hitRoot.GetComponent<CanvasGroup>();
                hitGroup.alpha = 0f;
                hitGroup.blocksRaycasts = false;
                hitGroup.interactable = false;
                Image[] hitLines = new Image[4];
                for (int i = 0; i < hitLines.Length; i++)
                    hitLines[i] = CreateImage($"HitLine_{i + 1}", hitRoot.transform, rounded);

                WeaponReticleView view = root.GetComponent<WeaponReticleView>();
                SerializedObject serialised = new(view);
                serialised.FindProperty("m_canvasGroup").objectReferenceValue = group;
                serialised.FindProperty("m_centreDot").objectReferenceValue = dot;
                SetArray(serialised.FindProperty("m_crosshairLines"), crosshair);
                SetArray(serialised.FindProperty("m_circleSegments"), segments);
                serialised.FindProperty("m_hitMarkerGroup").objectReferenceValue = hitGroup;
                SetArray(serialised.FindProperty("m_hitMarkerLines"), hitLines);
                serialised.ApplyModifiedPropertiesWithoutUndo();

                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, ReticlePrefabPath);
                return prefab.GetComponent<WeaponReticleView>();
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static Image CreateImage(string name, Transform parent, Sprite sprite)
        {
            GameObject gameObject = new(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            gameObject.transform.SetParent(parent, false);
            RectTransform rect = gameObject.GetComponent<RectTransform>();
            Centre(rect);
            Image image = gameObject.GetComponent<Image>();
            image.sprite = sprite;
            image.type = sprite != null && sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
            image.color = Color.white;
            image.raycastTarget = false;
            return image;
        }

        private static void ConfigurePlayer(ReticleSettings pistol, ReticleSettings rifle,
            ReticleSettings shotgun, WeaponReticleView viewPrefab)
        {
            GameObject player = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                PlayerWeaponController weapons = player.GetComponent<PlayerWeaponController>();
                if (weapons == null) throw new UnityException("PlayerWeaponController is missing from Player.prefab.");
                ReticleTargetDetector detector = player.GetComponent<ReticleTargetDetector>() ??
                    player.AddComponent<ReticleTargetDetector>();
                WeaponReticleController controller = player.GetComponent<WeaponReticleController>() ??
                    player.AddComponent<WeaponReticleController>();

                SerializedObject weaponObject = new(weapons);
                SerializedProperty definitions = weaponObject.FindProperty("m_weapons");
                ReticleSettings[] settings = { pistol, rifle, shotgun };
                for (int index = 0; index < settings.Length; index++)
                    definitions.GetArrayElementAtIndex(index).FindPropertyRelative("m_reticleSettings").objectReferenceValue = settings[index];
                weaponObject.ApplyModifiedPropertiesWithoutUndo();

                SerializedObject controllerObject = new(controller);
                controllerObject.FindProperty("m_reticlePrefab").objectReferenceValue = viewPrefab;
                controllerObject.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(detector);
                EditorUtility.SetDirty(controller);
                PrefabUtility.SaveAsPrefabAsset(player, PlayerPrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(player); }
        }

        private static void Verify(ReticleSettings pistol, ReticleSettings rifle,
            ReticleSettings shotgun, WeaponReticleView viewPrefab)
        {
            GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (pistol == null || rifle == null || shotgun == null || viewPrefab == null ||
                player == null || player.GetComponent<WeaponReticleController>() == null ||
                player.GetComponent<ReticleTargetDetector>() == null)
                throw new UnityException("Weapon reticle asset verification failed.");
            SerializedObject weapons = new(player.GetComponent<PlayerWeaponController>());
            SerializedProperty definitions = weapons.FindProperty("m_weapons");
            for (int index = 0; index < 3; index++)
                if (definitions.GetArrayElementAtIndex(index).FindPropertyRelative("m_reticleSettings").objectReferenceValue == null)
                    throw new UnityException($"Weapon {index} has no reticle settings assigned.");
        }

        private static void Set(SerializedObject target, string property, float value) =>
            target.FindProperty(property).floatValue = value;
        private static void Set(SerializedObject target, string property, int value) =>
            target.FindProperty(property).enumValueIndex = value;

        private static void SetArray(SerializedProperty property, Image[] values)
        {
            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }

        private static void Centre(RectTransform rect)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
        }

        private static void EnsureFolder(string parent, string child)
        {
            string path = parent + "/" + child;
            if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, child);
        }
    }
}
