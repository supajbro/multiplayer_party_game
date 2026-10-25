using CouchGuys.CameraSystem;
using CouchGuys.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CouchGuys.Networking
{
    /// <summary>Local debug UI; world changes are requested through the owned network player.</summary>
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class PlayerDebugMenu : MonoBehaviour
    {
        private static PlayerDebugMenu s_openMenu;
        public static bool IsOpen => s_openMenu != null;
        private NetworkPlayerOwnership m_owner;
        private ThirdPersonPlayerController m_player;
        private ThirdPersonCameraController m_camera;
        private CursorLockMode m_previousLock;
        private bool m_previousVisible;
        private int m_tab;
        private string m_currencyAmount = "1000";
        private Vector2 m_scroll;
        private static readonly string[] Tabs = { "Player", "Spawnables" };
        private static readonly string[] SpawnNames =
            { "Pistol", "Assault Rifle", "Shotgun", "Light Truck", "Good Truck", "Best Truck", "Grapple Gun" };

        private void Awake()
        {
            m_owner = GetComponent<NetworkPlayerOwnership>();
            m_player = GetComponent<ThirdPersonPlayerController>();
            m_camera = GetComponentInChildren<ThirdPersonCameraController>(true);
        }

        private void Update()
        {
            if (!m_owner.IsOwner || !m_owner.IsClientInitialized || GameplaySessionController.BlocksLocalInput)
            {
                Close();
                return;
            }
            if (Keyboard.current?.f2Key.wasPressedThisFrame ?? false)
            {
                if (s_openMenu == this) Close();
                else
                {
                    if (s_openMenu != null) s_openMenu.Close();
                    m_previousLock = Cursor.lockState;
                    m_previousVisible = Cursor.visible;
                    s_openMenu = this;
                }
            }
            if (s_openMenu == this)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        private void OnDisable() => Close();
        public static void CloseActive()
        {
            if (s_openMenu != null) s_openMenu.Close();
        }

        private void Close()
        {
            if (s_openMenu != this) return;
            s_openMenu = null;
            Cursor.lockState = m_previousLock;
            Cursor.visible = m_previousVisible;
        }

        private void OnGUI()
        {
            if (s_openMenu != this) return;
            float scale = Mathf.Max(0.25f, Mathf.Min(1f, Screen.width / 540f, Screen.height / 580f));
            Matrix4x4 previousMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(Vector3.one * scale);
            GUILayout.BeginArea(new Rect(20f, 20f, 500f, 540f), "Debug Menu — F2 to close", GUI.skin.window);
            m_tab = GUILayout.Toolbar(m_tab, Tabs, GUILayout.Height(32f));
            m_scroll = GUILayout.BeginScrollView(m_scroll);
            if (m_tab == 0) DrawPlayer();
            else
            {
                GUILayout.Label("Spawn in front of your player (synced to everyone)");
                for (int index = 0; index < SpawnNames.Length; index++)
                    if (GUILayout.Button(SpawnNames[index], GUILayout.Height(38f))) m_owner.DebugSpawnServerRpc(index);
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
            GUI.matrix = previousMatrix;
        }

        private void DrawPlayer()
        {
            if (m_player != null)
            {
                m_player.WalkSpeed = Slider("Walk speed", m_player.WalkSpeed, 0f, 30f);
                m_player.RunSpeed = Slider("Run speed", m_player.RunSpeed, 0f, 50f);
                m_player.JumpHeight = Slider("Jump height", m_player.JumpHeight, 0f, 15f);
            }
            if (m_camera != null) m_camera.VerticalAngle = Slider("Camera angle", m_camera.VerticalAngle, -35f, 65f);
            Camera camera = m_owner.PlayerCamera;
            if (camera != null) camera.fieldOfView = Slider("Field of view", camera.fieldOfView, 30f, 120f);
            GUILayout.Space(12f);
            var delivery = FindFirstObjectByType<CouchGuys.Gameplay.Delivery.DeliveryManager>();
            GUILayout.Label($"Shared currency: {(delivery != null ? delivery.CurrentCurrency : 0)}");
            GUILayout.BeginHorizontal();
            m_currencyAmount = GUILayout.TextField(m_currencyAmount, 6, GUILayout.Width(180f));
            bool validAmount = int.TryParse(m_currencyAmount, out int amount) && amount > 0 && amount <= 100000;
            GUI.enabled = validAmount && delivery != null;
            if (GUILayout.Button("Add currency", GUILayout.Height(28f))) m_owner.DebugAddCurrencyServerRpc(amount);
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.Label("Amount: 1–100,000");
        }

        private static float Slider(string label, float value, float minimum, float maximum)
        {
            GUILayout.Label($"{label}: {value:0.0}");
            return GUILayout.HorizontalSlider(value, minimum, maximum, GUILayout.Height(22f));
        }
    }
}
