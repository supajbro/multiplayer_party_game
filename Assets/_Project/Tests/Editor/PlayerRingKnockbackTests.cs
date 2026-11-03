using System;
using System.IO;
using System.Reflection;
using CouchGuys.Player;
using FishNet.Object.Synchronizing;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CouchGuys.Tests.Editor
{
    public sealed class PlayerRingKnockbackTests
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static void Set(object target,string name,object value)=>target.GetType().GetField(name,Flags).SetValue(target,value);
        private static void Sync<T>(PlayerHealth health,string name,T value)=>
            ((SyncVar<T>)typeof(PlayerHealth).GetField(name,Flags).GetValue(health)).Value=value;
        private static void Step(PlayerHealth health,float time)=>typeof(PlayerHealth).GetMethod("SimulateRingKnockback",Flags).Invoke(health,new object[]{time});
        private static void Start(PlayerHealth health)=>typeof(PlayerHealth).GetMethod("StartRingKnockback",Flags).Invoke(health,new object[]{Vector3.right*24f});
        private static Vector3 ReturnPosition(PlayerHealth health)=>((SyncVar<Vector3>)typeof(PlayerHealth).GetField("m_ringReturnPosition",Flags).GetValue(health)).Value;

        [TestCase(false)]
        [TestCase(true)]
        public void RingSlidesIndependentlyAndKeepsRecoveryDestinationTogether(bool wallInWay)
        {
            Vector3 origin=new Vector3(24000,0,24000);
            var root=new GameObject("Ring recoil test");root.transform.position=origin;
            var health=root.AddComponent<PlayerHealth>();
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.position=origin+Vector3.down*.5f;
            floor.transform.localScale=new Vector3(20,1,20);
            var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.transform.position=origin+new Vector3(1,1.5f,0);
            wall.transform.localScale=new Vector3(.1f,3,4);wall.SetActive(wallInWay);
            try
            {
                Sync(health,"m_knockedDown",true);Sync(health,"m_ringAnchor",origin+Vector3.up*.2f);Sync(health,"m_ringReturnPosition",origin);
                Set(health,"m_ringKnockbackMaxDistance",wallInWay ? 3f : .4f);
                Physics.SyncTransforms();Start(health);
                root.transform.position+=new Vector3(-10,12,0); // The launched player goes elsewhere.
                Vector3 player=root.transform.position;
                for(int i=0;i<100;i++)Step(health,.02f);
                Vector3 moved=health.RingAnchor-(origin+Vector3.up*.2f);
                Assert.That(moved.x,Is.GreaterThan(.05f));
                Assert.That(moved.x,Is.LessThanOrEqualTo(wallInWay ? .56f : .405f));
                Assert.That((ReturnPosition(health)-origin-moved).magnitude,Is.LessThan(.005f));
                Assert.That(root.transform.position,Is.EqualTo(player),"Ring motion must not drag the player.");
                Vector3 stopped=health.RingAnchor;Step(health,.5f);
                Assert.That(health.RingAnchor,Is.EqualTo(stopped),"Ring should settle instead of sliding forever.");
                Sync(health,"m_returningToRing",true);Start(health);Step(health,.2f);
                Assert.That(health.RingAnchor,Is.EqualTo(stopped),"Recovery destination must remain still while the player returns.");
            }
            finally{Object.DestroyImmediate(root);Object.DestroyImmediate(floor);Object.DestroyImmediate(wall);}
        }

        [Test]
        public void UnsupportedRingStopsRatherThanMovingRecoveryIntoMidair()
        {
            var root=new GameObject("Unsupported ring recoil");root.transform.position=new Vector3(24000,20,24000);
            try
            {
                var health=root.AddComponent<PlayerHealth>();
                Sync(health,"m_knockedDown",true);Sync(health,"m_ringAnchor",root.transform.position+Vector3.up*.2f);
                Sync(health,"m_ringReturnPosition",root.transform.position);
                Vector3 anchor=health.RingAnchor;Start(health);Step(health,.2f);
                Assert.That(health.RingAnchor,Is.EqualTo(anchor));
            }
            finally{Object.DestroyImmediate(root);}
        }

        [MenuItem("Couch Guys/Player/Test Ring Knockback %#&k")]
        public static void Run()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Run the ring checks outside Play mode.");
            Directory.CreateDirectory("Temp/PlayerRingKnockback");
            try
            {
                var tests=new PlayerRingKnockbackTests();
                tests.RingSlidesIndependentlyAndKeepsRecoveryDestinationTogether(false);
                tests.RingSlidesIndependentlyAndKeepsRecoveryDestinationTogether(true);
                tests.UnsupportedRingStopsRatherThanMovingRecoveryIntoMidair();
                File.WriteAllText("Temp/PlayerRingKnockback/results.txt","PASS: independent ring recoil; matching recovery destination; distance limit and damping; wall stop; no movement over unsupported ground or during player return.\n");
                Debug.Log("Player ring knockback checks PASS.");
            }
            catch(Exception error){File.WriteAllText("Temp/PlayerRingKnockback/results.txt","FAIL: "+error);throw;}
        }
    }
}
