using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CouchGuys.Gameplay.Healing;
using CouchGuys.Gameplay.Shop;
using CouchGuys.Gameplay.Weapons;
using FishNet.Component.Transforming;
using FishNet.Managing.Object;
using FishNet.Object;
using GameKit.Dependencies.Utilities;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CouchGuys.Editor
{
    // Explicit authoring only: existing assets and Inspector edits are preserved.
    public static class HealingItemAuthoring
    {
        private const string Folder = "Assets/_Project/Settings/Healing";
        private const string Prefabs = "Assets/_Project/Prefabs/Healing";
        [MenuItem("Couch Guys/Healing Items/Install Missing Assets %#&F2")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play mode first.");
            Directory.CreateDirectory(Folder); Directory.CreateDirectory(Prefabs);
            Directory.CreateDirectory("Assets/_Project/Resources/Healing");
            AssetDatabase.Refresh();
            string[] names = { "EmergencyDuctTape", "ProbablyFineRepairSpray", "PremiumRobotOil" };
            string[] display = { "Emergency Duct Tape", "Probably-Fine Repair Spray", "Premium Robot Oil" };
            float[] percentages = { 10, 50, 100 }, chances = { .025f, .0075f, .0005f }, durations = { 1f, 2f, 3f };
            int[] prices = { 25, 100, 250 };
            Color[] colours = { new(.2f,.18f,.85f), new(.12f,.85f,.4f), new(.65f,.9f,.05f) };
            var settings = new HealingItemSettings[3];
            var shopItems = new ShopItemDefinition[3];
            for (int i = 0; i < 3; i++)
            {
                string path = $"{Folder}/{names[i]}.asset";
                settings[i] = AssetDatabase.LoadAssetAtPath<HealingItemSettings>(path);
                if (settings[i] == null)
                {
                    settings[i] = ScriptableObject.CreateInstance<HealingItemSettings>();
                    AssetDatabase.CreateAsset(settings[i], path);
                    var data = new SerializedObject(settings[i]);
                    data.FindProperty("m_displayName").stringValue = display[i];
                    data.FindProperty("m_healingPercentage").floatValue = percentages[i];
                    data.FindProperty("m_purchasePrice").intValue = prices[i];
                    data.FindProperty("m_dropChance").floatValue = chances[i];
                    data.FindProperty("m_useDuration").floatValue = durations[i];
                    data.FindProperty("m_icon").objectReferenceValue = CreateIcon(names[i], colours[i], i);
                    data.ApplyModifiedPropertiesWithoutUndo();
                }
                var material = AssetDatabase.LoadAssetAtPath<Material>($"{Folder}/{names[i]}.mat");
                if (material == null)
                {
                    material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                    material.color = colours[i];
                    AssetDatabase.CreateAsset(material, $"{Folder}/{names[i]}.mat");
                }
                string pickupPath = $"{Prefabs}/{names[i]}Pickup.prefab";
                var pickup = AssetDatabase.LoadAssetAtPath<GameObject>(pickupPath);
                if (pickup == null) pickup = CreatePickup(pickupPath, settings[i], material, i);
                var itemData = new SerializedObject(settings[i]);
                if (itemData.FindProperty("m_icon").objectReferenceValue == null)
                    itemData.FindProperty("m_icon").objectReferenceValue = CreateIcon(names[i], colours[i], i);
                if (itemData.FindProperty("m_pickupPrefab").objectReferenceValue == null)
                    itemData.FindProperty("m_pickupPrefab").objectReferenceValue = pickup.GetComponent<WeaponPickup>();
                var effect = CreateEffect(names[i], colours[i]);
                foreach (string field in new[] { "m_useEffectPrefab", "m_healEffectPrefab" })
                    if (itemData.FindProperty(field).objectReferenceValue == null) itemData.FindProperty(field).objectReferenceValue = effect;
                if (itemData.FindProperty("m_healAudio").objectReferenceValue == null)
                    itemData.FindProperty("m_healAudio").objectReferenceValue = CreateAudio(names[i], i);
                itemData.ApplyModifiedPropertiesWithoutUndo();
                Register(pickup);
                string shopPath = $"{Folder}/{names[i]}Shop.asset";
                shopItems[i] = AssetDatabase.LoadAssetAtPath<ShopItemDefinition>(shopPath);
                if (shopItems[i] == null)
                {
                    shopItems[i] = ScriptableObject.CreateInstance<ShopItemDefinition>();
                    var data = new SerializedObject(shopItems[i]);
                    data.FindProperty("m_itemId").stringValue = names[i];
                    data.FindProperty("m_displayName").stringValue = display[i];
                    data.FindProperty("m_description").stringValue = $"Restores {percentages[i]}% of maximum health. Select slot and click to use.";
                    data.FindProperty("m_category").enumValueIndex = (int)ShopItemCategory.Equipment;
                    data.FindProperty("m_purchaseBehaviour").objectReferenceValue = settings[i];
                    data.ApplyModifiedPropertiesWithoutUndo();
                    AssetDatabase.CreateAsset(shopItems[i], shopPath);
                }
            }
            string cataloguePath = "Assets/_Project/Resources/Healing/HealingItems.asset";
            if (AssetDatabase.LoadAssetAtPath<HealingItemCatalogue>(cataloguePath) == null)
            {
                var catalogue = ScriptableObject.CreateInstance<HealingItemCatalogue>();
                var data = new SerializedObject(catalogue); var items = data.FindProperty("m_items"); items.arraySize = 3;
                for (int i = 0; i < 3; i++) items.GetArrayElementAtIndex(i).objectReferenceValue = settings[i];
                data.ApplyModifiedPropertiesWithoutUndo(); AssetDatabase.CreateAsset(catalogue, cataloguePath);
            }
            var shop = new SerializedObject(AssetDatabase.LoadAssetAtPath<ShopCatalogue>(ShopAssetBuilder.CataloguePath));
            var list = shop.FindProperty("m_items");
            foreach (var item in shopItems)
            {
                bool present = false;
                for (int j = 0; j < list.arraySize; j++) if (list.GetArrayElementAtIndex(j).objectReferenceValue == item) present = true;
                if (!present) { int j = list.arraySize++; list.GetArrayElementAtIndex(j).objectReferenceValue = item; }
            }
            shop.ApplyModifiedPropertiesWithoutUndo();
            var player = PrefabUtility.LoadPrefabContents("Assets/_Project/Prefabs/Player.prefab");
            try
            {
                if (player.GetComponent<HealingItem>() == null)
                {
                    var healing = player.AddComponent<HealingItem>();
                    var no = player.GetComponent<NetworkObject>();
                    var data = new SerializedObject(no); var behaviours = data.FindProperty("NetworkBehaviours");
                    int index = behaviours.arraySize++; behaviours.GetArrayElementAtIndex(index).objectReferenceValue = healing;
                    data.ApplyModifiedPropertiesWithoutUndo(); Configure(healing, no, index);
                    PrefabUtility.SaveAsPrefabAsset(player, "Assets/_Project/Prefabs/Player.prefab");
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(player); }
            AssetDatabase.SaveAssets();
            Debug.Log("Healing items installed: three settings, shop entries, registered pickups and player action.");
        }
        private static void Register(GameObject prefab)
        {
            var no = prefab.GetComponent<NetworkObject>();
            string stableName = new(($"{AssetDatabase.GetAssetPath(prefab)}{prefab.name}".ToLowerInvariant()).Where(char.IsLetterOrDigit).ToArray());
            no.SetAssetPathHash(stableName.GetStableHashU64()); EditorUtility.SetDirty(no);
            foreach (string path in new[] { "Assets/_Project/Settings/NetworkSpawnablePrefabs.asset", "Assets/DefaultPrefabObjects.asset" })
            {
                var collection = AssetDatabase.LoadAssetAtPath<SinglePrefabObjects>(path);
                if (collection != null && !collection.Prefabs.Contains(no)) { collection.AddObject(no, false, false); EditorUtility.SetDirty(collection); }
            }
        }
        private static void Configure(NetworkBehaviour behaviour, NetworkObject no, int index)
        {
            var data = new SerializedObject(behaviour);
            data.FindProperty("_componentIndexCache").intValue = index;
            data.FindProperty("_addedNetworkObject").objectReferenceValue = no;
            data.FindProperty("_networkObjectCache").objectReferenceValue = no;
            data.ApplyModifiedPropertiesWithoutUndo();
        }
        private static GameObject CreatePickup(string path, HealingItemSettings settings, Material material, int kind)
        {
            var root = new GameObject(settings.name + "Pickup");
            try
            {
                var no = root.AddComponent<NetworkObject>();
                var rb = root.AddComponent<Rigidbody>(); rb.mass = .3f; rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                var nt = root.AddComponent<NetworkTransform>(); var pickup = root.AddComponent<WeaponPickup>();
                var data = new SerializedObject(pickup); data.FindProperty("m_healingItem").objectReferenceValue = settings; data.ApplyModifiedPropertiesWithoutUndo();
                var collider = root.AddComponent<BoxCollider>(); collider.size = new Vector3(.45f,.5f,.35f);
                if (kind == 0)
                {
                    // A recognisable hollow tape roll made from saved primitive pieces.
                    for (int segment = 0; segment < 16; segment++)
                    {
                        float angle = segment * Mathf.PI * 2 / 16;
                        var piece = Part(root, PrimitiveType.Cube, material, new Vector3(Mathf.Cos(angle)*.17f,0,Mathf.Sin(angle)*.17f), new Vector3(.08f,.16f,.09f));
                        piece.transform.localRotation = Quaternion.Euler(0,-segment*22.5f,0);
                    }
                }
                else if (kind == 1)
                {
                    Part(root, PrimitiveType.Cylinder, material, Vector3.zero, new Vector3(.24f,.2f,.24f));
                    Part(root, PrimitiveType.Cylinder, material, Vector3.up*.24f, new Vector3(.1f,.04f,.1f));
                    Part(root, PrimitiveType.Cube, material, new Vector3(0,.27f,.07f), new Vector3(.08f,.06f,.16f));
                }
                else
                {
                    Part(root, PrimitiveType.Cube, material, Vector3.zero, new Vector3(.3f,.36f,.15f));
                    Part(root, PrimitiveType.Cube, material, new Vector3(0,.23f,0), new Vector3(.2f,.1f,.1f));
                    var spout = Part(root, PrimitiveType.Cylinder, material, new Vector3(-.19f,.24f,0), new Vector3(.05f,.13f,.05f));
                    spout.transform.localRotation = Quaternion.Euler(0,0,60);
                }
                var transformData = new SerializedObject(nt);
                transformData.FindProperty("_clientAuthoritative").boolValue = false;
                transformData.FindProperty("_componentConfiguration").enumValueIndex = (int)NetworkTransform.ComponentConfigurationType.Rigidbody;
                transformData.ApplyModifiedPropertiesWithoutUndo();
                var network = new SerializedObject(no); var list = network.FindProperty("NetworkBehaviours"); list.arraySize = 2;
                list.GetArrayElementAtIndex(0).objectReferenceValue = nt; list.GetArrayElementAtIndex(1).objectReferenceValue = pickup;
                network.ApplyModifiedPropertiesWithoutUndo(); Configure(nt,no,0); Configure(pickup,no,1);
                return PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally { Object.DestroyImmediate(root); }
        }
        private static GameObject Part(GameObject parent, PrimitiveType shape, Material material, Vector3 position, Vector3 scale)
        {
            var part = GameObject.CreatePrimitive(shape); part.transform.SetParent(parent.transform, false);
            part.transform.localPosition = position; part.transform.localScale = scale;
            part.GetComponent<Renderer>().sharedMaterial = material; Object.DestroyImmediate(part.GetComponent<Collider>()); return part;
        }
        private static GameObject CreateEffect(string name, Color colour)
        {
            string path = $"{Prefabs}/{name}Effect.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path); if (existing != null) return existing;
            var root = new GameObject(name + "Effect");
            try
            {
                var ps = root.AddComponent<ParticleSystem>();
                var main = ps.main; main.duration = .8f; main.loop = false; main.startLifetime = .8f; main.startSpeed = .45f;
                main.startSize = .16f; main.startColor = colour; main.maxParticles = 24;
                var emission = ps.emission; emission.rateOverTime = 0; emission.SetBursts(new[] { new ParticleSystem.Burst(0, 18) });
                var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = .4f;
                root.GetComponent<ParticleSystemRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Prefabs/Weapons/Materials/Particle_Smoke.mat");
                return PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally { Object.DestroyImmediate(root); }
        }
        private static AudioClip CreateAudio(string name, int kind)
        {
            string path = $"{Folder}/{name}Heal.wav";
            if (!File.Exists(path))
            {
                const int rate = 22050, samples = 6615;
                using (var writer = new BinaryWriter(File.Create(path)))
                {
                    writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + samples*2);
                    writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
                    writer.Write((short)1); writer.Write((short)1); writer.Write(rate); writer.Write(rate*2);
                    writer.Write((short)2); writer.Write((short)16); writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(samples*2);
                    for (int n=0;n<samples;n++)
                    {
                        float t=n/(float)rate, envelope=Mathf.Sin(Mathf.PI*n/samples);
                        writer.Write((short)(Mathf.Sin(2*Mathf.PI*(440+kind*160)*t)*envelope*3500));
                    }
                }
                AssetDatabase.ImportAsset(path);
            }
            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }
        private static Sprite CreateIcon(string name, Color colour, int kind)
        {
            string path = $"{Folder}/{name}Icon.png";
            var texture = new Texture2D(64,64,TextureFormat.RGBA32,false);
            for (int y=0;y<64;y++) for (int x=0;x<64;x++)
            {
                float radius = Vector2.Distance(new Vector2(x,y),new Vector2(32,32));
                bool inside = kind == 0 ? radius > 12 && radius < 27 : kind == 1 ? x>19 && x<45 && y>5 && y<53 : x>13 && x<51 && y>6 && y<51;
                texture.SetPixel(x,y,inside ? colour : Color.clear);
            }
            texture.Apply(); File.WriteAllBytes(path,texture.EncodeToPNG()); Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path); importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 64; importer.alphaIsTransparency = true; importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
    }
}


