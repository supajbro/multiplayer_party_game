using CouchGuys.Input;
using UnityEngine;

namespace CouchGuys.CameraSystem
{
    /// <summary>
    /// A lightweight mouse-driven orbit camera with over-the-shoulder framing and collision.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ThirdPersonCameraController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform m_cameraTarget;
        [SerializeField] private PlayerInputReader m_input;

        [Header("Framing")]
        [SerializeField, Min(0.1f)] private float m_cameraDistance = 4.5f;
        [SerializeField] private float m_pivotHeight = 0.15f;
        [SerializeField] private float m_shoulderOffset = 0.65f;

        [Header("Orbit")]
        [SerializeField, Min(0f)] private float m_mouseSensitivity = 0.12f;
        [SerializeField, Range(-89f, 0f)] private float m_minimumVerticalAngle = -35f;
        [SerializeField, Range(0f, 89f)] private float m_maximumVerticalAngle = 65f;
        [SerializeField] private float m_initialVerticalAngle = 12f;

        [Header("Smoothing")]
        [SerializeField, Min(0f)] private float m_followSmoothing = 45f;
        [SerializeField, Min(0f)] private float m_rotationSmoothing = 45f;

        [Header("Camera Collision")]
        [SerializeField] private LayerMask m_collisionLayers = ~0;
        [SerializeField, Min(0.01f)] private float m_collisionRadius = 0.2f;
        [SerializeField, Min(0f)] private float m_collisionPadding = 0.1f;

        [Header("Cursor")]
        [SerializeField] private bool m_lockCursor = true;

        private float m_yaw;
        private float m_pitch;
        private Transform m_playerRoot;
        private Vector3 m_previousPivotPosition;
        private bool m_hasPreviousPivotPosition;
        private readonly RaycastHit[] m_collisionHits = new RaycastHit[16];

        private void Awake()
        {
            m_playerRoot = m_cameraTarget != null ? m_cameraTarget.root : null;
            m_yaw = m_playerRoot != null ? m_playerRoot.eulerAngles.y : transform.eulerAngles.y;
            m_pitch = Mathf.Clamp(m_initialVerticalAngle, m_minimumVerticalAngle, m_maximumVerticalAngle);

            Vector3 pivot = GetPivotPosition();
            m_previousPivotPosition = pivot;
            m_hasPreviousPivotPosition = true;
            Quaternion orbitRotation = Quaternion.Euler(m_pitch, m_yaw, 0f);
            transform.SetPositionAndRotation(CalculateCollisionPosition(pivot, orbitRotation), orbitRotation);
        }

        private void OnEnable()
        {
            m_previousPivotPosition = GetPivotPosition();
            m_hasPreviousPivotPosition = true;
            ApplyCursorState();
        }

        private void OnDisable()
        {
            if (m_lockCursor && Cursor.lockState == CursorLockMode.Locked)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        private void LateUpdate()
        {
            if (m_cameraTarget == null || m_input == null)
            {
                return;
            }

            Vector3 pivot = GetPivotPosition();
            CompensateForTargetMovement(pivot);

            Vector2 lookInput = m_input.Look;
            m_yaw += lookInput.x * m_mouseSensitivity;
            m_pitch = Mathf.Clamp(
                m_pitch - lookInput.y * m_mouseSensitivity,
                m_minimumVerticalAngle,
                m_maximumVerticalAngle);

            Quaternion desiredRotation = Quaternion.Euler(m_pitch, m_yaw, 0f);
            Vector3 desiredPosition = CalculateCollisionPosition(pivot, desiredRotation);

            float positionBlend = 1f - Mathf.Exp(-m_followSmoothing * Time.deltaTime);
            float rotationBlend = 1f - Mathf.Exp(-m_rotationSmoothing * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, desiredPosition, positionBlend);
            transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, rotationBlend);
        }

        private void CompensateForTargetMovement(Vector3 pivot)
        {
            if (!m_hasPreviousPivotPosition)
            {
                m_previousPivotPosition = pivot;
                m_hasPreviousPivotPosition = true;
                return;
            }

            // A locally owned camera rig is detached from the rotating network Player.
            // Carry it by the target's exact translation before applying orbit smoothing,
            // otherwise normal movement appears to lag or rubber-band behind the Player.
            bool inheritsPlayerTransform = m_playerRoot != null && transform.IsChildOf(m_playerRoot);
            if (!inheritsPlayerTransform)
            {
                transform.position += pivot - m_previousPivotPosition;
            }

            m_previousPivotPosition = pivot;
        }

        private Vector3 GetPivotPosition()
        {
            return m_cameraTarget != null
                ? m_cameraTarget.position + Vector3.up * m_pivotHeight
                : transform.position;
        }

        private Vector3 CalculateCollisionPosition(Vector3 pivot, Quaternion orbitRotation)
        {
            Vector3 desiredOffset = orbitRotation * new Vector3(m_shoulderOffset, 0f, -m_cameraDistance);
            float desiredDistance = desiredOffset.magnitude;
            Vector3 castDirection = desiredOffset / desiredDistance;
            float nearestDistance = desiredDistance;

            int hitCount = Physics.SphereCastNonAlloc(
                pivot,
                m_collisionRadius,
                castDirection,
                m_collisionHits,
                desiredDistance,
                m_collisionLayers,
                QueryTriggerInteraction.Ignore);

            for (int index = 0; index < hitCount; index++)
            {
                Transform hitTransform = m_collisionHits[index].transform;
                if (m_playerRoot != null && hitTransform.IsChildOf(m_playerRoot))
                {
                    continue;
                }

                nearestDistance = Mathf.Min(nearestDistance, m_collisionHits[index].distance);
            }

            float correctedDistance = Mathf.Max(0f, nearestDistance - m_collisionPadding);
            return pivot + castDirection * correctedDistance;
        }

        private void ApplyCursorState()
        {
            if (!m_lockCursor)
            {
                return;
            }

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            m_cameraDistance = Mathf.Max(0.1f, m_cameraDistance);
            m_maximumVerticalAngle = Mathf.Max(m_minimumVerticalAngle, m_maximumVerticalAngle);
            m_initialVerticalAngle = Mathf.Clamp(m_initialVerticalAngle, m_minimumVerticalAngle, m_maximumVerticalAngle);
        }
#endif
    }
}
