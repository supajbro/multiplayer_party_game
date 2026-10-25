using System.Collections.Generic;
using System.Linq;
using CouchGuys.Gameplay.Pedestrians;
using FishNet.Component.Transforming;
using FishNet.Managing.Object;
using FishNet.Object;
using GameKit.Dependencies.Utilities;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace CouchGuys.Editor
{
    public static class PedestrianSystemBuilder
    {
        private const string Directory = "Assets/_Project/Resources/Pedestrians";
        private const string SettingsPath = Directory + "/PedestrianSettings.asset";
        private const string Source = "Assets/ThirdParty/Hodaart/HodaartLowPolyCharacterCollection3";
        private const string Spawnables = "Assets/_Project/Settings/NetworkSpawnablePrefabs.asset";

        [InitializeOnLoadMethod]
        private static void EnsureAssets()
        {
            if (AssetDatabase.LoadAssetAtPath<PedestrianSettings>(SettingsPath) == null)
                EditorApplication.delayCall += Build;
        }

        [MenuItem("Couch Guys/Pedestrians/Build Pedestrian System")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!AssetDatabase.IsValidFolder(Directory))
                AssetDatabase.CreateFolder("Assets/_Project/Resources", "Pedestrians");
            PedestrianSettings settings = AssetDatabase.LoadAssetAtPath<PedestrianSettings>(SettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<PedestrianSettings>();
                AssetDatabase.CreateAsset(settings, SettingsPath);
            }
            var collection = AssetDatabase.LoadAssetAtPath<SinglePrefabObjects>(Spawnables);
            if (collection == null) throw new UnityException("Pedestrians require the existing network spawnables collection.");
            AnimatorController animator = BuildAnimator();
            var characters = new List<NetworkObject>();
            for (int i = 1; i <= 10; i++)
            {
                GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>($"{Source}/Prefabs/Character {i:00}.prefab");
                if (source == null) continue;
                GameObject root = new GameObject($"Pedestrian_{i:00}");
                try
                {
                    Rigidbody body = root.AddComponent<Rigidbody>();
                    body.isKinematic = true;
                    body.useGravity = false;
                    CapsuleCollider collider = root.AddComponent<CapsuleCollider>();
                    collider.center = Vector3.up * 0.9f;
                    collider.height = 1.8f;
                    collider.radius = 0.28f;
                    collider.isTrigger = true;
                    NetworkObject no = root.AddComponent<NetworkObject>();
                    NetworkTransform nt = root.AddComponent<NetworkTransform>();
                    PedestrianController npc = root.AddComponent<PedestrianController>();
                    GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(source);
                    visual.transform.SetParent(root.transform, false);
                    Animator rig = visual.GetComponentInChildren<Animator>();
                    if (rig == null || rig.avatar == null || !rig.avatar.isHuman)
                        throw new UnityException($"Character {i:00} needs its supplied humanoid avatar.");
                    rig.runtimeAnimatorController = animator;
                    rig.applyRootMotion = false;
                    rig.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                    SetReference(npc, "m_animator", rig);
                    SetReference(npc, "m_ragdoll", BuildRagdoll(visual, rig));
                    ConfigureNetwork(no, nt, npc);
                    GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, $"{Directory}/Pedestrian_{i:00}.prefab");
                    NetworkObject prefab = saved.GetComponent<NetworkObject>();
                    if (!collection.Prefabs.Contains(prefab)) collection.AddObject(prefab, false, false);
                    string pathAndName = (AssetDatabase.GetAssetPath(saved) + saved.name).Trim().ToLowerInvariant();
                    string stableName = new string(pathAndName.Where(c =>
                        (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')).ToArray());
                    prefab.SetAssetPathHash(stableName.GetStableHashU64());
                    EditorUtility.SetDirty(prefab);
                    characters.Add(prefab);
                }
                finally { Object.DestroyImmediate(root); }
            }
            if (characters.Count == 0) throw new UnityException("No supplied pedestrian character prefabs were found.");
            settings.Characters = characters.ToArray();
            EditorUtility.SetDirty(settings);
            EditorUtility.SetDirty(collection);
            AssetDatabase.SaveAssets();
            Debug.Log($"Built {characters.Count} pedestrian prefabs, animation, ragdolls and network registration.");
        }

        private static AnimatorController BuildAnimator()
        {
            string path = Directory + "/Pedestrian.controller";
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller != null)
            {
                EnsureDanceState(controller);
                return controller;
            }
            controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            AnimatorState state = controller.CreateBlendTreeInController("Locomotion", out BlendTree tree);
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            string[] clips = { "Idle", "Walking", "Running" };
            for (int i = 0; i < clips.Length; i++)
            {
                AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{Source}/Animations/{clips[i]}.anim");
                if (clip == null) throw new UnityException($"Missing pedestrian animation: {clips[i]}");
                tree.AddChild(clip, i * 0.5f);
            }
            machine.defaultState = state;
            EnsureDanceState(controller);
            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static void EnsureDanceState(AnimatorController controller)
        {
            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            AnimatorState dance = machine.states.FirstOrDefault(state => state.state.name == "Dance").state;
            if (dance == null) dance = machine.AddState("Dance");
            dance.motion = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{Source}/Animations/Dance 1.anim");
            if (dance.motion == null) throw new UnityException("Missing pedestrian dance animation.");
            EditorUtility.SetDirty(dance);
            EditorUtility.SetDirty(controller);
        }

        private static PedestrianRagdoll BuildRagdoll(GameObject visual, Animator animator)
        {
            HumanBodyBones[] bones = { HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Head,
                HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.RightUpperArm,
                HumanBodyBones.RightLowerArm, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg,
                HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg };
            var bodies = new List<Rigidbody>();
            var colliders = new List<Collider>();
            foreach (HumanBodyBones bone in bones)
            {
                Transform joint = animator.GetBoneTransform(bone);
                if (joint == null) continue;
                Rigidbody body = joint.gameObject.AddComponent<Rigidbody>();
                body.mass = bone == HumanBodyBones.Hips ? 12f : 4f;
                body.isKinematic = true;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                body.linearDamping = 0.4f;
                body.angularDamping = 1.5f;
                SphereCollider collider = joint.gameObject.AddComponent<SphereCollider>();
                collider.radius = bone == HumanBodyBones.Hips || bone == HumanBodyBones.Spine ? 0.17f : 0.11f;
                collider.enabled = false;
                bodies.Add(body);
                colliders.Add(collider);
            }
            foreach (Rigidbody body in bodies)
            {
                Rigidbody parent = body.transform.parent.GetComponentInParent<Rigidbody>();
                // Hips is the free root; never attach the ragdoll to the network root.
                if (parent == null || !bodies.Contains(parent)) continue;
                CharacterJoint joint = body.gameObject.AddComponent<CharacterJoint>();
                joint.connectedBody = parent;
                joint.enableProjection = true;
                joint.lowTwistLimit = new SoftJointLimit { limit = -35f };
                joint.highTwistLimit = new SoftJointLimit { limit = 35f };
                joint.swing1Limit = new SoftJointLimit { limit = 45f };
                joint.swing2Limit = new SoftJointLimit { limit = 30f };
            }
            PedestrianRagdoll ragdoll = visual.AddComponent<PedestrianRagdoll>();
            ragdoll.enabled = false;
            SetReference(ragdoll, "m_animator", animator);
            SetArray(ragdoll, "m_bones", bodies.Cast<Object>().ToArray());
            SetArray(ragdoll, "m_colliders", colliders.Cast<Object>().ToArray());
            return ragdoll;
        }

        private static void ConfigureNetwork(NetworkObject no, NetworkTransform nt, PedestrianController npc)
        {
            SerializedObject transformSo = new SerializedObject(nt);
            transformSo.FindProperty("_clientAuthoritative").boolValue = false;
            transformSo.FindProperty("_synchronizePosition").boolValue = true;
            transformSo.FindProperty("_synchronizeRotation").boolValue = true;
            transformSo.FindProperty("_synchronizeScale").boolValue = false;
            transformSo.FindProperty("_interval").intValue = 3;
            transformSo.FindProperty("_interpolation").intValue = 2;
            transformSo.ApplyModifiedPropertiesWithoutUndo();
            SetArray(no, "NetworkBehaviours", new Object[] { nt, npc });
            NetworkBehaviour[] behaviours = { nt, npc };
            for (int i = 0; i < behaviours.Length; i++)
            {
                SerializedObject so = new SerializedObject(behaviours[i]);
                so.FindProperty("_componentIndexCache").intValue = i;
                so.FindProperty("_addedNetworkObject").objectReferenceValue = no;
                so.FindProperty("_networkObjectCache").objectReferenceValue = no;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static void SetReference(Object target, string field, Object value)
        {
            SerializedObject so = new SerializedObject(target);
            so.FindProperty(field).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetArray(Object target, string field, Object[] values)
        {
            SerializedObject so = new SerializedObject(target);
            SerializedProperty property = so.FindProperty(field);
            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
