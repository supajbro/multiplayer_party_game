using System;
using CouchGuys.ProceduralGeneration;
using UnityEngine;

namespace CouchGuys.Gameplay.Delivery
{
    public enum DeliveryTier
    {
        Easy,
        Medium,
        Hard
    }

    [Flags]
    public enum DeliveryDestinationCategories
    {
        None = 0,
        Standard = 1 << 0,
        Commercial = 1 << 1,
        Landmark = 1 << 2,
        Special = 1 << 3,
        Mansion = 1 << 4,
        All = ~0
    }

    /// <summary>Data-driven purchase, reward, timing and route eligibility for one delivery tier.</summary>
    [CreateAssetMenu(menuName = "Couch Guys/Gameplay/Delivery Tier", fileName = "DeliveryTier_")]
    public sealed class DeliveryTierConfig : ScriptableObject
    {
        [Header("Presentation")]
        [SerializeField] private DeliveryTier m_tier;
        [SerializeField] private string m_displayName = "Delivery";
        [SerializeField, TextArea] private string m_description = "Deliver a couch to a local customer.";
        [SerializeField] private Sprite m_icon;
        [SerializeField] private Color m_colour = new Color(1f, 0.72f, 0.12f);

        [Header("Economy")]
        [SerializeField, Min(0)] private int m_purchasePrice;
        [SerializeField, Min(0)] private int m_reward = 100;

        [Header("Timer")]
        [SerializeField, Min(0f)] private float m_baseDeliveryTime = 45f;
        [Tooltip("Seconds added per metre of route distance.")]
        [SerializeField, Min(0f)] private float m_timePerRouteMetre = 0.5f;
        [SerializeField, Min(1f)] private float m_minimumDeliveryTime = 60f;
        [SerializeField, Min(1f)] private float m_maximumDeliveryTime = 600f;
        [SerializeField, Min(0f)] private float m_uphillDistanceMultiplier = 2f;

        [Header("Destination Eligibility")]
        [SerializeField, Min(0f)] private float m_minimumRouteDistance;
        [SerializeField, Min(0f)] private float m_maximumRouteDistance = 120f;
        [SerializeField] private SuburbZone m_minimumProgressionZone = SuburbZone.Start;
        [SerializeField] private SuburbZone m_maximumProgressionZone = SuburbZone.Easy;
        [SerializeField, Min(0f)] private float m_maximumElevationGain = 6f;
        [SerializeField, Range(0f, 60f)] private float m_maximumRoadGrade = 18f;
        [SerializeField] private DeliveryDestinationCategories m_allowedCategories =
            DeliveryDestinationCategories.Standard;

        public DeliveryTier Tier => m_tier;
        public string DisplayName => string.IsNullOrWhiteSpace(m_displayName) ? name : m_displayName;
        public string Description => m_description;
        public Sprite Icon => m_icon;
        public Color Colour => m_colour;
        public int PurchasePrice => m_purchasePrice;
        public int Reward => m_reward;
        public float BaseDeliveryTime => m_baseDeliveryTime;
        public float TimePerRouteMetre => m_timePerRouteMetre;
        public float MinimumDeliveryTime => m_minimumDeliveryTime;
        public float MaximumDeliveryTime => m_maximumDeliveryTime;
        public float UphillDistanceMultiplier => m_uphillDistanceMultiplier;

        public bool IsDestinationEligible(DeliveryDestination destination)
        {
            DeliveryRouteMetrics metrics = destination != null ? destination.RouteMetrics : null;
            if (destination == null || !destination.CanReceiveDelivery || metrics == null || !metrics.IsReachable)
            {
                return false;
            }

            DeliveryDestinationCategories category = destination.DestinationType switch
            {
                DeliveryDestinationType.Commercial => DeliveryDestinationCategories.Commercial,
                DeliveryDestinationType.Landmark => DeliveryDestinationCategories.Landmark,
                DeliveryDestinationType.Special => DeliveryDestinationCategories.Special,
                DeliveryDestinationType.Mansion => DeliveryDestinationCategories.Mansion,
                _ => DeliveryDestinationCategories.Standard
            };
            return (m_allowedCategories & category) != 0 &&
                   metrics.RouteLength >= m_minimumRouteDistance &&
                   metrics.RouteLength <= m_maximumRouteDistance &&
                   metrics.ProgressionZone >= m_minimumProgressionZone &&
                   metrics.ProgressionZone <= m_maximumProgressionZone &&
                   metrics.CumulativeElevationGain <= m_maximumElevationGain &&
                   metrics.MaximumRoadGrade <= m_maximumRoadGrade;
        }

        public float CalculateDuration(DeliveryDestination destination)
        {
            DeliveryRouteMetrics metrics = destination != null ? destination.RouteMetrics : null;
            float weightedDistance = metrics != null
                ? metrics.RouteLength + metrics.FinalCarryDistance +
                  (metrics.CumulativeElevationGain + metrics.FinalCarryElevationGain) * m_uphillDistanceMultiplier
                : 0f;
            return Mathf.Clamp(
                m_baseDeliveryTime + weightedDistance * m_timePerRouteMetre,
                m_minimumDeliveryTime,
                m_maximumDeliveryTime);
        }

#if UNITY_EDITOR
        public void ConfigureDefaults(DeliveryTier tier, string displayName, string description,
            int price, int reward, float baseTime, float timePerMetre, float minimumTime,
            float maximumTime, float minimumRoute, float maximumRoute, SuburbZone minimumZone,
            SuburbZone maximumZone, float maximumElevation, float maximumGrade,
            DeliveryDestinationCategories categories, Color colour)
        {
            m_tier = tier;
            m_displayName = displayName;
            m_description = description;
            m_purchasePrice = price;
            m_reward = reward;
            m_baseDeliveryTime = baseTime;
            m_timePerRouteMetre = timePerMetre;
            m_minimumDeliveryTime = minimumTime;
            m_maximumDeliveryTime = maximumTime;
            m_minimumRouteDistance = minimumRoute;
            m_maximumRouteDistance = maximumRoute;
            m_minimumProgressionZone = minimumZone;
            m_maximumProgressionZone = maximumZone;
            m_maximumElevationGain = maximumElevation;
            m_maximumRoadGrade = maximumGrade;
            m_allowedCategories = categories;
            m_colour = colour;
        }

        private void OnValidate()
        {
            m_maximumRouteDistance = Mathf.Max(m_minimumRouteDistance, m_maximumRouteDistance);
            if (m_maximumProgressionZone < m_minimumProgressionZone)
                m_maximumProgressionZone = m_minimumProgressionZone;
            m_maximumDeliveryTime = Mathf.Max(m_minimumDeliveryTime, m_maximumDeliveryTime);
        }
#endif
    }
}
