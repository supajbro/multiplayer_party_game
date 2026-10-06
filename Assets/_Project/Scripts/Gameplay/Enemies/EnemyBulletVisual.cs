using UnityEngine;

namespace CouchGuys.Gameplay.Enemies
{
    /// <summary>Lightweight visual-only projectile for an already-resolved hitscan shot.</summary>
    [DisallowMultipleComponent]
    public sealed class EnemyBulletVisual : MonoBehaviour
    {
        private Vector3 m_start;
        private Vector3 m_end;
        private float m_duration;
        private float m_elapsed;

        public void Initialise(
            Vector3 start,
            Vector3 end,
            float duration,
            float width,
            Material material,
            Color colour)
        {
            m_start = start;
            m_end = end;
            m_duration = Mathf.Max(0.01f, duration);
            m_elapsed = 0f;

            Vector3 direction = end - start;
            transform.position = start;
            if (direction.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            }

            // Unity cubes point down local Z after LookRotation. Keep them chunky
            // enough to read during chaotic gameplay without resembling a laser.
            float visualWidth = Mathf.Max(0.025f, width * 2f);
            transform.localScale = new Vector3(visualWidth, visualWidth, visualWidth * 4f);

            if (TryGetComponent(out MeshRenderer meshRenderer))
            {
                meshRenderer.sharedMaterial = material;
                MaterialPropertyBlock properties = new MaterialPropertyBlock();
                properties.SetColor("_Color", colour);
                properties.SetColor("_BaseColor", colour);
                meshRenderer.SetPropertyBlock(properties);
                meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                meshRenderer.receiveShadows = false;
            }

            Destroy(gameObject, m_duration + 0.05f);
        }

        private void Update()
        {
            m_elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(m_elapsed / m_duration);
            transform.position = Vector3.Lerp(m_start, m_end, progress);
            if (progress >= 1f)
            {
                Destroy(gameObject);
            }
        }
    }
}
