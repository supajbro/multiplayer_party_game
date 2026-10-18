using UnityEngine;

namespace CouchGuys.Gameplay.Depot
{
    /// <summary>Local marker validated by the authoritative DeliveryManager.</summary>
    [DisallowMultipleComponent]
    public sealed class DepotDoorButton : MonoBehaviour
    {
        [SerializeField] private DepotDoorKind m_doorKind = DepotDoorKind.Exit;

        public DepotDoorKind DoorKind => m_doorKind;

        public void Configure(DepotDoorKind kind)
        {
            m_doorKind = kind;
        }
    }
}
