"""Offline expression authoring. Python 3 + Pillow; never called by Unity import/runtime.
R=eyes, G=animated accents, B=preblurred glow. 5 columns x 2 rows.
Run from the project root. All shapes stay inside a generous screen-safe border.
"""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter

OUT = Path('Assets/_Project/Art/RobotFace')
W, H = 256, 128
NAMES = ['Happy', 'Excited', 'Surprised', 'Focused', 'Confused',
         'Damaged', 'Downed', 'Reviving', 'Angry', 'Celebrating']

def line(d, points, width=8):
    d.line(points, fill=255, width=width, joint='curve')
    r=width//2
    for x,y in (points[0],points[-1]): d.ellipse((x-r,y-r,x+r,y+r),fill=255)

def star(d,x,y,r=10):
    import math
    pts=[]
    for i in range(10):
        a=-math.pi/2+i*math.pi/5;rr=r if i%2==0 else r*.43
        pts.append((x+math.cos(a)*rr,y+math.sin(a)*rr))
    d.polygon(pts,fill=255)

def create(i):
    eyes=Image.new('L',(W,H)); accents=eyes.copy()
    d=ImageDraw.Draw(eyes); a=ImageDraw.Draw(accents)
    if i in (0,9):
        for x in (80,176):
            if i==0:
                pts=[(x+dx,67-29*(1-(dx/29)**2)) for dx in range(-29,30)]
                line(d,pts,10)
            else: line(d,[(x-25,68),(x,35),(x+25,68)],9)
        if i==0:
            for x,s in ((37,-1),(219,1)):
                line(a,[(x,84),(x+s*8,80)],4);line(a,[(x+3,95),(x+s*5,100)],4)
        else:
            star(a,36,88);star(a,220,88)
    elif i==1:
        line(d,[(55,36),(95,61),(55,86)],10)
        line(d,[(201,36),(161,61),(201,86)],10)
        for x,s in ((35,1),(221,-1)):
            for dy in (-15,0,15): line(a,[(x,61+dy),(x+s*12,61+dy*.7)],4)
    elif i==2:
        for x in (82,174):d.ellipse((x-23,38,x+23,84),outline=255,width=9)
    elif i in (3,8):
        d.polygon([(52,44),(110,62),(106,82),(65,85)],fill=255)
        d.polygon([(204,44),(146,62),(150,82),(191,85)],fill=255)
        if i==8:
            for pts in ([(199,24),(199,32),(191,32)],[(211,24),(211,32),(219,32)],
                        [(191,44),(199,44),(199,52)],[(219,44),(211,44),(211,52)]):line(a,pts,4)
    elif i==4:
        line(d,[(54,76),(100,67)],11);line(d,[(151,65),(191,80)],10)
        line(d,[(60,46),(90,48)],4);line(d,[(156,44),(181,38)],4)
        line(a,[(205,31),(210,23),(223,23),(228,30),(224,39),(216,43),(216,49)],5)
        a.ellipse((213,57,219,63),fill=255)
    elif i==5:
        d.pieslice((51,49,110,96),0,180,fill=255);d.pieslice((146,49,205,96),0,180,fill=255)
        a.polygon([(215,24),(207,42),(207,48),(211,53),(219,53),(223,48),(223,42)],fill=255)
    elif i==6:
        for x in (80,176):
            line(d,[(x-19,41),(x+19,80)],9);line(d,[(x+19,41),(x-19,80)],9)
    elif i==7:
        # Small heart centred between boot brackets.
        d.polygon([(99,56),(99,46),(110,37),(120,37),(128,45),(136,37),(146,37),(157,46),(157,56),(128,88)],fill=255)
        line(a,[(84,31),(70,44),(70,78),(84,91)],6)
        line(a,[(172,31),(186,44),(186,78),(172,91)],6)
    combined=Image.frombytes('L',(W,H),bytes(max(x,y) for x,y in zip(eyes.tobytes(),accents.tobytes())))
    halo=combined.filter(ImageFilter.GaussianBlur(3.2))
    return Image.merge('RGB',(eyes,accents,halo))

def main():
    OUT.mkdir(parents=True,exist_ok=True)
    atlas=Image.new('RGB',(W*5,H*2))
    preview=Image.new('RGB',(W*5,H*2),(1,5,11))
    for i,name in enumerate(NAMES):
        tile=create(i);atlas.paste(tile,((i%5)*W,(i//5)*H))
        rgb=Image.new('RGB',(W,H));pix=[]
        for r,g,b in tile.getdata():
            e=max(r,g)/255;gl=b/255
            pix.append((int(2+12*e+3*gl),min(255,int(8+225*e+22*gl)),min(255,int(16+235*e+25*gl))))
        rgb.putdata(pix); preview.paste(rgb,((i%5)*W,(i//5)*H))
    atlas.save(OUT/'RobotFaceExpressions.png')
    preview.save(OUT/'RobotFaceExpressions_Preview.png')
    print('Authored ten expressions:', ', '.join(NAMES))

if __name__=='__main__':main()
