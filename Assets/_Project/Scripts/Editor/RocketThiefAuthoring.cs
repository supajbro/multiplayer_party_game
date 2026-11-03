using System;
using System.IO;
using System.Linq;
using CouchGuys.Gameplay.Delivery;
using CouchGuys.Gameplay.Enemies;
using CouchGuys.Gameplay.Weapons;
using FishNet.Managing.Object;
using FishNet.Object;
using GameKit.Dependencies.Utilities;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CouchGuys.Editor
{
    /// <summary>Explicit installation; preserves existing rocket-thief prefab and settings customizations.</summary>
    public static class RocketThiefAuthoring
    {
        public const string PrefabPath = "Assets/_Project/Prefabs/Enemies/Thief_RocketLauncher.prefab";
        public const string SettingsPath = "Assets/_Project/Settings/Thieves/RocketLauncherThiefSettings.asset";

        [MenuItem("Couch Guys/Thieves/Install Rocket Launcher Variant %#&o")]
        public static void Install()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Exit Play mode first.");
            var settings=AssetDatabase.LoadAssetAtPath<ThiefSettings>(SettingsPath);
            if(settings==null)
            {
                settings=Object.Instantiate(AssetDatabase.LoadAssetAtPath<ThiefSettings>("Assets/_Project/Settings/Thieves/ShotgunThiefSettings.asset"));
                settings.name="RocketLauncherThiefSettings";
                settings.Combat.WeaponType=EnemyWeaponType.RocketLauncher;
                settings.Combat.FireInterval=4f;settings.Combat.BurstCooldown=4f;
                settings.Combat.MinBurstShots=1;settings.Combat.MaxBurstShots=1;
                settings.Combat.Range=60f;settings.Combat.PreferredRange=18f;
                settings.Combat.PelletsPerShot=1;settings.Combat.SpreadAngle=0;
                settings.Combat.DamagePerPellet=60;settings.Combat.KnockbackForce=24;
                settings.Combat.MissChance=0;settings.Combat.ReactionTime=1.1f;
                settings.Alerts.AlertChance=0;
                AssetDatabase.CreateAsset(settings,SettingsPath);
            }
            if(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath)==null)BuildPrefab(settings);
            var no=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath).GetComponent<NetworkObject>();
            foreach(string path in new[]{"Assets/_Project/Settings/NetworkSpawnablePrefabs.asset","Assets/DefaultPrefabObjects.asset"})
            {
                var list=AssetDatabase.LoadAssetAtPath<SinglePrefabObjects>(path);
                if(list!=null && !list.Prefabs.Contains(no)){list.AddObject(no,false,false);EditorUtility.SetDirty(list);}
            }
            string stable=new((PrefabPath+no.name).ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
            no.SetAssetPathHash(stable.GetStableHashU64());EditorUtility.SetDirty(no);
            var hard=AssetDatabase.LoadAssetAtPath<DeliveryTierConfig>("Assets/_Project/Settings/Delivery/HardDelivery.asset");
            var tier=new SerializedObject(hard);
            if(tier.FindProperty("m_rocketThiefPrefab").objectReferenceValue==null)
            {
                tier.FindProperty("m_rocketThiefPrefab").objectReferenceValue=no;
                tier.FindProperty("m_rocketThiefChance").floatValue=.2f;
                tier.FindProperty("m_maxActiveRocketThieves").intValue=2;
                tier.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(hard);
            }
            AssetDatabase.SaveAssets();
            CouchGuys.Tests.Editor.RocketThiefTests.RunChecks();
        }

        private static void BuildPrefab(ThiefSettings settings)
        {
            var root=PrefabUtility.LoadPrefabContents("Assets/_Project/Prefabs/Enemies/Thief_Shotgun.prefab");
            try
            {
                root.name="Thief_RocketLauncher";
                foreach(var component in root.GetComponentsInChildren<Component>(true))
                {
                    if(component==null)continue;
                    var data=new SerializedObject(component);var property=data.FindProperty("m_settings");
                    if(property!=null && property.objectReferenceValue is ThiefSettings)
                    {property.objectReferenceValue=settings;data.ApplyModifiedPropertiesWithoutUndo();}
                }
                var weapon=root.GetComponent<EnemyWeapon>();var so=new SerializedObject(weapon);
                var oldMuzzle=(Transform)so.FindProperty("m_muzzle").objectReferenceValue;
                var hand=root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>
                    t.name.IndexOf("hand",StringComparison.OrdinalIgnoreCase)>=0 &&
                    (t.name.EndsWith("_R")||t.name.EndsWith(".R")||t.name.IndexOf("Right",StringComparison.OrdinalIgnoreCase)>=0));
                if(hand==null)throw new UnityException("Could not find the shotgun thief's right hand.");
                var renderers=root.GetComponentsInChildren<Renderer>(true);
                var gunRenderers=renderers.Where(r=>r.name.IndexOf("shotgun",StringComparison.OrdinalIgnoreCase)>=0 &&
                    r.name.IndexOf("thief",StringComparison.OrdinalIgnoreCase)<0 ||
                    r.name.StartsWith("Weapon",StringComparison.OrdinalIgnoreCase)||r.name.StartsWith("Gun",StringComparison.OrdinalIgnoreCase)).ToArray();
                Directory.CreateDirectory("Temp/RocketThief");
                File.WriteAllText("Temp/RocketThief/model-parts.txt",string.Join("\n",renderers.Select(r=>r.name))+"\nHand: "+hand.name);
                if(gunRenderers.Length==0)throw new UnityException("No separate shotgun mesh identified; see Temp/RocketThief/model-parts.txt.");
                foreach(var r in gunRenderers)r.enabled=false;
                Vector3 forward=oldMuzzle.position-hand.position;
                if(forward.sqrMagnitude<.001f)forward=root.transform.forward;
                var visual=AssetDatabase.LoadAssetAtPath<GameObject>(RocketLauncherAuthoring.Visual);
                var gun=((GameObject)PrefabUtility.InstantiatePrefab(visual,hand)).transform;
                gun.name="Weapon_RocketLauncher";gun.position=hand.position;
                gun.rotation=Quaternion.LookRotation(forward.normalized,root.transform.up);
                Vector3 scale=hand.lossyScale;gun.localScale=new Vector3(1/Mathf.Abs(scale.x),1/Mathf.Abs(scale.y),1/Mathf.Abs(scale.z));
                so.FindProperty("m_muzzle").objectReferenceValue=gun.Find("Muzzle");
                so.FindProperty("m_rocketWeapon").boxedValue=WeaponDefinition.CreateDefaults()[4];
                so.FindProperty("m_rocketWeapon").FindPropertyRelative("m_bulletPrefab").objectReferenceValue=
                    AssetDatabase.LoadAssetAtPath<GameObject>(RocketLauncherAuthoring.Projectile);
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root,PrefabPath);
            }
            finally{PrefabUtility.UnloadPrefabContents(root);}
        }
    }
}
