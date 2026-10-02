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
            CacheCameraRigHierarchy();
            SetLocalControl(false);
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            SetCameraRigDetached(IsOwner);
            SetLocalControl(IsOwner);
            ApplyPlayerColour(OwnerId);
        }

        public override void OnOwnershipClient(NetworkConnection previousOwner)
        {
            base.OnOwnershipClient(previousOwner);
            SetCameraRigDetached(IsOwner);
            SetLocalControl(IsOwner);
            ApplyPlayerColour(OwnerId);
        }

        public override void OnStopClient()
        {
            SetLocalControl(false);
            SetCameraRigDetached(false);
            base.OnStopClient();
        }

        private void OnDestroy()
        {
            // A detached camera is no longer a child of the network Player, so it must be
            // explicitly cleaned up if the Player is destroyed without OnStopClient.
            if (m_cameraRigDetached && m_cameraRig != null)
            {
                Destroy(m_cameraRig.gameObject);
            }
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
                // The player root rotates to face movement. Keeping the camera underneath
                // that root makes it inherit the rotation before its LateUpdate can run,
                // which produces a visible correction on every turn.
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
