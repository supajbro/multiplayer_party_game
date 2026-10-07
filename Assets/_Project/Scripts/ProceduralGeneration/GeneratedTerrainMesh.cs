using UnityEngine;

namespace CouchGuys.ProceduralGeneration
{
    /// <summary>Owns the transient mesh and fallback material created for generated terrain.</summary>
    [DisallowMultipleComponent]
    public sealed class GeneratedTerrainMesh : MonoBehaviour
    {
        [SerializeField] private int m_vertexCount;
        [SerializeField] private int m_triangleCount;

        private Mesh m_mesh;
        private Material m_runtimeMaterial;

        public int VertexCount => m_vertexCount;
        public int TriangleCount => m_triangleCount;

        public void Initialise(Mesh mesh, Material runtimeMaterial)
        {
            m_mesh = mesh;
            m_runtimeMaterial = runtimeMaterial;
            m_vertexCount = mesh != null ? mesh.vertexCount : 0;
            m_triangleCount = mesh != null ? mesh.triangles.Length / 3 : 0;
        }

        private void OnDestroy()
        {
            DestroyOwnedObject(m_mesh);
            DestroyOwnedObject(m_runtimeMaterial);
            m_mesh = null;
            m_runtimeMaterial = null;
        }

        private static void DestroyOwnedObject(Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }
    }
}
