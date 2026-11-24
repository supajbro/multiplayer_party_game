"""Bake clean Blender palette regions, never illumination, into the existing UV atlas."""
from pathlib import Path
import numpy as np
from PIL import Image
import json,shutil,hashlib
ROOT=Path(__file__).resolve().parents[1];SRC=ROOT/'ArtSource/Characters/ThiefEnemy01'
OUT=SRC/'PBRSurfaces';OUT.mkdir(exist_ok=True);BEFORE=OUT/'Before';BEFORE.mkdir(exist_ok=True)
TEX=ROOT/'Assets/_Project/Textures/Thieves/ThiefEnemy01'
paths=[SRC/'thief_enemy_01.blend',ROOT/'Assets/_Project/Materials/Thieves/ThiefEnemy01/thief_enemy_01.mat']+list(TEX.glob('*.png'))
for p in paths:
    if not (BEFORE/p.name).exists():shutil.copy2(p,BEFORE/p.name)
manifest=OUT/'PreservedAssetHashes.json'
if not manifest.exists():
    preserve=[ROOT/'Assets/_Project/Models/Thieves/thief_enemy_01.fbx',ROOT/'Assets/_Project/Models/Thieves/thief_enemy_01.fbx.meta']
    for folder in ['Assets/_Project/Prefabs/Enemies','Assets/_Project/Animations/Thieves/ThiefEnemy01']:
        preserve.extend(p for p in (ROOT/folder).rglob('*') if p.is_file())
    preserve.extend(TEX.glob('*.meta'));preserve.append(ROOT/'Assets/_Project/Materials/Thieves/ThiefEnemy01/thief_enemy_01.mat.meta')
    manifest.write_text(json.dumps({p.relative_to(ROOT).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in preserve},indent=2))
# name, display/sRGB colour, metallic, roughness. All surfaces remain opaque/lit.
specs=[
 ('Skin',[.76,.46,.32],0,.6),
 ('Burgundy cloth',[.53,.105,.25],0,.8),
 ('Plum cloth',[.235,.105,.27],0,.82),
 ('Gold fabric graphics',[1,.69,.025],0,.7),
 ('Charcoal trousers',[.105,.083,.135],0,.86),
 ('Satin domino mask',[.14,.105,.18],.18,.44),
 ('Burgundy leather',[.37,.065,.155],0,.66),
 ('Rubber soles',[.045,.04,.055],0,.9),
 ('Eye whites',[.86,.86,.79],0,.24),
 ('Brown irises',[.24,.115,.045],0,.23),
 ('Pupils and eye rims',[.018,.012,.017],0,.28),
]
data=np.load(SRC/'GetawayCrew/surface.npz');size=2048
pos=np.zeros((size,size,3),np.float32);valid=np.zeros((size,size),bool)
for xyz,uv in zip(data['xyz'],data['uv']):
    q=uv*size-.5;q[:,1]=size-1-q[:,1]
    x0,y0=np.maximum(0,np.floor(q.min(0)).astype(int));x1,y1=np.minimum(size-1,np.ceil(q.max(0)).astype(int))
    if x1<x0 or y1<y0:continue
    xx,yy=np.meshgrid(np.arange(x0,x1+1),np.arange(y0,y1+1));a,b,c=q
    den=(b[1]-c[1])*(a[0]-c[0])+(c[0]-b[0])*(a[1]-c[1])
    if abs(den)<1e-10:continue
    u=((b[1]-c[1])*(xx-c[0])+(c[0]-b[0])*(yy-c[1]))/den
    v=((c[1]-a[1])*(xx-c[0])+(a[0]-c[0])*(yy-c[1]))/den
    inside=(u>=-1e-5)&(v>=-1e-5)&(u+v<=1.00001)
    pos[y0:y1+1,x0:x1+1][inside]=(u[...,None]*xyz[0]+v[...,None]*xyz[1]+(1-u-v)[...,None]*xyz[2])[inside]
    valid[y0:y1+1,x0:x1+1]|=inside
