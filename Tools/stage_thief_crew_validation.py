"""Refresh only the relevant assets in the existing isolated validation project."""
from pathlib import Path
import shutil
ROOT=Path(__file__).resolve().parents[1];STAGE=ROOT/'.utmp/thief-prefab-upgrade-unity'
OUT=ROOT/'ArtSource/Characters/ThiefEnemy01/GetawayCrew'
assert not (STAGE/'Temp/UnityLockfile').exists(),'Validation project is already open'
paths=[ROOT/'Assets/_Project/Models/Thieves/thief_enemy_01.fbx']
for folder in ['Assets/_Project/Prefabs/Enemies','Assets/_Project/Animations/Thieves/ThiefEnemy01','Assets/_Project/Materials/Thieves/ThiefEnemy01','Assets/_Project/Textures/Thieves/ThiefEnemy01']:
    paths.extend(p for p in (ROOT/folder).rglob('*') if p.is_file() and p.suffix!='.meta')
for p in paths:
    dest=STAGE/p.relative_to(ROOT);dest.parent.mkdir(parents=True,exist_ok=True)
    shutil.copy2(OUT/p.name if p.suffix=='.png' and (OUT/p.name).exists() else p,dest)
    meta=Path(str(p)+'.meta')
    if meta.exists():shutil.copy2(meta,Path(str(dest)+'.meta'))
shutil.copy2(ROOT/'Tools/Unity/ValidateThiefCrew.cs',STAGE/'Assets/Editor/ValidateThiefCrew.cs')
print('Staged current saved production assets and candidate crew maps for read-only validation.')
