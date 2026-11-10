using UnityEngine;

namespace CouchGuys.Gameplay.Pedestrians
{
    // Shared by thief impacts and pedestrian punches. Keep launch and standing
    // knockback calculations identical while each controller owns its AI state.
    internal static class NpcImpactPhysics
    {
        internal const float KnockbackDuration = .22f;
        internal const float KnockbackDamping = 14f;
        internal const float DamageRagdollImpulseScale = .2f;
        internal const float GroundedRecoveryDuration = 2.5f;
        internal static readonly Vector2 AirborneFlipSpeed = new(7f, 11f);

        internal static Vector3 Impulse(Vector3 direction, float force, float lift) =>
            direction.normalized * Mathf.Max(0f, force) + Vector3.up * Mathf.Max(0f, lift);

        internal static Vector3 StepKnockback(ref Vector3 velocity, float damping, float deltaTime)
        {
            Vector3 displacement = velocity * deltaTime;
            velocity = Vector3.MoveTowards(velocity, Vector3.zero, damping * deltaTime);
            return displacement;
        }

        internal static bool TryStandingPosition(Transform root, Collider standingCollider, Vector3 candidate, out Vector3 position)
        {
            position = default;
            foreach (RaycastHit hit in Physics.RaycastAll(candidate + Vector3.up * .4f, Vector3.down,
                         .8f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.transform.IsChildOf(root) || hit.normal.y < .65f) continue;
                float radius = .35f;
                float height = 1.8f;
                if (standingCollider is CapsuleCollider capsule)
                {
                    Vector3 scale = capsule.transform.lossyScale;
                    radius = capsule.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
                    height = Mathf.Max(radius * 2f, capsule.height * Mathf.Abs(scale.y));
                }
                Vector3 bottom = hit.point + Vector3.up * (radius + .05f);
                Vector3 top = hit.point + Vector3.up * (height - radius + .05f);
                bool blocked = false;
                foreach (Collider obstacle in Physics.OverlapCapsule(bottom, top, radius * .9f,
                             ~0, QueryTriggerInteraction.Ignore))
                    if (!obstacle.transform.IsChildOf(root)) { blocked = true; break; }
                if (blocked) continue;
                position = hit.point;
                return true;
            }
            return false;
        }

        internal static Vector3 CreateAirborneSpin(Vector3 velocity, Vector3 forward, Vector2 flipSpeed)
        {
            Vector3 travel = Vector3.ProjectOnPlane(velocity, Vector3.up);
            if (travel.sqrMagnitude < .01f) travel = forward;
            Vector3 flipAxis = Vector3.Cross(Vector3.up, travel).normalized;
            if (Random.value < .5f) flipAxis = -flipAxis;
            Vector3 axis = (flipAxis + Random.insideUnitSphere * .35f).normalized;
            float minimum = Mathf.Max(0f, Mathf.Min(flipSpeed.x, flipSpeed.y));
            float maximum = Mathf.Max(minimum, Mathf.Max(flipSpeed.x, flipSpeed.y));
            return axis * Random.Range(minimum, maximum);
        }
    }

    /// <summary>Shared, locally simulated bone physics. Gameplay state remains server-owned.</summary>
    public sealed class PedestrianRagdoll : MonoBehaviour
    {
        [SerializeField] private Animator m_animator;
        [SerializeField] private Rigidbody[] m_bones;
        [SerializeField] private Collider[] m_colliders;
        private bool m_fallen;
        private bool m_permanent;
        private float m_sleepAt;
        private Vector3[] m_posePositions;
        private Quaternion[] m_poseRotations;
        private Transform[] m_pose;
        private float m_recoverAt;
        private Vector3 m_hipOffset;
        private Vector3 m_previousRoot;
        private Quaternion m_previousRootRotation;
        private Vector3 m_visualLocalPosition;
        private Quaternion m_visualLocalRotation;
        private Vector3[] m_animationPositions, m_targetPositions;
        private Quaternion[] m_animationRotations, m_targetRotations;
        private float[] m_tumbleDamping;
        private bool m_tumbling;
        private float m_tumbleStartedAt;
        public Rigidbody Body => m_bones[0];
        public bool IsFallen => m_fallen;

