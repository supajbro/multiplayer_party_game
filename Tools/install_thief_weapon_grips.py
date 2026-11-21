"""Install the four validated grip corrections with root settings preserved."""
from pathlib import Path
import re,json,hashlib
ROOT=Path(__file__).resolve().parents[1];OUT=ROOT/'ArtSource/Characters/ThiefEnemy01/GripFix';STAGE=ROOT/'.utmp/thief-prefab-upgrade-unity'
before=json.loads((OUT/'BeforeHashes.json').read_text())
for rel,digest in before.items():assert hashlib.sha256((ROOT/rel).read_bytes()).hexdigest()==digest,'Changed during authoring: '+rel
validation=(OUT/'GripValidation.txt').read_text()
assert validation.count('PASS ')==25 and 'runtime aiming' in validation
rx=re.compile(r'^--- !u!\d+ &(-?\d+)(?: stripped)?\n(.*?)(?=^--- !u!|\Z)',re.M|re.S)
names=['Thief_Pistol','Thief_AssaultRifle','Thief_Shotgun','GrapplerThief'];candidates={};preservation={}
for name in names:
    rel=f'Assets/_Project/Prefabs/Enemies/{name}.prefab';text=(STAGE/rel).read_text()
    old={int(m[1]):m[2] for m in rx.finditer((ROOT/rel).read_text())};new={int(m[1]):m[2] for m in rx.finditer(text)}
    rootid=next(k for k,v in old.items() if v.startswith('GameObject:') and re.search(r'^  m_Name: '+re.escape(name)+r'\s*$',v,re.M))
    keep={};changed=[]
    for key,value in old.items():
        if not value.startswith('MonoBehaviour:') or not re.search(r'^  m_GameObject: \{fileID: '+str(rootid)+r'\}',value,re.M):continue
        assert key in new
        source=value
        if name=='GrapplerThief' and re.search(r'^  m_gun:',value,re.M):
            a=re.search(r'^  m_gun:.*$',value,re.M);b=re.search(r'^  m_gun:.*$',new[key],re.M)
            assert b;source=value[:a.start()]+b[0]+value[a.end():];changed.append('GrapplerThief.m_gun -> WeaponGrip aim pivot')
        refs=lambda t:sorted(i for i in re.findall(r'fileID: (-?\d+)',t) if i!='0')
        assert refs(source)==refs(new[key]),(name,'unexpected root reference change')
        assert sorted(re.findall(r'guid: (\w+)',source))==sorted(re.findall(r'guid: (\w+)',new[key]))
        keep[key]=source
    def replace(m):return m[0][:m[0].index('\n')+1]+keep[int(m[1])] if int(m[1]) in keep else m[0]
    candidate=rx.sub(replace,text)
    assert re.search(r'^guid: (\w+)',(ROOT/(rel+'.meta')).read_text(),re.M)[1]==re.search(r'^guid: (\w+)',(STAGE/(rel+'.meta')).read_text(),re.M)[1]
    candidates[rel]='\n'.join(line.rstrip() for line in candidate.splitlines())+'\n'
    preservation[name]={'root_id':rootid,'root_components_preserved':len(keep),'intentional_reference_changes':changed}
manifest={}
for rel,text in candidates.items():(ROOT/rel).write_text(text);manifest[rel]=hashlib.sha256((ROOT/rel).read_bytes()).hexdigest()
(OUT/'InstalledGripHashes.json').write_text(json.dumps(manifest,indent=2));(OUT/'RootSettingsPreservation.json').write_text(json.dumps(preservation,indent=2))
print('Installed four saved grip mounts; original prefab GUIDs, gameplay settings, gun/muzzle objects and renderer references retained. Grappler aim now uses the palm pivot.')
