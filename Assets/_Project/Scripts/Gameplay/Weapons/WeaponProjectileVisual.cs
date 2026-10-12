using System;
using UnityEngine;

namespace CouchGuys.Gameplay.Weapons
{
    [DisallowMultipleComponent]
    public sealed class WeaponProjectileVisual : MonoBehaviour
    {
        private Vector3 m_end;
        private float m_speed;
        private float m_expiresAt;
        private Action<WeaponProjectileVisual> m_onComplete;
        private TrailRenderer m_trail;

        private void Awake() => m_trail = GetComponentInChildren<TrailRenderer>(true);

        public void Launch(Vector3 start, Vector3 end, WeaponDefinition weapon,
            Action<WeaponProjectileVisual> onComplete)
        {
            m_end = end;
            m_speed = Mathf.Max(1f, weapon.BulletSpeed);
            m_expiresAt = Time.time + Mathf.Max(0.05f, weapon.BulletLifetime);
            m_onComplete = onComplete;
            transform.SetPositionAndRotation(start,
                (end - start).sqrMagnitude > 0.0001f
                    ? Quaternion.LookRotation((end - start).normalized, Vector3.up)
                    : Quaternion.identity);
            transform.localScale = weapon.BulletSize;

            Renderer renderer = GetComponentInChildren<Renderer>(true);
            if (renderer != null)
            {
                MaterialPropertyBlock properties = new();
                renderer.GetPropertyBlock(properties);
                properties.SetColor("_BaseColor", weapon.BulletColour);
                properties.SetColor("_Color", weapon.BulletColour);
                properties.SetColor("_EmissionColor", weapon.BulletColour * 1.5f);
                renderer.SetPropertyBlock(properties);
            }

            m_trail ??= GetComponentInChildren<TrailRenderer>(true);
            if (m_trail != null)
            {
                m_trail.Clear();
                m_trail.startWidth = weapon.TrailWidth;
                m_trail.endWidth = 0f;
                m_trail.startColor = weapon.TrailColour;
                m_trail.endColor = new Color(weapon.TrailColour.r, weapon.TrailColour.g,
                    weapon.TrailColour.b, 0f);
                m_trail.emitting = true;
            }
            gameObject.SetActive(true);
        }

        private void Update()
        {
            float distance = Vector3.Distance(transform.position, m_end);
            float step = m_speed * Time.deltaTime;
            if (distance <= step || Time.time >= m_expiresAt)
            {
                transform.position = m_end;
                Complete();
                return;
            }
            transform.position = Vector3.MoveTowards(transform.position, m_end, step);
        }

        private void Complete()
        {
            if (m_trail != null) m_trail.emitting = false;
            Action<WeaponProjectileVisual> callback = m_onComplete;
            m_onComplete = null;
            callback?.Invoke(this);
        }
    }
}
