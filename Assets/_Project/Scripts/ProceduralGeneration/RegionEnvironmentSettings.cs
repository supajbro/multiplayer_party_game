using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace CouchGuys.ProceduralGeneration
{
    [Serializable]
    public struct RegionEnvironmentSettings
    {
        [Header("Lighting")]
        public CartoonLightingSettings Lighting;
        [Header("Streetlights")]
        public StreetlightGenerationSettings Streetlights;
        [Header("Parks")]
        public ParkGenerationSettings Parks;
        [Header("Landmarks")]
        public LandmarkGenerationSettings Landmarks;
        [Header("Residential Decorations")]
        public ResidentialDecorationSettings ResidentialDecorations;
        [Header("Driveways")]
        public Material DrivewayMaterial;
        [Header("Shared Placeholder Prefabs")]
        public EnvironmentPrefabLibrary Prefabs;

        public static RegionEnvironmentSettings SuburbsDefault => new RegionEnvironmentSettings
        {
            Lighting = CartoonLightingSettings.Default,
            Streetlights = StreetlightGenerationSettings.Default,
            Parks = ParkGenerationSettings.Default,
            Landmarks = LandmarkGenerationSettings.Default,
            ResidentialDecorations = ResidentialDecorationSettings.Default,
            Prefabs = EnvironmentPrefabLibrary.Empty
        };

        public void Validate()
        {
            Lighting.Validate();
            Streetlights.Validate();
            Parks.Validate();
            Landmarks.Validate();
            ResidentialDecorations.Validate();
            Prefabs.Validate();
        }
    }

    [Serializable]
    public struct CartoonLightingSettings
    {
        public bool Enabled;
        public Color SunColour;
        [Min(0f)] public float SunIntensity;
        public Vector3 SunEulerAngles;
        public Color AmbientSkyColour;
        public Color AmbientEquatorColour;
        public Color AmbientGroundColour;
        [Range(0f, 2f)] public float AmbientIntensity;
        public bool FogEnabled;
        public Color FogColour;
        [Min(0f)] public float FogStartDistance;
        [Min(1f)] public float FogEndDistance;
        public Material SkyboxMaterial;
        public VolumeProfile PostProcessingProfile;

        public static CartoonLightingSettings Default => new CartoonLightingSettings
        {
            Enabled = true,
            SunColour = new Color(1f, 0.91f, 0.76f),
            SunIntensity = 1.25f,
            SunEulerAngles = new Vector3(48f, -32f, 0f),
            AmbientSkyColour = new Color(0.46f, 0.72f, 1f),
            AmbientEquatorColour = new Color(0.55f, 0.72f, 0.82f),
            AmbientGroundColour = new Color(0.28f, 0.36f, 0.29f),
            AmbientIntensity = 1.05f,
            FogEnabled = true,
            FogColour = new Color(0.62f, 0.82f, 0.96f),
            FogStartDistance = 180f,
            FogEndDistance = 520f
        };

        public void Validate()
        {
            SunIntensity = Mathf.Max(0f, SunIntensity);
            AmbientIntensity = Mathf.Clamp(AmbientIntensity, 0f, 2f);
            FogStartDistance = Mathf.Max(0f, FogStartDistance);
            FogEndDistance = Mathf.Max(FogStartDistance + 1f, FogEndDistance);
        }
    }

    [Serializable]
    public struct StreetlightGenerationSettings
    {
        public bool Enabled;
        public GameObject Prefab;
        [Min(2f)] public float AverageSpacing;
        [Min(0f)] public float SpacingVariation;
        [Min(0f)] public float SidewalkOffset;
        [Min(0f)] public float MinimumDistanceFromIntersections;
        [Min(0f)] public float MinimumClearanceFromDriveways;
        [Min(0)] public int MaximumCount;
        public bool LightingEnabled;
        public Color LightColour;
        [Min(0f)] public float LightIntensity;
        [Min(0.1f)] public float LightRange;

        public static StreetlightGenerationSettings Default => new StreetlightGenerationSettings
        {
            Enabled = true,
            AverageSpacing = 24f,
            SpacingVariation = 4f,
            SidewalkOffset = 0.7f,
            MinimumDistanceFromIntersections = 10f,
            MinimumClearanceFromDriveways = 3.5f,
            MaximumCount = 96,
            LightingEnabled = false,
            LightColour = new Color(1f, 0.68f, 0.28f),
            LightIntensity = 2.2f,
            LightRange = 11f
        };

        public void Validate()
        {
            AverageSpacing = Mathf.Max(2f, AverageSpacing);
            SpacingVariation = Mathf.Clamp(SpacingVariation, 0f, AverageSpacing * 0.8f);
            SidewalkOffset = Mathf.Max(0f, SidewalkOffset);
            MinimumDistanceFromIntersections = Mathf.Max(0f, MinimumDistanceFromIntersections);
            MinimumClearanceFromDriveways = Mathf.Max(0f, MinimumClearanceFromDriveways);
            MaximumCount = Mathf.Max(0, MaximumCount);
            LightIntensity = Mathf.Max(0f, LightIntensity);
            LightRange = Mathf.Max(0.1f, LightRange);
        }
    }

    public enum ParkSize { Small, Medium, Large }

    [Serializable]
    public struct ParkGenerationSettings
    {
        public bool Enabled;
        [Min(0)] public int MinimumParks;
        [Min(0)] public int MaximumParks;
        [Range(0f, 1f)] public float SmallProbability;
        [Range(0f, 1f)] public float MediumProbability;
        [Range(0f, 1f)] public float LargeProbability;
        public Vector2Int SmallFootprint;
        public Vector2Int MediumFootprint;
        public Vector2Int LargeFootprint;
        [Range(0f, 35f)] public float MaximumTerrainSlope;
        [Range(0f, 2f)] public float DecorationDensity;
        [Min(0f)] public float BoundaryClearance;

        public static ParkGenerationSettings Default => new ParkGenerationSettings
        {
            Enabled = true,
            MinimumParks = 1,
            MaximumParks = 2,
            SmallProbability = 0.45f,
            MediumProbability = 0.4f,
            LargeProbability = 0.15f,
            SmallFootprint = new Vector2Int(2, 2),
            MediumFootprint = new Vector2Int(4, 4),
            LargeFootprint = new Vector2Int(6, 6),
            MaximumTerrainSlope = 12f,
            DecorationDensity = 0.8f,
            BoundaryClearance = 1f
        };

        public void Validate()
        {
            MinimumParks = Mathf.Clamp(MinimumParks, 1, 2);
            MaximumParks = Mathf.Clamp(MaximumParks, MinimumParks, 2);
            SmallProbability = Mathf.Clamp01(SmallProbability);
            MediumProbability = Mathf.Clamp01(MediumProbability);
            LargeProbability = Mathf.Clamp01(LargeProbability);
            SmallFootprint = ClampFootprint(SmallFootprint);
            MediumFootprint = ClampFootprint(MediumFootprint);
            LargeFootprint = ClampFootprint(LargeFootprint);
            MaximumTerrainSlope = Mathf.Clamp(MaximumTerrainSlope, 0f, 35f);
            DecorationDensity = Mathf.Clamp(DecorationDensity, 0f, 2f);
            BoundaryClearance = Mathf.Max(0f, BoundaryClearance);
        }

        private static Vector2Int ClampFootprint(Vector2Int value) =>
            new Vector2Int(Mathf.Max(1, value.x), Mathf.Max(1, value.y));
    }

    [Serializable]
    public struct LandmarkGenerationSettings
    {
        public bool Enabled;
        [Min(0)] public int MinimumCount;
        [Min(0)] public int MaximumCount;
        [Min(0f)] public float MinimumSpacing;
        [Min(0f)] public float Clearance;
        [Range(0f, 35f)] public float MaximumTerrainSlope;
        public WeightedEnvironmentPrefab[] Prefabs;

        public static LandmarkGenerationSettings Default => new LandmarkGenerationSettings
        {
            Enabled = true,
            MinimumCount = 1,
            MaximumCount = 3,
            MinimumSpacing = 35f,
            Clearance = 2f,
            MaximumTerrainSlope = 14f,
            Prefabs = Array.Empty<WeightedEnvironmentPrefab>()
        };

        public void Validate()
        {
            MinimumCount = Mathf.Max(0, MinimumCount);
            MaximumCount = Mathf.Max(MinimumCount, MaximumCount);
            MinimumSpacing = Mathf.Max(0f, MinimumSpacing);
            Clearance = Mathf.Max(0f, Clearance);
            MaximumTerrainSlope = Mathf.Clamp(MaximumTerrainSlope, 0f, 35f);
            Prefabs ??= Array.Empty<WeightedEnvironmentPrefab>();
        }
    }

    [Serializable]
    public struct WeightedEnvironmentPrefab
    {
        public GameObject Prefab;
        [Min(0.01f)] public float Weight;
        public bool AllowedInParks;
        public bool AllowedRoadside;
    }

    [Serializable]
    public struct ResidentialDecorationSettings
    {
        public bool Enabled;
        [Range(0f, 1f)] public float BinProbability;
        [Range(0f, 1f)] public float SecondBinProbability;
        [Range(0f, 1f)] public float MailboxProbability;
        [Range(0f, 1f)] public float FlowerBedProbability;
        [Range(0f, 1f)] public float BushProbability;
        [Range(0f, 1f)] public float OrnamentalTreeProbability;
        [Range(0f, 2f)] public float GardenDensity;
        [Min(0f)] public float DoorClearance;
        [Min(0f)] public float DrivewayClearance;

        public static ResidentialDecorationSettings Default => new ResidentialDecorationSettings
        {
            Enabled = true,
            BinProbability = 0.7f,
            SecondBinProbability = 0.2f,
            MailboxProbability = 0.8f,
            FlowerBedProbability = 0.72f,
            BushProbability = 0.68f,
            OrnamentalTreeProbability = 0.18f,
            GardenDensity = 0.85f,
            DoorClearance = 2.5f,
            DrivewayClearance = 1.25f
        };

        public void Validate()
        {
            BinProbability = Mathf.Clamp01(BinProbability);
            SecondBinProbability = Mathf.Clamp01(SecondBinProbability);
            MailboxProbability = Mathf.Clamp01(MailboxProbability);
            FlowerBedProbability = Mathf.Clamp01(FlowerBedProbability);
            BushProbability = Mathf.Clamp01(BushProbability);
            OrnamentalTreeProbability = Mathf.Clamp01(OrnamentalTreeProbability);
            GardenDensity = Mathf.Clamp(GardenDensity, 0f, 2f);
            DoorClearance = Mathf.Max(0f, DoorClearance);
            DrivewayClearance = Mathf.Max(0f, DrivewayClearance);
        }
    }

    [Serializable]
    public struct EnvironmentPrefabLibrary
    {
        public GameObject SmallPark;
        public GameObject MediumPark;
        public GameObject LargePark;
        [Tooltip("Tree prefabs selected deterministically for park and residential tree spawns.")]
        public GameObject[] TreePrefabs;
        public GameObject Bench;
        public GameObject Bin;
        public GameObject PublicBin;
        public GameObject Mailbox;
        public GameObject FlowerCluster;
        public GameObject Bush;
        public GameObject Fountain;
        public GameObject Statue;
        public GameObject Playground;
        public GameObject Gazebo;
        public GameObject BusStop;
        public GameObject ParkSign;
        public GameObject ParkPath;
        public GameObject ParkBoundary;
        public GameObject NoticeBoard;
        public GameObject PicnicArea;
        public GameObject Plaza;
        public GameObject FireHydrant;
        public GameObject StreetSign;
        public GameObject UtilityBox;
        public GameObject PlantingBed;

        public static EnvironmentPrefabLibrary Empty => new EnvironmentPrefabLibrary
        {
            TreePrefabs = Array.Empty<GameObject>()
        };

        public void Validate()
        {
            TreePrefabs ??= Array.Empty<GameObject>();
        }
    }
}
