using System;
using System.Collections.Generic;
using CouchGuys.Gameplay.Delivery;
using CouchGuys.UI;
using CouchGuys.CameraSystem;
using CouchGuys.Input;
using CouchGuys.Player;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using Steamworks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CouchGuys.Networking
{
    /// <summary>
    /// Enables input and the gameplay camera only for the client which owns this player.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NetworkPlayerOwnership : NetworkBehaviour
    {
        public static event Action<NetworkPlayerOwnership> ServerPlayerStarted;
        public static event Action<NetworkPlayerOwnership> ServerPlayerStopped;
        public static NetworkPlayerOwnership LocalPlayer { get; private set; }

        // Identity belongs to this owned network object, never to a lobby index or object name.
        // SyncVar state is included when a late observer receives the player spawn.
        private readonly SyncVar<string> m_displayName = new(string.Empty);
        public string DisplayName => m_displayName.Value;
        public Color IdentityColour => m_colourPalette.GetColour(OwnerId);

        public struct PingState
        {
            public bool Active;
            public Vector3 Position;
            public uint Sequence;
        }

        private static readonly List<NetworkPlayerOwnership> s_clientPlayers = new(8);
        public static IReadOnlyList<NetworkPlayerOwnership> ClientPlayers => s_clientPlayers;
        private readonly SyncVar<PingState> m_ping = new();
        public PingState Ping => m_ping.Value;

        [Header("Location Ping")]
        [SerializeField] private PingWaypointView m_pingPrefab;
        [SerializeField, Min(1f)] private float m_pingLifetime = 10f;
        [SerializeField, Min(1f)] private float m_pingRange = 1000f;
        [SerializeField] private LayerMask m_pingLayers = Physics.DefaultRaycastLayers;
        private readonly RaycastHit[] m_pingHits = new RaycastHit[32];
        private PingWaypointView m_pingView;
        private PlayerHealth m_pingHealth;
        private NeighbourhoodMap m_pingMap;
        private double m_pingExpiresAt;
        private double m_nextPingAt;
        private uint m_presentedPingSequence;

        [Header("Local-only Components")]
        [SerializeField] private PlayerInputReader m_input;
        [SerializeField] private ThirdPersonPlayerController m_playerController;
        [SerializeField] private ThirdPersonCameraController m_cameraController;
        [SerializeField] private Camera m_playerCamera;
        public Camera PlayerCamera => m_playerCamera;
        [SerializeField] private AudioListener m_audioListener;

        [Header("Temporary Player Identification")]
        [SerializeField] private Renderer m_playerRenderer;
        [SerializeField] private ColourPalette m_colourPalette = new ColourPalette();

        private static readonly int BaseColourProperty = Shader.PropertyToID("_BaseColor");
        private static readonly int ColourProperty = Shader.PropertyToID("_Color");
        private static readonly int EmissionColourProperty = Shader.PropertyToID("_EmissionColor");
        private MaterialPropertyBlock m_propertyBlock;
        private Renderer[] m_playerRenderers;
        private Transform m_cameraRig;
        private Transform m_cameraRigOriginalParent;
        private Vector3 m_cameraRigOriginalLocalPosition;
        private Quaternion m_cameraRigOriginalLocalRotation;
        private Vector3 m_cameraRigOriginalLocalScale;
        private bool m_cameraRigDetached;

        [System.Serializable]
        private sealed class ColourPalette
        {
            [SerializeField] private Color[] m_colours =
            {
                new Color(0.13f, 0.55f, 1f),
                new Color(1f, 0.25f, 0.20f),
                new Color(0.20f, 0.85f, 0.35f),
                new Color(1f, 0.72f, 0.12f)
            };

            public Color GetColour(int playerIndex)
            {
                return m_colours != null && m_colours.Length > 0
                    ? m_colours[Mathf.Abs(playerIndex) % m_colours.Length]
                    : Color.white;
            }
        }

        private void Awake()
        {
            ResolveReferences();
            m_pingHealth = GetComponent<PlayerHealth>();
            m_pingMap = GetComponent<NeighbourhoodMap>();
            CacheCameraRigHierarchy();
            SetLocalControl(false);
            gameObject.AddComponent<PlayerDebugMenu>();
        }

        private float m_nextDebugActionTime;

        // Ownership is required by FishNet: each client can request actions only for its own player.
        [ServerRpc]
        public void DebugSpawnServerRpc(int spawnIndex)
        {
            if (GameplaySessionController.IsServerGameOver || Time.unscaledTime < m_nextDebugActionTime) return;
            m_nextDebugActionTime = Time.unscaledTime + 0.25f;
            bool hoverboard = spawnIndex == 7;
            bool vehicle = spawnIndex == 3 || hoverboard;
            if (spawnIndex < 0 || spawnIndex > 7) return;
            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            Vector3 position = transform.position + forward * (vehicle ? 8f : 2f);
            // Ground the spawn rather than inheriting the player's altitude while jumping.
            if (Physics.Raycast(position + Vector3.up * 10f, Vector3.down, out RaycastHit hit,
                    50f, ~0, QueryTriggerInteraction.Ignore)) position = hit.point;
            Quaternion rotation = Quaternion.LookRotation(forward);
            if (!vehicle)
            {
                CouchGuys.Gameplay.Weapons.WeaponPickup.SpawnServer(
                    spawnIndex == 6 ? CouchGuys.Gameplay.Weapons.PlayerWeaponType.BaseballBat :
                    spawnIndex == 5 ? CouchGuys.Gameplay.Weapons.PlayerWeaponType.RocketLauncher :
                    spawnIndex == 4 ? CouchGuys.Gameplay.Weapons.PlayerWeaponType.GrappleGun :
                        (CouchGuys.Gameplay.Weapons.PlayerWeaponType)spawnIndex,
                    position + Vector3.up * 0.5f, rotation, Vector3.zero);
                return;
            }

            string prefabName = hoverboard ? "MagneticHoverboard" : "Truck";
            var prefabs = NetworkManager.SpawnablePrefabs;
            if (prefabs == null) return;
            for (int index = 0; index < prefabs.GetObjectCount(); index++)
            {
                NetworkObject prefab = prefabs.GetObject(true, index);
                if (prefab == null || prefab.name != prefabName) continue;
                var board = prefab.GetComponent<CouchGuys.Gameplay.Vehicles.MagneticHoverboardController>();
                if (hoverboard ? board == null || board.Settings == null :
                    prefab.GetComponent<CouchGuys.Gameplay.Vehicles.TruckController>() == null) continue;
                float groundOffset = hoverboard ? board.Settings.HoverHeight : .3f;
                NetworkObject spawned = Instantiate(prefab, position + Vector3.up * groundOffset, rotation);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(spawned.gameObject, gameObject.scene);
                ServerManager.Spawn(spawned);
                return;
            }
            Debug.LogWarning($"Debug spawn: {prefabName} is missing from the network spawnable prefabs.");
        }

        [ServerRpc]
        public void DebugAddCurrencyServerRpc(int amount)
        {
            if (GameplaySessionController.IsServerGameOver || amount < 1 || amount > 100000 ||
                Time.unscaledTime < m_nextDebugActionTime) return;
            m_nextDebugActionTime = Time.unscaledTime + 0.25f;
            FindFirstObjectByType<CouchGuys.Gameplay.Delivery.DeliveryManager>()?.AddTestCurrencyServer(amount);
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            SetCameraRigDetached(IsOwner);
            SetLocalControl(IsOwner);
            ApplyIdentityColour();
            if (!s_clientPlayers.Contains(this)) s_clientPlayers.Add(this);
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            m_ping.Value = default;
            m_nextPingAt = 0;
            ServerPlayerStarted?.Invoke(this);
        }

        public override void OnStopServer()
        {
            ServerPlayerStopped?.Invoke(this);
            base.OnStopServer();
        }

        public override void OnOwnershipClient(NetworkConnection previousOwner)
        {
            base.OnOwnershipClient(previousOwner);
            SetCameraRigDetached(IsOwner);
            SetLocalControl(IsOwner);
            ApplyIdentityColour();
            PublishLocalDisplayName();
        }

        private void PublishLocalDisplayName()
        {
            if (!IsOwner) return;
            string displayName = SteamClientBootstrap.IsInitialised
                ? SteamFriends.GetPersonaName()
                : $"Player {OwnerId + 1}";
            SetDisplayNameServerRpc(displayName);
        }

        [ServerRpc]
        private void SetDisplayNameServerRpc(string displayName)
        {
            // FishNet requires ownership for this RPC. Bound presentation data once at entry;
            // rich text is disabled on the view so Steam names cannot inject formatting.
            if (string.IsNullOrWhiteSpace(displayName)) return;
            displayName = displayName.Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ').Trim();
            if (displayName.Length > 128)
            {
                int length = char.IsHighSurrogate(displayName[127]) ? 127 : 128;
                displayName = displayName.Substring(0, length);
            }
            m_displayName.Value = displayName;
        }

        public override void OnStopClient()
        {
            ClearPingPresentation();
            SetLocalControl(false);
            SetCameraRigDetached(false);
            base.OnStopClient();
        }

        private void OnDestroy()
        {
            ClearPingPresentation();
            if (LocalPlayer == this) LocalPlayer = null;
            // A detached camera is no longer a child of the network Player, so it must be
            // explicitly cleaned up if the Player is destroyed without OnStopClient.
            if (m_cameraRigDetached && m_cameraRig != null)
            {
                Destroy(m_cameraRig.gameObject);
            }
        }

        private void Update()
        {
            // Only changes are replicated: placement/replacement and one server expiry.
            if (IsServerInitialized && m_ping.Value.Active && Time.unscaledTimeAsDouble >= m_pingExpiresAt)
                m_ping.Value = new PingState { Sequence = m_ping.Value.Sequence };

            if (!IsClientInitialized) return;
            PingState ping = m_ping.Value;
            if (!ping.Active)
            {
                if (m_pingView != null) m_pingView.gameObject.SetActive(false);
                return;
            }
            if (m_pingView == null && m_pingPrefab != null)
            {
                m_pingView = Instantiate(m_pingPrefab);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(m_pingView.gameObject, gameObject.scene);
                m_pingView.Bind(this);
            }
            if (m_pingView == null) return;
            m_pingView.transform.position = ping.Position + Vector3.up * .15f;
            if (!m_pingView.gameObject.activeSelf || m_presentedPingSequence != ping.Sequence)
            {
                m_pingView.gameObject.SetActive(true);
                m_pingView.Replay();
                m_presentedPingSequence = ping.Sequence;
            }
        }

        private void LateUpdate()
        {
            // All menu Update handlers must resolve before accepting a gameplay click.
            if (IsClientInitialized && IsOwner && m_input != null && m_input.PingPressedThisFrame &&
                (m_pingMap == null || !m_pingMap.IsExpanded) && m_playerCamera != null &&
                m_playerCamera.isActiveAndEnabled && m_pingHealth != null && m_pingHealth.IsAlive &&
                !m_pingHealth.IsAiTeammate)
            {
                Ray ray = m_playerCamera.ViewportPointToRay(new Vector3(.5f, .5f, 0f));
                // Match the existing non-alloc camera trace pattern; ignore our own body.
                int count = gameObject.scene.GetPhysicsScene().Raycast(ray.origin, ray.direction,
                    m_pingHits, m_pingRange, m_pingLayers, QueryTriggerInteraction.Ignore);
                float nearest = float.PositiveInfinity;
                Vector3 point = default;
                for (int i = 0; i < count; i++)
                {
                    RaycastHit hit = m_pingHits[i];
                    if (hit.transform.IsChildOf(transform) || hit.distance >= nearest) continue;
                    nearest = hit.distance;
                    point = hit.point;
                }
                // A saturated buffer might omit an obstruction. Don't place a misleading ping.
                if (count < m_pingHits.Length && nearest < float.PositiveInfinity) PlacePingServerRpc(point);
            }

        }

        [ServerRpc] // FishNet's default ownership check prevents pinging as another player.
        private void PlacePingServerRpc(Vector3 position)
        {
            if (GameplaySessionController.IsServerGameOver || m_pingHealth == null || !m_pingHealth.IsAlive ||
                m_pingHealth.IsAiTeammate || Time.unscaledTimeAsDouble < m_nextPingAt ||
                !float.IsFinite(position.x) || !float.IsFinite(position.y) || !float.IsFinite(position.z) ||
                (position - transform.position).sqrMagnitude > (m_pingRange + 50f) * (m_pingRange + 50f)) return;
            m_nextPingAt = Time.unscaledTimeAsDouble + .2;
            m_pingExpiresAt = Time.unscaledTimeAsDouble + m_pingLifetime;
            m_ping.Value = new PingState { Active = true, Position = position, Sequence = m_ping.Value.Sequence + 1 };
        }

        private void ClearPingPresentation()
        {
            s_clientPlayers.Remove(this);
            if (m_pingView != null) Destroy(m_pingView.gameObject);
            m_pingView = null;
            m_presentedPingSequence = 0;
        }

        private void ResolveReferences()
        {
            m_input ??= GetComponent<PlayerInputReader>();
            m_playerController ??= GetComponent<ThirdPersonPlayerController>();
            m_cameraController ??= GetComponentInChildren<ThirdPersonCameraController>(true);
            m_playerCamera ??= GetComponentInChildren<Camera>(true);
            m_audioListener ??= GetComponentInChildren<AudioListener>(true);
            m_playerRenderer ??= GetComponentInChildren<Renderer>(true);
            m_playerRenderers = GetComponentsInChildren<Renderer>(true);
        }

        private void CacheCameraRigHierarchy()
        {
            if (m_cameraController == null)
            {
                return;
            }

            m_cameraRig = m_cameraController.transform;
            m_cameraRigOriginalParent = m_cameraRig.parent;
            m_cameraRigOriginalLocalPosition = m_cameraRig.localPosition;
            m_cameraRigOriginalLocalRotation = m_cameraRig.localRotation;
            m_cameraRigOriginalLocalScale = m_cameraRig.localScale;
        }

        private void SetCameraRigDetached(bool shouldDetach)
        {
            if (m_cameraRig == null || shouldDetach == m_cameraRigDetached)
            {
                return;
            }

            if (shouldDetach)
            {
                // The player root rotates to face the camera or an interaction target.
                // Keeping the camera underneath that root makes it inherit the rotation
                // before its LateUpdate can run, producing a visible correction on every turn.
                m_cameraRig.SetParent(null, true);
                m_cameraRigDetached = true;
                return;
            }

            m_cameraRig.SetParent(m_cameraRigOriginalParent, false);
            m_cameraRig.localPosition = m_cameraRigOriginalLocalPosition;
            m_cameraRig.localRotation = m_cameraRigOriginalLocalRotation;
            m_cameraRig.localScale = m_cameraRigOriginalLocalScale;
            m_cameraRigDetached = false;
        }

        private void SetLocalControl(bool isLocalOwner)
        {
            if (isLocalOwner) LocalPlayer = this;
            else if (LocalPlayer == this) LocalPlayer = null;
            if (m_input != null)
            {
                m_input.enabled = isLocalOwner;
            }

            if (m_playerController != null)
            {
                m_playerController.enabled = isLocalOwner;
            }

            if (m_cameraController != null)
            {
                m_cameraController.enabled = isLocalOwner;
            }

            if (m_playerCamera != null)
            {
                m_playerCamera.enabled = isLocalOwner;
                if (isLocalOwner)
                {
                    m_playerCamera.tag = "MainCamera";
                }
            }

            if (m_audioListener != null)
            {
                m_audioListener.enabled = isLocalOwner;
            }
        }

        private void ApplyIdentityColour()
        {
            PlayerHealth health = GetComponent<PlayerHealth>();
            ApplyPlayerColour(health != null && health.IsAiTeammate ? 2 : OwnerId);
        }

        private void ApplyPlayerColour(int playerIndex)
        {
            if (m_playerRenderer == null && (m_playerRenderers == null || m_playerRenderers.Length == 0))
            {
                return;
            }

            m_propertyBlock ??= new MaterialPropertyBlock();
            Color colour = m_colourPalette.GetColour(playerIndex);
            Color emissionColour = colour * 4f;
            emissionColour.a = 1f;
            bool appliedGlowColour = false;

            foreach (Renderer playerRenderer in m_playerRenderers)
            {
                if (playerRenderer == null)
                {
                    continue;
                }

                Material[] materials = playerRenderer.sharedMaterials;
                for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                {
                    Material material = materials[materialIndex];
                    if (material == null || !material.name.StartsWith("PlayerGlow", System.StringComparison.Ordinal))
                    {
                        continue;
                    }

                    playerRenderer.GetPropertyBlock(m_propertyBlock, materialIndex);
                    m_propertyBlock.SetColor(BaseColourProperty, colour);
                    m_propertyBlock.SetColor(ColourProperty, colour);
                    m_propertyBlock.SetColor(EmissionColourProperty, emissionColour);
                    playerRenderer.SetPropertyBlock(m_propertyBlock, materialIndex);
                    m_propertyBlock.Clear();
                    appliedGlowColour = true;
                }
            }

            // Preserve the temporary capsule's whole-body colour until the robot FBX exists.
            if (!appliedGlowColour && m_playerRenderer != null)
            {
                m_playerRenderer.GetPropertyBlock(m_propertyBlock);
                m_propertyBlock.SetColor(BaseColourProperty, colour);
                m_propertyBlock.SetColor(ColourProperty, colour);
                m_playerRenderer.SetPropertyBlock(m_propertyBlock);
                m_propertyBlock.Clear();
            }
        }
    }
}
