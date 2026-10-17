using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CouchGuys.UI;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;

namespace CouchGuys.Editor
{
    /// <summary>Non-destructively imports, slices and packages the authored Couch Dispatch sprite sheet.</summary>
    public static class CouchDispatchUIBuilder
    {
        private const string SessionBuildKey = "CouchGuys.CouchDispatchUIBuilder.AutomaticBuild.V2";
        private const string BuilderScriptPath = "Assets/_Project/Scripts/Editor/CouchDispatchUIBuilder.cs";
        private const string PresenterScriptPath = "Assets/_Project/Scripts/UI/DialogueShopUI.cs";
        private const string SheetPath = "Assets/_Project/UI/Couch Dispatch UI Sprite Kit.png";
        private const string ResourceFolder = "Assets/_Project/Resources/UI";
        private const string StylePath = ResourceFolder + "/CouchDispatchUIStyle.asset";
        private const string PrefabPath = ResourceFolder + "/CouchDispatchUI.prefab";
        private const string AtlasPath = "Assets/_Project/UI/CouchDispatchUI.spriteatlas";
        private const int SheetHeight = 1536;

        private readonly struct Slice
        {
            public readonly string Name;
            public readonly Rect Rect;
            public readonly Vector4 Border;

            public Slice(string name, int x, int top, int width, int height, Vector4 border = default)
            {
                Name = name;
                Rect = new Rect(x, SheetHeight - top - height, width, height);
                Border = border;
            }
        }

        // Coordinates are measured against the original 1024 x 1536 source, top-left origin.
        // They intentionally hug each authored element so neighbouring art never bleeds into a slice.
        private static readonly Slice[] Slices =
        {
            new("WindowFull", 14, 10, 995, 598),
            new("WindowHeader", 237, 10, 553, 92, new Vector4(42, 25, 42, 25)),
            new("CloseButton", 921, 25, 85, 74, new Vector4(20, 20, 20, 20)),
            new("TabDeliveries", 106, 116, 405, 63, new Vector4(24, 20, 24, 20)),
            new("TabShop", 515, 116, 406, 63, new Vector4(24, 20, 24, 20)),
            new("ContentPanel", 65, 198, 895, 271, new Vector4(30, 30, 30, 30)),
            new("ShopPrompt", 214, 495, 599, 72, new Vector4(24, 20, 24, 20)),

            new("SectionWindowPanels", 18, 634, 324, 60, new Vector4(22, 18, 22, 18)),
            new("SectionButtonsTabs", 355, 634, 325, 60, new Vector4(22, 18, 22, 18)),
            new("CardBlank", 698, 633, 308, 152, new Vector4(30, 30, 30, 30)),
            new("CardLocked", 698, 793, 308, 105, new Vector4(26, 24, 26, 24)),

            new("ButtonSelectPrimary", 18, 709, 208, 63, new Vector4(25, 20, 25, 20)),
            new("ButtonBuyPrimary", 245, 709, 204, 63, new Vector4(25, 20, 25, 20)),
            new("ButtonEquip", 468, 709, 214, 63, new Vector4(25, 20, 25, 20)),
            new("ButtonSelectPressed", 18, 784, 208, 63, new Vector4(25, 20, 25, 20)),
            new("ButtonBuyPressed", 246, 784, 204, 63, new Vector4(25, 20, 25, 20)),
            new("ButtonBackLocked", 470, 784, 214, 63, new Vector4(25, 20, 25, 20)),
            new("ButtonBack", 18, 859, 208, 63, new Vector4(25, 20, 25, 20)),
            new("ButtonOkay", 246, 859, 214, 63, new Vector4(25, 20, 25, 20)),
            new("ButtonLocked", 471, 859, 214, 63, new Vector4(25, 20, 25, 20)),

            new("DeliveryEasy", 18, 939, 310, 154, new Vector4(25, 25, 25, 25)),
            new("DeliveryMedium", 348, 939, 314, 154, new Vector4(25, 25, 25, 25)),
            new("DeliveryHard", 691, 939, 316, 154, new Vector4(25, 25, 25, 25)),
            new("CategoryVans", 18, 1105, 226, 63, new Vector4(25, 20, 25, 20)),
            new("CategoryWeapons", 269, 1105, 233, 63, new Vector4(25, 20, 25, 20)),
            new("CategoryUpgrades", 519, 1105, 239, 63, new Vector4(25, 20, 25, 20)),
            new("PageDots", 773, 1105, 235, 63, new Vector4(25, 20, 25, 20)),

            new("VanIcon", 23, 1193, 213, 116),
            new("WeaponsIcon", 252, 1184, 250, 136),
            new("UpgradeCrate", 506, 1182, 174, 133),
            new("CurrencyCoin", 687, 1184, 104, 108),
            new("CurrencyCoins", 803, 1185, 205, 113),
            new("ClipboardIcon", 31, 1328, 137, 124),
            new("ArrowIcon", 186, 1333, 148, 111),
            new("CashIcon", 349, 1322, 154, 123),
            new("EnergySmall", 505, 1310, 101, 137),
            new("EnergyLarge", 620, 1306, 107, 144),
            new("CashReward", 738, 1309, 165, 135),
            new("Sparkles", 906, 1305, 102, 143)
        };

