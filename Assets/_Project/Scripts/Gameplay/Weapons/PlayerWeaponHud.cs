using FishNet.Object;
using UnityEngine;
using UnityEngine.UI;

namespace CouchGuys.Gameplay.Weapons
{
    /// <summary>Minimal owner-only weapon readout attached to the existing player UI flow.</summary>
    [RequireComponent(typeof(NetworkObject), typeof(PlayerWeaponController))]
    [DisallowMultipleComponent]
    public sealed class PlayerWeaponHud : MonoBehaviour
    {
        private NetworkObject m_networkObject;
        private PlayerWeaponController m_weapons;
        private GameObject m_canvasRoot;
        private Text m_text;
        private int m_lastMagazine = -1;
        private int m_lastReserve = -1;
        private string m_lastWeapon;
        private bool m_lastReloading;

        private void Awake()
        {
            m_networkObject = GetComponent<NetworkObject>();
            m_weapons = GetComponent<PlayerWeaponController>();
        }

        private void Update()
        {
            if (m_networkObject == null || !m_networkObject.IsOwner) return;
            if (m_canvasRoot == null) BuildHud();
            int magazine = m_weapons.CurrentMagazine;
            int reserve = m_weapons.CurrentReserve;
            string weapon = m_weapons.EquippedWeaponName;
            bool reloading = m_weapons.IsReloading;
            if (magazine == m_lastMagazine && reserve == m_lastReserve &&
                weapon == m_lastWeapon && reloading == m_lastReloading) return;

            m_text.text = reloading
                ? $"{weapon}\nRELOADING  {magazine} / {reserve}"
                : $"{weapon}\n{magazine} / {reserve}";
            m_lastMagazine = magazine;
            m_lastReserve = reserve;
            m_lastWeapon = weapon;
            m_lastReloading = reloading;
        }

        private void OnDestroy()
        {
            if (m_canvasRoot != null) Destroy(m_canvasRoot);
        }

        private void BuildHud()
        {
            m_canvasRoot = new GameObject("PlayerWeaponHud", typeof(Canvas), typeof(CanvasScaler));
            Canvas canvas = m_canvasRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            CanvasScaler scaler = m_canvasRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            GameObject panel = new GameObject("WeaponPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            panel.transform.SetParent(m_canvasRoot.transform, false);
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(1f, 0f);
            panelRect.pivot = new Vector2(1f, 0f);
            panelRect.anchoredPosition = new Vector2(-28f, 28f);
            panelRect.sizeDelta = new Vector2(290f, 92f);
            panel.GetComponent<Image>().color = new Color(0.025f, 0.035f, 0.055f, 0.82f);

            GameObject label = new GameObject("WeaponText", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            label.transform.SetParent(panel.transform, false);
            RectTransform labelRect = label.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(16f, 8f);
            labelRect.offsetMax = new Vector2(-16f, -8f);
            m_text = label.GetComponent<Text>();
            m_text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            m_text.fontSize = 24;
            m_text.alignment = TextAnchor.MiddleRight;
            m_text.color = Color.white;
            m_text.raycastTarget = false;
        }
    }
}
