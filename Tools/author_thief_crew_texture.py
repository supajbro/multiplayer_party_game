"""Explicit UV-space surface bake; never changes meshes, rig, or prefab assets.

Run inspect_thief_reskin.py first. Original textures are archived once and
surface graphics are authored in rest-space then baked into the existing UVs.
"""
from pathlib import Path
import numpy as np
from PIL import Image, ImageFilter
import json, hashlib, shutil
ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'ArtSource/Characters/ThiefEnemy01/GetawayCrew'
TEX=ROOT/'Assets/_Project/Textures/Thieves/ThiefEnemy01'
OUT.mkdir(exist_ok=True)
BEFORE=OUT/'Before';BEFORE.mkdir(exist_ok=True)
for p in TEX.glob('*.png'):
    if not (BEFORE/p.name).exists():shutil.copy2(p,BEFORE/p.name)
manifest=OUT/'PreservedAssetHashes.json'
if not manifest.exists():
    paths=[ROOT/'Assets/_Project/Models/Thieves/thief_enemy_01.fbx',ROOT/'Assets/_Project/Models/Thieves/thief_enemy_01.fbx.meta']
    for folder in ['Assets/_Project/Animations/Thieves/ThiefEnemy01','Assets/_Project/Prefabs/Enemies','Assets/_Project/Materials/Thieves/ThiefEnemy01','Assets/_Project/Textures/Thieves/ThiefEnemy01']:
        paths.extend(p for p in (ROOT/folder).rglob('*') if p.is_file() and (folder.split('/')[-1]!='ThiefEnemy01' or p.suffix!='.png'))
    manifest.write_text(json.dumps({p.relative_to(ROOT).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in paths},indent=2))
data=np.load(OUT/'surface.npz');size=2048
pos=np.zeros((size,size,3),np.float32);valid=np.zeros((size,size),bool)
for xyz,uv in zip(data['xyz'],data['uv']):
    q=uv*size-.5;q[:,1]=size-1-q[:,1]
    x0,y0=np.maximum(0,np.floor(q.min(axis=0)).astype(int));x1,y1=np.minimum(size-1,np.ceil(q.max(axis=0)).astype(int))
    if x1<x0 or y1<y0:continue
    xx,yy=np.meshgrid(np.arange(x0,x1+1),np.arange(y0,y1+1))
    a,b,c=q;den=(b[1]-c[1])*(a[0]-c[0])+(c[0]-b[0])*(a[1]-c[1])
    if abs(den)<1e-10:continue
    u=((b[1]-c[1])*(xx-c[0])+(c[0]-b[0])*(yy-c[1]))/den
    v=((c[1]-a[1])*(xx-c[0])+(a[0]-c[0])*(yy-c[1]))/den;w=1-u-v
    mask=(u>=-1e-5)&(v>=-1e-5)&(w>=-1e-5)
    pos[y0:y1+1,x0:x1+1][mask]=(u[...,None]*xyz[0]+v[...,None]*xyz[1]+w[...,None]*xyz[2])[mask]
    valid[y0:y1+1,x0:x1+1][mask]=True
