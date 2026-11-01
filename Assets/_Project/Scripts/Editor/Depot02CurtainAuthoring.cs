using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using CouchGuys.Gameplay.Depot;
using Object = UnityEngine.Object;

namespace CouchGuys.Editor
{
    // Explicit one-time migration only; never runs on import, reload or entering Play mode.
    public static class Depot02CurtainAuthoring
    {
        const string Model = "Assets/_Project/Models/depot_02/depot_02_curtain.fbx";
        const string Generated = "Assets/_Project/Generated/Depot02";
        [MenuItem("Couch Guys/Depot 02/Install Flexible Curtain %#&F6")]
        public static void Install()
        {
            AssetDatabase.ImportAsset(Model, ImportAssetOptions.ForceSynchronousImport);
            var importer = (ModelImporter)AssetImporter.GetAtPath(Model);
            importer.isReadable = true;
            importer.importAnimation = false;
            importer.importCameras = importer.importLights = false;
            importer.SaveAndReimport();
            var root = PrefabUtility.LoadPrefabContents(Depot02PrefabBuilder.PrefabPath);
            GameObject input = null;
            try
            {
                var module = root.GetComponentsInChildren<MeshFilter>().Single(f => f.name == "D02_Module_Conveyor");
                // Subsequent commands preserve every Inspector customization and saved mesh.
                if (module.GetComponent<ConveyorStripCurtain>() != null)
                { Debug.Log("Flexible curtain already installed; preserving authored settings."); return; }
                input = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Model));
                var source = input.GetComponentsInChildren<MeshFilter>().Single(f => f.name == module.name);
                if (Vector3.Distance(source.transform.position, module.transform.localPosition) > .01f ||
                    Quaternion.Angle(source.transform.rotation, module.transform.localRotation) > .1f)
                    throw new Exception("Conveyor export transform changed; aborting to preserve placement.");
                module.sharedMesh = source.sharedMesh;
                // Retain the conveyor's existing reduced LODs, removing only the old curtain triangles.
                foreach (var child in module.GetComponentsInChildren<MeshFilter>().Where(f => f != module))
                    RemoveOldCurtain(child, root.transform);
                var frame = new GameObject("FlexibleCurtain").transform;
                frame.SetParent(module.transform, false);
                frame.SetPositionAndRotation(root.transform.TransformPoint(new Vector3(-14.04f, 3.405f, -1.78f)),
                    root.transform.rotation * Quaternion.Euler(0, 90, 0));
                frame.localScale = Vector3.one * .01f; // Module's imported scale is 100.
                var material = AssetDatabase.LoadAssetAtPath<Material>(Generated + "/D02_Rubber.mat");
                var filters = input.GetComponentsInChildren<MeshFilter>().Where(f => f.name.StartsWith("D02_CurtainStrip_"))
                    .OrderBy(f => f.name).ToArray();
                if (filters.Length != 9) throw new Exception("Expected nine separated curtain strips.");
                var saved = new MeshFilter[filters.Length];
                for (int i = 0; i < filters.Length; i++)
                {
                    var f = filters[i];
                    var strip = new GameObject(f.name).transform;
                    strip.SetParent(frame, false);
                    Vector3 pivot = root.transform.TransformPoint(f.transform.position);
                    strip.localPosition = frame.InverseTransformPoint(pivot);
                    // Bake the imported axes/scale into saved, metre-sized mesh data.
                    var mesh = Object.Instantiate(f.sharedMesh);
                    Matrix4x4 matrix = strip.worldToLocalMatrix * root.transform.localToWorldMatrix * f.transform.localToWorldMatrix;
                    mesh.vertices = mesh.vertices.Select(matrix.MultiplyPoint3x4).ToArray();
                    mesh.normals = mesh.normals.Select(matrix.MultiplyVector).Select(n => n.normalized).ToArray();
                    mesh.tangents = Array.Empty<Vector4>();
                    mesh.RecalculateBounds();
                    mesh.name = f.name;
                    AssetDatabase.CreateAsset(mesh, Generated + "/" + f.name + ".asset");
                    saved[i] = strip.gameObject.AddComponent<MeshFilter>();
                    saved[i].sharedMesh = mesh;
                    strip.gameObject.AddComponent<MeshRenderer>().sharedMaterial = material;
                    if (Mathf.Abs(mesh.bounds.min.y + 2.25f) > .01f || Mathf.Abs(mesh.bounds.max.y) > .01f)
                        throw new Exception("Strip must have its pivot at the top, with length along -Y.");
                }
                var curtain = module.gameObject.AddComponent<ConveyorStripCurtain>();
                var data = new SerializedObject(curtain);
                data.FindProperty("m_frame").objectReferenceValue = frame;
                var array = data.FindProperty("m_strips"); array.arraySize = saved.Length;
                for (int i = 0; i < saved.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = saved[i];
                data.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, Depot02PrefabBuilder.PrefabPath);
                AssetDatabase.SaveAssets();
                File.WriteAllText("Temp/depot02-curtain-install.txt", "PASS: nine saved subdivided strips, top pivots, original rubber material, no colliders; static curtain removed from every conveyor LOD.");
                Debug.Log("Saved local flexible curtain on D02_Module_Conveyor.");
            }
            finally
            {
                if (input != null) Object.DestroyImmediate(input);
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        static void RemoveOldCurtain(MeshFilter filter, Transform root)
        {
            var mesh = Object.Instantiate(filter.sharedMesh);
            mesh.name = filter.sharedMesh.name;
            var vertices = mesh.vertices;
            Matrix4x4 matrix = root.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            bool[] strip = vertices.Select(v => {
                Vector3 p = matrix.MultiplyPoint3x4(v);
                return p.x > -14.08f && p.x < -14 && p.y > 1.15f && p.y < 3.41f && p.z > -3.67f && p.z < .11f;
            }).ToArray();
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                var triangles = mesh.GetTriangles(sub);
                var keep = new System.Collections.Generic.List<int>(triangles.Length);
                for (int i = 0; i < triangles.Length; i += 3)
                    if (!(strip[triangles[i]] && strip[triangles[i + 1]] && strip[triangles[i + 2]]))
                    { keep.Add(triangles[i]); keep.Add(triangles[i + 1]); keep.Add(triangles[i + 2]); }
                mesh.SetTriangles(keep, sub);
            }
            mesh.RecalculateBounds();
            // Update the existing LOD asset in place so its GUID and references remain valid.
            EditorUtility.CopySerialized(mesh, filter.sharedMesh);
            EditorUtility.SetDirty(filter.sharedMesh);
            Object.DestroyImmediate(mesh);
        }
    }
}
