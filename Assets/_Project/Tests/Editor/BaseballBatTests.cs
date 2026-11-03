using System.Linq;
using System.Reflection;
using CouchGuys.Editor;
using CouchGuys.Gameplay.Weapons;
using CouchGuys.Player;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace CouchGuys.Tests.Editor
{
    public sealed class BaseballBatTests
    {
        [Test]
        public void AssetsSupportPhysicalNetworkPickupAndShop()
        {
            BaseballBatAuthoring.Validate();
            var config=AssetDatabase.LoadAssetAtPath<BaseballBatAssets>(BaseballBatAuthoring.Config);
            Assert.That(config.Damage,Is.EqualTo(5));
            Assert.That(config.EquippedPrefab.GetComponent<BaseballBatPresentation>(),Is.Not.Null);
            Assert.That(new SerializedObject(config.EquippedPrefab.GetComponent<BaseballBatPresentation>())
                .FindProperty("m_swingSound").objectReferenceValue,Is.Not.Null);
        }

        [Test]
        public void SwingRetainsTwoHandGripAndDoesNotAnimateTheHoverRing()
        {
            var player=PrefabUtility.LoadPrefabContents("Assets/_Project/Prefabs/Player.prefab");
            try
            {
                var animator=player.GetComponent<CouchGuyAnimationDriver>().Animator;
                var rig=animator.GetComponentsInChildren<Transform>(true).First(t=>t.name=="CouchGuy_Rig");
                var support=rig.GetComponentsInChildren<Transform>(true).First(t=>t.name=="Hand_R");
                var bat=(Transform)new SerializedObject(player.GetComponent<PlayerWeaponController>())
                    .FindProperty("m_weaponModels").GetArrayElementAtIndex(5).objectReferenceValue;
                var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(BaseballBatAuthoring.AnimationFolder+"/Bat_Swing.anim");
                Assert.That(clip.length,Is.EqualTo(.85f).Within(.002f));
                Vector3 previous=Vector3.zero; float travel=0;
                foreach(float t in new[]{0,.2f,.3f,.4f,.65f,.85f})
                {
                    clip.SampleAnimation(animator.gameObject,t);
                    Assert.That(Vector3.Distance(support.position,bat.position+bat.forward*.095f),Is.LessThan(.04f),"Support grip at "+t);
                    Vector3 tip=bat.TransformPoint(Vector3.forward*.8f);
                    if(t>0)travel+=Vector3.Distance(tip,previous);
                    previous=tip;
                }
                Assert.That(travel,Is.GreaterThan(1.5f));
                var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(CouchGuyAnimatorControllerBuilder.ControllerPath);
                var mask=controller.layers.Single(l=>l.name=="Baseball Bat").avatarMask;
                for(int i=0;i<mask.transformCount;i++)
                {
                    string path=mask.GetTransformPath(i);
                    Assert.That(path.Length==0 || animator.transform.Find(path)!=null,Is.True,"Mask path is not relative to Animator: "+path);
                    if(mask.GetTransformPath(i).Contains("Hover") || mask.GetTransformPath(i).Contains("Leg") || mask.GetTransformPath(i).Contains("Foot"))
                        Assert.That(mask.GetTransformActive(i),Is.False,mask.GetTransformPath(i));
                }
                var torso=rig.GetComponentsInChildren<Transform>(true).First(t=>t.name=="Torso");
                clip.SampleAnimation(animator.gameObject,.3f);
                Quaternion expectedTorso=torso.localRotation;
                animator.Rebind();
                int layer=animator.GetLayerIndex("Baseball Bat");
                animator.SetLayerWeight(layer,1);
                animator.Play(Animator.StringToHash("Baseball Bat.Bat_Swing"),layer,.3f/.85f);
                animator.Update(0);
                Assert.That(Quaternion.Angle(expectedTorso,torso.localRotation),Is.LessThan(2f),"Masked Animator layer did not apply to the rig.");
            }
            finally{PrefabUtility.UnloadPrefabContents(player);}
        }

        [Test]
        public void ThinWallBlocksBatLineOfSightButOwnColliderDoesNot()
        {
            var player=PrefabUtility.LoadPrefabContents("Assets/_Project/Prefabs/Player.prefab");
            var target=new GameObject("BatTestTarget");var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                var health=target.AddComponent<PlayerHealth>();
                var collider=target.AddComponent<BoxCollider>();
                target.transform.position=new Vector3(50,20,52);
                wall.transform.position=new Vector3(50,20,51);wall.transform.localScale=new Vector3(2,2,.01f);
                Vector3 origin=new(50,20,50);Vector3 point=new(50,20,51.9f);
                Physics.SyncTransforms();
                var controller=player.GetComponent<PlayerWeaponController>();
                var method=typeof(PlayerWeaponController).GetMethod("BatHasLineOfSight",BindingFlags.Instance|BindingFlags.NonPublic);
                Assert.That((bool)method.Invoke(controller,new object[]{origin,point,health}),Is.False);
                wall.transform.position+=Vector3.right*5;Physics.SyncTransforms();
                Assert.That((bool)method.Invoke(controller,new object[]{origin,point,health}),Is.True);
            }
            finally{Object.DestroyImmediate(target);Object.DestroyImmediate(wall);PrefabUtility.UnloadPrefabContents(player);}
        }
    }
}