        private void Awake()
        {
            m_pose = m_animator.GetComponentsInChildren<Transform>();
            m_posePositions = new Vector3[m_pose.Length];
            m_poseRotations = new Quaternion[m_pose.Length];
            m_visualLocalPosition = m_animator.transform.localPosition;
            m_visualLocalRotation = m_animator.transform.localRotation;
            m_animationPositions = new Vector3[m_pose.Length];
            m_animationRotations = new Quaternion[m_pose.Length];
            m_targetPositions = new Vector3[m_pose.Length];
            m_targetRotations = new Quaternion[m_pose.Length];
        }

        public void Fall(Vector3 impulse) => Fall(impulse + Vector3.up, true);

        public void Fall(Vector3 impulse, bool permanent)
        {
            if (m_fallen && m_permanent) return;
            m_permanent = permanent;
            if (!m_fallen)
            {
                m_fallen = true;
                m_recoverAt = 0f;
                m_animator.enabled = false;
                // Animated transforms may not yet have reached the physics scene.
                Physics.SyncTransforms();
                m_hipOffset = Body.position - transform.root.position;
                m_previousRoot = transform.root.position;
                m_previousRootRotation = transform.root.rotation;
            }
            foreach (Collider bone in m_colliders) bone.enabled = true;
            for (int i = 0; i < m_colliders.Length; i++)
            for (int j = i + 1; j < m_colliders.Length; j++)
                Physics.IgnoreCollision(m_colliders[i], m_colliders[j]);
            foreach (Rigidbody bone in m_bones) bone.isKinematic = false;
            AddImpulse(impulse);
            m_sleepAt = permanent ? Time.time + 4f : float.PositiveInfinity;
            enabled = true;
        }

        public void AddImpulse(Vector3 impulse)
        {
            foreach (Rigidbody bone in m_bones) bone.AddForce(impulse, ForceMode.VelocityChange);
        }

        public void AddTumble(Vector3 angularVelocity)
        {
            if (!m_fallen || angularVelocity.sqrMagnitude < .001f) return;
            angularVelocity = Vector3.ClampMagnitude(angularVelocity, 14f);
            m_tumbleDamping ??= new float[m_bones.Length];
            Vector3 pivot = Body.worldCenterOfMass;
            for (int i = 0; i < m_bones.Length; i++)
            {
                Rigidbody bone = m_bones[i];
                if (!m_tumbling) m_tumbleDamping[i] = bone.angularDamping;
                bone.angularDamping = Mathf.Min(m_tumbleDamping[i], .15f);
                bone.maxAngularVelocity = Mathf.Max(bone.maxAngularVelocity, 14f);
                // Rotate the whole body about its hips, rather than just twisting
                // individual joints. Joint limits produce the floppy limb motion.
                bone.AddForce(Vector3.Cross(angularVelocity, bone.worldCenterOfMass - pivot), ForceMode.VelocityChange);
                bone.angularVelocity = Vector3.ClampMagnitude(bone.angularVelocity + angularVelocity, 14f);
            }
            m_tumbling = true;
            m_tumbleStartedAt = Time.time;
        }

        private void RestoreTumbleDamping()
        {
            if (!m_tumbling) return;
            for (int i = 0; i < m_bones.Length; i++) m_bones[i].angularDamping = m_tumbleDamping[i];
            m_tumbling = false;
        }

        private void FixedUpdate()
        {
            if (m_tumbling && Time.time >= m_tumbleStartedAt + .15f && TryGround(out _))
                RestoreTumbleDamping();
        }

        public void Pull(Vector3 direction, float acceleration, float speed, float dt)
        {
            float change = Mathf.Min(acceleration * dt, Mathf.Max(0f, speed - Vector3.Dot(Body.linearVelocity, direction)));
            AddImpulse(direction * change);
        }

        public void RepositionRoot(Transform root, Vector3 position)
        {
            Vector3 visualPosition = m_animator.transform.position;
            root.position = position;
            m_animator.transform.position = visualPosition;
        }

