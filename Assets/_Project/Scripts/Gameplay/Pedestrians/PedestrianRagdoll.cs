using UnityEngine;

namespace CouchGuys.Gameplay.Pedestrians
{
    /// <summary>Cosmetic local bone physics; the network root and death state remain server-owned.</summary>
    public sealed class PedestrianRagdoll : MonoBehaviour
    {
        [SerializeField] private Animator m_animator;
        [SerializeField] private Rigidbody[] m_bones;
        [SerializeField] private Collider[] m_colliders;
        private bool m_fallen;
        private float m_sleepAt;

        public void Fall(Vector3 impulse)
        {
            if (m_fallen) return;
            m_fallen = true;
            m_animator.enabled = false;
            for (int i = 0; i < m_colliders.Length; i++)
            for (int j = i + 1; j < m_colliders.Length; j++)
                Physics.IgnoreCollision(m_colliders[i], m_colliders[j]);
            foreach (Collider bone in m_colliders) bone.enabled = true;
            foreach (Rigidbody bone in m_bones)
            {
                bone.isKinematic = false;
                bone.AddForce(impulse + Vector3.up, ForceMode.VelocityChange);
            }
            m_sleepAt = Time.time + 4f;
            enabled = true;
        }

        private void Update()
        {
            if (!m_fallen || Time.time < m_sleepAt) return;
            foreach (Rigidbody bone in m_bones) bone.isKinematic = true;
            // A corpse should not become an immovable vehicle obstacle.
            foreach (Collider bone in m_colliders) bone.enabled = false;
            enabled = false;
        }
    }
}
