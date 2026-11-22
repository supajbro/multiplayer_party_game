using System;
using UnityEngine;

namespace CouchGuys.ProceduralGeneration
{
    /// <summary>Saved pillow geometry, fitted once at placement; bounce settings for the player motor.</summary>
    [DisallowMultipleComponent]
    public sealed class JumpingPillow : MonoBehaviour
    {
        [SerializeField] private MeshFilter[] m_visualMeshes;
        [SerializeField] private MeshCollider m_surfaceCollider;
        [SerializeField, Min(0.1f)] private float m_bounceHeight = 3.5f;
        [SerializeField, Min(0f)] private float m_minimumLandingSpeed = 3f;
        [SerializeField, Range(0f, 1f)] private float m_minimumUpNormal = 0.55f;
        [SerializeField, Min(0.05f)] private float m_bounceCooldown = 0.2f;
        [SerializeField] private float m_groundOffset = 0.01f;

        private Mesh[] m_sources;
        private Mesh m_collisionSource;
        private Mesh[] m_fittedVisuals;
        private Mesh m_fittedCollision;

        public MeshCollider SurfaceCollider => m_surfaceCollider;
        public float BounceHeight => m_bounceHeight;
        public float MinimumLandingSpeed => m_minimumLandingSpeed;
        public float MinimumUpNormal => m_minimumUpNormal;
        public float BounceCooldown => m_bounceCooldown;

        public void FitToTerrain(Func<Vector3, float> sampleHeight)
        {
            if (sampleHeight == null) throw new ArgumentNullException(nameof(sampleHeight));
            if (m_sources == null)
            {
                m_sources = new Mesh[m_visualMeshes.Length];
                for (int i = 0; i < m_sources.Length; i++) m_sources[i] = m_visualMeshes[i].sharedMesh;
                m_collisionSource = m_surfaceCollider.sharedMesh;
                m_fittedVisuals = new Mesh[m_sources.Length];
            }
            ReleaseFittedMeshes();
            for (int i = 0; i < m_sources.Length; i++)
            {
                m_fittedVisuals[i] = FitMesh(m_sources[i], m_visualMeshes[i].transform, sampleHeight);
                m_visualMeshes[i].sharedMesh = m_fittedVisuals[i];
            }
            m_fittedCollision = FitMesh(m_collisionSource, m_surfaceCollider.transform, sampleHeight);
            m_surfaceCollider.sharedMesh = null;
            m_surfaceCollider.sharedMesh = m_fittedCollision;
        }

        private Mesh FitMesh(Mesh source, Transform meshTransform, Func<Vector3, float> sampleHeight)
        {
            // Instance the saved mesh; preserve topology, material regions, UVs and vertex mask.
            Mesh fitted = Instantiate(source);
            fitted.name = source.name + " (terrain fitted)";
            Vector3[] vertices = source.vertices;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 world = meshTransform.TransformPoint(vertices[i]);
                world.y += sampleHeight(world) - transform.position.y + m_groundOffset;
                vertices[i] = meshTransform.InverseTransformPoint(world);
            }
            fitted.vertices = vertices;
            fitted.RecalculateNormals();
            fitted.RecalculateBounds();
            return fitted;
        }

        private void OnDestroy() => ReleaseFittedMeshes();

        private void ReleaseFittedMeshes()
        {
            if (m_fittedVisuals != null)
                for (int i = 0; i < m_fittedVisuals.Length; i++)
                {
                    Release(m_fittedVisuals[i]);
                    m_fittedVisuals[i] = null;
                }
            Release(m_fittedCollision);
            m_fittedCollision = null;
        }

        private static void Release(Mesh mesh)
        {
            if (mesh == null) return;
            if (Application.isPlaying) Destroy(mesh);
            else DestroyImmediate(mesh);
        }
    }
}
