using System.Collections.Generic;
using UnityEngine;

namespace CouchGuys.Gameplay.Weapons
{
    /// <summary>Pooled, client-side presentation for server-resolved weapon shots.</summary>
    [DisallowMultipleComponent]
    public sealed class PlayerWeaponEffects : MonoBehaviour
    {
        private const int InitialProjectilePool = 16;
        private const int InitialEffectPool = 8;

        [Header("Surface Impact Prefabs")]
        [SerializeField] private GameObject m_terrainImpactPrefab;
        [SerializeField] private GameObject m_wallImpactPrefab;
        [SerializeField] private GameObject m_metalImpactPrefab;
        [SerializeField] private GameObject m_enemyImpactPrefab;
        [SerializeField, Min(0.05f)] private float m_effectLifetime = 1.5f;

        [Header("Camera Feedback")]
        [SerializeField] private Camera m_ownerCamera;
        [SerializeField, Min(1f)] private float m_shakeFrequency = 34f;
        [SerializeField, Min(1f)] private float m_shakeRecovery = 14f;

        private readonly Dictionary<GameObject, Queue<GameObject>> m_pools = new();
        private readonly Dictionary<GameObject, GameObject> m_instancePrefabs = new();
        private readonly HashSet<GameObject> m_invalidProjectilePrefabs = new();
        private Vector3 m_cameraBasePosition;
        private float m_shakeAmplitude;
        private float m_shakeSeed;

        private void Awake()
        {
            m_ownerCamera ??= GetComponentInChildren<Camera>(true);
            if (m_ownerCamera != null) m_cameraBasePosition = m_ownerCamera.transform.localPosition;
        }

        private void LateUpdate()
        {
            if (m_ownerCamera == null) return;
            m_shakeAmplitude = Mathf.MoveTowards(m_shakeAmplitude, 0f, m_shakeRecovery * Time.deltaTime);
            float time = (Time.time + m_shakeSeed) * m_shakeFrequency;
            Vector3 shake = new(
                (Mathf.PerlinNoise(time, 0.13f) - 0.5f) * 2f,
                (Mathf.PerlinNoise(0.37f, time) - 0.5f) * 2f,
                0f);
            m_ownerCamera.transform.localPosition = m_cameraBasePosition + shake * m_shakeAmplitude;
        }

        private void OnDisable()
        {
            if (m_ownerCamera != null) m_ownerCamera.transform.localPosition = m_cameraBasePosition;
            m_shakeAmplitude = 0f;
        }

        public void PlayShot(WeaponDefinition weapon, Vector3 muzzle, Vector3[] ends,
            Vector3[] normals, byte[] surfaces, bool applyCameraShake)
        {
            if (weapon == null || ends == null) return;
            SpawnTimedEffect(weapon.MuzzleFlashPrefab, muzzle,
                ends.Length > 0 ? ends[0] - muzzle : transform.forward, Vector3.one);
            SpawnTimedEffect(weapon.ShellEjectionPrefab, muzzle, transform.right, Vector3.one);

            if (applyCameraShake && weapon.CameraShake > 0f)
            {
                m_shakeAmplitude = Mathf.Max(m_shakeAmplitude, weapon.CameraShake);
                m_shakeSeed = Random.value * 100f;
            }

            for (int index = 0; index < ends.Length; index++)
            {
                int capturedIndex = index;
                WeaponProjectileVisual projectile = GetProjectile(weapon.BulletPrefab);
                if (projectile == null)
                {
                    PlayImpact(weapon, ends, normals, surfaces, capturedIndex);
                    continue;
                }
                projectile.Launch(muzzle, ends[index], weapon, completed =>
                {
                    ReturnProjectile(completed);
                    PlayImpact(weapon, ends, normals, surfaces, capturedIndex);
                });
            }
        }

        private WeaponProjectileVisual GetProjectile(GameObject prefab)
        {
            if (prefab == null) return null;
            if (prefab.GetComponent<WeaponProjectileVisual>() == null)
            {
                if (m_invalidProjectilePrefabs.Add(prefab))
                {
                    Debug.LogError(
                        $"{prefab.name} is assigned as a bullet but has no {nameof(WeaponProjectileVisual)}. " +
                        "Assign a projectile prefab instead of a weapon, character, building, or environment model.",
                        this);
                }
                return null;
            }
            GameObject instance = Get(prefab, InitialProjectilePool);
            WeaponProjectileVisual projectile = instance.GetComponent<WeaponProjectileVisual>();
            return projectile;
        }

        private void ReturnProjectile(WeaponProjectileVisual projectile) => Return(projectile.gameObject);

        private void PlayImpact(WeaponDefinition weapon, Vector3[] ends, Vector3[] normals,
            byte[] surfaces, int index)
        {
            if (surfaces == null || index >= surfaces.Length ||
                (WeaponImpactSurface)surfaces[index] == WeaponImpactSurface.None) return;
            GameObject prefab = GetImpactPrefab((WeaponImpactSurface)surfaces[index]);
            Vector3 normal = normals != null && index < normals.Length && normals[index].sqrMagnitude > 0.01f
                ? normals[index].normalized : Vector3.up;
            SpawnTimedEffect(prefab, ends[index] + normal * 0.015f, normal,
                Vector3.one * weapon.ImpactScale);
        }

        private GameObject GetImpactPrefab(WeaponImpactSurface surface) => surface switch
        {
            WeaponImpactSurface.Terrain => m_terrainImpactPrefab,
            WeaponImpactSurface.Metal => m_metalImpactPrefab,
            WeaponImpactSurface.Enemy => m_enemyImpactPrefab,
            _ => m_wallImpactPrefab
        };

        private void SpawnTimedEffect(GameObject prefab, Vector3 position, Vector3 forward, Vector3 scale)
        {
            if (prefab == null) return;
            GameObject instance = Get(prefab, InitialEffectPool);
            instance.transform.SetPositionAndRotation(position,
                forward.sqrMagnitude > 0.001f ? Quaternion.LookRotation(forward, Vector3.up) : Quaternion.identity);
            instance.transform.localScale = scale;
            foreach (ParticleSystem particles in instance.GetComponentsInChildren<ParticleSystem>(true))
            {
                particles.Clear(true);
                particles.Play(true);
            }
            StartCoroutine(ReturnAfter(instance, m_effectLifetime));
        }

        private System.Collections.IEnumerator ReturnAfter(GameObject instance, float delay)
        {
            yield return new WaitForSeconds(delay);
            Return(instance);
        }

        private GameObject Get(GameObject prefab, int warmCount)
        {
            if (!m_pools.TryGetValue(prefab, out Queue<GameObject> pool))
            {
                pool = new Queue<GameObject>(warmCount);
                m_pools.Add(prefab, pool);
                for (int i = 0; i < warmCount; i++) pool.Enqueue(Create(prefab));
            }
            GameObject instance = pool.Count > 0 ? pool.Dequeue() : Create(prefab);
            instance.SetActive(true);
            return instance;
        }

        private GameObject Create(GameObject prefab)
        {
            GameObject instance = Instantiate(prefab, transform);
            instance.name = prefab.name + "_Pooled";
            instance.SetActive(false);
            m_instancePrefabs[instance] = prefab;
            return instance;
        }

        private void Return(GameObject instance)
        {
            if (instance == null || !m_instancePrefabs.TryGetValue(instance, out GameObject prefab)) return;
            instance.SetActive(false);
            instance.transform.SetParent(transform, false);
            m_pools[prefab].Enqueue(instance);
        }
    }
}
