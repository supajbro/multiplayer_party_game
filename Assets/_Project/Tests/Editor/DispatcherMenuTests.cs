using System;
using System.Linq;
using System.Reflection;
using CouchGuys.CameraSystem;
using CouchGuys.Editor;
using CouchGuys.Input;
using CouchGuys.UI;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace CouchGuys.Tests.Editor
{
    public sealed class DispatcherMenuTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, Private).GetValue(target);
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
        private static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private).Invoke(target, args);

        [Test]
        public void SavedMenuFitsCurvedDisplayAndFocusFramesTvIncludingBeaconAndButtons()
        {
            var npc = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(DispatcherMenuAuthoring.NpcPath));
            var menu = AssetDatabase.LoadAssetAtPath<DispatcherMenuUI>(DispatcherMenuAuthoring.MenuPath);
            var cameraObject = new GameObject("DispatcherFramingCamera", typeof(Camera));
            try
            {
                var controller = npc.GetComponent<DispatcherMenuController>();
                var anchor = Get<Transform>(controller, "m_screenCanvasAnchor");
                var focus = Get<Transform>(controller, "m_cameraFocusPoint");
                var camera = cameraObject.GetComponent<Camera>();camera.fieldOfView = Get<float>(controller,"m_focusFieldOfView");camera.aspect=16f/9f;
                camera.transform.SetPositionAndRotation(focus.position, focus.rotation);
                var rect = menu.GetComponent<RectTransform>();
                var corners = new Vector3[4]; rect.GetLocalCorners(corners);
                var screen = Get<Renderer[]>(controller,"m_screens")[0].bounds;
                foreach(var corner in corners)
                {
                    var world=anchor.TransformPoint(Vector3.Scale(corner,rect.localScale));
                    Assert.That(world.x, Is.InRange(screen.min.x,screen.max.x));
                    Assert.That(world.y, Is.InRange(screen.min.y,screen.max.y));
                    Assert.That(world.z, Is.GreaterThan(screen.max.z));
                }
                foreach(float x in new[]{-.49f,.49f})
                    foreach(float y in new[]{1.21f,2.2f})
                    {
                        var projected=camera.WorldToViewportPoint(npc.transform.TransformPoint(new Vector3(x,y,screen.max.z)));
                        Assert.That(projected.z,Is.GreaterThan(0));
                        Assert.That(projected.x,Is.InRange(.02f,.98f));Assert.That(projected.y,Is.InRange(.02f,.98f));
                    }
                Assert.That(menu.GetComponent<Canvas>().renderMode,Is.EqualTo(RenderMode.WorldSpace));
                Assert.That(menu.GetComponentsInChildren<Button>(true).Length,Is.EqualTo(3));
                Assert.That(menu.GetComponentsInChildren<TMP_Text>(true).All(t=>t.font.name.StartsWith("BarlowSemiCondensed")),Is.True);
            }
            finally {Object.DestroyImmediate(npc);Object.DestroyImmediate(cameraObject);}
        }

        [Test]
        public void WholeCardUsesExistingCallbacksAndUnavailableCardExplainsWithoutPurchasing()
        {
            var card=Object.Instantiate(AssetDatabase.LoadAssetAtPath<DispatcherDeliveryButton>(DispatcherMenuAuthoring.CardPath));
            try
            {
                int purchases=0;string feedback=null;
                var button=card.GetComponent<Button>();Assert.That(button,Is.Not.Null);
                card.Bind(new DialogueShopOption("Easy Delivery","Description","FREE","$100",Color.white,true,"",()=>purchases++),message=>feedback=message);
                button.onClick.Invoke();Assert.That(purchases,Is.EqualTo(1));
                card.Bind(new DialogueShopOption("Hard Delivery","Description","$100","$500",Color.white,false,"No available Hard delivery houses",()=>purchases++),message=>feedback=message);
                button.onClick.Invoke();Assert.That(purchases,Is.EqualTo(1));Assert.That(feedback,Does.Contain("No available"));
                card.Bind(new DialogueShopOption("Shop item","Description","$250","",Color.white,false,"Need $200 more",()=>purchases++),message=>feedback=message);
                button.onClick.Invoke();Assert.That(purchases,Is.EqualTo(1));Assert.That(feedback,Is.EqualTo("Need $200 more"));
                card.OnPointerEnter(null);Assert.That(Get<Image>(card,"m_highlight").enabled,Is.True);
                card.OnPointerExit(null);Assert.That(Get<Image>(card,"m_highlight").enabled,Is.False);
                Assert.That(card.GetComponentsInChildren<Button>(true).Length,Is.EqualTo(1));
            }
            finally {Object.DestroyImmediate(card.gameObject);}
        }

        [Test]
        public void FocusFollowsTranslatedRotatedDispatcherAndRepeatedReturnsPreserveOrbitAndPlayer()
        {
            var player=new GameObject("FocusTestPlayer",typeof(PlayerInputReader));player.transform.position=Vector3.one*5000f;
            var rig=new GameObject("FocusTestRig");var cameraObject=new GameObject("FocusTestCamera",typeof(Camera));cameraObject.transform.SetParent(rig.transform,false);cameraObject.transform.localPosition=new Vector3(.1f,0,.2f);
            var focus=new GameObject("FocusTestDispatcher");focus.transform.position=player.transform.position+new Vector3(0,2,3);
            var controller=rig.AddComponent<ThirdPersonCameraController>();
            try
            {
                Set(controller,"m_cameraTarget",player.transform);Set(controller,"m_input",player.GetComponent<PlayerInputReader>());Call(controller,"Awake");
                float yaw=Get<float>(controller,"m_yaw"),pitch=Get<float>(controller,"m_pitch");var playerPosition=player.transform.position;
                float initialFov=cameraObject.GetComponent<Camera>().fieldOfView;
                for(int i=0;i<5;i++)
                {
                    controller.FocusDispatcher(focus.transform,0f,48f);Call(controller,"LateUpdate");
                    Assert.That(Vector3.Distance(cameraObject.transform.position,focus.transform.position),Is.LessThan(.001f));
                    focus.transform.SetPositionAndRotation(focus.transform.position+Vector3.right,Quaternion.Euler(0,30f*(i+1),0));
                    Call(controller,"LateUpdate");
                    Assert.That(Vector3.Distance(cameraObject.transform.position,focus.transform.position),Is.LessThan(.001f));
                    Assert.That(Quaternion.Angle(cameraObject.transform.rotation,focus.transform.rotation),Is.LessThan(.01f));
                    bool restored=false;controller.ReturnFromDispatcher(.6f,()=>restored=true);
                    Assert.That(restored,Is.False);Assert.That(controller.DispatcherCameraActive,Is.True);
                    controller.ReturnFromDispatcher(0f,()=>restored=true,true);
                    Assert.That(restored,Is.True);Assert.That(controller.DispatcherCameraActive,Is.False);
                    Assert.That(cameraObject.GetComponent<Camera>().fieldOfView,Is.EqualTo(initialFov));
                    Assert.That(Get<float>(controller,"m_yaw"),Is.EqualTo(yaw));Assert.That(Get<float>(controller,"m_pitch"),Is.EqualTo(pitch));
                    Assert.That(player.transform.position,Is.EqualTo(playerPosition));
                }
            }
            finally {Object.DestroyImmediate(rig);Object.DestroyImmediate(focus);Object.DestroyImmediate(player);}
        }

        [Test]
        public void TabPropertiesAffectOnlyThisInstanceAndFaceSlotAcrossAllLods()
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(DispatcherMenuAuthoring.NpcPath);
            var first=Object.Instantiate(prefab);var second=Object.Instantiate(prefab);
            var ui=Object.Instantiate(AssetDatabase.LoadAssetAtPath<DispatcherMenuUI>(DispatcherMenuAuthoring.MenuPath));
            try
            {
                var controller=first.GetComponent<DispatcherMenuController>();Set(controller,"m_ui",ui);ui.ShowCategory(1);Call(controller,"UpdateButtonColors");
                var block=new MaterialPropertyBlock();int property=Shader.PropertyToID("_BaseColor");
                foreach(var renderer in Get<Renderer[]>(controller,"m_shopButtons"))
                {
                    renderer.GetPropertyBlock(block,1);Assert.That(block.GetColor(property).r,Is.GreaterThan(.9f));
                    renderer.GetPropertyBlock(block,2);Assert.That(block.isEmpty,Is.True,"Icon material was changed");
                    Assert.That(renderer.sharedMaterials[1].GetColor(property).r,Is.LessThan(.8f),"Shared material was mutated");
                }
                foreach(var renderer in Get<Renderer[]>(second.GetComponent<DispatcherMenuController>(),"m_shopButtons"))
                {renderer.GetPropertyBlock(block,1);Assert.That(block.isEmpty,Is.True,"Another client's instance was modified");}
            }
            finally {Object.DestroyImmediate(first);Object.DestroyImmediate(second);Object.DestroyImmediate(ui.gameObject);}
        }
    }
}
