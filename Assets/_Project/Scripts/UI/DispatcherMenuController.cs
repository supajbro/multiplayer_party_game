using System;
using System.Collections.Generic;
using CouchGuys.CameraSystem;
using CouchGuys.Input;
using CouchGuys.Networking;
using CouchGuys.Player;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CouchGuys.UI
{
    /// <summary>Presentation only. Purchases remain callbacks to the existing authoritative systems.</summary>
    public sealed class DispatcherMenuController : MonoBehaviour
    {
        [Header("Saved UI and Dispatcher Anchors")]
        [SerializeField] private DispatcherMenuUI m_menuPrefab;
        [SerializeField] private Transform m_cameraFocusPoint, m_screenCanvasAnchor;
        [SerializeField, Min(.01f)] private float m_transitionSeconds = .6f;
        [SerializeField, Range(25f, 75f)] private float m_focusFieldOfView = 48f;
        [Header("Existing Screen and Physical Buttons (all LODs)")]
        [SerializeField] private Renderer[] m_screens, m_deliveriesButtons, m_shopButtons;
        [SerializeField] private Collider m_deliveriesCollider, m_shopCollider;
        [SerializeField, Min(0)] private int m_faceMaterialIndex = 1;
        [SerializeField] private Color m_inactiveColor = new(.60f, .62f, .64f, 1f);
        [SerializeField] private Color m_activeColor = new(1f, .76f, .06f, 1f);
        [SerializeField, Min(0f)] private float m_selectedEmission = 1.2f;
        private static DispatcherMenuController s_active;
        private static int s_closedFrame = -1;
        public static bool IsLocalInteractionActive => s_active != null;
        public static bool BlocksPauseThisFrame => IsLocalInteractionActive || s_closedFrame == Time.frameCount;
        public static DispatcherMenuController Active => s_active;
        public bool IsClosing { get; private set; }
        public PlayerCouchCarrier Owner { get; private set; }
        private DispatcherMenuUI m_ui;
        private ThirdPersonCameraController m_cameraController;
        private Camera m_camera;
        private PlayerInputReader m_input;
        private ThirdPersonPlayerController m_motor;
        private bool m_previousSuppressed, m_previousMotorPaused, m_previousCursorVisible;
        private CursorLockMode m_previousCursorLock;
        private Action m_closed;
        private int m_openFrame, m_hovered = -1;
        private readonly List<EventSystem> m_suspendedSystems = new();
        private readonly List<GraphicRaycaster> m_suspendedRaycasters = new();
        private readonly List<(Renderer renderer, int slot, MaterialPropertyBlock block)> m_originalBlocks = new();
        private MaterialPropertyBlock m_block;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int Emission = Shader.PropertyToID("_EmissionColor");
        private static readonly int WaveColor = Shader.PropertyToID("_WaveColor");
        private void Awake() => m_block = new MaterialPropertyBlock();

        public bool OpenLocal(PlayerCouchCarrier owner, IReadOnlyList<DialogueShopCategory> categories,
            int funds, string notice, Action closed)
        {
            if (owner == null || !owner.IsOwner || IsClosing) return false;
            if (s_active == this)
            {
                if (Owner != owner) return false;
                m_ui.Bind(categories, funds, notice);
                return true;
            }
            if (s_active != null) s_active.CloseLocal(false, true);
            var ownership = owner.GetComponent<NetworkPlayerOwnership>();
            m_camera = ownership != null ? ownership.PlayerCamera : null;
            m_cameraController = m_camera != null ? m_camera.GetComponentInParent<ThirdPersonCameraController>() : null;
            if (m_menuPrefab == null || m_cameraController == null || m_cameraFocusPoint == null || m_screenCanvasAnchor == null)
            {
                Debug.LogError("Dispatcher menu references are incomplete.", this);
                return false;
            }
            Owner = owner;
            m_closed = closed;
            m_input = owner.GetComponent<PlayerInputReader>();
            m_motor = owner.GetComponent<ThirdPersonPlayerController>();
            m_previousSuppressed = m_input != null && m_input.GameplaySuppressionRequested;
            m_previousMotorPaused = m_motor != null && m_motor.ExternalControlPaused;
            m_previousCursorLock = Cursor.lockState;
            m_previousCursorVisible = Cursor.visible;
            m_input?.SetGameplaySuppressed(true);
            m_motor?.SetExternalControlPaused(true);
            m_openFrame = Time.frameCount;
            m_readyAt = Time.unscaledTime + m_transitionSeconds;
            s_active = this;
            IsClosing = false;
            m_ui = Instantiate(m_menuPrefab, m_screenCanvasAnchor, false);
            m_ui.ConfigureCamera(m_camera);
            m_ui.Bind(categories, funds, notice);
            m_ui.ShowCategory(0);
            m_ui.SetInteractive(false);
            var ownSystem = m_ui.GetComponentInChildren<EventSystem>(true);
            foreach (var system in FindObjectsByType<EventSystem>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (system == ownSystem || !system.enabled) continue;
                m_suspendedSystems.Add(system); system.enabled = false;
            }
            if (ownSystem != null) { ownSystem.gameObject.SetActive(true); ownSystem.enabled = true; }
            var menuRaycaster = m_ui.GetComponent<GraphicRaycaster>();
            foreach (var raycaster in FindObjectsByType<GraphicRaycaster>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (raycaster == menuRaycaster || !raycaster.enabled) continue;
                m_suspendedRaycasters.Add(raycaster); raycaster.enabled = false;
            }
            SaveProperties(m_screens, -1);
            SaveProperties(m_deliveriesButtons, m_faceMaterialIndex);
            SaveProperties(m_shopButtons, m_faceMaterialIndex);
            SetWaveformHidden();
            UpdateButtonColors();
            m_cameraController.FocusDispatcher(m_cameraFocusPoint, m_transitionSeconds, m_focusFieldOfView);
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            return true;
        }

        private void SaveProperties(Renderer[] renderers, int slot)
        {
            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;
                var original = new MaterialPropertyBlock();
                if (slot < 0) renderer.GetPropertyBlock(original); else renderer.GetPropertyBlock(original, slot);
                m_originalBlocks.Add((renderer, slot, original));
            }
        }

        private void SetWaveformHidden()
        {
            m_block ??= new MaterialPropertyBlock();
            // The existing waveform component preserves other block properties. The curved display
            // stays black and keeps its depth surface; only this client's wave colour is hidden.
            foreach (var screen in m_screens)
            {
                if (screen == null) continue;
                screen.GetPropertyBlock(m_block); m_block.SetColor(WaveColor, Color.black); screen.SetPropertyBlock(m_block);
            }
        }

        private void Update()
        {
            if (s_active != this || IsClosing) return;
            if (Owner == null || !Owner.IsSpawned || m_camera == null || m_cameraController == null || !m_cameraController.isActiveAndEnabled ||
                (Owner.TryGetComponent(out PlayerHealth health) && !health.IsAlive) || GameplaySessionController.BlocksLocalInput)
            { CloseLocal(true); return; }
            if (Time.frameCount > m_openFrame && ((Keyboard.current?.escapeKey.wasPressedThisFrame ?? false) ||
                (Keyboard.current?.eKey.wasPressedThisFrame ?? false))) { CloseLocal(); return; }
            if (m_ui.IsConfirmingPurchase)
            {
                if (m_hovered != -1) { m_hovered = -1; UpdateButtonColors(); }
                return;
            }
            bool ready = Time.unscaledTime >= m_readyAt;
            // Ignore clicks until the camera has settled into the first focus blend.
            if (!ready) return;
            m_ui.SetInteractive(true);
            Ray ray = m_camera.ScreenPointToRay(Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero);
            int hovered = -1;
            if (!(EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()))
            {
                if (m_deliveriesCollider != null && m_deliveriesCollider.Raycast(ray, out _, 10f)) hovered = 0;
                else if (m_shopCollider != null && m_shopCollider.Raycast(ray, out _, 10f)) hovered = 1;
            }
            if (m_hovered != hovered) { m_hovered = hovered; UpdateButtonColors(); }
            if (hovered >= 0 && (Mouse.current?.leftButton.wasPressedThisFrame ?? false))
            {
                EventSystem.current?.SetSelectedGameObject(null);
                SelectCategory(hovered);
            }
        }

        private float m_readyAt;
        public void ShowPurchaseConfirmation(string message)
        {
            if (m_ui != null && !IsClosing) m_ui.ShowPurchaseConfirmation(message);
        }
        public void SelectCategory(int category)
        {
            if (m_ui == null || IsClosing || m_ui.IsConfirmingPurchase) return;
            m_ui.ShowCategory(category);
            UpdateButtonColors();
        }
        private void LateUpdate()
        {
            if (s_active == this && !IsClosing) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
        }

        private void UpdateButtonColors()
        {
            if (m_ui == null) return;
            ApplyButtons(m_deliveriesButtons, 0); ApplyButtons(m_shopButtons, 1);
        }
        private void ApplyButtons(Renderer[] renderers, int category)
        {
            m_block ??= new MaterialPropertyBlock();
            bool active = m_ui.SelectedCategory == category;
            Color color = active ? m_activeColor : m_inactiveColor;
            if (m_hovered == category) color = Color.Lerp(color, Color.white, .12f);
            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;
                renderer.GetPropertyBlock(m_block, m_faceMaterialIndex);
                m_block.SetColor(BaseColor, color);
                m_block.SetColor(Emission, active ? m_activeColor * m_selectedEmission : Color.black);
                renderer.SetPropertyBlock(m_block, m_faceMaterialIndex);
            }
        }

        public void CloseLocal(bool notifyServer = true, bool immediate = false)
        {
            if (s_active != this) return;
            if (IsClosing && !immediate) return;
            IsClosing = true;
            s_closedFrame = Time.frameCount;
            if (m_ui != null) { m_ui.SetInteractive(false); Destroy(m_ui.gameObject); m_ui = null; }
            foreach (var entry in m_originalBlocks)
            {
                if (entry.renderer == null) continue;
                var block = entry.block.isEmpty ? null : entry.block;
                if (entry.slot < 0) entry.renderer.SetPropertyBlock(block); else entry.renderer.SetPropertyBlock(block, entry.slot);
            }
            m_originalBlocks.Clear();
            foreach (var system in m_suspendedSystems) if (system != null) system.enabled = true;
            m_suspendedSystems.Clear();
            foreach (var raycaster in m_suspendedRaycasters) if (raycaster != null) raycaster.enabled = true;
            m_suspendedRaycasters.Clear();
            Action closed = m_closed; m_closed = null;
            if (notifyServer) closed?.Invoke();
            if (m_cameraController != null) m_cameraController.ReturnFromDispatcher(m_transitionSeconds, FinishClosing, immediate);
            else FinishClosing();
        }
        private void FinishClosing()
        {
            m_input?.SetGameplaySuppressed(m_previousSuppressed);
            m_motor?.SetExternalControlPaused(m_previousMotorPaused);
            Cursor.lockState = m_previousCursorLock; Cursor.visible = m_previousCursorVisible;
            Owner = null; m_hovered = -1; IsClosing = false;
            if (s_active == this) s_active = null;
            s_closedFrame = Time.frameCount;
        }
        private void OnDisable() { if (s_active == this) CloseLocal(true, true); }
    }
}
