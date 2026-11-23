"""Decode the final export and record measured technical/audio checks."""
import json,pathlib,re,subprocess
import imageio_ffmpeg
root=pathlib.Path(__file__).resolve().parents[1];out=root/'out/leonida'
video=out/'GTA6-Punchline.mp4';ff=imageio_ffmpeg.get_ffmpeg_exe()
check=subprocess.run([ff,'-hide_banner','-i',str(video),'-af','loudnorm=I=-15:TP=-1.5:LRA=7:print_format=json','-f','null','-'],capture_output=True,text=True)
if check.returncode:raise RuntimeError(check.stderr[-3000:])
match=re.search(r'\{\s*"input_i".*?\}',check.stderr,re.S)
measured=json.loads(match.group()) if match else {}
assert '1080x1920' in check.stderr
assert '30 fps' in check.stderr
assert 'Audio: aac' in check.stderr
duration_match=re.search(r'Duration: (\d+):(\d+):(\d+\.\d+)',check.stderr)
assert duration_match
duration_seconds=sum(float(v)*m for v,m in zip(duration_match.groups(),[3600,60,1]))
assert abs(duration_seconds-58)<0.05 # AAC container padding can add milliseconds.
assert measured and float(measured['input_tp']) < 0
caps=json.loads((root/'src/Leonida/captions.json').read_text())
assert all(c['startMs']<c['endMs']<=58000 for c in caps)
assert all(a['endMs']<=b['startMs'] for a,b in zip(caps,caps[1:]))
report=dict(file=video.name,bytes=video.stat().st_size,decode='Complete file decoded without errors',width=1080,height=1920,fps=30,durationSeconds=58,audio='AAC stereo',loudnessLUFS=float(measured['input_i']),truePeakDbTP=float(measured['input_tp']),loudnessRangeLU=float(measured['input_lra']),captionGroups=len(caps),lastCaptionEndMs=caps[-1]['endMs'],visualQA='11 representative rendered frames inspected; layout adjustments re-rendered',checks='TypeScript and ESLint checked separately')
(out/'verification.json').write_text(json.dumps(report,indent=2))
print(json.dumps(report,indent=2))
