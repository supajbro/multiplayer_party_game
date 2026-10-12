using UnityEngine;

namespace CouchGuys.Gameplay.Weapons
{
    public enum ReticleStyle
    {
        Crosshair,
        Circle,
        Custom
    }

    [CreateAssetMenu(fileName = "ReticleSettings", menuName = "Couch Guys/Weapons/Reticle Settings")]
    public sealed class ReticleSettings : ScriptableObject
    {
        [Header("Appearance")]
        [SerializeField] private ReticleStyle m_style = ReticleStyle.Crosshair;
        [SerializeField, Min(4f)] private float m_defaultSize = 24f;
        [SerializeField, Min(1f)] private float m_lineThickness = 3f;
        [SerializeField, Min(1f)] private float m_lineLength = 10f;
        [SerializeField, Min(0f)] private float m_centreGap = 6f;
        [SerializeField, Min(0f)] private float m_centreDotSize = 4f;
        [SerializeField] private Color m_normalColour = Color.white;
        [SerializeField] private Color m_enemyColour = new(1f, 0.16f, 0.12f, 1f);
        [SerializeField] private Color m_teammateColour = new(0.2f, 1f, 0.35f, 1f);

        [Header("Animation")]
        [SerializeField, Min(0f)] private float m_shootingExpansion = 10f;
        [SerializeField, Min(1f)] private float m_expansionSpeed = 110f;
        [SerializeField, Min(1f)] private float m_recoverySpeed = 48f;
        [SerializeField] private bool m_sustainedFireExpansion;
        [SerializeField, Min(1f)] private float m_maximumSustainedMultiplier = 2.2f;
        [SerializeField] private AnimationCurve m_expansionCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Hit Marker")]
        [SerializeField] private bool m_hitMarkerEnabled = true;
        [SerializeField, Min(4f)] private float m_hitMarkerSize = 18f;
        [SerializeField, Range(0.08f, 0.5f)] private float m_hitMarkerDuration = 0.2f;
        [SerializeField] private Color m_hitMarkerColour = Color.white;
        [SerializeField] private Color m_eliminationColour = new(1f, 0.18f, 0.12f, 1f);

        public ReticleStyle Style => m_style;
        public float DefaultSize => m_defaultSize;
        public float LineThickness => m_lineThickness;
        public float LineLength => m_lineLength;
        public float CentreGap => m_centreGap;
        public float CentreDotSize => m_centreDotSize;
        public Color NormalColour => m_normalColour;
        public Color EnemyColour => m_enemyColour;
        public Color TeammateColour => m_teammateColour;
        public float ShootingExpansion => m_shootingExpansion;
        public float ExpansionSpeed => m_expansionSpeed;
        public float RecoverySpeed => m_recoverySpeed;
        public bool SustainedFireExpansion => m_sustainedFireExpansion;
        public float MaximumSustainedMultiplier => m_maximumSustainedMultiplier;
        public AnimationCurve ExpansionCurve => m_expansionCurve;
        public bool HitMarkerEnabled => m_hitMarkerEnabled;
        public float HitMarkerSize => m_hitMarkerSize;
        public float HitMarkerDuration => m_hitMarkerDuration;
        public Color HitMarkerColour => m_hitMarkerColour;
        public Color EliminationColour => m_eliminationColour;
    }
}