rgb=np.asarray(Image.open(BEFORE/'thief_enemy_01_BaseColor.png').convert('RGB').resize((size,size),Image.Resampling.NEAREST),dtype=np.float32)/255
x,y,z=np.moveaxis(pos,-1,0);ax=abs(x);r,g,b=np.moveaxis(rgb,-1,0)
# The existing outfit already has clean graphic regions. Replace all generated
# skin, leather and eye shading with authored material colours.
palette=np.array([s[1] for s in specs[:5]])
region=np.argmin(((rgb[:,:,None,:]-palette[None,None,:,:])**2).sum(3),axis=2).astype(np.uint8)
skin=(r-g>.10)&(g-b>.04)&(r>g*1.25)
head=valid&(z>1.115);shoes=valid&(z<.24)
region[skin]=0
region[valid&(z>.24)&(z<.70)&~skin]=4
region[shoes]=7
region[shoes&(z>.095)]=6
gold=(r>.7)&(g>.35)&(b<.22)
region[gold]=3
mask=head&(y<-.115)&(z>1.205)&(z<1.435)&(ax<.245)&~skin
region[mask]=5
# Eyes are the only preserved detailed graphic. Uniform sclera/iris/pupil
# materials keep their original expression without painted reflections.
eyes=head&(y<-.17)&(z>1.27)&(z<1.395)&(ax<.19)&(ax>.032)
maskcol=np.array([.23,.17,.27])
mask_exact=np.max(abs(rgb-maskcol),axis=2)<.045
white=eyes&(rgb.min(2)>.30)&((rgb.max(2)-rgb.min(2))<.24)&~mask_exact
brown=eyes&(r>g*1.2)&(g>b*1.12)&~skin&~mask_exact
black=eyes&(rgb.max(2)<.16)&~mask_exact
region[white]=8;region[brown]=9;region[black]=10
# Preserve yellow cap strap; it is a graphic/fabric material, not metal.
region[gold]=3
for _ in range(14):
    new=valid.copy()
    for dy,dx in [(0,1),(0,-1),(1,0),(-1,0)]:
        near=np.roll(valid,(dy,dx),(0,1));take=~new&near
        region[take]=np.roll(region,(dy,dx),(0,1))[take];new[take]=True
    valid=new
colors=np.array([s[1] for s in specs]);metal=np.array([s[2] for s in specs]);rough=np.array([s[3] for s in specs])
base=np.uint8(np.clip(colors[region],0,1)*255)
mos=np.zeros((size,size,4),np.uint8);mos[:,:,0]=np.uint8(metal[region]*255);mos[:,:,1]=255;mos[:,:,3]=np.uint8((1-rough[region])*255)
orm=np.zeros_like(mos);orm[:,:,0]=255;orm[:,:,1]=np.uint8(rough[region]*255);orm[:,:,2]=mos[:,:,0];orm[:,:,3]=255
for suffix,array in [('BaseColor',base),('MetallicOcclusionSmoothness',mos)]:
    Image.fromarray(array).resize((1024,1024),Image.Resampling.LANCZOS).save(OUT/f'thief_enemy_01_{suffix}.png')
Image.new('RGB',(1024,1024),(128,128,255)).save(OUT/'thief_enemy_01_Normal.png')
Image.fromarray(orm).resize((1024,1024),Image.Resampling.LANCZOS).save(OUT/'Blender_ORM.png')
Image.fromarray(region*20).resize((1024,1024),Image.Resampling.NEAREST).save(OUT/'MaterialRegions.png')
(OUT/'SurfacePalette.json').write_text(json.dumps([{'name':s[0],'color_srgb':s[1],'metallic':s[2],'roughness':s[3]} for s in specs],indent=2))
print('BAKED_CLEAN_MATERIAL_REGIONS',dict(zip([s[0] for s in specs],np.bincount(region.ravel(),minlength=len(specs)).tolist())))
