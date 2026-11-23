"""Finish eyes and back graphics on the existing UV atlas; no mesh edits."""
from pathlib import Path
import numpy as np
from PIL import Image
ROOT=Path(__file__).resolve().parents[1];SRC=ROOT/'ArtSource/Characters/Hotwire';TEX=ROOT/'Assets/_Project/Textures/Thieves/Hotwire'
path=TEX/'Hotwire_BaseColor.png';original=SRC/'Generated_BaseColor.png'
if not original.exists():original.write_bytes(path.read_bytes())
data=np.load(SRC/'SurfaceMapping.npz');size=2048
pos=np.zeros((size,size,3),np.float32);valid=np.zeros((size,size),bool)
for xyz,uv in zip(data['xyz'],data['uv']):
    q=uv*size-.5;q[:,1]=size-1-q[:,1];x0,y0=np.maximum(0,np.floor(q.min(axis=0)).astype(int));x1,y1=np.minimum(size-1,np.ceil(q.max(axis=0)).astype(int))
    if x1<x0 or y1<y0:continue
    xx,yy=np.meshgrid(np.arange(x0,x1+1),np.arange(y0,y1+1));a,b,c=q;den=(b[1]-c[1])*(a[0]-c[0])+(c[0]-b[0])*(a[1]-c[1])
    if abs(den)<1e-10:continue
    u=((b[1]-c[1])*(xx-c[0])+(c[0]-b[0])*(yy-c[1]))/den;v=((c[1]-a[1])*(xx-c[0])+(a[0]-c[0])*(yy-c[1]))/den;w=1-u-v;mask=(u>=-1e-5)&(v>=-1e-5)&(w>=-1e-5)
    pos[y0:y1+1,x0:x1+1][mask]=(u[...,None]*xyz[0]+v[...,None]*xyz[1]+w[...,None]*xyz[2])[mask];valid[y0:y1+1,x0:x1+1][mask]=True
color=np.asarray(Image.open(original).convert('RGB').resize((size,size),Image.Resampling.BILINEAR))/255
x,y,z=np.moveaxis(pos,-1,0);r,g,b=np.moveaxis(color,-1,0)
# Find the actual sclera in mesh space; make readable brown irises with dark
# pupils and small highlights. These are painted details, not new eye bones.
for sign in [-1,1]:
    white=valid&(y<-.10)&(z>1.235)&(z<1.40)&(x*sign>.025)&(x*sign<.20)&(r>.65)&(g>.65)&(b>.60)
    xs=x[white];zs=z[white];assert len(xs)>0
    cx=float(np.median(xs));cz=float(np.median(zs))+.003
    print('EYE_CENTER',sign,cx,cz,'extent',np.quantile(xs,[.05,.95]),np.quantile(zs,[.05,.95]))
    ellipse=((x-cx)/.028)**2+((z-cz)/.034)**2
    iris=valid&(y<-.10)&(ellipse<1)&(z>1.16)&(z<1.40)
    color[iris]=[.25,.12,.045]
    pupil=iris&((((x-cx)/.014)**2+((z-cz)/.022)**2)<1);color[pupil]=[.025,.018,.016]
    shine=iris&((((x-cx+.008)/.006)**2+((z-cz-.012)/.007)**2)<1);color[shine]=[.98,.92,.78]
# Deliberately finish the back, which the single-view conversion approximates.
back=valid&(y>.065)&(z>.70)&(z<1.075)&(abs(x)<.30)
color[back]=[.27,.105,.25]
shoulder=back&(z>.91);color[shoulder]=[.94,.59,.025]
straps=shoulder&(abs(x)>.075)&(abs(x)<.12);color[straps]=[.26,.105,.25]
bib=back&(z>.77)&(z<.94)&(abs(x)<.13);color[bib]=[.50,.075,.20]
bolt=np.array([[.07,.925],[-.105,.855],[-.02,.867],[-.06,.805],[.105,.89],[.015,.88]])
inside=np.zeros(x.shape,bool)
for a,b in zip(bolt,np.roll(bolt,1,axis=0)):inside^=((a[1]>z)!=(b[1]>z))&(-x<(b[0]-a[0])*(z-a[1])/(b[1]-a[1]+1e-20)+a[0])
color[bib&inside]=[1,.69,.025]
# Extend islands to avoid colored seams in mipmaps on all LODs.
for _ in range(10):
    filled=valid.copy()
    for dy,dx in [(0,1),(0,-1),(1,0),(-1,0)]:
        take=~filled&np.roll(valid,(dy,dx),(0,1));color[take]=np.roll(color,(dy,dx),(0,1))[take];filled[take]=True
    valid=filled
Image.fromarray(np.uint8(np.clip(color,0,1)*255)).resize((1024,1024),Image.Resampling.LANCZOS).save(path)
print('HOTWIRE_TEXTURE_FINISHED')
