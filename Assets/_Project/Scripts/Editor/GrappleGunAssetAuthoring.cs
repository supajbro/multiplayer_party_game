using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using CouchGuys.Gameplay.Weapons;

namespace CouchGuys.Editor
{
    // Explicit authoring only: never rebuilds assets on import, reload or Play mode.
    public static class GrappleGunAssetAuthoring
    {
        private const string Folder = "Assets/_Project/Prefabs/Weapons";
        private const string ModelPath = "Assets/_Project/Models/Weapons/GrappleGun/CG_GrappleGun.fbx";
        private const string VisualPath = Folder + "/GrappleGun.prefab";
        private const string ConfigPath = "Assets/_Project/Resources/Weapons/GrappleGunAssets.asset";
        private const string PlayerPath = "Assets/_Project/Prefabs/Player.prefab";

        [MenuItem("Couch Guys/Weapons/Integrate Player Grapple Gun %#&j")]
        public static void Integrate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new UnityException("Exit Play mode before authoring assets.");
            var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
            importer.importAnimation = false;
            importer.SaveAndReimport();
            if (AssetDatabase.LoadAssetAtPath<GameObject>(VisualPath) == null)
            {
                string error = AssetDatabase.MoveAsset("Assets/_Project/Resources/Weapons/GrappleGunModel.prefab", VisualPath);
                if (!string.IsNullOrEmpty(error)) throw new UnityException(error);
                BuildVisual();
            }
            var config = AssetDatabase.LoadAssetAtPath<GrappleGunAssets>(ConfigPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<GrappleGunAssets>();
                AssetDatabase.CreateAsset(config, ConfigPath);
            }
            var data = new SerializedObject(config);
            data.FindProperty("m_equippedPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(VisualPath);
            data.FindProperty("m_pickupPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Resources/WeaponPickups/GrappleGun.prefab");
            data.ApplyModifiedPropertiesWithoutUndo();
            AttachPlayer();
            AssetDatabase.SaveAssets();
            Verify();
        }

        private static void BuildVisual()
        {
            var root = new GameObject("Weapon_GrappleGun");
            try
            {
                var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath), root.transform);
                var all = model.GetComponentsInChildren<Transform>(true);
                foreach (var t in all) t.gameObject.SetActive(true);
                Transform claw = all.Single(t => t.name == "Claw_LOD0");
                Transform spool = all.Single(t => t.name == "Spool_LOD0");
                Transform body = all.Single(t => t.name == "Body_LOD0");
                Vector3 forward = (claw.position - spool.position).normalized;
                Vector3 up = Vector3.ProjectOnPlane(spool.position - body.position, forward).normalized;
                model.transform.rotation = Quaternion.Inverse(Quaternion.LookRotation(forward, up)) * model.transform.rotation;
                var renderers = model.GetComponentsInChildren<MeshRenderer>(true);
                Bounds bounds = renderers[0].bounds;
                foreach (var r in renderers) bounds.Encapsulate(r.bounds);
                model.transform.localScale *= .63f / bounds.size.z;
                var muzzle = new GameObject("Muzzle").transform;
                muzzle.SetParent(root.transform, false);
                muzzle.localPosition = root.transform.InverseTransformPoint(claw.position) + Vector3.forward * .0375f;
                var materials = new System.Collections.Generic.Dictionary<string, Material>();
                foreach (var r in renderers)
                {
                    r.enabled = true;
                    r.sharedMaterials = r.sharedMaterials.Select(m => GrappleMaterial(m.name, materials)).ToArray();
                }
                foreach (var nested in model.GetComponentsInChildren<LODGroup>(true)) nested.enabled = false;
                var group = root.AddComponent<LODGroup>();
                var lods = new LOD[3];
                for (int i = 0; i < 3; i++)
                {
                    var rs = renderers.Where(r => r.name.EndsWith("_LOD" + i)).Cast<Renderer>().ToArray();
                    if (rs.Length != 4) throw new UnityException("Expected body, claw, spool and trigger in each LOD.");
                    lods[i] = new LOD(new[] { .35f, .12f, .015f }[i], rs);
                }
                group.SetLODs(lods);
                group.RecalculateBounds();
                PrefabUtility.SaveAsPrefabAsset(root, VisualPath);
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static Material GrappleMaterial(string name, System.Collections.Generic.Dictionary<string, Material> cache)
        {
            name = name.Replace(" (Instance)", "");
            if (cache.TryGetValue(name, out var existing)) return existing;
            string path = Folder + "/Materials/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(AssetDatabase.LoadAssetAtPath<Material>(Folder + "/Materials/CG_Weapon_Yellow.mat")) { name = name };
                Color color = name.EndsWith("Orange") ? new Color(1f,.52f,.025f) :
                    name.EndsWith("Yellow") ? new Color(1f,.76f,.035f) :
                    name.EndsWith("Steel") ? new Color(.42f,.48f,.57f) :
                    name.EndsWith("Trigger") ? new Color(.9f,.065f,.025f) :
                    name.EndsWith("CouchGlow") ? new Color(.35f,.8f,1f) :
                    name.EndsWith("Rope") ? new Color(.07f,.085f,.11f) : new Color(.025f,.035f,.05f);
                mat.SetColor("_BaseColor", color);
                mat.SetColor("_EmissionColor", name.EndsWith("CouchGlow") ? color * 2f : Color.black);
                mat.SetFloat("_SpecularIntensity", name.EndsWith("Rubber") || name.EndsWith("Rope") ? .08f : .45f);
                AssetDatabase.CreateAsset(mat, path);
            }
            cache[name] = mat;
            return mat;
        }

