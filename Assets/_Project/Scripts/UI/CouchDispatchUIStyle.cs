using System;
using System.Collections.Generic;
using UnityEngine;

namespace CouchGuys.UI
{
    [CreateAssetMenu(fileName = "CouchDispatchUIStyle", menuName = "Couch Guys/UI/Couch Dispatch Style")]
    public sealed class CouchDispatchUIStyle : ScriptableObject
    {
        [Serializable]
        public struct SpriteEntry
        {
            public string Name;
            public Sprite Sprite;
        }

        [SerializeField] private SpriteEntry[] m_sprites = Array.Empty<SpriteEntry>();
        private Dictionary<string, Sprite> m_lookup;

        public Sprite Get(string spriteName)
        {
            if (m_lookup == null)
            {
                m_lookup = new Dictionary<string, Sprite>(StringComparer.Ordinal);
                foreach (SpriteEntry entry in m_sprites)
                    if (!string.IsNullOrEmpty(entry.Name) && entry.Sprite != null)
                        m_lookup[entry.Name] = entry.Sprite;
            }

            return !string.IsNullOrEmpty(spriteName) && m_lookup.TryGetValue(spriteName, out Sprite sprite)
                ? sprite
                : null;
        }

#if UNITY_EDITOR
        public void SetSprites(SpriteEntry[] sprites)
        {
            m_sprites = sprites ?? Array.Empty<SpriteEntry>();
            m_lookup = null;
        }
#endif
    }
}
