using UnityEngine;

namespace CouchGuys.UI
{
    /// <summary>
    /// Retained so older serialized scenes do not report a missing script.
    /// MainMenu is now authored entirely in MainMenu.unity.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MainMenuRuntimeBootstrap : MonoBehaviour
    {
        private void Awake()
        {
            Destroy(this);
        }
    }
}
