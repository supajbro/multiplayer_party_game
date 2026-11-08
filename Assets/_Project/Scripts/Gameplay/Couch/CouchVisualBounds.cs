using UnityEngine;

namespace CouchGuys.Gameplay.Couch
{
    /// <summary>Editable collision envelope for a saved couch visual, in couch-local metres.</summary>
    public sealed class CouchVisualBounds : MonoBehaviour
    {
        [SerializeField] private Bounds m_collisionBounds = new Bounds(new Vector3(0, .72f, 0), new Vector3(2.12f, 1.44f, 1f));
        public Bounds CollisionBounds => m_collisionBounds;
    }
}
