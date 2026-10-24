"""Prepare an isolated Unity import of affected assets without touching the open scene."""
from pathlib import Path
import shutil
root=Path(__file__).resolve().parents[1]
dest=root/'Temp/VehicleValidation'
def copy(relative):
    src=root/relative; target=dest/relative
    if src.is_dir(): shutil.copytree(src,target,dirs_exist_ok=True)
    elif src.exists(): target.parent.mkdir(parents=True,exist_ok=True); shutil.copy2(src,target)
for p in ['ProjectSettings','Packages','Assets/ThirdParty/FishySteamworks','Assets/_Project/Shaders',
          'Assets/_Project/Prefabs/Vehicles','Assets/_Project/Settings/Vehicles','Assets/_Project/Settings/Shop',
          'Assets/_Project/Resources/Shop','Assets/_Project/Resources/Traffic','Assets/_Project/Settings/NetworkSpawnablePrefabs.asset',
          'Assets/_Project/Settings/NetworkSpawnablePrefabs.asset.meta']:
    copy(p)
for src in (root/'Assets/_Project/Scripts').rglob('*'):
    if src.is_file() and 'Editor' not in src.relative_to(root/'Assets/_Project/Scripts').parts:
        copy(str(src.relative_to(root)))
for p in ['VehicleAssetUpgrade','TrafficSystemBuilder','ShopAssetBuilder']:
    for ext in ['.cs','.cs.meta']:copy('Assets/_Project/Scripts/Editor/'+p+ext)
for p in ['truck/truck.fbx','truck/truck.fbx.meta','truck/TruckRollerShutter.fbx','truck/TruckRollerShutter.fbx.meta','Cars/GreenHatchback.fbx','Cars/GreenHatchback.fbx.meta']:
    copy('Assets/_Project/Models/'+p)
for src in (root/'Assets/_Project/Models/truck').glob('*.png*'):copy(str(src.relative_to(root)))
# Never initialise unrelated project editor builders or open/save the user's scene.
print(dest)
