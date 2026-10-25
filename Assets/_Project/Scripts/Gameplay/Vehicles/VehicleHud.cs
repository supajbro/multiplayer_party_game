using CouchGuys.Player;
using FishNet.Object;
using UnityEngine;

namespace CouchGuys.Gameplay.Vehicles
{
    /// <summary>Owner-only lightweight vehicle telemetry. Interaction presentation lives in the player HUD prefab.</summary>
    [RequireComponent(typeof(NetworkObject), typeof(PlayerTruckInteractor))]
    [DisallowMultipleComponent]
    public sealed class VehicleHud : MonoBehaviour
    {
        private NetworkObject m_networkObject;
        private PlayerTruckInteractor m_interactor;
        private PlayerCouchCarrier m_carrier;
        private GUIStyle m_title, m_label, m_warning;

        private void Awake() { m_networkObject = GetComponent<NetworkObject>(); m_interactor = GetComponent<PlayerTruckInteractor>(); m_carrier = GetComponent<PlayerCouchCarrier>(); }

        private void OnGUI()
        {
            if (m_networkObject == null || !m_networkObject.IsOwner) return;
            EnsureStyles();
            TruckController truck = m_interactor.CurrentTruck;
            if (truck != null) DrawVehicleHud(truck);
        }

        private void DrawVehicleHud(TruckController truck)
        {
            Rect panel = new(Screen.width - 330f, Screen.height - 235f, 300f, 200f);
            GUI.color = new Color(0.055f, 0.07f, 0.09f, 0.94f); GUI.Box(panel, GUIContent.none); GUI.color = Color.white;
            GUI.Label(new Rect(panel.x + 18f, panel.y + 12f, 265f, 32f), truck.Settings.DisplayName, m_title);
            GUI.Label(new Rect(panel.x + 18f, panel.y + 47f, 265f, 30f), $"{Mathf.RoundToInt(truck.SpeedKph)} km/h", m_label);
            DrawBar(new Rect(panel.x + 18f, panel.y + 86f, 264f, 20f), truck.FuelFraction, new Color(1f, .66f, .08f), "FUEL");
            DrawBar(new Rect(panel.x + 18f, panel.y + 123f, 264f, 20f), truck.HealthFraction, new Color(.25f, .92f, .35f), "TRUCK");
            if (truck.Fuel <= 0f) GUI.Label(new Rect(panel.x + 18f, panel.y + 151f, 264f, 25f), "OUT OF FUEL", m_warning);
            else if (truck.FuelFraction <= truck.Settings.LowFuelWarningThreshold) GUI.Label(new Rect(panel.x + 18f, panel.y + 151f, 264f, 25f), "LOW FUEL", m_warning);
            else if (truck.HealthFraction <= truck.Settings.CriticalHealthThreshold) GUI.Label(new Rect(panel.x + 18f, panel.y + 151f, 264f, 25f), "CRITICAL DAMAGE", m_warning);
        }

        private void DrawBar(Rect rect, float value, Color colour, string text)
        {
            GUI.color = new Color(.25f, .28f, .3f); GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = colour; GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(value), rect.height), Texture2D.whiteTexture);
            GUI.color = Color.white; GUI.Label(new Rect(rect.x, rect.y - 20f, rect.width, 20f), text, m_label);
        }

        private TruckController m_promptTruck;
        private int m_promptSeat = -2;
        private string m_entryPrompt;

        // Preserve the existing priority and labels; cache the only composed label.
        public string GetInteractionPrompt(out float progress, out float duration)
        {
            progress = m_interactor.HoldProgress;
            duration = 0f;
            TruckController truck = m_interactor.CurrentTruck;
            if (truck != null)
            {
                duration = truck.Settings.ExitHoldDuration;
                return "Hold E to Exit";
            }
            string world = m_interactor.WorldInteractionPrompt;
            if (!string.IsNullOrEmpty(world)) return world;
            string sled = m_carrier != null ? m_carrier.SledInteractionPrompt : null;
            if (!string.IsNullOrEmpty(sled)) return sled;
            truck = m_interactor.NearbyTruck;
            if (truck == null) return null;
            int seat = truck.FindEntrySeat(m_interactor.transform.position);
            if (truck != m_promptTruck || seat != m_promptSeat)
            {
                m_promptTruck = truck;
                m_promptSeat = seat;
                m_entryPrompt = $"Hold E to {(seat == 0 ? "Drive" : "Ride in")} {truck.Settings.DisplayName}";
            }
            duration = truck.Settings.EnterHoldDuration;
            return m_entryPrompt;
        }

        private void EnsureStyles()
        {
            if (m_title != null) return;
            m_title = new GUIStyle(GUI.skin.label) { fontSize = 23, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            m_label = new GUIStyle(GUI.skin.label) { fontSize = 17, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            m_warning = new GUIStyle(m_label) { alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(1f, .25f, .15f) } };
        }
    }
}
