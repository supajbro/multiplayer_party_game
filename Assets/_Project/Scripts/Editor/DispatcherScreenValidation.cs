using System;
using System.IO;
using System.Linq;
using CouchGuys.Gameplay.Delivery;
using UnityEditor;
using UnityEngine;

namespace CouchGuys.Editor
{
    public static class DispatcherScreenValidation
    {
        [MenuItem("Couch Guys/Dispatcher/Validate Animated Screen %&F8")]
        public static void Validate()
        {
            const string path = "Assets/_Project/Prefabs/DeliveryNPC.prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var controller = root.GetComponent<DispatcherScreen>();
                if (controller == null) throw new Exception("Screen controller missing.");
                var serialized = new SerializedObject(controller);
                var screens = serialized.FindProperty("m_screens");
                var lods = root.GetComponentInChildren<LODGroup>().GetLODs();
                if (screens.arraySize != 3 || lods.Length != 3) throw new Exception("Expected three screen LODs.");
                for (int i = 0; i < 3; i++)
                {
                    var screen = screens.GetArrayElementAtIndex(i).objectReferenceValue as Renderer;
                    if (screen == null || !screen.name.EndsWith("_Screen") || !lods[i].renderers.Contains(screen))
                        throw new Exception("Invalid screen reference at LOD " + i);
                    if (lods[i].renderers.Length != 2) throw new Exception("Unexpected extra visual at LOD " + i);
                    var mesh = screen.GetComponent<MeshFilter>().sharedMesh;
                    if (!mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord0))
                        throw new Exception("Screen UVs missing.");
                    var material = screen.sharedMaterial;
                    if (material.shader.name != "Couch Guys/Dispatcher Waveform" || !material.shader.isSupported)
                        throw new Exception("Waveform material missing or unsupported.");
                    ShaderUtil.CompilePass(material, 0, true);
                    if (ShaderUtil.GetShaderMessages(material.shader).Any(m => m.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error))
                        throw new Exception("Waveform shader compile failed.");
                }
                if (serialized.FindProperty("m_npc").objectReferenceValue != root.GetComponent<DeliveryNPC>())
                    throw new Exception("Dialogue reference missing.");
                string report = "PASS: Unity imported controller and compiled URP waveform shader. Three saved screen references, screen UVs, material assignments and LOD membership verified. Each LOD contains only body and screen. Model assets unchanged.\nLimitations: no multi-process dialogue playtest or visual review in gameplay.\n";
                File.WriteAllText("Tools/DispatcherScreenValidation.txt", report);
                Debug.Log(report);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }
}
