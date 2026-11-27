using System;
using FishNet.Object;
using UnityEngine;

namespace CouchGuys.Player
{
    [CreateAssetMenu(menuName = "Couch Guys/Player Characters")]
    public sealed class PlayerCharacterCatalog : ScriptableObject
    {
        private const string PreferenceKey = "Player.Character";

        [Serializable]
        public sealed class Character
        {
            public string Id;
            public NetworkObject PlayerPrefab;
            public GameObject MenuVisualPrefab;
        }

        [SerializeField] private Character[] m_characters = Array.Empty<Character>();
        public int Count => m_characters.Length;
        public Character this[int index] => m_characters[index];
        public int SelectedIndex => FindIndex(PlayerPrefs.GetString(PreferenceKey, string.Empty));

        public int FindIndex(string id)
        {
            for (int i = 0; i < Count; i++)
                if (string.Equals(m_characters[i].Id, id, StringComparison.Ordinal)) return i;
            return 0;
        }

        public void Select(int index)
        {
            if (Count == 0) return;
            index = ((index % Count) + Count) % Count;
            PlayerPrefs.SetString(PreferenceKey, m_characters[index].Id);
            PlayerPrefs.Save();
        }
    }
}
