using System.Reflection;
using CouchGuys.Gameplay.Couch;
using NUnit.Framework;
using UnityEngine;

namespace CouchGuys.Tests.Editor
{
    public sealed class CouchPhysicsTests
    {
        private const BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;

        [Test]
        public void CooperationEfficiency_IsPredictableForAgreementAndConflict()
        {
            MethodInfo method = typeof(CouchCarryController).GetMethod(
                "CalculateCooperationEfficiency", StaticPrivate);
            Assert.That(method, Is.Not.Null);

            float agreement = (float)method.Invoke(null,
                new object[] { 2, Vector3.forward * 2f, 2f });
            float conflict = (float)method.Invoke(null,
                new object[] { 2, Vector3.zero, 2f });

            Assert.That(agreement, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(conflict, Is.EqualTo(0f).Within(0.0001f));
        }

        [Test]
        public void CarryAcceleration_PreservesMomentumButReversesDecisively()
        {
            MethodInfo method = typeof(CouchCarryController).GetMethod(
                "SelectAccelerationLimit", StaticPrivate);
            Assert.That(method, Is.Not.Null);

            float braking = (float)method.Invoke(null,
                new object[] { Vector3.forward * 4f, Vector3.zero });
            float accelerating = (float)method.Invoke(null,
                new object[] { Vector3.forward, Vector3.forward * 4f });
            float reversing = (float)method.Invoke(null,
                new object[] { Vector3.forward * 4f, Vector3.back * 4f });

            Assert.That(braking, Is.LessThan(accelerating));
            Assert.That(reversing, Is.GreaterThan(accelerating));
        }
    }
}
