using System;
using System.IO;
using System.Linq;
using CouchGuys.Gameplay.Couch;
using CouchGuys.Gameplay.Sled;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CouchGuys.Editor
{
    // Explicit menu commands only. Never modifies authored assets on import/reload.
    public static class FourCouchSledAuthoring
    {
        private const string PrefabPath = "Assets/_Project/Prefabs/Vehicles/Sled.prefab";
        private const string ModelPath = "Assets/_Project/Models/Sled/CouchGuys_4Couch_Sled.fbx";
        private static readonly string[] Corners = { "Front_Left", "Front_Right", "Rear_Left", "Rear_Right" };

        [MenuItem("Couch Guys/Vehicles/Apply Four-Couch Sled Model")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode before editing the sled prefab.");
            RemapMaterials();
            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                Transform visual = root.transform.Find("Visual");
                if (visual == null) throw new InvalidOperationException("Sled Visual root is missing.");
                // Only the explicitly replaced visual subtree is changed.
                foreach (Transform old in visual.Cast<Transform>().ToArray()) Object.DestroyImmediate(old.gameObject);
                var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath), visual);
                model.name = "CouchGuys_4Couch_Sled";
                model.transform.localPosition = Vector3.zero;
                model.transform.localScale = Vector3.one;
                Transform Named(string name) => model.GetComponentsInChildren<Transform>(true).Single(t => t.name == name);
                Vector3 forward = Named("Grab_Handle_Front_Left").position - Named("Grab_Handle_Rear_Left").position;
                Vector3 up = (Named("Magnetic_Dock_Front_Left").position + Named("Magnetic_Dock_Rear_Right").position) * .5f -
                    (Named("Left_Runner").position + Named("Right_Runner").position) * .5f;
                model.transform.rotation = Quaternion.Inverse(Quaternion.LookRotation(forward, up)) * model.transform.rotation;
                if (root.transform.InverseTransformPoint(Named("Grab_Handle_Front_Left").position).x > 0f)
                    model.transform.localScale = new Vector3(-1f, 1f, 1f);

                var sled = root.GetComponent<SledController>();
                var so = new SerializedObject(sled);
                Transform anchor = root.transform.Find("CouchAnchor");
                anchor.localPosition = new Vector3(0f, .93f, 0f);
                anchor.localRotation = Quaternion.identity;
                var docks = so.FindProperty("m_couchDocks");
                docks.arraySize = 4;
                for (int i = 0; i < 4; i++)
                {
                    Transform dockMesh = Named("Magnetic_Dock_" + Corners[i]);
                    Vector3 pad = root.transform.InverseTransformPoint(dockMesh.position);
                    Transform dock = Child(anchor, "Dock_" + Corners[i]);
                    dock.position = root.transform.TransformPoint(new Vector3(pad.x, .93f, pad.z));
                    dock.localRotation = Quaternion.identity;
                    docks.GetArrayElementAtIndex(i).objectReferenceValue = dock;
                    Transform grip = root.transform.Find("GrabPoints/GrabPoint" + Corners[i].Replace("_", ""));
                    Vector3 handle = root.transform.InverseTransformPoint(Named("Grab_Handle_" + Corners[i]).position);
                    grip.localPosition = new Vector3(handle.x, 1.58f, handle.z);
                    grip.localRotation = Quaternion.Euler(0f, handle.x < 0f ? 90f : -90f, 0f);
                }
                so.FindProperty("m_dockHalfExtents").vector2Value = new Vector2(1.16f, .60f);
                so.FindProperty("m_loadingHalfExtents").vector2Value = new Vector2(2.5f, 1.7f);
                so.FindProperty("m_unloadOffset").vector3Value = new Vector3(0f, 0f, 3.25f);
                so.ApplyModifiedPropertiesWithoutUndo();
                root.transform.Find("UnloadPoint").localPosition = new Vector3(0f, 1.05f, 3.25f);
                root.transform.Find("WeaponRack1").localPosition = new Vector3(-2.96f, .92f, -.48f);
                root.transform.Find("WeaponRack2").localPosition = new Vector3(-2.96f, .92f, .48f);
                root.transform.Find("WeaponRack3").localPosition = new Vector3(2.96f, .92f, 0f);
                FitColliders(root);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            Validate();
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            EditorGUIUtility.PingObject(Selection.activeObject);
        }

        private static Transform Child(Transform parent, string name)
        {
            var existing = parent.Find(name);
            if (existing != null) return existing;
            var child = new GameObject(name).transform;
            child.SetParent(parent, false);
            return child;
        }

        private static void FitColliders(GameObject root)
        {
            var target = root.transform.Find("CompoundColliders").gameObject;
            var boxes = target.GetComponents<BoxCollider>();
            if (boxes.Length != 6) throw new InvalidOperationException("Expected six authored compound boxes.");
            Vector3[] centres = { new(0,.835f,0), new(-2.68f,.69f,0), new(2.68f,.69f,0),
                new(0,.64f,2.04f), new(0,.64f,-2.04f), new(0,.32f,0) };
            Vector3[] sizes = { new(5.15f,.16f,3.75f), new(.28f,.45f,3.5f), new(.28f,.45f,3.5f),
                new(5.05f,.42f,.28f), new(5.05f,.42f,.28f), new(5.3f,.40f,4f) };
            for (int i=0;i<boxes.Length;i++) { boxes[i].center=centres[i];boxes[i].size=sizes[i]; }
            var feet = target.GetComponents<SphereCollider>();
            for (int i=0;i<feet.Length;i++)
            {
                feet[i].center = new Vector3(i%2==0 ? -2.22f : 2.22f, .16f, i<2 ? 1.85f : -1.85f);
                feet[i].radius = .16f;
            }
        }

        private static void RemapMaterials()
        {
            string folder="Assets/_Project/Materials/Vehicles/Hoverboard/";
            string[] source={"Sled_Porcelain","Sled_Cobalt","Sled_Graphite","Sled_Rubber","Sled_Alloy","Sled_SafetyYellow","Sled_Orange","Sled_Cyan"};
            string[] dest={folder+"Porcelain white armour.mat",folder+"Cobalt blue enamel.mat",folder+"Graphite chassis.mat",
                folder+"Soft black rubber.mat",folder+"Brushed alloy.mat",folder+"Safety amber.mat",
                "Assets/_Project/Materials/Vehicles/HoverTruck/Hover_Orange.mat",folder+"Cyan emission.mat"};
            var importer=(ModelImporter)AssetImporter.GetAtPath(ModelPath);
            for(int i=0;i<source.Length;i++)
            {
                var material=AssetDatabase.LoadAssetAtPath<Material>(dest[i]);
                if(material==null)throw new InvalidOperationException("Missing shared vehicle material: "+dest[i]);
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material),source[i]),material);
            }
            importer.importAnimation=false;
            importer.SaveAndReimport();
        }

        [MenuItem("Couch Guys/Vehicles/Validate Four-Couch Sled")]
        public static void Validate()
        {
            var root=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var sled=root.GetComponent<SledController>();
            var model=root.transform.Find("Visual/CouchGuys_4Couch_Sled");
            if(model==null || model.GetComponentsInChildren<MeshRenderer>().Length!=13)throw new InvalidOperationException("New model hierarchy missing.");
            foreach(var renderer in model.GetComponentsInChildren<MeshRenderer>())
                foreach(var material in renderer.sharedMaterials)
                    if(material==null || !AssetDatabase.GetAssetPath(material).StartsWith("Assets/_Project/Materials/Vehicles/",StringComparison.Ordinal))
                        throw new InvalidOperationException("Vehicle material remapping missing on "+renderer.name);
            if(sled.CouchDockCount!=4 || sled.MaximumCargoItems!=4)throw new InvalidOperationException("Expected four dock slots and cargo capacity.");
            var report=new System.Text.StringBuilder("Four-couch sled prefab validation\n");
            foreach(var corner in Corners)
            {
                int i=Array.IndexOf(Corners,corner);
                Transform pad=model.GetComponentsInChildren<Transform>().Single(t=>t.name=="Magnetic_Dock_"+corner);
                Vector3 dock= root.transform.InverseTransformPoint(sled.GetCouchDock(i).position);
                Vector3 padOrigin=root.transform.InverseTransformPoint(pad.position);
                if(Vector2.Distance(new Vector2(dock.x,dock.z),new Vector2(padOrigin.x,padOrigin.z))>.005f || Mathf.Abs(dock.y-.93f)>.005f)
                    throw new InvalidOperationException("Dock does not align with magnetic pad: "+corner);
                Transform handle=model.GetComponentsInChildren<Transform>().Single(t=>t.name=="Grab_Handle_"+corner);
                Vector3 grip=root.transform.InverseTransformPoint(sled.GetPoint(i).transform.position);
                Vector3 handleOrigin=root.transform.InverseTransformPoint(handle.position);
                if(Vector2.Distance(new Vector2(grip.x,grip.z),new Vector2(handleOrigin.x,handleOrigin.z))>.005f || Mathf.Abs(grip.y-1.58f)>.005f)
                    throw new InvalidOperationException("Grab point does not align with handle: "+corner);
                if((i%2==0)!=(dock.x<0f) || (i<2)!=(dock.z>0f))throw new InvalidOperationException("Corner labels/orientation incorrect: "+corner);
                if(sled.GetPoint(i).GetComponent<CouchCarryPoint>()!=sled.CarryController.GetPoint(i))throw new InvalidOperationException("Carry point references differ.");
                report.AppendLine(corner+" dock="+dock.ToString("F3")+" grip="+grip.ToString("F3"));
            }
            var tests=typeof(FourCouchSledAuthoring).Assembly.GetType("CouchGuys.Tests.Editor.SledPhysicsTests");
            tests?.GetMethod("RunCargoValidation")?.Invoke(null,null);
            report.AppendLine("PASS: model hierarchy, pad/grip alignment, four slots, shared materials and cargo validation.");
            Directory.CreateDirectory("ArtSource/Vehicles/Sled4");
            File.WriteAllText("ArtSource/Vehicles/Sled4/PrefabValidation.txt",report.ToString());
            Debug.Log(report.ToString());
        }
    }
}
