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

        public RawImage Portrait => m_portrait;

        public void SetValue(float value) => m_fill.fillAmount = Mathf.Clamp01(value);

        private void OnEnable() => RefreshIcon();

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
