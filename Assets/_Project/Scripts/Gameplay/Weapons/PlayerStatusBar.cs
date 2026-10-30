using UnityEngine;
using UnityEngine.UI;

namespace CouchGuys.Gameplay.Weapons
{
    /// <summary>Shared prefab presentation; gameplay and smoothing remain in PlayerWeaponHud.</summary>
    public sealed class PlayerStatusBar : MonoBehaviour
    {
        [SerializeField] private Image m_fill;
        [SerializeField] private RawImage m_portrait;
        [SerializeField] private RawImage m_lightningIcon;
        [Tooltip("Assign the lightning icon texture here when it is available.")]
        public Texture lightningIconTexture;

        private static readonly int s_health = Shader.PropertyToID("_Health");
        private static readonly int s_barRect = Shader.PropertyToID("_BarRect");
        private Material m_fillMaterial;
        private Material m_originalMaterial;

        public RawImage Portrait => m_portrait;

        public void SetValue(float value)
        {
            m_fill.fillAmount = Mathf.Clamp01(value);
            if (m_fillMaterial == null) return;

            Rect rect = m_fill.rectTransform.rect;
            Vector4 bounds = new Vector4(rect.xMin, rect.yMin, rect.width, rect.height);
            m_fillMaterial.SetFloat(s_health, m_fill.fillAmount);
            m_fillMaterial.SetVector(s_barRect, bounds);
            // A parent UI Mask can supply a stencil-modified copy of this material.
            Material rendered = m_fill.materialForRendering;
            if (rendered != m_fillMaterial)
            {
                rendered.SetFloat(s_health, m_fill.fillAmount);
                rendered.SetVector(s_barRect, bounds);
            }
        }

        private void OnEnable()
        {
            RefreshIcon();
            if (m_fill == null || m_fillMaterial != null || !m_fill.material.HasProperty(s_health)) return;
            m_originalMaterial = m_fill.material;
            m_fillMaterial = new Material(m_originalMaterial) { name = m_originalMaterial.name + " (Instance)" };
            m_fill.material = m_fillMaterial;
            SetValue(m_fill.fillAmount);
        }

        private void OnDestroy()
        {
            if (m_fillMaterial == null) return;
            if (m_fill != null) m_fill.material = m_originalMaterial;
            Destroy(m_fillMaterial);
        }

        private void RefreshIcon()
        {
            if (m_lightningIcon == null) return;
            m_lightningIcon.texture = lightningIconTexture;
            m_lightningIcon.enabled = lightningIconTexture != null;
        }

#if UNITY_EDITOR
        private void OnValidate() => RefreshIcon();
#endif
    }
}
