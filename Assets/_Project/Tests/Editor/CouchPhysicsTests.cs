using System.Reflection;
using CouchGuys.Gameplay.Couch;
using CouchGuys.Player;
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

        [TestCase(0f)]
        [TestCase(90f)]
        [TestCase(180f)]
        [TestCase(270f)]
        public void DesiredCarryVelocity_IsIndependentOfCouchOrientation(float couchYaw)
        {
            MethodInfo method = typeof(CouchCarryController).GetMethod(
                "CalculateDesiredCarryVelocity", StaticPrivate);
            Assert.That(method, Is.Not.Null);

            Vector3 localIntent = Quaternion.Euler(0f, -couchYaw, 0f) * Vector3.forward;
            Vector3 worldIntent = Quaternion.Euler(0f, couchYaw, 0f) * localIntent;
            Vector3 velocity = (Vector3)method.Invoke(null,
                new object[] { worldIntent, 1, 2f });

            Assert.That(velocity.x, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(velocity.y, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(velocity.z, Is.EqualTo(2f).Within(0.0001f));
        }

        [TestCase(-1f, -1f)]
        [TestCase(1f, -1f)]
        [TestCase(-1f, 1f)]
        [TestCase(1f, 1f)]
        public void CarryFacingDirection_AlwaysPointsFromCarrierToAnchor(
            float anchorX,
            float anchorZ)
        {
            MethodInfo method = typeof(PlayerCouchCarrier).GetMethod(
                "CalculateCarryFacingDirection",
                StaticPrivate | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);

            Vector3 carrier = new Vector3(anchorX * 2f, 3f, anchorZ * 2f);
            Vector3 anchor = new Vector3(anchorX, 0.5f, anchorZ);
            Vector3 actual = (Vector3)method.Invoke(null, new object[] { carrier, anchor });
            Vector3 expected = Vector3.ProjectOnPlane(anchor - carrier, Vector3.up);

            Assert.That(actual.y, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(Vector3.Angle(actual, expected), Is.LessThan(0.01f));
        }

        [Test]
        public void DebugCarryBehaviours_OnlyChangeTheSharedMovementIntentSource()
        {
            MethodInfo method = typeof(DebugCouchBotController).GetMethod(
                "ResolveCarryMovementIntent", StaticPrivate);
            Assert.That(method, Is.Not.Null);

            Vector3 leaderIntent = new Vector3(0.25f, 0f, 0.5f);
            Vector3 cooperative = (Vector3)method.Invoke(null, new object[]
            {
                DebugCouchBotController.DebugCarryBehaviour.Cooperative,
                leaderIntent,
                Vector3.right
            });
            Vector3 opposing = (Vector3)method.Invoke(null, new object[]
            {
                DebugCouchBotController.DebugCarryBehaviour.Opposing,
                leaderIntent,
                Vector3.right
            });
            Vector3 fixedDirection = (Vector3)method.Invoke(null, new object[]
            {
                DebugCouchBotController.DebugCarryBehaviour.FixedDirection,
                leaderIntent,
                Vector3.right * 3f + Vector3.up
            });

            Assert.That(cooperative, Is.EqualTo(leaderIntent));
            Assert.That(opposing, Is.EqualTo(-leaderIntent));
            Assert.That(fixedDirection, Is.EqualTo(Vector3.right));
        }

        [Test]
        public void AttachmentCorrection_PreventsCarrierEscapingItsGrabPoint()
        {
            MethodInfo method = typeof(ThirdPersonPlayerController).GetMethod(
                "CalculateAttachmentCorrection", StaticPrivate);
            Assert.That(method, Is.Not.Null);

            Vector3 attemptedPosition = new Vector3(1.5f, 3f, 0f);
            Vector3 correction = (Vector3)method.Invoke(null, new object[]
            {
                attemptedPosition,
                Vector3.zero,
                Vector3.right,
                0.45f,
                1f
            });
            Vector3 correctedPosition = attemptedPosition + correction;

            Assert.That(correctedPosition.y, Is.EqualTo(attemptedPosition.y));
            Assert.That(Vector3.ProjectOnPlane(correctedPosition, Vector3.up).magnitude,
                Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void DirectionalResistance_ConstrainsPlayerButPreservesCouchIntent()
        {
            MethodInfo method = typeof(ThirdPersonPlayerController).GetMethod(
                "ApplyDirectionalResistance", StaticPrivate);
            Assert.That(method, Is.Not.Null);

            Vector3 pushingMovement = Vector3.back;
            object[] pushingArguments =
            {
                pushingMovement, new Vector3(0f, 0f, 0.45f), Vector3.zero,
                Vector3.forward, 0.45f, 0.7f, 1f, 0.3f, null
            };
            Vector3 resistedPush = (Vector3)method.Invoke(null, pushingArguments);
            Vector3 pushingIntent = (Vector3)pushingArguments[8];

            Vector3 pullingMovement = Vector3.forward;
            object[] pullingArguments =
            {
                pullingMovement, new Vector3(0f, 0f, 1f), Vector3.zero,
                Vector3.forward, 0.45f, 0.7f, 1f, 0.3f, null
            };
            Vector3 resistedPull = (Vector3)method.Invoke(null, pullingArguments);
            Vector3 pullingIntent = (Vector3)pullingArguments[8];

            Assert.That(resistedPush.sqrMagnitude, Is.LessThan(pushingMovement.sqrMagnitude));
            Assert.That(resistedPull.sqrMagnitude, Is.LessThan(pullingMovement.sqrMagnitude));
            Assert.That(pushingIntent, Is.EqualTo(pushingMovement));
            Assert.That(pullingIntent, Is.EqualTo(pullingMovement));
        }

        [Test]
        public void HorizontalCarryForce_DoesNotDragPointMovingWithCarrierIntent()
        {
            MethodInfo method = typeof(CouchCarryController).GetMethod(
                "CalculateHorizontalCarryForce", StaticPrivate);
            Assert.That(method, Is.Not.Null);

            Vector3 force = (Vector3)method.Invoke(null, new object[]
            {
                Vector3.zero,
                Vector3.forward * 2f,
                Vector3.forward,
                2f,
                1f,
                1f
            });

            Assert.That(force.sqrMagnitude, Is.LessThan(0.0001f));
        }

        [Test]
        public void OffCentreHorizontalForce_ProducesMirroredYawTorque()
        {
            MethodInfo method = typeof(CouchCarryController).GetMethod(
                "CalculateHorizontalCarryTorque", StaticPrivate);
            Assert.That(method, Is.Not.Null);

            Vector3 leftTorque = (Vector3)method.Invoke(null,
                new object[] { Vector3.zero, Vector3.left, Vector3.forward * 100f });
            Vector3 rightTorque = (Vector3)method.Invoke(null,
                new object[] { Vector3.zero, Vector3.right, Vector3.forward * 100f });
            Vector3 centreTorque = (Vector3)method.Invoke(null,
                new object[] { Vector3.zero, Vector3.zero, Vector3.forward * 100f });

            Assert.That(leftTorque.y, Is.EqualTo(-rightTorque.y).Within(0.0001f));
            Assert.That(Mathf.Abs(leftTorque.y), Is.GreaterThan(0f));
            Assert.That(centreTorque.sqrMagnitude, Is.LessThan(0.0001f));
        }

        [Test]
        public void OpposingInputs_ProduceZeroDesiredTranslation()
        {
            MethodInfo method = typeof(CouchCarryController).GetMethod(
                "CalculateDesiredCarryVelocity", StaticPrivate);
            Assert.That(method, Is.Not.Null);

            Vector3 velocity = (Vector3)method.Invoke(null,
                new object[] { Vector3.forward + Vector3.back, 2, 2.5f });

            Assert.That(velocity.sqrMagnitude, Is.LessThan(0.0001f));
        }

        [TestCase(0f, true)]
        [TestCase(45f, true)]
        [TestCase(90f, false)]
        [TestCase(180f, false)]
        public void GrabFacing_RequiresPlayerToLookTowardCouch(float yaw, bool expected)
        {
            MethodInfo method = typeof(PlayerCouchCarrier).GetMethod(
                "IsFacingTarget", StaticPrivate);
            Assert.That(method, Is.Not.Null);

            Vector3 forward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            bool result = (bool)method.Invoke(null, new object[]
            {
                Vector3.zero,
                forward,
                Vector3.forward * 2f,
                0.35f
            });

            Assert.That(result, Is.EqualTo(expected));
        }
    }
}
