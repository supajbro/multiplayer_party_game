"""Retain original root serialization, changing only the migrated visual references."""
from pathlib import Path
import re,json
ROOT=Path(__file__).resolve().parents[1]
REPORT=ROOT/'ArtSource/Characters/ThiefEnemy01/PrefabUpgrade'
STAGE=ROOT/'.utmp/thief-prefab-upgrade-unity'
pattern=re.compile(r'^--- !u!\d+ &(-?\d+)(?: stripped)?\n(.*?)(?=^--- !u!|\Z)',re.M|re.S)
allowed=['m_animator','m_ragdoll','m_muzzle','m_gun','m_heldWeaponRenderers']
def field(s,name):return re.search(r'^  '+re.escape(name)+r':[^\n]*(?:\n(?:    .*|  - .*))*',s,re.M)
report={}
for original in (REPORT/'OriginalPrefabs').glob('*.prefab'):
    target=STAGE/'Assets/_Project/Prefabs/Enemies'/original.name
    before=original.read_text();after=target.read_text()
    old={int(m[1]):m[2] for m in pattern.finditer(before)}
    new={int(m[1]):m[2] for m in pattern.finditer(after)}
    rootid=next(k for k,v in old.items() if v.startswith('GameObject:') and re.search(r'^  m_Name: '+re.escape(original.stem)+r'\s*$',v,re.M))
    ids=[k for k,v in old.items() if v.startswith('MonoBehaviour:') and re.search(r'^  m_GameObject: \{fileID: '+str(rootid)+r'\}',v,re.M)]
    replacements={};changes=[]
    for key in ids:
        assert key in new,'Root gameplay component removed'
        text=old[key]
        for name in allowed:
            a=field(text,name);b=field(new[key],name)
            assert bool(a)==bool(b),(original.name,name)
            if a and a[0]!=b[0]:text=text[:a.start()]+b[0]+text[a.end():];changes.append(name)
        replacements[key]=text
    def replace(m):
        key=int(m[1])
        if key not in replacements:return m[0]
        return m[0][:m[0].index('\n')+1]+replacements[key]
    target.write_text(pattern.sub(replace,after))
    report[original.stem]={'root_components_preserved':len(ids),'only_references_changed':changes,'root_game_object_id':rootid}
(REPORT/'GameplaySettingsPreservation.json').write_text(json.dumps(report,indent=2))
print('Preserved all seven original gameplay-root component blocks; changed only visual/attachment references.')
