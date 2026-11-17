"""Prepare a small, offline Unity import project for only the four new FBXs."""
import json
import shutil
import uuid
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
DEST=ROOT/'Temp/NPCTrafficValidation'
for folder in ('Assets/Editor','Packages','ProjectSettings'):(DEST/folder).mkdir(parents=True,exist_ok=True)
shutil.copy2(ROOT/'Tools/Unity/ValidateNPCTraffic.cs',DEST/'Assets/Editor/ValidateNPCTraffic.cs')
shutil.copy2(ROOT/'ProjectSettings/ProjectVersion.txt',DEST/'ProjectSettings/ProjectVersion.txt')
relative=Path('Assets/_Project/Models/Vehicles/NPCTraffic')
shutil.copytree(ROOT/relative,DEST/relative,dirs_exist_ok=True)
shader=(ROOT/'Assets/_Project/Shaders/CouchGuysToon.shader').read_text()
paint=shader.replace('Shader "Couch Guys/Toon Lit"','Shader "Couch Guys/NPC Traffic Paint"',1)
paint=paint.replace('[MainColor] _BaseColor', '_PaintMask("Paint Mask", 2D) = "black" {}\n        [HideInInspector] _PaintReferenceColor("Original Paint Colour", Color) = (1,1,1,1)\n        [MainColor] _BaseColor',1)
paint=paint.replace('_BaseColor("Base Colour"','_BaseColor("Body Paint Colour"',1)
paint=paint.replace('SAMPLER(sampler_BaseMap);','SAMPLER(sampler_BaseMap);\n            TEXTURE2D(_PaintMask); SAMPLER(sampler_PaintMask);')
paint=paint.replace('half4 _BaseColor;','half4 _BaseColor;\n                half4 _PaintReferenceColor;')
paint=paint.replace('half4 baseSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;', '''half4 baseSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half paintMask = SAMPLE_TEXTURE2D(_PaintMask, sampler_PaintMask, input.uv).r;
                half3 paintRatio = _BaseColor.rgb / max(_PaintReferenceColor.rgb, half3(0.001h,0.001h,0.001h));
                half3 painted = baseSample.rgb * paintRatio;
                half originalValue = max(baseSample.r, max(baseSample.g, baseSample.b));
                half paintedValue = max(painted.r, max(painted.g, painted.b));
                painted *= originalValue / max(paintedValue, 0.001h);
                baseSample.rgb = lerp(baseSample.rgb, painted, paintMask);
                baseSample.a *= _BaseColor.a;''')
(ROOT/relative/'NPCTrafficPaint.shader').write_text(paint)
(DEST/relative/'NPCTrafficPaint.shader').write_text(paint)
shader_meta=ROOT/relative/'NPCTrafficPaint.shader.meta'
if not shader_meta.exists():
    shader_meta.write_text('fileFormatVersion: 2\nguid: '+uuid.uuid5(uuid.NAMESPACE_URL,'couch-guys:npc-traffic-paint-v1').hex+'\n')
shutil.copy2(shader_meta,DEST/relative/shader_meta.name)
(DEST/'Assets/NPCTrafficDetails.shader').write_text(shader)
shutil.copy2(ROOT/'Assets/_Project/Shaders/CouchGuysToon.shader.meta',DEST/'Assets/NPCTrafficDetails.shader.meta')
cache={}
for path in (ROOT/'Library/PackageCache').glob('*/package.json'):
    info=json.loads(path.read_text(encoding='utf-8'));cache[info['name']]=(path.parent,info)
deps={}
def add(name):
    if name in deps:return
    if name.startswith('com.unity.modules.'):
        deps[name]='1.0.0';return
    path,info=cache[name];deps[name]='file:'+path.as_posix()
    for child in info.get('dependencies',{}):add(child)
add('com.unity.render-pipelines.universal')
(DEST/'Packages/manifest.json').write_text(json.dumps({'dependencies':deps},indent=2))
print(DEST)
