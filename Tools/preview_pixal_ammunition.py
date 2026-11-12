"""Assemble contact sheets from the actual Unity validation renders."""
from pathlib import Path
import json
from PIL import Image, ImageDraw, ImageFont

root=Path(__file__).resolve().parents[1]/'ArtSource/Weapons/Ammunition'
renders=root/'UnityValidation'
names=['Shotgun_Shell','AssaultRifle_Cartridge','Pistol_Cartridge',
       'Shotgun_Pellet','AssaultRifle_Bullet','Pistol_Bullet']
font=ImageFont.truetype('C:/Windows/Fonts/segoeuib.ttf',22)
small=ImageFont.truetype('C:/Windows/Fonts/segoeui.ttf',17)
background=(23,28,36)
counts={a['name']:a['triangles'] for a in json.loads((root/'AssetManifest.json').read_text())}
sheet=Image.new('RGB',(1260,1100),background)
draw=ImageDraw.Draw(sheet)
draw.text((24,16),'PIXAL3D / COUCH GUYS AMMUNITION',font=font,fill=(241,207,135))
draw.text((24,49),'Saved Unity prefabs  |  Enlarged previews; objects shown at different scales',font=small,fill=(180,188,201))
for i,name in enumerate(names):
    image=Image.open(renders/(name+'_0.png')).convert('RGB').resize((400,400),Image.Resampling.LANCZOS)
    x=10+(i%3)*420
    y=88+(i//3)*500
    sheet.paste(image,(x,y))
    draw.text((x+12,y+411),name.replace('_',' '),font=font,fill=(237,240,245))
    count=counts[name]
    draw.text((x+12,y+444),f'{count:,} triangles',font=small,fill=(163,174,190))
sheet.save(root/'AmmunitionPreview.png')
qa=Image.new('RGB',(930,1800),background)
draw=ImageDraw.Draw(qa)
for row,name in enumerate(names):
    for col in range(3):
        image=Image.open(renders/(name+f'_{col}.png')).convert('RGB').resize((288,270),Image.Resampling.LANCZOS)
        qa.paste(image,(col*310,row*300+24))
    draw.text((10,row*300+1),name.replace('_',' '),font=small,fill=(230,234,241))
qa.save(root/'MultiAngleReview.png')
print(root/'AmmunitionPreview.png')
