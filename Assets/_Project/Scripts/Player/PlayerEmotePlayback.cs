using UnityEngine;

namespace CouchGuys.Player
{
    /// <summary>Samples saved clips after the normal Animator, recoil and hover passes.
    /// Restores the underlying pose before the next Update; no gameplay transform is animated.</summary>
    [DefaultExecutionOrder(-100)]
    public sealed class PlayerEmotePlayback : MonoBehaviour
    {
        [SerializeField] private Animator m_animator;
        [SerializeField] private Transform m_visualRoot;
        [SerializeField] private Vector3 m_visualCentre = new(0, 1.4f, 0);
        [SerializeField] private Transform[] m_bones;
        [System.Serializable]
        private struct ClipOverride
        {
            public AnimationClip Source;
            public AnimationClip Replacement;
        }
        [SerializeField] private ClipOverride[] m_clipOverrides;
        private Vector3[] m_positions;
        private Quaternion[] m_rotations;
        private Vector3 m_visualPosition;
        private Quaternion m_visualRotation;
        private bool m_applied;

        private void Awake()
        {
            int count = m_bones != null ? m_bones.Length : 0;
            m_positions = new Vector3[count];
            m_rotations = new Quaternion[count];
        }

        // Early Update restores before health/vehicle code and Animator evaluation.
        private void Update() => RestorePose();
        private void OnDisable() => RestorePose();

        public void RestorePose()
        {
            if (!m_applied) return;
            m_applied = false;
            for (int i = 0; i < m_bones.Length; i++)
            {
                if (m_bones[i] == null) continue;
                m_bones[i].SetLocalPositionAndRotation(m_positions[i], m_rotations[i]);
            }
            m_visualRoot.SetLocalPositionAndRotation(m_visualPosition, m_visualRotation);
        }

        public void Sample(EmoteDefinition emote, float age)
        {
            RestorePose();
            if (emote == null || emote.Clip == null || m_animator == null || m_visualRoot == null ||
                m_positions == null || m_positions.Length == 0) return;
            float duration = emote.Duration;
            if (duration <= 0 || age < 0 || !emote.Loop && age >= duration) return;
            float t = emote.Loop ? age % duration : age;
            float weight = Mathf.SmoothStep(0, 1, age / emote.BlendTime);
            if (!emote.Loop) weight *= Mathf.SmoothStep(0, 1, (duration - age) / emote.BlendTime);
            for (int i = 0; i < m_bones.Length; i++)
            {
                if (m_bones[i] == null) continue;
                m_positions[i] = m_bones[i].localPosition;
                m_rotations[i] = m_bones[i].localRotation;
            }
            m_visualPosition = m_visualRoot.localPosition;
            m_visualRotation = m_visualRoot.localRotation;
            AnimationClip clip = emote.Clip;
            if (m_clipOverrides != null)
                foreach (var entry in m_clipOverrides)
                    if (entry.Source == clip && entry.Replacement != null) { clip = entry.Replacement; break; }
            clip.SampleAnimation(m_animator.gameObject, t);
            for (int i = 0; i < m_bones.Length; i++)
            {
                if (m_bones[i] == null) continue;
                m_bones[i].localPosition = Vector3.Lerp(m_positions[i], m_bones[i].localPosition, weight);
                m_bones[i].localRotation = Quaternion.Slerp(m_rotations[i], m_bones[i].localRotation, weight);
            }
            Quaternion rotation = Quaternion.Slerp(Quaternion.identity, emote.Rotation(t), weight);
            m_visualRoot.localRotation = m_visualRotation * rotation;
            m_visualRoot.localPosition = m_visualPosition + emote.Position(t) * weight +
                m_visualRotation * (m_visualCentre - rotation * m_visualCentre);
            m_applied = true;
        }
    }
}
