using UnityEngine;

namespace CouchGuys.Player
{
    /// <summary>Local cosmetic instance driven solely by the existing replicated attachment.</summary>
    [DefaultExecutionOrder(200)]
    [DisallowMultipleComponent]
    public sealed class MagneticCarryAura : MonoBehaviour
    {
        [SerializeField] private PlayerCouchCarrier m_carrier;
        [SerializeField] private CouchGuyAnimationDriver m_animation;
        [SerializeField] private MagneticCarryEffect m_auraPrefab;
        [SerializeField, Min(0.05f)] private float m_handClearance = .22f;
        [SerializeField, Range(0f, 10f)] private float m_movementLean = 4f;
        private MagneticCarryEffect m_effect;
        private PlayerTruckInteractor m_truckInteractor;
        private Transform m_target, m_torso;
        private readonly Transform[] m_upper = new Transform[2], m_lower = new Transform[2], m_hands = new Transform[2];
        private Collider[] m_couchColliders;
        private Vector3 m_previousPosition, m_lean;

        private void Awake()
        {
            m_carrier ??= GetComponent<PlayerCouchCarrier>();
            m_animation ??= GetComponent<CouchGuyAnimationDriver>();
            m_truckInteractor = GetComponent<PlayerTruckInteractor>();
            m_previousPosition = transform.position;
            if (m_animation == null || m_animation.Animator == null) return;
            Transform rig = Find(m_animation.Animator.transform, "CouchGuy_Rig");
            if (rig == null) return;
            m_torso = Find(rig, "Torso");
            for (int i = 0; i < 2; i++)
            {
                string side = i == 0 ? "L" : "R";
                m_upper[i] = Find(rig, "UpperArm_" + side);
                m_lower[i] = Find(rig, "LowerArm_" + side);
                m_hands[i] = Find(rig, "Hand_" + side);
            }
        }

        private void LateUpdate()
        {
            var couch = m_carrier != null && m_carrier.IsSpawned ? m_carrier.CarriedCouch : null;
            var point = couch != null && !couch.IsSled && !couch.IsLoaded
                ? couch.GetPoint(m_carrier.CarriedPointIndex) : null;
            Transform target = point != null ? point.transform : null;
            var truck = m_truckInteractor != null ? m_truckInteractor.CurrentTruck : null;
            bool usingTurret = truck != null && !m_truckInteractor.IsDriver;
            if (usingTurret) target = truck.GetTurretAuraTarget(m_truckInteractor.CurrentSeatIndex);
            if (target != m_target)
            {
                if (m_effect != null) m_effect.Release();
                m_effect = null;
                m_target = target;
                m_couchColliders = couch != null ? couch.GetComponentsInChildren<Collider>() : null;
                if (target != null && m_auraPrefab != null)
                {
                    // Instantiate a saved, fully authored effect. Never construct its hierarchy here.
                    m_effect = Instantiate(m_auraPrefab);
                    m_effect.Attach(m_hands[0], m_hands[1], m_torso, target);
                }
            }
            Vector3 velocity = (transform.position - m_previousPosition) / Mathf.Max(Time.deltaTime, .0001f);
            m_previousPosition = transform.position;
            if (target == null) return;
            if (usingTurret) return;
            if (velocity.sqrMagnitude > 225f) velocity = Vector3.zero;
            Vector3 local = transform.InverseTransformDirection(velocity);
            m_lean = Vector3.Lerp(m_lean, new Vector3(local.z, 0, -local.x) * m_movementLean,
                1f - Mathf.Exp(-8f * Time.deltaTime));
            if (m_torso != null) m_torso.localRotation *= Quaternion.Euler(Vector3.ClampMagnitude(m_lean, 7f));
            // A visual-only reach correction keeps the open palms away from rotating couch geometry.
            // It changes bones, never attachment positions, colliders or physics.
            for (int i = 0; i < 2; i++) KeepHandClear(i);
        }

        private void KeepHandClear(int i)
        {
            Transform hand = m_hands[i], upper = m_upper[i], lower = m_lower[i];
            if (hand == null || upper == null || lower == null || m_couchColliders == null) return;
            Vector3 desired = hand.position;
            foreach (Collider collider in m_couchColliders)
            {
                if (collider == null || !collider.enabled || collider.isTrigger) continue;
                // A conservative world-space envelope includes the palm and fingers, not just the wrist.
                Bounds envelope = collider.bounds;
                envelope.Expand(m_handClearance * 2f);
                if (!envelope.Contains(desired)) continue;
                Vector3 outward = Vector3.ProjectOnPlane(upper.position - envelope.center, Vector3.up).normalized;
                if (outward.sqrMagnitude < .001f) outward = -transform.forward;
                float distance = Mathf.Abs(outward.x) * envelope.extents.x + Mathf.Abs(outward.z) * envelope.extents.z;
                desired = envelope.center + outward * (distance + .01f) + Vector3.up * (desired.y - envelope.center.y);
            }
            if ((desired - hand.position).sqrMagnitude < .000001f) return;
            Quaternion wrist = hand.rotation;
            float a = Vector3.Distance(upper.position, lower.position), b = Vector3.Distance(lower.position, hand.position);
            Vector3 direction = desired - upper.position;
            float d = Mathf.Clamp(direction.magnitude, Mathf.Abs(a - b) + .001f, a + b - .001f);
            if (d <= .001f || a <= .001f || b <= .001f) return;
            Vector3 forward = direction.normalized;
            Vector3 bend = Vector3.ProjectOnPlane(-transform.up, forward).normalized;
            float along = (a * a + d * d - b * b) / (2f * d);
            Vector3 elbow = upper.position + forward * along + bend * Mathf.Sqrt(Mathf.Max(0, a * a - along * along));
            upper.rotation = Quaternion.FromToRotation(lower.position - upper.position, elbow - upper.position) * upper.rotation;
            Vector3 reachable = upper.position + forward * d;
            lower.rotation = Quaternion.FromToRotation(hand.position - lower.position, reachable - lower.position) * lower.rotation;
            hand.rotation = wrist;
        }

        private void OnDisable()
        {
            // Player despawn/pooling must not leave a world-space aura behind.
            if (m_effect != null) Destroy(m_effect.gameObject);
            m_effect = null;
            m_target = null;
        }

        private static Transform Find(Transform root, string name)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                if (child.name == name) return child;
            return null;
        }
    }
}