        [InitializeOnLoadMethod]
        private static void BuildOnFirstImport()
        {
            if (SessionState.GetBool(SessionBuildKey, false) || !NeedsBuild())
                return;
            SessionState.SetBool(SessionBuildKey, true);
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                {
                    SessionState.SetBool(SessionBuildKey, false);
                    BuildOnFirstImport();
                    return;
                }
                BuildAll();
            };
        }

        private static bool NeedsBuild()
        {
            if (!File.Exists(SheetPath)) return false;
            if (!File.Exists(StylePath) || !File.Exists(PrefabPath) || !File.Exists(AtlasPath)) return true;
            DateTime generated = File.GetLastWriteTimeUtc(PrefabPath);
            return File.GetLastWriteTimeUtc(SheetPath) > generated ||
                   File.GetLastWriteTimeUtc(BuilderScriptPath) > generated ||
                   File.GetLastWriteTimeUtc(PresenterScriptPath) > generated;
        }

        [MenuItem("Tools/Couch Guys/UI/Rebuild Couch Dispatch UI")]
        public static void BuildAll()
        {
            if (!File.Exists(SheetPath))
                throw new FileNotFoundException("Couch Dispatch sprite sheet was not found.", SheetPath);

            ConfigureAndSliceSheet();
            EnsureFolder(ResourceFolder);
            CouchDispatchUIStyle style = BuildStyleAsset();
            BuildAtlas();
            BuildPrefab(style);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Couch Dispatch UI rebuilt: {Slices.Length} sprites, style, atlas and prefab.");
        }

        public static void BuildAllBatch() => BuildAll();

        private static void ConfigureAndSliceSheet()
        {
            AssetDatabase.ImportAsset(SheetPath, ImportAssetOptions.ForceSynchronousImport);
            TextureImporter importer = AssetImporter.GetAtPath(SheetPath) as TextureImporter;
            if (importer == null) throw new InvalidOperationException($"No TextureImporter for {SheetPath}");

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 2048;
            importer.wrapMode = TextureWrapMode.Clamp;

#pragma warning disable 0618
            importer.spritesheet = Slices.Select(slice => new SpriteMetaData
            {
                name = slice.Name,
                rect = slice.Rect,
                alignment = (int)SpriteAlignment.Center,
                pivot = new Vector2(0.5f, 0.5f),
                border = slice.Border
            }).ToArray();
#pragma warning restore 0618
            importer.SaveAndReimport();
        }

        private static CouchDispatchUIStyle BuildStyleAsset()
        {
            CouchDispatchUIStyle style = AssetDatabase.LoadAssetAtPath<CouchDispatchUIStyle>(StylePath);
            if (style == null)
            {
                style = ScriptableObject.CreateInstance<CouchDispatchUIStyle>();
                AssetDatabase.CreateAsset(style, StylePath);
            }

            Dictionary<string, Sprite> sprites = AssetDatabase.LoadAllAssetsAtPath(SheetPath)
                .OfType<Sprite>()
                .ToDictionary(sprite => sprite.name, StringComparer.Ordinal);
            CouchDispatchUIStyle.SpriteEntry[] entries = Slices
                .Where(slice => sprites.ContainsKey(slice.Name))
                .Select(slice => new CouchDispatchUIStyle.SpriteEntry
                {
                    Name = slice.Name,
                    Sprite = sprites[slice.Name]
                }).ToArray();
            style.SetSprites(entries);
            EditorUtility.SetDirty(style);
            return style;
        }

        private static void BuildAtlas()
        {
            SpriteAtlas atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(AtlasPath);
            if (atlas == null)
            {
                atlas = new SpriteAtlas();
                AssetDatabase.CreateAsset(atlas, AtlasPath);
            }

            SpriteAtlasPackingSettings packing = atlas.GetPackingSettings();
            packing.enableRotation = false;
            packing.enableTightPacking = false;
            packing.padding = 4;
            atlas.SetPackingSettings(packing);
            SpriteAtlasTextureSettings texture = atlas.GetTextureSettings();
            texture.generateMipMaps = false;
            texture.filterMode = FilterMode.Bilinear;
            atlas.SetTextureSettings(texture);

            UnityEngine.Object[] current = atlas.GetPackables();
            if (current.Length > 0) atlas.Remove(current);
            Texture2D sheet = AssetDatabase.LoadAssetAtPath<Texture2D>(SheetPath);
            atlas.Add(new UnityEngine.Object[] { sheet });
            EditorUtility.SetDirty(atlas);
        }

        private static void BuildPrefab(CouchDispatchUIStyle style)
        {
            GameObject root = new("CouchDispatchUI");
            DialogueShopUI presenter = root.AddComponent<DialogueShopUI>();
            presenter.RebuildEditorPrefab(style);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
        }

        private static void EnsureFolder(string assetPath)
        {
            string[] parts = assetPath.Split('/');
            string current = parts[0];
            for (int index = 1; index < parts.Length; index++)
            {
                string next = current + "/" + parts[index];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[index]);
                current = next;
            }
        }
    }
}