        // Preserve physical bone positions while the server moves the replicated root.
        public void FollowRoot(Transform root, bool authority)
        {
            Vector3 visualPosition = m_animator.transform.position;
            if (authority)
                root.position = Body.position - m_hipOffset;
            else
            {
                Quaternion undoRotation = m_previousRootRotation * Quaternion.Inverse(root.rotation);
                visualPosition = m_previousRoot + undoRotation * (visualPosition - root.position);
                m_animator.transform.rotation = undoRotation * m_animator.transform.rotation;
            }
            // Compensate once at the non-physical visual parent. Moving individual
            // nested rigidbodies lets their parent transforms drag the children again
            // when Unity next synchronizes physics, accumulating a downward teleport.
            m_animator.transform.position = visualPosition;
            Physics.SyncTransforms();
            m_previousRoot = root.position;
            m_previousRootRotation = root.rotation;
            if (!authority)
            {
                Vector3 correction = root.position + m_hipOffset - Body.position;
                foreach (Rigidbody bone in m_bones)
                    bone.AddForce(correction * 25f - bone.linearVelocity * 5f, ForceMode.Acceleration);
            }
        }

        public bool TryGround(out Vector3 ground)
        {
            ground = default;
            if (Body.linearVelocity.y > 0.5f) return false;
            foreach (Collider bone in m_colliders)
            {
                Bounds bounds = bone.bounds;
                foreach (RaycastHit hit in Physics.RaycastAll(bounds.center, Vector3.down,
                    bounds.extents.y + 0.12f, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (hit.collider.transform.IsChildOf(transform.root) || hit.normal.y < 0.65f) continue;
                    ground = hit.point;
                    return true;
                }
            }
            return false;
        }

        public void Recover()
        {
            if (!m_fallen || m_permanent) return;
            RestoreTumbleDamping();
            for (int i = 0; i < m_pose.Length; i++)
            {
                m_posePositions[i] = m_pose[i].position;
                m_poseRotations[i] = m_pose[i].rotation;
            }
            foreach (Rigidbody bone in m_bones) bone.isKinematic = true;
            foreach (Collider bone in m_colliders) bone.enabled = false;
            m_fallen = false;
            m_animator.transform.SetLocalPositionAndRotation(m_visualLocalPosition, m_visualLocalRotation);
            m_animator.enabled = true;
            m_animator.Rebind();
            m_animator.Update(0f);
            for (int i = 0; i < m_pose.Length; i++)
            {
                m_animationPositions[i] = m_pose[i].localPosition;
                m_animationRotations[i] = m_pose[i].localRotation;
            }
            m_recoverAt = Time.time + 0.4f;
            enabled = true;
        }

        private void LateUpdate()
        {
            if (m_recoverAt <= 0f) return;
            float blend = Mathf.Clamp01((m_recoverAt - Time.time) / 0.4f);
            // Snapshot all animation targets before moving any parent transforms.
            for (int i = 0; i < m_pose.Length; i++)
            {
                m_targetPositions[i] = m_pose[i].position;
                m_targetRotations[i] = m_pose[i].rotation;
            }
            for (int i = 0; i < m_pose.Length; i++)
            {
                m_pose[i].SetPositionAndRotation(
                    Vector3.Lerp(m_targetPositions[i], m_posePositions[i], blend),
                    Quaternion.Slerp(m_targetRotations[i], m_poseRotations[i], blend));
            }
            if (blend <= 0f) { m_recoverAt = 0f; enabled = false; }
        }

        private void Update()
        {
            if (m_recoverAt > 0f)
            {
                // Restore unanimated transforms too, before Animator evaluates this frame.
                for (int i = 0; i < m_pose.Length; i++)
                    m_pose[i].SetLocalPositionAndRotation(m_animationPositions[i], m_animationRotations[i]);
            }
            // A corpse launched from height must finish falling before its
            // local physics can sleep; a fixed timeout alone can freeze it midair.
            if (!m_fallen || Time.time < m_sleepAt || !TryGround(out _)) return;
            RestoreTumbleDamping();
            foreach (Rigidbody bone in m_bones) bone.isKinematic = true;
            foreach (Collider bone in m_colliders) bone.enabled = false;
            enabled = false;
        }
    }
}
