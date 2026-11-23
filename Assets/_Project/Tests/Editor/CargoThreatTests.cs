using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using CouchGuys.Gameplay.Delivery;
using CouchGuys.Gameplay.Enemies;
using FishNet.Object;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CouchGuys.Tests.Editor
{
    public sealed class CargoThreatTests
    {
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        private static object Encounter(params int[] ids)
        {
            Type type = typeof(EnemySpawnManager).GetNestedType("CargoEncounter", BindingFlags.NonPublic);
            object encounter = Activator.CreateInstance(type, true);
            var jobs = (IList)type.GetField("Jobs").GetValue(encounter);
            foreach (int id in ids)
            {
                object job = Activator.CreateInstance(typeof(EnemySpawnManager).GetNestedType("CargoJob", BindingFlags.NonPublic), true);
                job.GetType().GetField("Id").SetValue(job, id);
                jobs.Add(job);
            }
            return encounter;
        }
        private static void JobField(object encounter, string field, object value) =>
            ((IList)encounter.GetType().GetField("Jobs").GetValue(encounter))[0].GetType().GetField(field)
                .SetValue(((IList)encounter.GetType().GetField("Jobs").GetValue(encounter))[0], value);
        private static object Call(object target, string method, params object[] args) =>
            target.GetType().GetMethod(method, Private).Invoke(target, args);

        [Test]
        public void MixedLoadUsesHardestWeightAndDiscountsExtraCouches()
        {
            var settings = new CargoThreatSettings();
            Assert.That(settings.Threat(settings.Weight(DeliveryTier.Medium) + 3 * settings.Weight(DeliveryTier.Easy),
                settings.Weight(DeliveryTier.Medium)), Is.EqualTo(2.75f));
            Assert.That(settings.Threat(4, 1), Is.EqualTo(1.75f));
            Assert.That(settings.Threat(12, 3), Is.EqualTo(5.25f));
            settings.AdditionalCouchContribution = 0;
            Assert.That(settings.Threat(12, 3), Is.EqualTo(3));
        }

        [Test]
        public void ThreatControlsCapWaveAndIntervalWithinConfiguredBounds()
        {
            var settings = new CargoThreatSettings();
            var tier = Object.Instantiate(AssetDatabase.LoadAssetAtPath<DeliveryTierConfig>("Assets/_Project/Settings/Delivery/MediumDelivery.asset"));
            try
            {
                float baseline = settings.Weight(tier.Tier);
                Assert.That(settings.Cap(2.75f), Is.EqualTo(7));
                Assert.That(settings.MinWave(tier, 2.75f, 4), Is.GreaterThanOrEqualTo(tier.MinThievesPerWave));
                Assert.That(settings.MaxWave(tier, 2.75f, 4), Is.GreaterThanOrEqualTo(tier.MaxThievesPerWave));
                Assert.That(settings.Interval(tier, 2.75f, 4, .5f),
                    Is.LessThan(settings.Interval(tier, baseline, 1, .5f)));
                float before = settings.Interval(tier, 2.75f, 4, 0);
                var data = new SerializedObject(tier);
                data.FindProperty("m_minSpawnInterval").floatValue = tier.MinSpawnInterval * 2;
                data.FindProperty("m_maxSpawnInterval").floatValue = tier.MaxSpawnInterval * 2;
                data.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(settings.Interval(tier, 2.75f, 4, 0), Is.EqualTo(before * 2).Within(.001f));
            }
            finally { Object.DestroyImmediate(tier); }
        }

        [Test]
        public void SingleDeliveryUsesTierSettingsRegardlessOfThreatCurves()
        {
            var settings = new CargoThreatSettings {
                MinimumWaveSize = AnimationCurve.Constant(0, 10, 100),
                MaximumWaveSize = AnimationCurve.Constant(0, 10, 100),
                WaveIntervalSeconds = AnimationCurve.Constant(0, 10, 100)
            };
            foreach (string name in new[] { "Easy", "Medium", "Hard" })
            {
                var tier = AssetDatabase.LoadAssetAtPath<DeliveryTierConfig>($"Assets/_Project/Settings/Delivery/{name}Delivery.asset");
                Assert.That(settings.MinWave(tier, 20, 1), Is.EqualTo(tier.MinThievesPerWave));
                Assert.That(settings.MaxWave(tier, 20, 1), Is.EqualTo(tier.MaxThievesPerWave));
                Assert.That(settings.Interval(tier, 20, 1, 0), Is.EqualTo(tier.MinSpawnInterval));
                Assert.That(settings.Interval(tier, 20, 1, 1), Is.EqualTo(tier.MaxSpawnInterval));
            }
        }

        [Test]
        public void SameVehicleAndNearbyUnloadedCargoShareButDistantCargoSeparates()
        {
            var root = new GameObject("Cargo grouping test"); root.SetActive(false);
            try
            {
                var manager = root.AddComponent<EnemySpawnManager>();
                var first = Encounter(1); var second = Encounter(2);
                JobField(second, "Position", Vector3.right * 20);
                Assert.That(Call(manager, "EncountersTouch", first, second), Is.True);
                JobField(second, "Position", Vector3.right * 200);
                Assert.That(Call(manager, "EncountersTouch", first, second), Is.False);
                JobField(first, "Vehicle", root.transform); JobField(second, "Vehicle", root.transform);
                Assert.That(Call(manager, "EncountersTouch", first, second), Is.True);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void MergingLoadingAndSplittingPreserveScheduledWaveDeadline()
        {
            var root = new GameObject("Cargo timer test"); root.SetActive(false);
            try
            {
                var manager = root.AddComponent<EnemySpawnManager>();
                var times = (Dictionary<int, float>)typeof(EnemySpawnManager).GetField("m_deliverySpawnTimes", Private).GetValue(manager);
                times[1] = 15; times[2] = 20;
                Assert.That(Call(manager, "EncounterDeadline", Encounter(1, 2, 3)), Is.EqualTo(20));
                times[1] = times[2] = times[3] = 20;
                Assert.That(Call(manager, "EncounterDeadline", Encounter(1)), Is.EqualTo(20));
                Assert.That(Call(manager, "EncounterDeadline", Encounter(2, 3)), Is.EqualTo(20));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void CompositionUsesEncounterTierAndSharedSpecialLimits()
        {
            var root = new GameObject("Cargo composition test"); root.SetActive(false);
            var hard = Object.Instantiate(AssetDatabase.LoadAssetAtPath<DeliveryTierConfig>("Assets/_Project/Settings/Delivery/HardDelivery.asset"));
            var easy = AssetDatabase.LoadAssetAtPath<DeliveryTierConfig>("Assets/_Project/Settings/Delivery/EasyDelivery.asset");
            try
            {
                var manager = root.AddComponent<EnemySpawnManager>();
                var deliveries = root.AddComponent<DeliveryManager>(); deliveries.SetDeliveryTiers(new[] { easy, hard });
                typeof(EnemySpawnManager).GetField("m_deliveryManager", Private).SetValue(manager, deliveries);
                manager.SetThiefPrefab(AssetDatabase.LoadAssetAtPath<NetworkObject>("Assets/_Project/Prefabs/Enemies/Thief_Pistol.prefab"));
                var encounter = Encounter(1, 2);
                encounter.GetType().GetField("TierIndex").SetValue(encounter, 1);
                typeof(EnemySpawnManager).GetField("m_encounter", Private).SetValue(manager, encounter);
                var data = new SerializedObject(hard);
                data.FindProperty("m_rocketThiefChance").floatValue = 1;
                data.FindProperty("m_grapplerThiefChance").floatValue = 1;
                data.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(Call(manager, "SelectEnemyPrefab", 0, 0), Is.EqualTo(hard.RocketThiefPrefab));
                Assert.That(Call(manager, "SelectEnemyPrefab", 1, 0), Is.EqualTo(hard.GrapplerThiefPrefab));
                var regular = (NetworkObject)Call(manager, "SelectEnemyPrefab", 1, 1);
                Assert.That(SpeakerThief.IsRegular(regular), Is.True);
                encounter.GetType().GetField("TierIndex").SetValue(encounter, 0);
                Assert.That(SpeakerThief.IsRegular((NetworkObject)Call(manager, "SelectEnemyPrefab", 0, 0)), Is.True);
                var single = Encounter(1);
                single.GetType().GetField("TierIndex").SetValue(single, 1);
                typeof(EnemySpawnManager).GetField("m_encounter", Private).SetValue(manager, single);
                Assert.That(Call(manager, "SelectEnemyPrefab", 1, 0), Is.EqualTo(hard.RocketThiefPrefab),
                    "A single Hard delivery must retain its tier's rocket limit rather than the shared limit of one.");
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(hard); }
        }
    }

    [InitializeOnLoad]
    public static class CargoThreatTestRunner
    {
        static CargoThreatTestRunner() => EditorApplication.update += Check;
        private static void Check()
        {
            const string request = "Temp/CargoThreat.Tests.request";
            if (!File.Exists(request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            File.Delete(request);
            try
            {
                var tests = new CargoThreatTests();
                tests.MixedLoadUsesHardestWeightAndDiscountsExtraCouches();
                tests.ThreatControlsCapWaveAndIntervalWithinConfiguredBounds();
                tests.SingleDeliveryUsesTierSettingsRegardlessOfThreatCurves();
                tests.SameVehicleAndNearbyUnloadedCargoShareButDistantCargoSeparates();
                tests.MergingLoadingAndSplittingPreserveScheduledWaveDeadline();
                tests.CompositionUsesEncounterTierAndSharedSpecialLimits();
                File.WriteAllText("Temp/CargoThreat.Tests.result.txt", "PASS: 6 cargo threat tests (weights, tier authority, scaling, cargo grouping, timer continuity, composition limits).");
            }
            catch (Exception error) { File.WriteAllText("Temp/CargoThreat.Tests.result.txt", "FAIL: " + error); }
        }
    }
}
