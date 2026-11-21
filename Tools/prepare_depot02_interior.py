"""Prepare a separate Unity conversion project; no changes to the user's editor session."""
from pathlib import Path
import shutil
ROOT=Path(__file__).resolve().parents[1]
stage=ROOT/'Temp/Depot02InteriorConversion'
for folder in ['Assets/Editor','Assets/_Project/Generated/Depot02','Packages','ProjectSettings']:(stage/folder).mkdir(parents=True,exist_ok=True)
code=(ROOT/'Tools/Unity/AuthorDepot02Exterior.cs').read_text().replace('AuthorDepot02Exterior','AuthorDepot02Interior').replace('Exterior','Interior').replace('exterior','interior')
# Keep the shared existing emission material mapping.
code=code.replace('D02_InteriorGreen','D02_ExteriorGreen')
(ROOT/'Tools/Unity/AuthorDepot02Interior.cs').write_text(code)
(stage/'Assets/Editor/AuthorDepot02Interior.cs').write_text(code)
for p in (ROOT/'Assets/_Project/Generated/Depot02').glob('*.mat*'):shutil.copy2(p,stage/'Assets/_Project/Generated/Depot02'/p.name)
(stage/'Packages/manifest.json').write_text('{"dependencies":{}}')
shutil.copy2(ROOT/'ProjectSettings/ProjectVersion.txt',stage/'ProjectSettings/ProjectVersion.txt')
shutil.copy2(ROOT/'ArtSource/Depot02/depot_02_interior_meshes.json',stage/'interior.json')
print(stage)
