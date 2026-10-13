using System.Reflection;
using CouchGuys.Gameplay.Sled;
using NUnit.Framework;
using UnityEngine;

namespace CouchGuys.Tests.Editor
{
    public sealed class SledPhysicsTests
    {
        [Test]
        public void SettingsProvideIndependentSpeedForEveryCarrierCount()
        {
            SledSettings settings = ScriptableObject.CreateInstance<SledSettings>();
            try
            {
                Set(settings, "m_speedOnePlayer", 1.1f);
                Set(settings, "m_speedTwoPlayers", 2.2f);
                Set(settings, "m_speedThreePlayers", 3.3f);
                Set(settings, "m_speedFourPlayers", 4.4f);
                Assert.That(settings.GetSpeed(1), Is.EqualTo(1.1f));
                Assert.That(settings.GetSpeed(2), Is.EqualTo(2.2f));
                Assert.That(settings.GetSpeed(3), Is.EqualTo(3.3f));
                Assert.That(settings.GetSpeed(4), Is.EqualTo(4.4f));
            }
            finally { Object.DestroyImmediate(settings); }
        }

        private static void Set(SledSettings settings, string fieldName, float value)
        {
            typeof(SledSettings).GetField(fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(settings, value);
        }
    }
}
