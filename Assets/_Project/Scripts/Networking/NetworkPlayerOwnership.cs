using CouchGuys.CameraSystem;
using CouchGuys.Input;
using CouchGuys.Player;
using FishNet.Connection;
using FishNet.Object;
using UnityEngine;

namespace CouchGuys.Networking
{
    /// <summary>
    /// Enables input and the gameplay camera only for the client which owns this player.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NetworkPlayerOwnership : NetworkBehaviour
    {
        [Header("Local-only Components")]
        [SerializeField] private PlayerInputReader m_input;
        [SerializeField] private ThirdPersonPlayerController m_playerController;
        [SerializeField] private ThirdPersonCameraController m_cameraController;
        [SerializeField] private Camera m_playerCamera;
        [SerializeField] private AudioListener m_audioListener;

        [Header("Temporary Player Identification")]
        [SerializeField] private Renderer m_playerRenderer;
        [SerializeField] private ColourPalette m_colourPalette = new ColourPalette();

        private static readonly int BaseColourProperty = Shader.PropertyToID("_BaseColor");
        private static readonly int ColourProperty = Shader.PropertyToID("_Color");
        private MaterialPropertyBlock m_propertyBlock;

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
            SetLocalControl(false);
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            SetLocalControl(IsOwner);
            ApplyPlayerColour(OwnerId);
        }

        public override void OnOwnershipClient(NetworkConnection previousOwner)
        {
            base.OnOwnershipClient(previousOwner);
            SetLocalControl(IsOwner);
            ApplyPlayerColour(OwnerId);
        }

        public override void OnStopClient()
        {
            SetLocalControl(false);
            base.OnStopClient();
        }

        private void ResolveReferences()
        {
            m_input ??= GetComponent<PlayerInputReader>();
            m_playerController ??= GetComponent<ThirdPersonPlayerController>();
            m_cameraController ??= GetComponentInChildren<ThirdPersonCameraController>(true);
            m_playerCamera ??= GetComponentInChildren<Camera>(true);
            m_audioListener ??= GetComponentInChildren<AudioListener>(true);
            m_playerRenderer ??= GetComponentInChildren<Renderer>(true);
        }

        private void SetLocalControl(bool isLocalOwner)
        {
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

        private void ApplyPlayerColour(int playerIndex)
        {
            if (m_playerRenderer == null)
            {
                return;
            }

            m_propertyBlock ??= new MaterialPropertyBlock();
            m_playerRenderer.GetPropertyBlock(m_propertyBlock);
            Color colour = m_colourPalette.GetColour(playerIndex);
            m_propertyBlock.SetColor(BaseColourProperty, colour);
            m_propertyBlock.SetColor(ColourProperty, colour);
            m_playerRenderer.SetPropertyBlock(m_propertyBlock);
        }
    }
}
