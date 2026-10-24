using System.Linq;
using CouchGuys.Gameplay.Shop;
using CouchGuys.Gameplay.Weapons;
using FishNet.Component.Transforming;
using FishNet.Managing.Object;
using FishNet.Object;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace CouchGuys.Tests.Editor
{
    public sealed class WeaponOwnershipTests
    {
        [TestCase(PlayerWeaponType.Pistol)]
        [TestCase(PlayerWeaponType.AssaultRifle)]
        [TestCase(PlayerWeaponType.Shotgun)]
        public void PickupIsPhysicalRegisteredAndUsesEquippedMaterials(PlayerWeaponType type)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/_Project/Resources/WeaponPickups/{type}.prefab");
            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.GetComponent<WeaponPickup>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<Rigidbody>().isKinematic, Is.False);
            Assert.That(prefab.GetComponent<Collider>().isTrigger, Is.False);
            var network = prefab.GetComponent<NetworkObject>();
            var spawnables = AssetDatabase.LoadAssetAtPath<SinglePrefabObjects>("Assets/_Project/Settings/NetworkSpawnablePrefabs.asset");
            Assert.That(spawnables.Prefabs.Contains(network), Is.True);
            Assert.That(new SerializedObject(prefab.GetComponent<NetworkTransform>()).FindProperty("_clientAuthoritative").boolValue, Is.False);
            var player = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Player.prefab");
            Renderer[] originals = player.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>())
            {
                Renderer original = originals.FirstOrDefault(candidate => candidate.name == renderer.name);
                Assert.That(original, Is.Not.Null, renderer.name);
                CollectionAssert.AreEqual(original.sharedMaterials, renderer.sharedMaterials, renderer.name);
                float originalSize = Vector3.Scale(original.localBounds.size, original.transform.lossyScale).magnitude;
                float pickupSize = Vector3.Scale(renderer.localBounds.size, renderer.transform.lossyScale).magnitude;
                Assert.That(pickupSize, Is.EqualTo(originalSize).Within(0.002f), renderer.name + " scale");
            }
        }

        [Test]
        public void PunchLayerPreservesLocomotionAndUsesBothAuthoredClips()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Models/CouchGuy/CouchGuy_Redesigned.fbx");
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/_Project/Animations/CouchGuy.controller");
            var layer = controller.layers.Single(candidate => candidate.name == "Punches");
            Assert.That(layer.defaultWeight, Is.Zero);
            Assert.That(layer.avatarMask, Is.Not.Null);
            foreach (string name in new[] { "Punch_Left", "Punch_Right" })
            {
                var state = layer.stateMachine.states.Single(candidate => candidate.state.name == name).state;
                Assert.That(state.motion, Is.TypeOf<AnimationClip>());
                Assert.That(((AnimationClip)state.motion).isLooping, Is.False);
                var armCurves = AnimationUtility.GetCurveBindings((AnimationClip)state.motion)
                    .Where(binding => binding.path.Contains("UpperArm_") || binding.path.Contains("LowerArm_")).ToArray();
                Assert.That(armCurves.Length, Is.GreaterThan(0));
                foreach (var curve in armCurves)
                    Assert.That(model.transform.Find(curve.path), Is.Not.Null, curve.path);
            }
            Assert.That(controller.layers[0].stateMachine.states.Length, Is.GreaterThan(5));
            for (int i = 0; i < layer.avatarMask.transformCount; i++)
                if (layer.avatarMask.GetTransformPath(i).Contains("Hover") || layer.avatarMask.GetTransformPath(i).EndsWith("/Torso"))
                    Assert.That(layer.avatarMask.GetTransformActive(i), Is.False);
        }

        [Test]
        public void AnimationExportRetainsAllOriginalActionsAndMeshes()
        {
            const string folder = "Assets/_Project/Models/CouchGuy/";
            var original = AssetDatabase.LoadAssetAtPath<GameObject>(folder + "CouchGuy_Redesigned.fbx");
            var updated = AssetDatabase.LoadAssetAtPath<GameObject>(folder + "CouchGuy_WithPunches.fbx");
            foreach (MeshFilter mesh in original.GetComponentsInChildren<MeshFilter>(true))
            {
                var copy = updated.GetComponentsInChildren<MeshFilter>(true).Single(candidate => candidate.name == mesh.name);
                Assert.That(copy.sharedMesh.vertexCount, Is.EqualTo(mesh.sharedMesh.vertexCount), mesh.name);
            }
            string[] originalClips = AssetDatabase.LoadAllAssetsAtPath(folder + "CouchGuy_Redesigned.fbx")
                .OfType<AnimationClip>().Select(clip => clip.name).Where(name => !name.StartsWith("__preview__")).ToArray();
            string[] updatedClips = AssetDatabase.LoadAllAssetsAtPath(folder + "CouchGuy_WithPunches.fbx")
                .OfType<AnimationClip>().Select(clip => clip.name).ToArray();
            CollectionAssert.IsSubsetOf(originalClips, updatedClips);
        }

        [Test]
        public void ShopWeaponsUseBuyerFulfilment()
        {
            var catalogue = AssetDatabase.LoadAssetAtPath<ShopCatalogue>("Assets/_Project/Resources/Shop/DeliveryManagerShop.asset");
            foreach (PlayerWeaponType type in System.Enum.GetValues(typeof(PlayerWeaponType)))
            {
                var item = catalogue.Find($"weapon_{type.ToString().ToLowerInvariant()}");
                Assert.That(item, Is.Not.Null);
                Assert.That(item.PurchaseBehaviour, Is.TypeOf<WeaponPurchaseBehaviour>());
                Assert.That(item.Price, Is.GreaterThan(0));
            }
        }
    }
}
