using CouchGuys.Player;
using FishNet.Object;
using UnityEngine;

namespace CouchGuys.Gameplay.Vehicles
{
    /// <summary>Owner-only lightweight vehicle HUD and interaction prompt.</summary>
    [RequireComponent(typeof(NetworkObject), typeof(PlayerVanInteractor))]
    [DisallowMultipleComponent]
    public sealed class VehicleHud : MonoBehaviour
    {
        private NetworkObject m_networkObject;
        private PlayerVanInteractor m_interactor;
        private GUIStyle m_title, m_label, m_warning, m_prompt;

        private void Awake() { m_networkObject = GetComponent<NetworkObject>(); m_interactor = GetComponent<PlayerVanInteractor>(); }

        private void OnGUI()
        {
            if (m_networkObject == null || !m_networkObject.IsOwner) return;
            EnsureStyles();
            VanController van = m_interactor.CurrentVan;
            if (van != null) DrawVehicleHud(van);
            else if (!string.IsNullOrEmpty(m_interactor.WorldInteractionPrompt))
                DrawPrompt(m_interactor.WorldInteractionPrompt, 0f, 1f);
            else if (m_interactor.NearbyVan != null)
                DrawPrompt($"Hold E to Drive {m_interactor.NearbyVan.Settings.DisplayName}", m_interactor.HoldProgress,
                    m_interactor.NearbyVan.Settings.EnterHoldDuration);
        }

        private void DrawVehicleHud(VanController van)
        {
            Rect panel = new(Screen.width - 330f, Screen.height - 235f, 300f, 200f);
            GUI.color = new Color(0.055f, 0.07f, 0.09f, 0.94f); GUI.Box(panel, GUIContent.none); GUI.color = Color.white;
            GUI.Label(new Rect(panel.x + 18f, panel.y + 12f, 265f, 32f), van.Settings.DisplayName, m_title);
            GUI.Label(new Rect(panel.x + 18f, panel.y + 47f, 265f, 30f), $"{Mathf.RoundToInt(van.SpeedKph)} km/h", m_label);
            DrawBar(new Rect(panel.x + 18f, panel.y + 86f, 264f, 20f), van.FuelFraction, new Color(1f, .66f, .08f), "FUEL");
            DrawBar(new Rect(panel.x + 18f, panel.y + 123f, 264f, 20f), van.HealthFraction, new Color(.25f, .92f, .35f), "VAN");
            if (van.Fuel <= 0f) GUI.Label(new Rect(panel.x + 18f, panel.y + 151f, 264f, 25f), "OUT OF FUEL", m_warning);
            else if (van.FuelFraction <= van.Settings.LowFuelWarningThreshold) GUI.Label(new Rect(panel.x + 18f, panel.y + 151f, 264f, 25f), "LOW FUEL", m_warning);
            else if (van.HealthFraction <= van.Settings.CriticalHealthThreshold) GUI.Label(new Rect(panel.x + 18f, panel.y + 151f, 264f, 25f), "CRITICAL DAMAGE", m_warning);
            DrawPrompt("Hold E to Exit", m_interactor.HoldProgress, van.Settings.ExitHoldDuration);
        }

        private void DrawBar(Rect rect, float value, Color colour, string text)
        {
            GUI.color = new Color(.25f, .28f, .3f); GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = colour; GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(value), rect.height), Texture2D.whiteTexture);
            GUI.color = Color.white; GUI.Label(new Rect(rect.x, rect.y - 20f, rect.width, 20f), text, m_label);
        }

        private void DrawPrompt(string text, float progress, float duration)
        {
            Rect rect = new(Screen.width * .5f - 170f, Screen.height - 105f, 340f, 46f);
            GUI.color = new Color(.055f, .07f, .09f, .94f); GUI.Box(rect, GUIContent.none);
            GUI.color = new Color(1f, .66f, .08f); GUI.DrawTexture(new Rect(rect.x, rect.y + 42f, rect.width * Mathf.Clamp01(progress / Mathf.Max(.01f, duration)), 4f), Texture2D.whiteTexture);
            GUI.color = Color.white; GUI.Label(rect, text, m_prompt);
        }

        private void EnsureStyles()
        {
            if (m_title != null) return;
            m_title = new GUIStyle(GUI.skin.label) { fontSize = 23, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            m_label = new GUIStyle(GUI.skin.label) { fontSize = 17, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            m_warning = new GUIStyle(m_label) { alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(1f, .25f, .15f) } };
            m_prompt = new GUIStyle(m_label) { alignment = TextAnchor.MiddleCenter };
        }
    }
}