        private static void AttachPlayer()
        {
            var root = PrefabUtility.LoadPrefabContents(PlayerPath);
            try
            {
                var data = new SerializedObject(root.GetComponent<PlayerWeaponController>());
                var models = data.FindProperty("m_weaponModels");
                Transform pistol = (Transform)models.GetArrayElementAtIndex(0).objectReferenceValue;
                var old = pistol.parent.Find("Weapon_GrappleGun") ?? pistol.parent.Find("GrappleGun");
                if (old != null) Object.DestroyImmediate(old.gameObject);
                var gun = ((GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(VisualPath), pistol.parent)).transform;
                gun.name = "Weapon_GrappleGun";
                gun.localPosition = pistol.localPosition;
                gun.rotation = Quaternion.LookRotation(pistol.TransformDirection(Vector3.down), pistol.TransformDirection(Vector3.forward));
                Vector3 s = pistol.parent.lossyScale;
                gun.localScale = new Vector3(1f / Mathf.Abs(s.x), 1f / Mathf.Abs(s.y), 1f / Mathf.Abs(s.z));
                gun.gameObject.SetActive(false);
                models.arraySize = 4;
                models.GetArrayElementAtIndex(3).objectReferenceValue = gun;
                var muzzles = data.FindProperty("m_muzzles");
                muzzles.arraySize = 4;
                muzzles.GetArrayElementAtIndex(3).objectReferenceValue = gun.Find("Muzzle");
                data.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, PlayerPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        [MenuItem("Couch Guys/Weapons/Verify Player Grapple Gun %#&u")]
        public static void Verify()
        {
            var player = PrefabUtility.LoadPrefabContents(PlayerPath);
            try
            {
                var data = new SerializedObject(player.GetComponent<PlayerWeaponController>());
                var gun = (Transform)data.FindProperty("m_weaponModels").GetArrayElementAtIndex(3).objectReferenceValue;
                var muzzle = (Transform)data.FindProperty("m_muzzles").GetArrayElementAtIndex(3).objectReferenceValue;
                if (gun == null || muzzle == null || !muzzle.IsChildOf(gun)) throw new UnityException("Grapple references are missing.");
                if (gun.GetComponentsInChildren<Rigidbody>(true).Length != 0 || gun.GetComponentsInChildren<Collider>(true).Length != 0)
                    throw new UnityException("Equipped grapple must have no physics components.");
                var lods = gun.GetComponent<LODGroup>().GetLODs();
                if (lods.Length != 3 || lods.Any(l => l.renderers.Length != 4)) throw new UnityException("Grapple LODs are incomplete.");
                if (gun.GetComponentsInChildren<MeshFilter>(true).Any(m => m.sharedMesh == null || m.sharedMesh.name == "Cube"))
                    throw new UnityException("Missing mesh or placeholder cube.");
                string output = Application.dataPath + "/../Temp/GrappleAuthoring/player-integration.txt";
                Directory.CreateDirectory(Path.GetDirectoryName(output));
                File.WriteAllText(output, $"PASS: saved player grapple, 3 LODs / 12 mesh parts, no equipped physics.\nHand: {gun.parent.name}\nScale: {gun.lossyScale:F4}\nMuzzle: {muzzle.position:F4}\nPrefab: {PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(gun.gameObject)}");
                RenderPreview(player, Path.ChangeExtension(output, ".png"));
                Debug.Log("Player grapple model integrated and verified.");
            }
            finally { PrefabUtility.UnloadPrefabContents(player); }
        }

        private static void RenderPreview(GameObject player, string output)
        {
            var preview = new PreviewRenderUtility();
            try
            {
                var instance = Object.Instantiate(player);
                preview.AddSingleGO(instance);
                var animator = instance.GetComponentsInChildren<Animator>(true).FirstOrDefault(a => a.runtimeAnimatorController != null);
                var clips = animator != null ? animator.runtimeAnimatorController.animationClips : new AnimationClip[0];
                File.AppendAllText(Path.ChangeExtension(output, ".txt"), "\nClips: " + string.Join(", ", clips.Select(c => c.name)));
                var clip = clips.FirstOrDefault(c => c.name.Contains("Pistol") && c.name.Contains("Idle"));
                if (clip != null) clip.SampleAnimation(animator.gameObject, 0f);
                foreach (var t in instance.GetComponentsInChildren<Transform>(true))
                    if (t.name == "Weapon_GrappleGun") t.gameObject.SetActive(true);
                    else if (t.name == "Weapon_Pistol" || t.name == "Weapon_AssaultRifle" || t.name == "Weapon_Shotgun") t.gameObject.SetActive(false);
                var renderers = instance.GetComponentsInChildren<Renderer>().Where(r => r.enabled && (r is MeshRenderer || r is SkinnedMeshRenderer)).ToArray();
                Bounds bounds = renderers[0].bounds;
                foreach (var r in renderers) bounds.Encapsulate(r.bounds);
                preview.camera.fieldOfView = 30f;
                preview.camera.nearClipPlane = .01f;
                preview.camera.farClipPlane = 1000f;
                preview.camera.transform.position = bounds.center + new Vector3(1.5f,.6f,2.5f).normalized * bounds.size.magnitude * 1.65f;
                preview.camera.transform.LookAt(bounds.center);
                preview.camera.clearFlags = CameraClearFlags.Color;
                preview.camera.backgroundColor = new Color(.08f,.10f,.14f);
                preview.lights[0].intensity = 1.2f;
                preview.lights[0].transform.rotation = Quaternion.Euler(35, -40, 0);
                preview.lights[1].intensity = .7f;
                preview.ambientColor = new Color(.4f,.4f,.4f);
                preview.BeginStaticPreview(new Rect(0,0,900,900));
                preview.Render(true);
                var image = preview.EndStaticPreview();
                File.WriteAllBytes(output, image.EncodeToPNG());
                Object.DestroyImmediate(image);
            }
            finally { preview.Cleanup(); }
        }
    }
}