original=np.asarray(Image.open(BEFORE/'thief_enemy_01_BaseColor.png').convert('RGB').resize((size,size),Image.Resampling.BILINEAR))/255.
x,y,z=np.moveaxis(pos,-1,0);ax=abs(x);r,g,b=np.moveaxis(original,-1,0)
color=original.copy()
BURG=np.array([.53,.105,.25]);PLUM=np.array([.235,.105,.27]);GOLD=np.array([1.,.69,.025]);DARK=np.array([.105,.067,.12]);METAL=np.array([.23,.17,.27])
skin=(r-g>.10)&(g-b>.045)&(r>g*1.25)
shirt=valid&(z>.69)&(z<1.115)&(ax<.638)&~skin
pants=valid&(z>=.205)&(z<=.705)&~skin
shoes=valid&((z<.225)|((z<.28)&skin))
head=valid&(z>=1.115)
mask=head&(y<-.115)&(z>1.205)&(z<1.435)&(ax<.245)&~skin&(r<.30)&(g<.30)
eyes=head&(y<-.17)&(z>1.27)&(z<1.395)&(ax<.19)&(ax>.032)
hat=head&~skin&~mask&~eyes
# Keep the face and eyes untouched. Recolor only the original domino mask.
color[hat]=PLUM
color[mask]=METAL
color[shirt]=BURG
color[pants]=DARK
color[shoes]=DARK
# Close-fitting shirt side panels, following the existing torso only.
panel=shirt&(ax<.255)&(ax>(.13+.035*(z-.70)/.40))
color[panel]=PLUM
seam=panel&(abs(ax-(.13+.035*(z-.70)/.40))<.004)
color[seam]=BURG*1.12
# Paired cuff/sleeve bands in the same direction as the vehicle racing stripes.
bands=shirt&(((ax>.425)&(ax<.444))|((ax>.461)&(ax<.480)))
color[bands]=GOLD
# Beanie burgundy band and one fine gold pinstripe, without new geometry.
color[hat&(z>1.425)&(z<1.487)]=BURG
color[hat&(z>=1.487)&(z<1.499)]=GOLD
# The original mask strap is tan; make it the same yellow as the crew trim.
strap=head&skin&(ax>.235)&(z>1.32)&(z<1.425)
color[strap]=GOLD
# Recognizable broad angular lightning graphic on front and back torso.
bolt=np.array([[.060,1.035],[-.125,.914],[-.020,.929],[-.060,.822],[.135,.971],[.021,.951]])
def polygon(points,px,py):
    inside=np.zeros(px.shape,bool)
    for a,b in zip(points,np.roll(points,1,axis=0)):
        inside^=((a[1]>py)!=(b[1]>py))&(px<(b[0]-a[0])*(py-a[1])/(b[1]-a[1]+1e-20)+a[0])
    return inside
front=shirt&(y<-.045)&(ax<.15)&polygon(bolt,x,z)
backbolt=bolt.copy();backbolt[:,0]*=.78;backbolt[:,1]=.94+(backbolt[:,1]-.94)*.78
back=shirt&(y>.045)&(ax<.15)&polygon(backbolt,-x,z)
color[front|back]=GOLD
# Trouser side piping. Shoe upper, cuff and small yellow side trim.
color[pants&(ax>.17)&(abs(y)<.018)]=BURG
color[shoes&(z>.095)]=BURG*.85
color[shoes&(z>.052)&(z<.073)&(ax>.22)]=GOLD
# Retain a little original microvariation on leather; remove baked stripe contrast.
color[shoes]*=(.9+.15*original[shoes].mean(axis=-1))[:,None]
changed=shirt|pants|shoes|hat|mask|strap
# UV seam padding from adjacent authored texels, never from original stripes.
for _ in range(12):
    newvalid=valid.copy()
    for dy,dx in [(0,1),(0,-1),(1,0),(-1,0)]:
        near=np.roll(valid,(dy,dx),(0,1));take=~newvalid&near
        color[take]=np.roll(color,(dy,dx),(0,1))[take]
        pos[take]=np.roll(pos,(dy,dx),(0,1))[take]
        changed[take]=np.roll(changed,(dy,dx),(0,1))[take]
        newvalid[take]=True
    valid=newvalid
Image.fromarray(np.uint8(np.clip(color,0,1)*255)).resize((1024,1024),Image.Resampling.LANCZOS).save(OUT/'thief_enemy_01_BaseColor.png')
# Cloth is matte, mask and leather have restrained highlights. Preserve original
# face/hands normal map; erase the former clothing stripe relief on recolored cloth.
normal=np.asarray(Image.open(BEFORE/'thief_enemy_01_Normal.png').convert('RGB')).copy()
cloth=np.asarray(Image.fromarray(np.uint8((shirt|hat|pants)*255)).resize((normal.shape[1],normal.shape[0]),Image.Resampling.NEAREST))>127
normal[cloth]=[128,128,255]
Image.fromarray(normal).save(OUT/'thief_enemy_01_Normal.png')
orm=np.asarray(Image.open(BEFORE/'thief_enemy_01_MetallicOcclusionSmoothness.png').convert('RGBA')).copy()
change=np.asarray(Image.fromarray(np.uint8(changed*255)).resize((orm.shape[1],orm.shape[0]),Image.Resampling.NEAREST))>127
orm[change,0]=0;orm[change,1]=255;orm[change,2]=0;orm[change,3]=48
metal=np.asarray(Image.fromarray(np.uint8(mask*255)).resize((orm.shape[1],orm.shape[0]),Image.Resampling.NEAREST))>127
orm[metal,0]=80;orm[metal,3]=130
Image.fromarray(orm).save(OUT/'thief_enemy_01_MetallicOcclusionSmoothness.png')
print('Authored staged crew textures; original geometry and production textures untouched.')
