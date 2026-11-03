using FishNet.Object;

namespace CouchGuys.Gameplay.Weapons
{
    public sealed partial class PlayerWeaponController
    {
        [Server]
        public void ConfirmRocketHitServer(bool elimination)
        {
            if (Owner.IsValid) ConfirmHostileHitTargetRpc(Owner, elimination);
        }
    }
}
