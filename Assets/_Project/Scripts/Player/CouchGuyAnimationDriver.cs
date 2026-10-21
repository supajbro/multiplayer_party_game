using UnityEngine;
using CouchGuys.Gameplay.Weapons;

namespace CouchGuys.Player
{
    /// <summary>
    /// Converts replicated player state into Animator parameters. Movement is measured
    /// from the transform so the same component animates local players, remote players,
    /// and server-driven debug bots without sending separate animation messages.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CouchGuyAnimationDriver : MonoBehaviour
    {
        private static readonly int SpeedHash = Animator.StringToHash("Speed");
        private static readonly int MoveXHash = Animator.StringToHash("MoveX");
        private static readonly int MoveYHash = Animator.StringToHash("MoveY");
        private static readonly int GroundedHash = Animator.StringToHash("Grounded");
        private static readonly int CarryingHash = Animator.StringToHash("IsCarrying");
        private static readonly int HoldingItemHash = Animator.StringToHash("HoldingItem");
        private static readonly int PointingHash = Animator.StringToHash("Pointing");
        private static readonly int PickupHash = Animator.StringToHash("Pickup");
        private static readonly int DropHash = Animator.StringToHash("Drop");
        private static readonly int HitHash = Animator.StringToHash("Hit");
        private static readonly int EquippedWeaponHash = Animator.StringToHash("EquippedWeapon");
        private static readonly int WeaponShootHash = Animator.StringToHash("WeaponShoot");
        private static readonly int WeaponAimHash = Animator.StringToHash("WeaponAim");

        [Header("References")]
        [SerializeField] private Animator m_animator;
        [SerializeField] private ThirdPersonPlayerController m_playerController;
        [SerializeField] private PlayerCouchCarrier m_couchCarrier;

        [Header("Movement Animation")]
        [SerializeField, Min(0.1f)] private float m_fullMoveSpeed = 3.5f;
        [SerializeField, Min(0f)] private float m_movementDampTime = 0.12f;
        [SerializeField, Min(1f)] private float m_teleportSpeed = 15f;
        [SerializeField, Min(1f)] private float m_maximumAnimatedSpeed = 1.75f;

        private Vector3 m_previousPosition;
        private bool m_wasCarrying;
        private bool m_holdingItem;
        private bool m_pointing;
        private bool m_hasPositionSample;
        private bool m_hasWeaponParameters;
        private bool m_hasWeaponAimParameter;
        private int m_equippedWeapon;
        private float m_weaponAimUntil;
        private PlayerWeaponType m_recoilWeapon;
        private Transform m_torsoBone;
        private Transform m_headBone;
        private Transform m_upperArmLeft;
        private Transform m_lowerArmLeft;
        private Transform m_upperArmRight;
        private Transform m_lowerArmRight;
        private Transform m_weaponHand;
        private float m_recoilAmount;
        private float m_recoilRecovery = 12f;
        private float m_recoilRoll;
        private int m_recoilDirection = 1;
        private bool m_vehicleSeated;
        private bool m_vehicleDriver;

        public Animator Animator => m_animator;
        public float NormalizedMovementSpeed { get; private set; }

        private void Awake()
        {
            if (GetComponent<RobotFaceExpressionController>() == null)
            {
                gameObject.AddComponent<RobotFaceExpressionController>();
            }
            if (GetComponent<RobotHoverVFX>() == null)
            {
                gameObject.AddComponent<RobotHoverVFX>();
            }
            CacheReferences();
        }

        private void OnEnable()
        {
            CacheReferences();
            m_previousPosition = transform.position;
            m_hasPositionSample = true;
            m_wasCarrying = m_couchCarrier != null && m_couchCarrier.IsCarrying;

            if (m_animator != null)
            {
                CacheWeaponParameterSupport();
                m_animator.SetBool(CarryingHash, m_wasCarrying);
                m_animator.SetBool(HoldingItemHash, m_holdingItem);
                m_animator.SetBool(PointingHash, m_pointing);
            }
        }

        private void Update()
        {
            if (m_animator == null || !m_hasWeaponAimParameter) return;
            bool aiming = m_equippedWeapon > 0 && !m_vehicleSeated &&
                !(m_couchCarrier != null && m_couchCarrier.IsCarrying) &&
                Time.time < m_weaponAimUntil;
            // Feed before Animator evaluation; all observers use the existing shot
            // callback and replicated movement, with no cosmetic RPC or SyncVar.
            m_animator.SetFloat(WeaponAimHash, aiming ? 1f : 0f,
                aiming ? 0.045f : 0.18f, Time.deltaTime);
        }

        private void LateUpdate()
        {
            if (m_animator == null)
            {
                CacheReferences();
                if (m_animator == null)
                {
                    return;
                }
            }

            UpdateMovementParameters();
            UpdateCarryingParameters();
            if (m_vehicleSeated) ApplyVehicleSeatPose();
            else ApplyProceduralRecoil();
        }

        public void SetVehicleSeated(bool seated, bool driver)
        {
            if (m_vehicleSeated == seated && m_vehicleDriver == driver) return;
            m_vehicleSeated = seated;
            m_vehicleDriver = driver;
            m_recoilAmount = 0f;
            m_recoilRoll = 0f;
            m_hasPositionSample = false;
            if (m_animator == null) return;
            m_animator.SetFloat(MoveXHash, 0f);
            m_animator.SetFloat(MoveYHash, 0f);
            m_animator.SetFloat(SpeedHash, 0f);
            m_animator.SetBool(GroundedHash, true);
        }

        /// <summary>Allows weapon/gameplay code to enter and leave the pointing pose.</summary>
        public void SetPointing(bool pointing)
        {
            m_pointing = pointing;
            if (m_animator != null)
            {
                m_animator.SetBool(PointingHash, pointing);
            }
        }

        /// <summary>Uses the generic two-handed holding loop for non-couch objects.</summary>
        public void SetHoldingItem(bool holdingItem)
        {
            m_holdingItem = holdingItem;
            if (m_animator != null)
            {
                m_animator.SetBool(HoldingItemHash, holdingItem);
            }
        }

        /// <summary>Plays the authored hit reaction without affecting gameplay state.</summary>
        public void PlayHitReaction()
        {
            m_animator?.SetTrigger(HitHash);
        }

        public void SetEquippedWeapon(int weaponIndex)
        {
            if (m_equippedWeapon != weaponIndex)
            {
                m_weaponAimUntil = 0f;
                m_recoilAmount = 0f;
                m_recoilRoll = 0f;
            }
            m_equippedWeapon = weaponIndex;
            if (m_animator != null && m_hasWeaponParameters)
                m_animator.SetInteger(EquippedWeaponHash, weaponIndex);
        }

        public void PlayWeaponShot(PlayerWeaponType weapon)
        {
            m_recoilWeapon = weapon;
            m_weaponAimUntil = Time.time + (weapon == PlayerWeaponType.Shotgun ? 0.55f :
                weapon == PlayerWeaponType.AssaultRifle ? 0.22f : 0.34f);
            if (m_animator != null && m_hasWeaponParameters)
                m_animator.SetInteger(EquippedWeaponHash, (int)weapon + 1);

            float impulse;
            float maximum;
            switch (weapon)
            {
                case PlayerWeaponType.AssaultRifle:
                    impulse = 0.38f;
                    maximum = 1.05f;
                    m_recoilRecovery = 8.5f;
                    break;
                case PlayerWeaponType.Shotgun:
                    impulse = 1.7f;
                    maximum = 1.7f;
                    m_recoilRecovery = 4.8f;
                    break;
                default:
                    impulse = 1f;
                    maximum = 1f;
                    m_recoilRecovery = 11.5f;
                    break;
            }

            m_recoilAmount = Mathf.Min(maximum, m_recoilAmount + impulse);
            m_recoilDirection = -m_recoilDirection;
            m_recoilRoll = m_recoilDirection * (weapon == PlayerWeaponType.Shotgun ? 1f : 0.45f);
        }

        private void UpdateMovementParameters()
        {
            Vector3 currentPosition = transform.position;
            float deltaTime = Mathf.Max(Time.deltaTime, 0.0001f);
            if (m_vehicleSeated)
            {
                m_previousPosition = currentPosition;
                m_hasPositionSample = true;
                NormalizedMovementSpeed = 0f;
                m_animator.SetFloat(MoveXHash, 0f);
                m_animator.SetFloat(MoveYHash, 0f);
                m_animator.SetFloat(SpeedHash, 0f);
                m_animator.SetBool(GroundedHash, true);
                return;
            }
            Vector3 worldVelocity = m_hasPositionSample
                ? (currentPosition - m_previousPosition) / deltaTime
                : Vector3.zero;
            m_previousPosition = currentPosition;
            m_hasPositionSample = true;

            if (worldVelocity.magnitude >= m_teleportSpeed)
            {
                worldVelocity = Vector3.zero;
            }

            bool isGrounded = m_playerController == null || !m_playerController.enabled
                ? Mathf.Abs(worldVelocity.y) < 0.15f
                : m_playerController.IsGrounded;
            worldVelocity = Vector3.ProjectOnPlane(worldVelocity, Vector3.up);

            Vector3 localVelocity = transform.InverseTransformDirection(worldVelocity);
            Vector2 normalizedMovement = Vector2.ClampMagnitude(
                new Vector2(localVelocity.x, localVelocity.z) / m_fullMoveSpeed,
                m_maximumAnimatedSpeed);
            NormalizedMovementSpeed = normalizedMovement.magnitude;

            m_animator.SetFloat(
                MoveXHash,
                normalizedMovement.x,
                m_movementDampTime,
                deltaTime);
            m_animator.SetFloat(
                MoveYHash,
                normalizedMovement.y,
                m_movementDampTime,
                deltaTime);
            m_animator.SetFloat(
                SpeedHash,
                normalizedMovement.magnitude,
                m_movementDampTime,
                deltaTime);
            m_animator.SetBool(GroundedHash, isGrounded);
        }

        private void UpdateCarryingParameters()
        {
            bool isCarrying = m_couchCarrier != null && m_couchCarrier.IsCarrying;
            m_animator.SetBool(CarryingHash, isCarrying);

            if (isCarrying == m_wasCarrying)
            {
                return;
            }

            m_animator.ResetTrigger(isCarrying ? DropHash : PickupHash);
            m_animator.SetTrigger(isCarrying ? PickupHash : DropHash);
            m_wasCarrying = isCarrying;
        }

        private void CacheReferences()
        {
            m_playerController ??= GetComponent<ThirdPersonPlayerController>();
            m_couchCarrier ??= GetComponent<PlayerCouchCarrier>();
            m_animator ??= GetComponentInChildren<Animator>(true);
            if (m_animator != null && m_torsoBone == null)
            {
                // Mesh objects share these names with the bones. Recoil must act
                // on the animated skeleton, not accumulate on static mesh pivots.
                Transform skeleton = FindDescendant(m_animator.transform, "CouchGuy_Rig") ?? m_animator.transform;
                m_torsoBone = FindDescendant(skeleton, "Torso");
                m_headBone = FindDescendant(skeleton, "Head");
                m_upperArmLeft = FindDescendant(skeleton, "UpperArm_L");
                m_lowerArmLeft = FindDescendant(skeleton, "LowerArm_L");
                m_upperArmRight = FindDescendant(skeleton, "UpperArm_R");
                m_lowerArmRight = FindDescendant(skeleton, "LowerArm_R");
                // Imported FBX Hand_L is on the player's physical right side.
                m_weaponHand = FindDescendant(skeleton, "Hand_L");
            }
        }

        private void ApplyProceduralRecoil()
        {
            if (m_recoilAmount <= 0.0001f || (m_couchCarrier != null && m_couchCarrier.IsCarrying))
            {
                m_recoilAmount = 0f;
                return;
            }

            float recoil = m_recoilAmount;
            ApplyAdditiveRotation(m_torsoBone, new Vector3(7.5f * recoil, 0f, 2.2f * m_recoilRoll * recoil));
            ApplyAdditiveRotation(m_headBone, new Vector3(-5.2f * recoil, 0f, -1.7f * m_recoilRoll * recoil));
            ApplyAdditiveRotation(m_upperArmLeft, new Vector3(10.5f * recoil, 0f, -1.8f * m_recoilRoll * recoil));
            ApplyAdditiveRotation(m_lowerArmLeft, new Vector3(6.5f * recoil, 0f, 0f));
            ApplyAdditiveRotation(m_upperArmRight, new Vector3(7.2f * recoil, 0f, 1.2f * m_recoilRoll * recoil));
            ApplyAdditiveRotation(m_lowerArmRight, new Vector3(4.5f * recoil, 0f, 0f));

            // Keep the existing per-weapon visual impulses and pistol wrist flick.
            // Backward shotgun travel already lives on the weapon's restored local
            // pose. Never translate the torso here: constant imported position
            // channels can be optimised out, allowing additive offsets to accumulate.
            if (m_weaponHand != null)
            {
                float kick = m_recoilWeapon == PlayerWeaponType.Pistol ? 11f :
                    m_recoilWeapon == PlayerWeaponType.AssaultRifle ? 2.5f : 4f;
                m_weaponHand.rotation = Quaternion.AngleAxis(-kick * recoil, transform.right) *
                    m_weaponHand.rotation;
            }
            m_recoilAmount = Mathf.MoveTowards(m_recoilAmount, 0f, m_recoilRecovery * Time.deltaTime);
            m_recoilRoll = Mathf.MoveTowards(m_recoilRoll, 0f, m_recoilRecovery * 0.75f * Time.deltaTime);
        }

        private void ApplyVehicleSeatPose()
        {
            if (m_vehicleDriver)
            {
                ApplyAdditiveRotation(m_upperArmLeft, new Vector3(22f, -12f, -18f));
                ApplyAdditiveRotation(m_lowerArmLeft, new Vector3(-48f, 0f, 0f));
                ApplyAdditiveRotation(m_upperArmRight, new Vector3(22f, 12f, 18f));
                ApplyAdditiveRotation(m_lowerArmRight, new Vector3(-48f, 0f, 0f));
            }
            else
            {
                ApplyAdditiveRotation(m_upperArmLeft, new Vector3(8f, -4f, -7f));
                ApplyAdditiveRotation(m_lowerArmLeft, new Vector3(-18f, 0f, 0f));
                ApplyAdditiveRotation(m_upperArmRight, new Vector3(8f, 4f, 7f));
                ApplyAdditiveRotation(m_lowerArmRight, new Vector3(-18f, 0f, 0f));
            }
        }

        private static void ApplyAdditiveRotation(Transform bone, Vector3 euler)
        {
            if (bone != null)
                bone.localRotation *= Quaternion.Euler(euler);
        }

        private static Transform FindDescendant(Transform root, string name)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                if (child.name == name) return child;
            return null;
        }

        private void CacheWeaponParameterSupport()
        {
            m_hasWeaponParameters = false;
            m_hasWeaponAimParameter = false;
            if (m_animator == null) return;
            bool hasEquipped = false;
            bool hasShoot = false;
            foreach (AnimatorControllerParameter parameter in m_animator.parameters)
            {
                hasEquipped |= parameter.nameHash == EquippedWeaponHash;
                hasShoot |= parameter.nameHash == WeaponShootHash;
                m_hasWeaponAimParameter |= parameter.nameHash == WeaponAimHash;
            }
            m_hasWeaponParameters = hasEquipped && hasShoot;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            m_fullMoveSpeed = Mathf.Max(0.1f, m_fullMoveSpeed);
            m_movementDampTime = Mathf.Max(0f, m_movementDampTime);
            m_teleportSpeed = Mathf.Max(1f, m_teleportSpeed);
            m_maximumAnimatedSpeed = Mathf.Max(1f, m_maximumAnimatedSpeed);
            CacheReferences();
        }
#endif
    }
}
