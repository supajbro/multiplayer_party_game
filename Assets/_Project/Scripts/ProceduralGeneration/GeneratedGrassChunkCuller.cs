using System;
using UnityEngine;

namespace CouchGuys.ProceduralGeneration
{
    /// <summary>
    /// Rejects grass chunks that are wholly outside the shader's draw distance. The shader
    /// still performs exact per-tuft clipping for intersecting chunks, so this only removes
    /// submissions whose complete bounds would already be invisible.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GeneratedGrassChunkCuller : MonoBehaviour
    {
        private Renderer[] m_renderers = Array.Empty<Renderer>();
        private Camera m_camera;
        private float m_drawDistanceSquared;

        public void Initialise(float drawDistance)
        {
            m_renderers = GetComponentsInChildren<Renderer>(true);
            m_drawDistanceSquared = Mathf.Max(0f, drawDistance) * Mathf.Max(0f, drawDistance);
            UpdateVisibility();
        }

        private void LateUpdate() => UpdateVisibility();

        private void UpdateVisibility()
        {
            if (m_camera == null || !m_camera.isActiveAndEnabled) m_camera = Camera.main;
            if (m_camera == null) return;

            Vector3 cameraPosition = m_camera.transform.position;
            for (int index = 0; index < m_renderers.Length; index++)
            {
                Renderer renderer = m_renderers[index];
                if (renderer == null) continue;
                bool visible = renderer.bounds.SqrDistance(cameraPosition) <= m_drawDistanceSquared;
                if (renderer.enabled != visible) renderer.enabled = visible;
            }
        }
    }
}
