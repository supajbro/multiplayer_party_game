using UnityEngine;

namespace CouchGuys.Player
{
    [CreateAssetMenu(menuName = "Couch Guys/Player/Emote")]
    public sealed class EmoteDefinition : ScriptableObject
    {
        [Range(1, 255)] public byte Id;
        public string DisplayName;
        public string PreviewLabel;
        public bool Loop;
        public AnimationClip Clip;
        [Min(.01f)] public float BlendTime = .15f;
        public RobotFaceExpression Expression = RobotFaceExpression.Happy;
        public AnimationCurve HoverX = AnimationCurve.Constant(0, 1, 0);
        public AnimationCurve HoverY = AnimationCurve.Constant(0, 1, 0);
        public AnimationCurve HoverZ = AnimationCurve.Constant(0, 1, 0);
        public AnimationCurve Pitch = AnimationCurve.Constant(0, 1, 0);
        public AnimationCurve Yaw = AnimationCurve.Constant(0, 1, 0);
        public AnimationCurve Roll = AnimationCurve.Constant(0, 1, 0);
        public float Duration => Clip != null ? Clip.length : 0f;
        public Vector3 Position(float t) => new(HoverX.Evaluate(t), HoverY.Evaluate(t), HoverZ.Evaluate(t));
        public Quaternion Rotation(float t) => Quaternion.Euler(Pitch.Evaluate(t), Yaw.Evaluate(t), Roll.Evaluate(t));
    }
}
