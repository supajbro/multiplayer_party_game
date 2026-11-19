using System.Linq;
using System.Reflection;
using CouchGuys.Gameplay.Couch;
using CouchGuys.Gameplay.Enemies;
using CouchGuys.Gameplay.Weapons;
using CouchGuys.Player;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CouchGuys.Tests.Editor
{
    public sealed class CarryPresentationTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Private).GetValue(target);
        static void Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private).Invoke(target, args);

        [TestCase("Thief")]
        [TestCase("Thief_Pistol")]
        [TestCase("Thief_AssaultRifle")]
        [TestCase("Thief_Shotgun")]
        [TestCase("Thief_RocketLauncher")]
        [TestCase("GrapplerThief")]
        public void ThiefPrefabsHaveWorkingAuraOriginsAndWeaponReferences(string name)
        {
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/_Project/Prefabs/Enemies/{name}.prefab"));
            try
            {
                var aura = root.GetComponent<MagneticCarryAura>();
                Assert.That(aura, Is.Not.Null);
                Assert.That(Field<ThiefNavigator>(aura, "m_thief"), Is.SameAs(root.GetComponent<ThiefNavigator>()));
                Call(aura, "Awake");
                Assert.That(Field<MagneticCarryEffect>(aura, "m_auraPrefab"), Is.Not.Null);
                Assert.That(Field<Transform>(aura, "m_torso"), Is.Not.Null);
                Assert.That(Field<Transform[]>(aura, "m_hands").All(t => t != null), Is.True);
                var weapon = root.GetComponent<EnemyWeapon>();
                if (weapon != null)
                {
                    var renderers = Field<Renderer[]>(weapon, "m_heldWeaponRenderers");
                    Assert.That(renderers, Is.Not.Empty);
                    Assert.That(renderers.All(r => r != null && r.name.StartsWith("Weapon_")), Is.True);
                }
            }
            finally { Object.DestroyImmediate(root); }
        }

        [TestCase("Couch")]
        [TestCase("Vehicles/Sled")]
        public void ThiefAttachmentHidesAndRestoresWeapon(string carryPrefab)
        {
            var thief = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Enemies/Thief_Pistol.prefab"));
            var target = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/_Project/Prefabs/{carryPrefab}.prefab"));
            try
            {
                var navigator = thief.GetComponent<ThiefNavigator>();
                var weapon = thief.GetComponent<EnemyWeapon>();
                Call(weapon, "Awake");
                var couch = target.GetComponent<CouchCarryController>();
                var carrier = (ICouchCarrierServer)navigator;
                carrier.SetCarriedCouchServer(couch, 0);
                Assert.That(navigator.CarriedCouch, Is.SameAs(couch));
                Assert.That(navigator.CarriedPointIndex, Is.Zero);
                Assert.That(navigator.CanAttack, Is.False);
                Call(weapon, "LateUpdate");
                var renderers = Field<Renderer[]>(weapon, "m_heldWeaponRenderers");
                Assert.That(renderers.All(r => r.forceRenderingOff), Is.True);
                carrier.ClearCarriedCouchServer(couch, 0);
                Call(weapon, "LateUpdate");
                Assert.That(navigator.IsCarrying, Is.False);
                Assert.That(renderers.All(r => !r.forceRenderingOff), Is.True);
            }
            finally { Object.DestroyImmediate(thief); Object.DestroyImmediate(target); }
        }

        [TestCase("Couch")]
        [TestCase("Vehicles/Sled")]
        public void PlayerSwitchingWeaponsWhileCarryingKeepsHandsEmpty(string carryPrefab)
        {
            var player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Player.prefab"));
            var target = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/_Project/Prefabs/{carryPrefab}.prefab"));
            try
            {
                var weapon = player.GetComponent<PlayerWeaponController>();
                Call(weapon, "Awake");
                var carrier = player.GetComponent<PlayerCouchCarrier>();
                var attachment = Field<SyncVar<NetworkObject>>(carrier, "m_carriedCouch");
                attachment.Value = target.GetComponent<NetworkObject>();
                var models = Field<Transform[]>(weapon, "m_weaponModels");
                for (int index = 0; index < models.Length; index++)
                {
                    Call(weapon, "ApplyEquippedWeapon", index);
                    Assert.That(models.All(m => m == null || !m.gameObject.activeSelf), Is.True);
                }
                attachment.Value = null;
                Call(weapon, "ApplyEquippedWeapon", 0);
                Assert.That(models[0].gameObject.activeSelf, Is.True);
            }
            finally { Object.DestroyImmediate(player); Object.DestroyImmediate(target); }
        }
    }
}
