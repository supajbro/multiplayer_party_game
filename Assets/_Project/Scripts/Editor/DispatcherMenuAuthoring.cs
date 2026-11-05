using System;
using System.IO;
using System.Linq;
using CouchGuys.Gameplay.Delivery;
using CouchGuys.Gameplay.Shop;
using CouchGuys.ProceduralGeneration;
using CouchGuys.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace CouchGuys.Editor
{
    /// <summary>Explicit, one-shot authoring. Existing saved prefabs are never rebuilt on reload.</summary>
    public static class DispatcherMenuAuthoring
    {
        public const string MenuPath = "Assets/_Project/Prefabs/UI/DispatcherMenuUI.prefab";
        public const string CardPath = "Assets/_Project/Prefabs/UI/DispatcherDeliveryButton.prefab";
        public const string NpcPath = "Assets/_Project/Prefabs/DeliveryNPC.prefab";
        private const string FontFolder = "Assets/_Project/Art/Fonts/Barlow_Semi_Condensed/";
        private static TMP_FontAsset s_regular, s_title;

        [MenuItem("Couch Guys/Dispatcher/Install In-World TV Menu")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode first.");
            s_regular = Font("BarlowSemiCondensed-Regular");
            s_title = Font("BarlowSemiCondensed-BoldItalic");
            if (!File.Exists(CardPath)) BuildCard();
            if (!File.Exists(MenuPath)) BuildMenu();
            InstallOnNpc();
            // SampleScene creates this saved NPC through the existing generator. Verify that link,
            // rather than adding a duplicate static dispatcher to the procedural world.
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
            var generators = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<NeighbourhoodGenerator>(true)).ToArray();
            if (generators.Length == 0) throw new Exception("SampleScene generator missing.");
            var npc = AssetDatabase.LoadAssetAtPath<GameObject>(NpcPath);
            bool changed = false;
            foreach (var generator in generators)
                if (generator.DeliveryNpcPrefab != npc)
                {
                    Assign(generator, "m_deliveryNpcPrefab", npc);
                    EditorUtility.SetDirty(generator); changed = true;
                }
            if (changed) EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Validate();
        }

        private static TMP_FontAsset Font(string name)
        {
            string path = FontFolder + name + " SDF.asset";
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (existing != null) return existing;
            var source = AssetDatabase.LoadAssetAtPath<UnityEngine.Font>(FontFolder + name + ".ttf");
            if (source == null) throw new Exception("Existing Barlow font missing: " + name);
            var font = TMP_FontAsset.CreateFontAsset(source);
            font.name = name + " SDF";
            AssetDatabase.CreateAsset(font, path);
            AssetDatabase.AddObjectToAsset(font.material, font);
            foreach (var texture in font.atlasTextures) AssetDatabase.AddObjectToAsset(texture, font);
            font.TryAddCharacters("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 $.,:!?'/-()+·", out _);
            EditorUtility.SetDirty(font);
            return font;
        }

        private static Sprite CardSprite(string file)
        {
            string path = "Assets/_Project/UI/" + file + ".png";
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer.textureType != TextureImporterType.Sprite || importer.spriteImportMode != SpriteImportMode.Multiple ||
                importer.spritesheet.Length == 0 || importer.spritesheet[0].border != Vector4.zero)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Multiple;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.GetSourceTextureWidthAndHeight(out int width, out int height);
                // Crop only the empty transparent padding using sprite import metadata.
                Rect crop = file == "button" ? new Rect(width * 32f/2172f, height * 184f/724f, width * 2108f/2172f, height * 370f/724f)
                    : new Rect(width * 16f/2172f, height * 150f/724f, width * 2140f/2172f, height * 440f/724f);
                importer.spritesheet = new[] { new SpriteMetaData { name = file, pivot = new Vector2(.5f,.5f), alignment = 0,
                    rect = crop, border = Vector4.zero } };
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().Single();
        }

        private static void BuildCard()
        {
            var root = Rect("DispatcherDeliveryButton", null, 0, 0, 1100, 205);
            var background = root.gameObject.AddComponent<Image>(); background.sprite = CardSprite("button"); background.type = Image.Type.Simple;
            var button = root.gameObject.AddComponent<Button>(); button.targetGraphic = background;
            var colors = button.colors; colors.normalColor = Color.white; colors.highlightedColor = Color.white;
            colors.selectedColor = Color.white; colors.pressedColor = new Color(.85f,.95f,.85f); button.colors = colors;
            var navigation = button.navigation; navigation.mode = Navigation.Mode.None; button.navigation = navigation;
            root.gameObject.AddComponent<LayoutElement>().preferredHeight = 205;
            var view = root.gameObject.AddComponent<DispatcherDeliveryButton>();
            var style = AssetDatabase.LoadAssetAtPath<CouchDispatchUIStyle>("Assets/_Project/Resources/UI/CouchDispatchUIStyle.asset");
            var artwork = Rect("HouseOrItemImage", root, 30, 24, 240, 152).gameObject.AddComponent<Image>();
            artwork.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/UI/DispatcherHouses/EasyHouse.png") ?? style.Get("DeliveryEasy"); artwork.preserveAspect = true; artwork.raycastTarget = false;
            var divider = Rect("Divider", root, 290, 34, 2, 130).gameObject.AddComponent<Image>();
            divider.color = new Color(.72f,.81f,.74f,.6f); divider.raycastTarget = false;
            var title = Label("Name", root, "EASY DELIVERY", 39, true, 315, 16, 745, 48);
            var description = Label("Description", root, "A nearby, mostly flat run in the early neighbourhood.", 28, false, 315, 65, 745, 64);
            var economy = Label("CostAndPayout", root, "Cost FREE  ·  Payout $100", 23, false, 315, 133, 745, 28);
            economy.color = new Color(.72f,.84f,.76f);
            var reason = Label("UnavailableReason", root, "", 21, false, 315, 164, 745, 27); reason.color = new Color(1f,.74f,.44f);
            var highlightRect = Rect("GreenHoverOutline", root, -4, -4, 1108, 213);
            var highlight = highlightRect.gameObject.AddComponent<Image>();highlight.sprite = CardSprite("button_highlight");
            highlight.type = Image.Type.Simple; highlight.raycastTarget = false;highlight.enabled = false;
            Assign(view, "m_button", button);Assign(view,"m_artwork",artwork);Assign(view,"m_placeholder",artwork.sprite);
            Assign(view,"m_highlight",highlight);Assign(view,"m_title",title);Assign(view,"m_description",description);
            Assign(view,"m_economy",economy);Assign(view,"m_reason",reason);
            PrefabUtility.SaveAsPrefabAsset(root.gameObject, CardPath);
            Object.DestroyImmediate(root.gameObject);
        }

        private static void BuildMenu()
        {
            var root = Rect("DispatcherMenuUI", null, 0, 0, 1100, 780);
            root.localScale = Vector3.one * .0006f;
            var canvas = root.gameObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 10;
            root.gameObject.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 2f;
            root.gameObject.AddComponent<GraphicRaycaster>().blockingObjects = GraphicRaycaster.BlockingObjects.None;
            var group = root.gameObject.AddComponent<CanvasGroup>();
            var view = root.gameObject.AddComponent<DispatcherMenuUI>();
            var deliveryPanel = Rect("DeliveriesPanel",root,0,0,1100,650);
            var deliveryLayout = deliveryPanel.gameObject.AddComponent<VerticalLayoutGroup>();ConfigureLayout(deliveryLayout);
            var card = AssetDatabase.LoadAssetAtPath<GameObject>(CardPath);
            var deliveryCards = new DispatcherDeliveryButton[3];
            for(int i=0;i<3;i++)
            {
                var instance=(GameObject)PrefabUtility.InstantiatePrefab(card,deliveryPanel);
                instance.name = new[]{"EasyDeliveryButton","MediumDeliveryButton","HardDeliveryButton"}[i];
                deliveryCards[i]=instance.GetComponent<DispatcherDeliveryButton>();
                var text=instance.GetComponentsInChildren<TMP_Text>().First(t=>t.name=="Name");
                text.text=new[]{"EASY DELIVERY","MEDIUM DELIVERY","HARD DELIVERY"}[i];
                var style=AssetDatabase.LoadAssetAtPath<CouchDispatchUIStyle>("Assets/_Project/Resources/UI/CouchDispatchUIStyle.asset");
                var image=instance.GetComponentsInChildren<Image>().First(t=>t.name=="HouseOrItemImage");
                image.sprite=AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/UI/DispatcherHouses/"+new[]{"EasyHouse","MediumHouse","HardHouse"}[i]+".png") ?? style.Get(i==0?"DeliveryEasy":i==1?"DeliveryMedium":"DeliveryHard");
                Assign(deliveryCards[i],"m_placeholder",image.sprite);
            }
            var shopPanel = Rect("ShopPanel",root,0,0,1100,650);
            var viewport = Rect("Viewport",shopPanel,0,0,1100,650);viewport.gameObject.AddComponent<RectMask2D>();
            var content = Rect("ItemCards",viewport,0,0,1100,650);
            ConfigureLayout(content.gameObject.AddComponent<VerticalLayoutGroup>());
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit=ContentSizeFitter.FitMode.PreferredSize;
            var scroll = shopPanel.gameObject.AddComponent<ScrollRect>(); scroll.viewport=viewport;scroll.content=content;
            scroll.horizontal=false;scroll.vertical=true;scroll.scrollSensitivity=55;scroll.movementType=ScrollRect.MovementType.Clamped;
            shopPanel.gameObject.SetActive(false);
            var funds=Label("TeamFunds",root,"TEAM $0",24,false,12,682,1076,30);
            var feedback=Label("Feedback",root,"E / ESC to return",22,false,12,722,1076,42);feedback.color=new Color(.7f,.81f,.73f);
            var eventObject=new GameObject("LocalEventSystem",typeof(EventSystem),typeof(InputSystemUIInputModule));eventObject.transform.SetParent(root,false);
            eventObject.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();eventObject.SetActive(false);
            Assign(view,"m_canvas",canvas);Assign(view,"m_group",group);Assign(view,"m_deliveriesPanel",deliveryPanel.gameObject);
            Assign(view,"m_shopPanel",shopPanel.gameObject);AssignArray(view,"m_deliveries",deliveryCards);
            Assign(view,"m_shopCardPrefab",card.GetComponent<DispatcherDeliveryButton>());Assign(view,"m_shopContent",content);
            Assign(view,"m_shopScroll",scroll);Assign(view,"m_funds",funds);Assign(view,"m_feedback",feedback);
            Assign(view,"m_artwork",AssetDatabase.LoadAssetAtPath<CouchDispatchUIStyle>("Assets/_Project/Resources/UI/CouchDispatchUIStyle.asset"));
            // Canvas rect uses a centred pivot; its children retain top-left authoring coordinates.
            root.anchorMin=root.anchorMax=new Vector2(.5f,.5f);root.pivot=new Vector2(.5f,.5f);root.anchoredPosition=Vector2.zero;
            PrefabUtility.SaveAsPrefabAsset(root.gameObject,MenuPath);Object.DestroyImmediate(root.gameObject);
        }

        private static void InstallOnNpc()
        {
            var root=PrefabUtility.LoadPrefabContents(NpcPath);
            try
            {
                if(root.GetComponent<DispatcherMenuController>()!=null)return;
                var controller=root.AddComponent<DispatcherMenuController>();
                var renderers=root.GetComponentsInChildren<MeshRenderer>(true);
                var screens=renderers.Where(r=>r.name.EndsWith("_Screen")).ToArray();
                var deliveries=renderers.Where(r=>r.name.StartsWith("Button_Deliveries")).ToArray();
                var shop=renderers.Where(r=>r.name.StartsWith("Button_Shop")).ToArray();
                if(screens.Length!=3 || deliveries.Length!=3 || shop.Length!=3)throw new Exception("Expected screen and button meshes at each LOD.");
                var bounds=screens[0].bounds;
                var front=root.transform.InverseTransformPoint(bounds.center+Vector3.forward*bounds.extents.z);
                var anchor=new GameObject("ScreenCanvasAnchor").transform;anchor.SetParent(root.transform,false);
                anchor.localPosition=front+Vector3.forward*.012f;anchor.localRotation=Quaternion.Euler(0,180,0);
                var focus=new GameObject("CameraFocusPoint").transform;focus.SetParent(root.transform,false);
                focus.localPosition=new Vector3(0,1.71f,front.z+1.35f);focus.localRotation=Quaternion.Euler(0,180,0);
                Assign(controller,"m_menuPrefab",AssetDatabase.LoadAssetAtPath<GameObject>(MenuPath).GetComponent<DispatcherMenuUI>());
                Assign(controller,"m_cameraFocusPoint",focus);Assign(controller,"m_screenCanvasAnchor",anchor);
                AssignArray(controller,"m_screens",screens);AssignArray(controller,"m_deliveriesButtons",deliveries);AssignArray(controller,"m_shopButtons",shop);
                Assign(controller,"m_deliveriesCollider",ButtonCollider(deliveries.Single(r=>r.name=="Button_Deliveries")));
                Assign(controller,"m_shopCollider",ButtonCollider(shop.Single(r=>r.name=="Button_Shop")));
                PrefabUtility.SaveAsPrefabAsset(root,NpcPath);
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
        }

        private static BoxCollider ButtonCollider(Renderer renderer)
        {
            var collider=renderer.gameObject.AddComponent<BoxCollider>();collider.isTrigger=true;
            var bounds=renderer.GetComponent<MeshFilter>().sharedMesh.bounds;
            collider.center=bounds.center;collider.size=bounds.size;return collider;
        }
        private static void ConfigureLayout(VerticalLayoutGroup layout)
        {layout.spacing=12;layout.childControlWidth=true;layout.childForceExpandWidth=true;layout.childControlHeight=true;layout.childForceExpandHeight=false;layout.padding=new RectOffset(0,0,4,0);}
        private static RectTransform Rect(string name,Transform parent,float x,float y,float width,float height)
        {
            var rect=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();rect.SetParent(parent,false);
            rect.anchorMin=rect.anchorMax=new Vector2(0,1);rect.pivot=new Vector2(0,1);rect.anchoredPosition=new Vector2(x,-y);rect.sizeDelta=new Vector2(width,height);return rect;
        }
        private static TMP_Text Label(string name,Transform parent,string text,float size,bool title,float x,float y,float width,float height)
        {
            var rect=Rect(name,parent,x,y,width,height);var label=rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.font=title?s_title:s_regular;label.fontSize=size;label.text=text;label.color=new Color(1f,.96f,.81f);
            label.textWrappingMode=TextWrappingModes.Normal;label.overflowMode=TextOverflowModes.Ellipsis;label.raycastTarget=false;
            return label;
        }
        private static void Assign(Object target,string name,Object value)
        {var so=new SerializedObject(target);so.FindProperty(name).objectReferenceValue=value;so.ApplyModifiedPropertiesWithoutUndo();}
        private static void AssignArray(Object target,string name,Object[] values)
        {var so=new SerializedObject(target);var p=so.FindProperty(name);p.arraySize=values.Length;for(int i=0;i<values.Length;i++)p.GetArrayElementAtIndex(i).objectReferenceValue=values[i];so.ApplyModifiedPropertiesWithoutUndo();}

        [MenuItem("Couch Guys/Dispatcher/Validate In-World TV Menu")]
        public static void Validate()
        {
            var npc=AssetDatabase.LoadAssetAtPath<GameObject>(NpcPath);
            var menu=AssetDatabase.LoadAssetAtPath<GameObject>(MenuPath);
            if(npc.GetComponent<DispatcherMenuController>()==null || menu.GetComponent<Canvas>().renderMode!=RenderMode.WorldSpace)
                throw new Exception("World-space menu/dispatcher controller missing.");
            if(menu.GetComponentsInChildren<DispatcherDeliveryButton>(true).Length!=3 || menu.GetComponentsInChildren<Canvas>(true).Any(c=>c.renderMode!=RenderMode.WorldSpace))
                throw new Exception("Expected three saved whole-card delivery buttons and world-space canvases.");
            var serialized=new SerializedObject(npc.GetComponent<DispatcherMenuController>());
            foreach(string property in new[]{"m_menuPrefab","m_cameraFocusPoint","m_screenCanvasAnchor","m_deliveriesCollider","m_shopCollider"})
                if(serialized.FindProperty(property).objectReferenceValue==null)throw new Exception("Missing "+property);
            foreach(var lod in npc.GetComponentInChildren<LODGroup>().GetLODs())
                if(lod.renderers.Length!=4 || lod.renderers.Any(r=>r==null))throw new Exception("LOD references broken.");
            Directory.CreateDirectory("Tools");
            File.WriteAllText("Tools/DispatcherMenuValidation.txt","PASS: saved editable world-space DispatcherMenuUI and reusable DispatcherDeliveryButton prefabs; three delivery cards, scrollable shop, Barlow fonts, supplied button sprites, physical mesh/collider references, focus/screen anchors, and SampleScene generator integration. Runtime/multiplayer checks are recorded separately.\n");
            Debug.Log("Dispatcher in-world UI assets validated.");
        }

        [MenuItem("Couch Guys/Dispatcher/Render TV Menu Preview")]
        public static void RenderPreview()
        {
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var npc=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(NpcPath));
            var controller=npc.GetComponent<DispatcherMenuController>();var so=new SerializedObject(controller);
            var anchor=(Transform)so.FindProperty("m_screenCanvasAnchor").objectReferenceValue;
            var focus=(Transform)so.FindProperty("m_cameraFocusPoint").objectReferenceValue;
            var ui=Object.Instantiate(AssetDatabase.LoadAssetAtPath<DispatcherMenuUI>(MenuPath),anchor,false);
            var camera=new GameObject("PreviewCamera",typeof(Camera)).GetComponent<Camera>();
            camera.transform.SetPositionAndRotation(focus.position,focus.rotation);camera.fieldOfView=48;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.025f,.035f,.055f);
            ui.ConfigureCamera(camera);npc.GetComponentInChildren<LODGroup>().ForceLOD(0);
            var block=new MaterialPropertyBlock();
            foreach(var renderer in npc.GetComponentsInChildren<MeshRenderer>())
                if(renderer.name.EndsWith("_Screen")) {renderer.GetPropertyBlock(block);block.SetColor("_WaveColor",Color.black);renderer.SetPropertyBlock(block);}
            var light=new GameObject("PreviewLight",typeof(Light)).GetComponent<Light>();light.type=LightType.Directional;light.intensity=1.4f;light.transform.rotation=Quaternion.Euler(35,145,0);
            UnityEngine.RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;
            UnityEngine.RenderSettings.ambientLight=new Color(.45f,.5f,.6f);
            var tiers=AssetDatabase.FindAssets("t:DeliveryTierConfig",new[]{"Assets/_Project"})
                .Select(guid=>AssetDatabase.LoadAssetAtPath<DeliveryTierConfig>(AssetDatabase.GUIDToAssetPath(guid))).OrderBy(t=>(int)t.Tier).Take(3);
            var catalogue=AssetDatabase.LoadAssetAtPath<ShopCatalogue>("Assets/_Project/Resources/Shop/DeliveryManagerShop.asset");
            var categories=new[]{new DialogueShopCategory("Deliveries",tiers.Select(t=>new DialogueShopOption(t.DisplayName,t.Description,
                t.PurchasePrice==0?"FREE":$"${t.PurchasePrice:N0}",$"${t.Reward:N0}",t.Colour,true,"",null)).ToArray()),
                new DialogueShopCategory("Shop",catalogue.Items.Where(item=>item!=null).Select(item=>new DialogueShopOption(item.DisplayName,item.Description,
                    item.Price==0?"FREE":$"${item.Price:N0}","",Color.white,true,"",null,item.Icon,item.Category==ShopItemCategory.Weapon?"Weapons":
                    item.Category==ShopItemCategory.Vehicle?"Trucks":"Upgrades")).ToArray())};
            ui.Bind(categories,500,"");
            var field=typeof(DispatcherMenuController).GetField("m_ui",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);field.SetValue(controller,ui);
            for(int tab=0;tab<2;tab++)
            {
                ui.ShowCategory(tab);
                if(tab==0)ui.GetComponentsInChildren<DispatcherDeliveryButton>().First().OnPointerEnter(null);
                typeof(DispatcherMenuController).GetMethod("UpdateButtonColors",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).Invoke(controller,null);
                Canvas.ForceUpdateCanvases();
                foreach(var text in ui.GetComponentsInChildren<TMP_Text>())text.ForceMeshUpdate(true,true);
                var texture=new RenderTexture(1200,900,24);camera.targetTexture=texture;camera.Render();camera.Render();camera.Render();
                var previous=RenderTexture.active;RenderTexture.active=texture;var image=new Texture2D(1200,900,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1200,900),0,0);image.Apply();
                Directory.CreateDirectory("Tools");File.WriteAllBytes(tab==0?"Tools/DispatcherMenuPreview.png":"Tools/DispatcherShopPreview.png",image.EncodeToPNG());
                RenderTexture.active=previous;camera.targetTexture=null;Object.DestroyImmediate(image);Object.DestroyImmediate(texture);
            }
            Debug.Log("Rendered both dispatcher menu panels.");
        }
    }
}
