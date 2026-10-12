using FishNet.Object;
using UnityEngine;

namespace CouchGuys.Gameplay.Weapons
{
    [RequireComponent(typeof(NetworkObject), typeof(PlayerWeaponController), typeof(ReticleTargetDetector))]
    [DisallowMultipleComponent]
    public sealed class WeaponReticleController : MonoBehaviour
    {
        [SerializeField] private WeaponReticleView m_reticlePrefab;
        [SerializeField, Min(1f)] private float m_colourResponse = 18f;

        private NetworkObject m_networkObject;
        private PlayerWeaponController m_weapons;
        private ReticleTargetDetector m_targetDetector;
        private WeaponReticleView m_view;
        private ReticleSettings m_settings;
        private Color m_displayColour = Color.white;
        private float m_expansion;
        private float m_expansionTarget;
        private bool m_bound;

        private void Awake()
        {
            m_networkObject = GetComponent<NetworkObject>();
            m_weapons = GetComponent<PlayerWeaponController>();
            m_targetDetector = GetComponent<ReticleTargetDetector>();
        }

        private void Update()
        {
            if (m_networkObject == null || !m_networkObject.IsOwner) return;
            if (!m_bound) Bind();
            if (m_view == null || m_settings == null) return;

            ReticleTargetType target = m_targetDetector != null
                ? m_targetDetector.Detect(m_weapons.CurrentWeaponRange)
                : ReticleTargetType.Neutral;
            Color targetColour = target switch
            {
                ReticleTargetType.Enemy => m_settings.EnemyColour,
                ReticleTargetType.Teammate => m_settings.TeammateColour,
                _ => m_settings.NormalColour
            };
            m_displayColour = Color.Lerp(m_displayColour, targetColour,
                1f - Mathf.Exp(-m_colourResponse * Time.unscaledDeltaTime));

            m_expansionTarget = Mathf.MoveTowards(m_expansionTarget, 0f,
                m_settings.RecoverySpeed * Time.unscaledDeltaTime);
            m_expansion = Mathf.MoveTowards(m_expansion, m_expansionTarget,
                m_settings.ExpansionSpeed * Time.unscaledDeltaTime);
            float maximum = Mathf.Max(0.01f, m_settings.ShootingExpansion *
                m_settings.MaximumSustainedMultiplier);
            float normalized = Mathf.Clamp01(m_expansion / maximum);
            float curvedExpansion = m_settings.ExpansionCurve.Evaluate(normalized) * maximum;
            m_view.UpdatePresentation(curvedExpansion, m_displayColour);
        }

        private void Bind()
        {
            m_bound = true;
            m_weapons.LocalShotFired += OnShotFired;
            m_weapons.LocalHitConfirmed += OnHitConfirmed;
            m_weapons.LocalReticleChanged += SetSettings;
            if (m_reticlePrefab != null)
            {
                m_view = Instantiate(m_reticlePrefab);
                m_view.name = "LocalWeaponReticle";
            }
            SetSettings(m_weapons.CurrentReticleSettings);
        }

        private void SetSettings(ReticleSettings settings)
        {
            m_settings = settings;
            m_expansion = 0f;
            m_expansionTarget = 0f;
            m_displayColour = settings != null ? settings.NormalColour : Color.white;
            if (m_view != null) m_view.SetSettings(settings);
        }

        private void OnShotFired(PlayerWeaponType weapon)
        {
            if (m_settings == null) return;
            if (m_settings.SustainedFireExpansion)
            {
                float maximum = m_settings.ShootingExpansion * m_settings.MaximumSustainedMultiplier;
                m_expansionTarget = Mathf.Min(maximum,
                    Mathf.Max(m_settings.ShootingExpansion, m_expansionTarget + m_settings.ShootingExpansion * 0.55f));
            }
            else
            {
                m_expansionTarget = Mathf.Max(m_expansionTarget, m_settings.ShootingExpansion);
            }
        }

        private void OnHitConfirmed(bool elimination) => m_view?.PlayHitMarker(elimination);

        private void OnDestroy()
        {
            if (m_weapons != null)
            {
                m_weapons.LocalShotFired -= OnShotFired;
                m_weapons.LocalHitConfirmed -= OnHitConfirmed;
                m_weapons.LocalReticleChanged -= SetSettings;
            }
            if (m_view != null) Destroy(m_view.gameObject);
        }
    }
}
